using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Caixa;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Vendas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Endpoints;

public record LancamentoSaida(Guid Id, string Tipo, string Categoria, decimal Valor, string? Descricao, string Forma, DateOnly Data,
    Guid? PedidoId, DateTimeOffset CriadoEm, string? Usuario);

public record FechamentoSaida(decimal Entradas, decimal Saidas, decimal Saldo, decimal DinheiroEsperado, decimal? DinheiroContado,
    decimal? Diferenca, string? Observacao, DateTimeOffset FechadoEm, string? FechadoPor);

public record CaixaDiaSaida(DateOnly Data, DateOnly Hoje, List<LancamentoSaida> Lancamentos, ResumoCaixa Resumo, FechamentoSaida? Fechamento,
    string[] CategoriasEntrada, string[] CategoriasSaida);

public record LancamentoEntrada(TipoLancamento Tipo, string? Categoria, decimal Valor, string? Descricao, FormaPagamento Forma, DateOnly? Data);

public record FecharCaixaEntrada(DateOnly Data, decimal? DinheiroContado, string? Observacao);

/// <summary>Caixa do dia: vendas pagas entram sozinhas, despesas e vendas no balcão o dono lança; no fim do dia, fecha.</summary>
public static class CaixaEndpoints
{
    public static void MapCaixaEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/caixa").WithTags("Caixa").RequireAuthorization(AuthEndpoints.PoliticaDono);

        grupo.MapGet("/", async (DateOnly? data, AppDbContext db, ILojaAtual lojaAtual, CancellationToken ct) =>
        {
            var hoje = await HojeAsync(db, lojaAtual, ct);
            var dia = data ?? hoje;
            var lancamentos = await db.LancamentosCaixa.AsNoTracking().Where(l => l.Data == dia).OrderByDescending(l => l.CriadoEm).ToListAsync(ct);
            var fechamento = await db.FechamentosCaixa.AsNoTracking().FirstOrDefaultAsync(f => f.Data == dia, ct);

            var ids = lancamentos.Select(l => l.UsuarioId).Append(fechamento?.FechadoPor).OfType<Guid>().Distinct().ToList();
            var nomes = await db.Usuarios.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome, ct);

            return new CaixaDiaSaida(
                dia, hoje,
                lancamentos.Select(l => new LancamentoSaida(l.Id, l.Tipo.ToString(), l.Categoria, l.Valor, l.Descricao, l.Forma.ToString(), l.Data,
                    l.PedidoId, l.CriadoEm, l.UsuarioId is { } u ? nomes.GetValueOrDefault(u) : null)).ToList(),
                RegrasCaixa.Resumir(lancamentos),
                fechamento is null ? null : new FechamentoSaida(fechamento.Entradas, fechamento.Saidas, fechamento.Saldo, fechamento.DinheiroEsperado,
                    fechamento.DinheiroContado, fechamento.Diferenca, fechamento.Observacao, fechamento.CriadoEm,
                    fechamento.FechadoPor is { } f ? nomes.GetValueOrDefault(f) : null),
                RegrasCaixa.CategoriasEntrada, RegrasCaixa.CategoriasSaida);
        });

        grupo.MapPost("/lancamentos", async (LancamentoEntrada e, AppDbContext db, ILojaAtual lojaAtual, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var dia = e.Data ?? await HojeAsync(db, lojaAtual, ct);
            try
            {
                var fechado = await db.FechamentosCaixa.AnyAsync(f => f.Data == dia, ct);
                var l = RegrasCaixa.Lancar(lojaAtual.LojaId!.Value, e.Tipo, e.Categoria, e.Valor, e.Descricao, e.Forma, dia, fechado, Usuario(user));
                db.LancamentosCaixa.Add(l);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { l.Id });
            }
            catch (RegraException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });

        // Venda de pedido não sai do caixa por aqui: ela segue o pedido.
        grupo.MapDelete("/lancamentos/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var l = await db.LancamentosCaixa.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (l is null)
                return Results.NotFound();
            if (l.PedidoId is not null)
                return Results.Problem("Este lançamento é o pagamento de um pedido e não pode ser apagado.", statusCode: StatusCodes.Status422UnprocessableEntity);
            if (await db.FechamentosCaixa.AnyAsync(f => f.Data == l.Data, ct))
                return Results.Problem("O caixa deste dia já foi fechado. Reabra o dia para apagar.", statusCode: StatusCodes.Status422UnprocessableEntity);
            db.LancamentosCaixa.Remove(l);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        grupo.MapPost("/fechar", async (FecharCaixaEntrada e, AppDbContext db, ILojaAtual lojaAtual, ClaimsPrincipal user, CancellationToken ct) =>
        {
            try
            {
                if (e.Data > await HojeAsync(db, lojaAtual, ct))
                    throw new RegraException("Não dá para fechar um dia que ainda não chegou.");
                var lancamentos = await db.LancamentosCaixa.AsNoTracking().Where(l => l.Data == e.Data).ToListAsync(ct);
                var fechado = await db.FechamentosCaixa.AnyAsync(f => f.Data == e.Data, ct);
                db.FechamentosCaixa.Add(RegrasCaixa.Fechar(lojaAtual.LojaId!.Value, e.Data, lancamentos, e.DinheiroContado, e.Observacao, fechado, Usuario(user)));
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            }
            catch (RegraException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });

        grupo.MapDelete("/fechamento", async (DateOnly data, AppDbContext db, CancellationToken ct) =>
        {
            var f = await db.FechamentosCaixa.FirstOrDefaultAsync(x => x.Data == data, ct);
            if (f is null)
                return Results.NotFound();
            db.FechamentosCaixa.Remove(f);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<DateOnly> HojeAsync(AppDbContext db, ILojaAtual lojaAtual, CancellationToken ct)
    {
        var fuso = await db.Lojas.Where(l => l.Id == lojaAtual.LojaId).Select(l => l.Fuso).FirstAsync(ct);
        return RegrasCaixa.Hoje(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(fuso));
    }

    private static Guid? Usuario(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : null;
}
