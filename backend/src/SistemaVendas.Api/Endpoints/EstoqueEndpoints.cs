using System.Security.Claims;
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

public record TanqueEntrada(string Nome, TipoTanque Tipo, int? Litros);

public record LoteNoTanque(
    Guid LoteId,
    Guid EspecieId,
    string Especie,
    string? Fornecedor,
    string Status,
    DateTimeOffset RecebidoEm,
    DateTimeOffset? QuarentenaAte,
    int Disponivel);

public record MovimentoResumo(Guid Id, string Tipo, int Quantidade, string Especie, string? Observacao, string? Usuario, DateTimeOffset CriadoEm);

public record TanqueDetalhe(Guid Id, string Nome, string Tipo, int? Litros, IReadOnlyList<LoteNoTanque> Lotes, IReadOnlyList<MovimentoResumo> Movimentos);

public record LoteEntrada(Guid EspecieId, Guid TanqueId, Guid? FornecedorId, int Quantidade, decimal? CustoUnitario, int QuarentenaDias);

public record LiberarEntrada(Guid? TanqueDestinoId);

public record MovimentoEntrada(Guid LoteId, int Quantidade, string? Observacao, Guid? TanqueDestinoId);

public record FornecedorEntrada(string Nome, string? Telefone);

public static class EstoqueEndpoints
{
    public static void MapEstoqueEndpoints(this IEndpointRouteBuilder app)
    {
        var tanques = app.MapGroup("/tanques").WithTags("Estoque").RequireAuthorization(AuthEndpoints.PoliticaEquipe);

        // Tanques com o que tem dentro: o lojista confere o estoque pelo celular.
        tanques.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var lista = await db.Tanques.AsNoTracking().Where(t => t.Ativo).OrderBy(t => t.Nome).ToListAsync(ct);
            var nomes = await db.Especies.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.NomePopular, ct);
            var saldos = await Saldos.PorLoteAsync(db, ct);

            return lista.Select(t =>
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

        tanques.MapPost("/", async (TanqueEntrada entrada, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(entrada.Nome))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["nome"] = ["Informe o nome do tanque."] });

            var tanque = new Tanque { Nome = entrada.Nome.Trim(), Tipo = entrada.Tipo, Litros = entrada.Litros };
            db.Tanques.Add(tanque);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/tanques/{tanque.Id}", new { tanque.Id, tanque.Nome });
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);

        // Excluir só desativa: as movimentações antigas continuam no histórico.
        tanques.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var tanque = await db.Tanques.FirstOrDefaultAsync(t => t.Id == id && t.Ativo, ct);
            if (tanque is null)
                return Results.NotFound();

            var peixes = (await Saldos.PorLoteAsync(db, ct)).Where(s => s.TanqueId == id).Sum(s => Math.Max(0, s.Disponivel));
            if (peixes > 0)
                return Results.Problem(
                    $"O tanque ainda tem {peixes} peixes. Transfira ou dê baixa antes de excluir.",
                    statusCode: StatusCodes.Status409Conflict);

            tanque.Ativo = false;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);

        tanques.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var tanque = await db.Tanques.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tanque is null)
                return Results.NotFound();

            var saldos = (await Saldos.PorLoteAsync(db, ct)).Where(s => s.TanqueId == id && s.Disponivel > 0).ToList();
            var loteIds = saldos.Select(s => s.LoteId).ToList();

            var lotes = await db.Lotes.AsNoTracking()
                .Where(l => loteIds.Contains(l.Id))
                .Select(l => new
                {
                    l.Id,
                    l.EspecieId,
                    Especie = db.Especies.Where(e => e.Id == l.EspecieId).Select(e => e.NomePopular).FirstOrDefault(),
                    Fornecedor = db.Fornecedores.Where(f => f.Id == l.FornecedorId).Select(f => f.Nome).FirstOrDefault(),
                    l.Status,
                    l.RecebidoEm,
                    l.QuarentenaAte
                })
                .ToListAsync(ct);

            var movimentos = await db.Movimentos.AsNoTracking()
                .Where(m => m.TanqueId == id)
                .OrderByDescending(m => m.CriadoEm)
                .Take(30)
                .Select(m => new
                {
                    m.Id,
                    m.Tipo,
                    m.Quantidade,
                    Especie = db.Lotes.Where(l => l.Id == m.LoteId)
                        .Select(l => db.Especies.Where(e => e.Id == l.EspecieId).Select(e => e.NomePopular).FirstOrDefault())
                        .FirstOrDefault(),
                    m.Observacao,
                    Usuario = db.Usuarios.Where(u => u.Id == m.UsuarioId).Select(u => u.Nome).FirstOrDefault(),
                    m.CriadoEm
                })
                .ToListAsync(ct);

            var disponivel = saldos.ToDictionary(s => s.LoteId, s => s.Disponivel);
            return Results.Ok(new TanqueDetalhe(
                tanque.Id, tanque.Nome, tanque.Tipo.ToString(), tanque.Litros,
                lotes.OrderBy(l => l.Especie)
                    .Select(l => new LoteNoTanque(l.Id, l.EspecieId, l.Especie ?? "?", l.Fornecedor, l.Status.ToString(), l.RecebidoEm, l.QuarentenaAte, disponivel[l.Id]))
                    .ToList(),
                movimentos.Select(m => new MovimentoResumo(m.Id, m.Tipo.ToString(), m.Quantidade, m.Especie ?? "?", m.Observacao, m.Usuario, m.CriadoEm)).ToList()));
        });

        tanques.MapPost("/{id:guid}/baixa", (Guid id, MovimentoEntrada entrada, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            Movimentar(db, id, entrada.LoteId, ct, async (lote, saldo) =>
            {
                db.Movimentos.Add(RegrasEstoque.Baixa(lote, id, saldo, entrada.Quantidade, entrada.Observacao, UsuarioId(user)));
                await Task.CompletedTask;
            }));

        tanques.MapPost("/{id:guid}/transferencia", (Guid id, MovimentoEntrada entrada, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            Movimentar(db, id, entrada.LoteId, ct, async (lote, saldo) =>
            {
                if (entrada.TanqueDestinoId is not { } destino || !await db.Tanques.AnyAsync(t => t.Id == destino && t.Ativo, ct))
                    throw new RegraException("Escolha o tanque de destino.");

                var (saida, chegada) = RegrasEstoque.Transferir(lote, id, destino, saldo, entrada.Quantidade, UsuarioId(user));
                db.Movimentos.AddRange(saida, chegada);
            }));

        tanques.MapPost("/{id:guid}/ajuste", (Guid id, MovimentoEntrada entrada, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            Movimentar(db, id, entrada.LoteId, ct, async (lote, saldo) =>
            {
                db.Movimentos.Add(RegrasEstoque.Ajuste(lote, id, saldo, entrada.Quantidade, entrada.Observacao, UsuarioId(user)));
                await Task.CompletedTask;
            }))
            .RequireAuthorization(AuthEndpoints.PoliticaDono);

        var lotes = app.MapGroup("/lotes").WithTags("Estoque").RequireAuthorization(AuthEndpoints.PoliticaEquipe);

        // Entrada de lote: chegada de peixe do fornecedor.
        lotes.MapPost("/", async (LoteEntrada entrada, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var tanque = await db.Tanques.FirstOrDefaultAsync(t => t.Id == entrada.TanqueId && t.Ativo, ct);
            if (tanque is null)
                return Problema("Tanque não encontrado.");
            if (!await db.Especies.AnyAsync(e => e.Id == entrada.EspecieId && e.Ativo, ct))
                return Problema("Espécie não encontrada.");
            if (entrada.FornecedorId is { } f && !await db.Fornecedores.AnyAsync(x => x.Id == f, ct))
                return Problema("Fornecedor não encontrado.");

            try
            {
                var (lote, mov) = RegrasEstoque.Receber(tanque.LojaId, entrada.EspecieId, tanque, entrada.FornecedorId,
                    entrada.Quantidade, entrada.CustoUnitario, entrada.QuarentenaDias, DateTimeOffset.UtcNow);
                mov.UsuarioId = UsuarioId(user);
                db.Lotes.Add(lote);
                db.Movimentos.Add(mov);
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/v1/lotes/{lote.Id}", new { lote.Id, Status = lote.Status.ToString(), lote.QuarentenaAte });
            }
            catch (RegraException e)
            {
                return Problema(e.Message);
            }
        });

        // Liberação da quarentena, opcionalmente movendo todo o lote para um tanque de venda.
        lotes.MapPost("/{id:guid}/liberar", async (Guid id, LiberarEntrada entrada, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var lote = await db.Lotes.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (lote is null)
                return Results.NotFound();

            try
            {
                RegrasEstoque.Liberar(lote);

                if (entrada.TanqueDestinoId is { } destino && destino != lote.TanqueId)
                {
                    if (!await db.Tanques.AnyAsync(t => t.Id == destino && t.Ativo, ct))
                        throw new RegraException("Tanque de destino não encontrado.");

                    var saldo = await SaldoAsync(db, lote.Id, lote.TanqueId, ct);
                    if (saldo > 0)
                    {
                        var (saida, chegada) = RegrasEstoque.Transferir(lote, lote.TanqueId, destino, saldo, saldo, UsuarioId(user));
                        db.Movimentos.AddRange(saida, chegada);
                    }
                    lote.TanqueId = destino;
                }

                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            }
            catch (RegraException e)
            {
                return Problema(e.Message);
            }
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);

        var fornecedores = app.MapGroup("/fornecedores").WithTags("Estoque").RequireAuthorization(AuthEndpoints.PoliticaEquipe);

        fornecedores.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            await db.Fornecedores.AsNoTracking().OrderBy(f => f.Nome).Select(f => new { f.Id, f.Nome, f.Telefone }).ToListAsync(ct));

        fornecedores.MapPost("/", async (FornecedorEntrada entrada, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(entrada.Nome))
                return Problema("Informe o nome do fornecedor.");

            var fornecedor = new Fornecedor { Nome = entrada.Nome.Trim(), Telefone = entrada.Telefone?.Trim() };
            db.Fornecedores.Add(fornecedor);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/fornecedores/{fornecedor.Id}", new { fornecedor.Id, fornecedor.Nome, fornecedor.Telefone });
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);
    }

    /// <summary>Carrega o lote, calcula o saldo dele no tanque e aplica o movimento numa transação.</summary>
    private static async Task<IResult> Movimentar(AppDbContext db, Guid tanqueId, Guid loteId, CancellationToken ct, Func<Lote, int, Task> aplicar)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var lote = await db.Lotes.FirstOrDefaultAsync(l => l.Id == loteId, ct);
        if (lote is null)
            return Problema("Lote não encontrado.");

        try
        {
            await aplicar(lote, await SaldoAsync(db, loteId, tanqueId, ct));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        }
        catch (RegraException e)
        {
            return Problema(e.Message);
        }
    }

    private static async Task<int> SaldoAsync(AppDbContext db, Guid loteId, Guid tanqueId, CancellationToken ct)
    {
        var movimentos = await db.Movimentos.AsNoTracking()
            .Where(m => m.LoteId == loteId && m.TanqueId == tanqueId)
            .Select(m => new { m.Tipo, m.Quantidade })
            .ToListAsync(ct);
        return movimentos.Sum(m => MovimentoEstoque.Efeito(m.Tipo, m.Quantidade));
    }

    private static Guid? UsuarioId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static IResult Problema(string mensagem) =>
        Results.Problem(mensagem, statusCode: StatusCodes.Status422UnprocessableEntity);
}
