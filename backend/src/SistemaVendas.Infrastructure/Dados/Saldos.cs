using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Estoque;

namespace SistemaVendas.Infrastructure.Dados;

public record SaldoLote(Guid LoteId, Guid EspecieId, Guid TanqueId, StatusLote Status, int Disponivel, int BaixasRecentes);

/// <summary>
/// Estoque é a soma dos movimentos de cada lote. No volume da fase 1 a soma é feita em memória;
/// se ficar lenta, vira uma tabela de saldo atualizada pelos próprios movimentos.
/// </summary>
public static class Saldos
{
    public static async Task<List<SaldoLote>> PorLoteAsync(AppDbContext db, CancellationToken ct)
    {
        var desde = DateTimeOffset.UtcNow.AddDays(-7);

        var linhas = await db.Movimentos.AsNoTracking()
            .Join(db.Lotes.Where(l => l.Status != StatusLote.Encerrado), m => m.LoteId, l => l.Id,
                (m, l) => new { m.LoteId, l.EspecieId, m.TanqueId, l.Status, m.Tipo, m.Quantidade, m.CriadoEm })
            .ToListAsync(ct);

        return linhas
            .GroupBy(x => new { x.LoteId, x.EspecieId, x.TanqueId, x.Status })
            .Select(g => new SaldoLote(
                g.Key.LoteId, g.Key.EspecieId, g.Key.TanqueId, g.Key.Status,
                g.Sum(x => MovimentoEstoque.Efeito(x.Tipo, x.Quantidade)),
                g.Where(x => x.Tipo == TipoMovimento.BaixaMorte && x.CriadoEm >= desde).Sum(x => x.Quantidade)))
            .ToList();
    }

    /// <summary>Disponível para venda por espécie: só lotes liberados.</summary>
    public static Dictionary<Guid, int> DisponivelPorEspecie(IEnumerable<SaldoLote> saldos) =>
        saldos.Where(s => s.Status == StatusLote.Liberado)
            .GroupBy(s => s.EspecieId)
            .ToDictionary(g => g.Key, g => Math.Max(0, g.Sum(s => s.Disponivel)));
}
