using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Api.Comum;
using SistemaVendas.Application.Catalogo;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Catalogo;
using SistemaVendas.Domain.Estoque;
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

            ((LojaAtualDaRequisicao)lojaAtual).Definir(loja.Id);

            var saldos = await db.Movimentos.AsNoTracking()
                .Join(db.Lotes.Where(l => l.Status == StatusLote.Liberado), m => m.LoteId, l => l.Id, (m, l) => new { l.EspecieId, m.Tipo, m.Quantidade })
                .GroupBy(x => x.EspecieId)
                .Select(g => new
                {
                    EspecieId = g.Key,
                    Disponivel = g.Sum(x =>
                        x.Tipo == TipoMovimento.Entrada || x.Tipo == TipoMovimento.LiberaReserva || x.Tipo == TipoMovimento.Ajuste || x.Tipo == TipoMovimento.Transferencia
                            ? x.Quantidade
                            : x.Tipo == TipoMovimento.BaixaMorte || x.Tipo == TipoMovimento.Reserva || x.Tipo == TipoMovimento.Venda
                                ? -x.Quantidade
                                : 0)
                })
                .Where(x => x.Disponivel > 0)
                .ToDictionaryAsync(x => x.EspecieId, x => x.Disponivel, ct);

            var especies = await db.Especies.AsNoTracking()
                .Where(e => e.Ativo && saldos.Keys.Contains(e.Id))
                .OrderBy(e => e.NomePopular)
                .Select(e => new { e.Id, e.NomePopular, e.NomeCientifico, e.TamanhoCm, e.Preco, FotoUrl = e.Fotos.OrderBy(f => f.Ordem).Select(f => f.Url).FirstOrDefault() })
                .ToListAsync(ct);

            var itens = especies.Select(e => new ItemCatalogoPublico(e.Id, e.NomePopular, e.NomeCientifico, e.TamanhoCm, e.Preco, saldos[e.Id], e.FotoUrl));
            return Results.Ok(new { loja = loja.Nome, itens });
        })
        .WithTags("Catálogo público");
    }

    public static void MapEspecieEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/especies").WithTags("Espécies");

        grupo.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            await db.Especies.AsNoTracking()
                .OrderBy(e => e.NomePopular)
                .Select(e => new EspecieSaida(e.Id, e.NomePopular, e.NomeCientifico, e.TamanhoCm, e.Preco, e.Observacoes, e.Ativo))
                .ToListAsync(ct));

        grupo.MapPost("/", async (EspecieEntrada entrada, IValidator<EspecieEntrada> validator, AppDbContext db, CancellationToken ct) =>
        {
            var validacao = await validator.ValidateAsync(entrada, ct);
            if (!validacao.IsValid)
                return Results.ValidationProblem(validacao.ToDictionary());

            var especie = new Especie
            {
                NomePopular = entrada.NomePopular.Trim(),
                NomeCientifico = entrada.NomeCientifico?.Trim(),
                TamanhoCm = entrada.TamanhoCm,
                Preco = entrada.Preco,
                Observacoes = entrada.Observacoes
            };
            db.Especies.Add(especie);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/especies/{especie.Id}",
                new EspecieSaida(especie.Id, especie.NomePopular, especie.NomeCientifico, especie.TamanhoCm, especie.Preco, especie.Observacoes, especie.Ativo));
        });

        grupo.MapPut("/{id:guid}", async (Guid id, EspecieEntrada entrada, IValidator<EspecieEntrada> validator, AppDbContext db, CancellationToken ct) =>
        {
            var validacao = await validator.ValidateAsync(entrada, ct);
            if (!validacao.IsValid)
                return Results.ValidationProblem(validacao.ToDictionary());

            var especie = await db.Especies.FindAsync([id], ct);
            if (especie is null)
                return Results.NotFound();

            especie.NomePopular = entrada.NomePopular.Trim();
            especie.NomeCientifico = entrada.NomeCientifico?.Trim();
            especie.TamanhoCm = entrada.TamanhoCm;
            especie.Preco = entrada.Preco;
            especie.Observacoes = entrada.Observacoes;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    public static void MapTanqueEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/tanques").WithTags("Estoque");

        grupo.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            await db.Tanques.AsNoTracking()
                .Where(t => t.Ativo)
                .OrderBy(t => t.Nome)
                .Select(t => new { t.Id, t.Nome, Tipo = t.Tipo.ToString(), t.Litros })
                .ToListAsync(ct));
    }
}
