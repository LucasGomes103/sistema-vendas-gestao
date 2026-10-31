using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Api.Comum;
using SistemaVendas.Application.Catalogo;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Catalogo;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Endpoints;

public static class CatalogoEndpoints
{
    public static void MapCatalogoPublicoEndpoints(this IEndpointRouteBuilder app)
    {
        // Catálogo público: só mostra o que está disponível agora (lote liberado, saldo > 0).
        app.MapGet("/public/{slug}/catalogo", async (string slug, AppDbContext db, ILojaAtual lojaAtual, CancellationToken ct) =>
        {
            var loja = await db.Lojas.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug, ct);
            if (loja is null)
                return Results.NotFound();

            lojaAtual.Definir(loja.Id);

            var disponivel = Saldos.DisponivelPorEspecie(await Saldos.PorLoteAsync(db, ct));
            var comEstoque = disponivel.Where(x => x.Value > 0).Select(x => x.Key).ToList();

            var especies = await db.Especies.AsNoTracking()
                .Where(e => e.Ativo && comEstoque.Contains(e.Id))
                .OrderBy(e => e.NomePopular)
                .Select(e => new { e.Id, e.NomePopular, e.NomeCientifico, e.TamanhoCm, e.Preco, FotoUrl = e.Fotos.OrderBy(f => f.Ordem).Select(f => f.Url).FirstOrDefault() })
                .ToListAsync(ct);

            var itens = especies.Select(e => new ItemCatalogoPublico(e.Id, e.NomePopular, e.NomeCientifico, e.TamanhoCm, e.Preco, disponivel[e.Id], e.FotoUrl));
            return Results.Ok(new { loja = loja.Nome, whatsApp = loja.WhatsAppNumero, itens });
        })
        .WithTags("Catálogo público");
    }

    public static void MapEspecieEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/especies").WithTags("Espécies").RequireAuthorization(AuthEndpoints.PoliticaEquipe);

        grupo.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var disponivel = Saldos.DisponivelPorEspecie(await Saldos.PorLoteAsync(db, ct));
            var especies = await db.Especies.AsNoTracking().Where(e => e.Ativo).OrderBy(e => e.NomePopular).ToListAsync(ct);
            return especies.Select(e => Saida(e, disponivel.GetValueOrDefault(e.Id)));
        });

        grupo.MapPost("/", async (EspecieEntrada entrada, IValidator<EspecieEntrada> validator, AppDbContext db, CancellationToken ct) =>
        {
            var validacao = await validator.ValidateAsync(entrada, ct);
            if (!validacao.IsValid)
                return Results.ValidationProblem(validacao.ToDictionary());

            var especie = new Especie { NomePopular = "" };
            Aplicar(especie, entrada);
            db.Especies.Add(especie);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/especies/{especie.Id}", Saida(especie, 0));
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);

        grupo.MapPut("/{id:guid}", async (Guid id, EspecieEntrada entrada, IValidator<EspecieEntrada> validator, AppDbContext db, CancellationToken ct) =>
        {
            var validacao = await validator.ValidateAsync(entrada, ct);
            if (!validacao.IsValid)
                return Results.ValidationProblem(validacao.ToDictionary());

            var especie = await db.Especies.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (especie is null)
                return Results.NotFound();

            Aplicar(especie, entrada);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);

        // Excluir só esconde (o histórico de lotes e vendas continua apontando para a espécie).
        grupo.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var especie = await db.Especies.FirstOrDefaultAsync(e => e.Id == id && e.Ativo, ct);
            if (especie is null)
                return Results.NotFound();

            var noEstoque = (await Saldos.PorLoteAsync(db, ct)).Where(s => s.EspecieId == id).Sum(s => Math.Max(0, s.Disponivel));
            if (noEstoque > 0)
                return Results.Problem(
                    $"Ainda há {noEstoque} {especie.NomePopular} no estoque. Dê baixa ou ajuste o estoque para zero antes de excluir.",
                    statusCode: StatusCodes.Status409Conflict);

            especie.Ativo = false;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);
    }

    private static void Aplicar(Especie especie, EspecieEntrada entrada)
    {
        especie.NomePopular = entrada.NomePopular.Trim();
        especie.NomeCientifico = string.IsNullOrWhiteSpace(entrada.NomeCientifico) ? null : entrada.NomeCientifico.Trim();
        especie.TamanhoCm = entrada.TamanhoCm;
        especie.Preco = entrada.Preco;
        especie.Observacoes = entrada.Observacoes;
    }

    private static EspecieSaida Saida(Especie e, int disponivel) =>
        new(e.Id, e.NomePopular, e.NomeCientifico, e.TamanhoCm, e.Preco, e.Observacoes, e.Ativo, disponivel);
}
