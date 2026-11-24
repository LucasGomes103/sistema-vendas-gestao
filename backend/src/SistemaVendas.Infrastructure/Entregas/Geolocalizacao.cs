using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Infrastructure.Entregas;

public class GeoOptions
{
    public const string Secao = "Geo";

    public string BrasilApi { get; set; } = "https://brasilapi.com.br/api/cep/v2/";
    public string Nominatim { get; set; } = "https://nominatim.openstreetmap.org/search";
    public string Osrm { get; set; } = "https://router.project-osrm.org/route/v1/driving/";

    /// <summary>Espera 1 s entre buscas no Nominatim (política do OpenStreetMap). Desligado só nos testes.</summary>
    public bool RespeitarLimite { get; set; } = true;
}

public record Coordenada(double Lat, double Lon);

/// <summary>Onde o CEP foi achado, de que jeito (rua, bairro ou BrasilAPI) e o endereço, para conferir.</summary>
public record LocalEncontrado(Coordenada Ponto, string Fonte, string Endereco);

/// <summary>Distância calculada: pela rota de carro quando o serviço responde, senão pela linha reta corrigida.</summary>
public record Distancia(double Km, bool PorRota);

public interface IGeolocalizacao
{
    /// <summary>Ponto do CEP, ou null se nenhum serviço souber onde fica com precisão de rua ou bairro.</summary>
    Task<LocalEncontrado?> LocalizarAsync(int cep, CancellationToken ct);

    Task<Distancia> DistanciaAsync(Coordenada de, Coordenada ate, CancellationToken ct);
}

/// <summary>
/// Serviço de mapas da plataforma, com cache compartilhado entre todas as lojas. Usa serviços gratuitos, sem chave:
/// BrasilAPI (CEP → endereço e, às vezes, coordenada), Nominatim do OpenStreetMap (endereço → coordenada) e o
/// servidor público do OSRM (rota de carro). Cada CEP localizado (local_cep) e cada rota calculada (rota_cache)
/// ficam no banco para todas as lojas: só a primeira consulta sai para a internet, o que poupa os limites dos
/// serviços grátis (e o bolso, se um dia trocar por um serviço pago).
/// </summary>
public class Geolocalizacao(AppDbContext db, MapasGratuitos mapas, TimeProvider? relogio = null) : IGeolocalizacao
{
    public async Task<LocalEncontrado?> LocalizarAsync(int cep, CancellationToken ct)
    {
        var salvo = await db.LocaisCep.AsNoTracking().FirstOrDefaultAsync(l => l.Cep == cep, ct);
        if (salvo is not null)
            return new LocalEncontrado(new Coordenada(salvo.Lat, salvo.Lon), salvo.Fonte, salvo.Endereco ?? "");

        var achado = await mapas.LocalizarAsync(cep, ct);
        if (achado is null)
            return null;

        var local = new LocalCep { Cep = cep, Lat = achado.Ponto.Lat, Lon = achado.Ponto.Lon, Fonte = achado.Fonte, Endereco = achado.Endereco };
        db.LocaisCep.Add(local);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Outra requisição guardou o mesmo CEP ao mesmo tempo: tudo bem, só não tenta de novo.
            db.Entry(local).State = EntityState.Detached;
        }
        return achado;
    }

    public async Task<Distancia> DistanciaAsync(Coordenada de, Coordenada ate, CancellationToken ct)
    {
        double oLat = RotaCache.Arredondar(de.Lat), oLon = RotaCache.Arredondar(de.Lon);
        double dLat = RotaCache.Arredondar(ate.Lat), dLon = RotaCache.Arredondar(ate.Lon);
        var agora = (relogio ?? TimeProvider.System).GetUtcNow();

        var salva = await db.RotasCache.FirstOrDefaultAsync(r => r.OrigemLat == oLat && r.OrigemLon == oLon && r.DestinoLat == dLat && r.DestinoLon == dLon, ct);
        if (salva is not null && agora - salva.CalculadaEm < RotaCache.Validade)
            return new Distancia(salva.Km, true);

        var distancia = await mapas.DistanciaAsync(de, ate, ct);
        // Linha reta (serviço de rotas fora do ar) não fica guardada: na próxima tenta a rota de verdade.
        if (!distancia.PorRota)
            return distancia;

        var rota = salva ?? db.RotasCache.Add(new RotaCache { OrigemLat = oLat, OrigemLon = oLon, DestinoLat = dLat, DestinoLon = dLon }).Entity;
        rota.Km = distancia.Km;
        rota.CalculadaEm = agora;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Outra requisição guardou a mesma rota ao mesmo tempo: tudo bem.
            db.Entry(rota).State = EntityState.Detached;
        }
        return distancia;
    }
}

/// <summary>Chamadas aos serviços de mapa, sem guardar nada (separado para poder testar sem banco).</summary>
public class MapasGratuitos(HttpClient http, IOptions<GeoOptions> opcoes, ILogger<MapasGratuitos> logger)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<Distancia> DistanciaAsync(Coordenada de, Coordenada ate, CancellationToken ct)
    {
        try
        {
            var url = $"{opcoes.Value.Osrm}{de.Lon.ToString(Inv)},{de.Lat.ToString(Inv)};{ate.Lon.ToString(Inv)},{ate.Lat.ToString(Inv)}?overview=false";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
            if (doc.RootElement.TryGetProperty("routes", out var rotas) && rotas.GetArrayLength() > 0)
                return new Distancia(rotas[0].GetProperty("distance").GetDouble() / 1000, true);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(e, "Rota de carro indisponível; usando a linha reta corrigida");
        }

        var reta = RegrasFreteKm.LinhaReta(de.Lat, de.Lon, ate.Lat, ate.Lon);
        return new Distancia(reta * RegrasFreteKm.FatorRuas, false);
    }

    /// <summary>
    /// Localiza o CEP pelo endereço: a BrasilAPI dá rua, bairro e cidade, e o OpenStreetMap acha o ponto.
    /// A coordenada que a BrasilAPI às vezes traz fica por último, porque muitas vezes é só o centro da cidade.
    /// Ponto em cima do centro da cidade é recusado: em São Paulo isso pode errar 20 km.
    /// </summary>
    public async Task<LocalEncontrado?> LocalizarAsync(int cep, CancellationToken ct)
    {
        var digitos = cep.ToString("00000000");
        string? rua = null, bairro = null, cidade = null, uf = null;
        Coordenada? daBrasilApi = null;
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"{opcoes.Value.BrasilApi}{digitos}", ct));
            var raiz = doc.RootElement;
            rua = Texto(raiz, "street");
            bairro = Texto(raiz, "neighborhood");
            cidade = Texto(raiz, "city");
            uf = Texto(raiz, "state");
            if (raiz.TryGetProperty("location", out var loc) && loc.TryGetProperty("coordinates", out var c)
                && Numero(c, "latitude") is { } lat && Numero(c, "longitude") is { } lon)
                daBrasilApi = new Coordenada(lat, lon);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(e, "BrasilAPI não respondeu para o CEP {Cep}", digitos);
        }

        if (cidade is null)
            return null; // CEP inexistente ou BrasilAPI fora do ar: o vendedor calcula

        var endereco = string.Join(", ", new[] { rua, bairro, $"{cidade}{(uf is null ? "" : " - " + uf)}" }.Where(p => p is not null));
        var centro = await BuscarAsync($"city={E(cidade)}&state={E(uf)}", ct);

        var tentativas = new List<(string Filtro, string Fonte)>();
        if (rua is not null)
        {
            tentativas.Add(($"street={E(rua)}&city={E(cidade)}&state={E(uf)}", "osm-rua"));
            if (bairro is not null)
                tentativas.Add(($"q={E($"{rua}, {bairro}, {cidade}, {uf}")}", "osm-rua"));
        }
        if (bairro is not null)
            tentativas.Add(($"q={E($"{bairro}, {cidade}, {uf}")}", "osm-bairro"));

        foreach (var (filtro, fonte) in tentativas)
        {
            if (await BuscarAsync(filtro, ct) is { } achado && !NoCentro(achado, centro))
                return new LocalEncontrado(achado, fonte, endereco);
        }

        if (daBrasilApi is not null && !NoCentro(daBrasilApi, centro))
            return new LocalEncontrado(daBrasilApi, "brasilapi", endereco);

        logger.LogWarning("CEP {Cep} ({Endereco}) só foi achado no centro da cidade; frete fica com o vendedor", digitos, endereco);
        return null;
    }

    /// <summary>Ponto a menos de 300 m do centro da cidade: é o mapa dizendo "em algum lugar desta cidade".</summary>
    private static bool NoCentro(Coordenada ponto, Coordenada? centro) =>
        centro is not null && RegrasFreteKm.LinhaReta(ponto.Lat, ponto.Lon, centro.Lat, centro.Lon) < 0.3;

    private static readonly SemaphoreSlim FilaNominatim = new(1, 1);
    private static DateTimeOffset _ultimaNominatim = DateTimeOffset.MinValue;

    /// <summary>Uma busca no Nominatim, respeitando o limite de 1 por segundo da política de uso.</summary>
    private async Task<Coordenada?> BuscarAsync(string filtro, CancellationToken ct)
    {
        await FilaNominatim.WaitAsync(ct);
        try
        {
            var espera = _ultimaNominatim.AddSeconds(1.1) - DateTimeOffset.UtcNow;
            if (espera > TimeSpan.Zero && opcoes.Value.RespeitarLimite)
                await Task.Delay(espera, ct);
            _ultimaNominatim = DateTimeOffset.UtcNow;

            var lista = await http.GetFromJsonAsync<List<ResultadoNominatim>>(
                $"{opcoes.Value.Nominatim}?{filtro}&countrycodes=br&format=json&limit=1", ct);
            return lista is [{ } r, ..] && double.TryParse(r.lat, Inv, out var lat) && double.TryParse(r.lon, Inv, out var lon)
                ? new Coordenada(lat, lon)
                : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(e, "OpenStreetMap não respondeu ({Filtro})", filtro);
            return null;
        }
        finally
        {
            FilaNominatim.Release();
        }
    }

    private static string E(string? texto) => Uri.EscapeDataString(texto ?? "");

    private static string? Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static double? Numero(JsonElement e, string nome)
    {
        if (!e.TryGetProperty(nome, out var v))
            return null;
        if (v.ValueKind == JsonValueKind.Number)
            return v.GetDouble();
        return v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), Inv, out var d) ? d : null;
    }

    private sealed record ResultadoNominatim(string lat, string lon);
}
