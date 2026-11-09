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
}

public record Coordenada(double Lat, double Lon);

/// <summary>Distância calculada: pela rota de carro quando o serviço responde, senão pela linha reta corrigida.</summary>
public record Distancia(double Km, bool PorRota);

public interface IGeolocalizacao
{
    /// <summary>Coordenada aproximada do CEP, ou null se nenhum serviço souber onde fica.</summary>
    Task<Coordenada?> LocalizarAsync(int cep, CancellationToken ct);

    Task<Distancia> DistanciaAsync(Coordenada de, Coordenada ate, CancellationToken ct);
}

/// <summary>
/// Serviços gratuitos, sem chave: BrasilAPI (CEP → endereço e, às vezes, coordenada), Nominatim do
/// OpenStreetMap (endereço → coordenada) e o servidor público do OSRM (rota de carro). Cada CEP
/// localizado fica guardado no banco, e só a primeira consulta de cada CEP sai para a internet.
/// </summary>
public class Geolocalizacao(AppDbContext db, MapasGratuitos mapas) : IGeolocalizacao
{
    public async Task<Coordenada?> LocalizarAsync(int cep, CancellationToken ct)
    {
        var salvo = await db.LocaisCep.AsNoTracking().FirstOrDefaultAsync(l => l.Cep == cep, ct);
        if (salvo is not null)
            return new Coordenada(salvo.Lat, salvo.Lon);

        var (coord, fonte) = await mapas.LocalizarAsync(cep, ct);
        if (coord is null)
            return null;

        var local = new LocalCep { Cep = cep, Lat = coord.Lat, Lon = coord.Lon, Fonte = fonte };
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
        return coord;
    }

    public Task<Distancia> DistanciaAsync(Coordenada de, Coordenada ate, CancellationToken ct) => mapas.DistanciaAsync(de, ate, ct);
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

    public async Task<(Coordenada?, string Fonte)> LocalizarAsync(int cep, CancellationToken ct)
    {
        var digitos = cep.ToString("00000000");
        string? rua = null, cidade = null, uf = null;
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync($"{opcoes.Value.BrasilApi}{digitos}", ct));
            var raiz = doc.RootElement;
            rua = Texto(raiz, "street");
            cidade = Texto(raiz, "city");
            uf = Texto(raiz, "state");
            if (raiz.TryGetProperty("location", out var loc) && loc.TryGetProperty("coordinates", out var c)
                && Numero(c, "latitude") is { } lat && Numero(c, "longitude") is { } lon)
                return (new Coordenada(lat, lon), "brasilapi");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(e, "BrasilAPI não respondeu para o CEP {Cep}", digitos);
        }

        // Sem coordenada na BrasilAPI: procura o endereço no OpenStreetMap. Só a cidade não serve:
        // numa cidade grande o centro pode estar a 20 km do cliente, e aí é melhor o vendedor calcular.
        var tentativas = new List<string>();
        if (cidade is not null && rua is not null)
            tentativas.Add($"street={Uri.EscapeDataString(rua)}&city={Uri.EscapeDataString(cidade)}&state={Uri.EscapeDataString(uf ?? "")}");
        tentativas.Add($"postalcode={digitos.Insert(5, "-")}");

        foreach (var filtro in tentativas)
        {
            try
            {
                var lista = await http.GetFromJsonAsync<List<ResultadoNominatim>>(
                    $"{opcoes.Value.Nominatim}?{filtro}&country=Brazil&format=json&limit=1", ct);
                if (lista is [{ } r, ..] && double.TryParse(r.lat, Inv, out var lat) && double.TryParse(r.lon, Inv, out var lon))
                    return (new Coordenada(lat, lon), "osm");
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(e, "OpenStreetMap não respondeu para o CEP {Cep}", digitos);
                break;
            }
        }

        return (null, "");
    }

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
