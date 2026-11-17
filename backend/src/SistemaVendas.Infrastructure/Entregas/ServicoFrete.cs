using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Infrastructure.Entregas;

/// <summary>Conta completa, para o dono conferir no teste de CEP se o mapa achou o lugar certo.</summary>
public record DetalheFrete(FreteCalculado Frete, decimal Km, bool PorRota, string? Endereco, string? Fonte, string? Origem);

/// <summary>Frete da loja atual pela distância de carro até o CEP do cliente.</summary>
public class ServicoFrete(AppDbContext db, IGeolocalizacao geo)
{
    public Task<FretePorKm?> ConfigAtivaAsync(CancellationToken ct) =>
        db.FretesPorKm.AsNoTracking().FirstOrDefaultAsync(f => f.Ativo, ct);

    /// <summary>
    /// Null quando a loja não configurou o frete ou o CEP não foi encontrado com precisão:
    /// nesses casos o robô passa para o vendedor em vez de inventar um valor.
    /// </summary>
    public async Task<FreteCalculado?> CalcularAsync(int cep, CancellationToken ct) =>
        (await DetalharAsync(cep, ct))?.Frete;

    public async Task<DetalheFrete?> DetalharAsync(int cep, CancellationToken ct)
    {
        var cfg = await db.FretesPorKm.FirstOrDefaultAsync(f => f.Ativo, ct);
        if (cfg is null)
            return null;

        // Origem ainda não localizada (ou apagada por uma correção): procura de novo.
        if (cfg.OrigemLat == 0 && cfg.OrigemLon == 0)
        {
            if (await geo.LocalizarAsync(cfg.CepOrigem, ct) is not { } origem)
                return null;
            cfg.OrigemLat = origem.Ponto.Lat;
            cfg.OrigemLon = origem.Ponto.Lon;
            cfg.OrigemEndereco = origem.Endereco;
            await db.SaveChangesAsync(ct);
        }

        var formatado = RegrasFrete.Formatar(cep);
        decimal km = 0;
        var porRota = false;
        LocalEncontrado? destino = null;
        if (cep != cfg.CepOrigem)
        {
            destino = await geo.LocalizarAsync(cep, ct);
            if (destino is null)
                return null;
            var distancia = await geo.DistanciaAsync(new Coordenada(cfg.OrigemLat, cfg.OrigemLon), destino.Ponto, ct);
            km = RegrasFreteKm.Arredondar(distancia.Km);
            porRota = distancia.PorRota;
        }

        var valor = RegrasFreteKm.Calcular(cfg, km);
        var regiao = $"a {km.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} km da loja";
        var frete = valor is null
            ? new FreteCalculado(formatado, false, regiao, null, null)
            : new FreteCalculado(formatado, true, regiao, valor, cfg.Dias);
        frete = frete with { Endereco = destino?.Endereco is { Length: > 0 } e ? e : cep == cfg.CepOrigem ? cfg.OrigemEndereco : null };
        return new DetalheFrete(frete, km, porRota, destino?.Endereco, destino?.Fonte, cfg.OrigemEndereco);
    }
}
