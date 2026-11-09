using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Infrastructure.Entregas;

/// <summary>Frete da loja atual pela distância de carro até o CEP do cliente.</summary>
public class ServicoFrete(AppDbContext db, IGeolocalizacao geo)
{
    /// <summary>Configuração ativa com a origem localizada, ou null (o vendedor calcula na mão).</summary>
    public Task<FretePorKm?> ConfigAtivaAsync(CancellationToken ct) =>
        db.FretesPorKm.AsNoTracking().FirstOrDefaultAsync(f => f.Ativo, ct);

    /// <summary>
    /// Null quando a loja não configurou o frete ou o CEP não foi encontrado nos mapas:
    /// nesses casos o robô passa para o vendedor em vez de inventar um valor.
    /// </summary>
    public async Task<FreteCalculado?> CalcularAsync(int cep, CancellationToken ct)
    {
        var cfg = await ConfigAtivaAsync(ct);
        if (cfg is null)
            return null;

        var formatado = RegrasFrete.Formatar(cep);
        var km = 0m;
        if (cep != cfg.CepOrigem)
        {
            if (await geo.LocalizarAsync(cep, ct) is not { } destino)
                return null;
            var distancia = await geo.DistanciaAsync(new Coordenada(cfg.OrigemLat, cfg.OrigemLon), destino, ct);
            km = RegrasFreteKm.Arredondar(distancia.Km);
        }

        var valor = RegrasFreteKm.Calcular(cfg, km);
        var regiao = $"a {km.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} km da loja";
        return valor is null
            ? new FreteCalculado(formatado, false, regiao, null, null)
            : new FreteCalculado(formatado, true, regiao, valor, cfg.Dias);
    }
}
