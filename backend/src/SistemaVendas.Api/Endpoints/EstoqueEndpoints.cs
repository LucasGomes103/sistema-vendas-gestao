using Microsoft.EntityFrameworkCore;
using SistemaVendas.Api.Comum;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Endpoints;

public record ItemTanque(Guid EspecieId, string Especie, int Disponivel, bool EmQuarentena);

public record TanqueResumo(
    Guid Id,
    string Nome,
    string Tipo,
    int? Litros,
    int Total,
    int BaixasUltimos7Dias,
    IReadOnlyList<ItemTanque> Itens);

public static class EstoqueEndpoints
{
    public static void MapEstoqueEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/tanques").WithTags("Estoque").RequireAuthorization(AuthEndpoints.PoliticaEquipe);

        // Tanques com o que tem dentro: o lojista confere o estoque pelo celular.
        grupo.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var tanques = await db.Tanques.AsNoTracking().Where(t => t.Ativo).OrderBy(t => t.Nome).ToListAsync(ct);
            var nomes = await db.Especies.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.NomePopular, ct);
            var saldos = await Saldos.PorLoteAsync(db, ct);

            return tanques.Select(t =>
            {
                var doTanque = saldos.Where(s => s.TanqueId == t.Id).ToList();
                var itens = doTanque
                    .GroupBy(s => new { s.EspecieId, Quarentena = s.Status == StatusLote.Quarentena })
                    .Select(g => new ItemTanque(g.Key.EspecieId, nomes.GetValueOrDefault(g.Key.EspecieId, "?"), g.Sum(s => s.Disponivel), g.Key.Quarentena))
                    .Where(i => i.Disponivel > 0)
                    .OrderBy(i => i.Especie)
                    .ToList();

                return new TanqueResumo(t.Id, t.Nome, t.Tipo.ToString(), t.Litros,
                    itens.Sum(i => i.Disponivel), doTanque.Sum(s => s.BaixasRecentes), itens);
            });
        });
    }
}
