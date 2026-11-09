using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Endpoints;

public record ZonaEntregaEntrada(string Nome, string CepInicio, string CepFim, decimal Valor, int Dias);

public record ZonaEntregaSaida(Guid Id, string Nome, string CepInicio, string CepFim, decimal Valor, int Dias);

/// <summary>Tabela de frete por faixa de CEP. O dono cadastra; o robô e a equipe consultam.</summary>
public static class FreteEndpoints
{
    public static void MapFreteEndpoints(this IEndpointRouteBuilder app)
    {
        var zonas = app.MapGroup("/config/frete").WithTags("Frete").RequireAuthorization(AuthEndpoints.PoliticaDono);

        zonas.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            (await db.ZonasEntrega.AsNoTracking().OrderBy(z => z.CepInicio).ToListAsync(ct)).Select(Saida));

        zonas.MapPost("/", async (ZonaEntregaEntrada entrada, AppDbContext db, CancellationToken ct) =>
        {
            var zona = new ZonaEntrega { Nome = "" };
            if (Aplicar(zona, entrada) is { } erro)
                return Results.Problem(erro, statusCode: StatusCodes.Status422UnprocessableEntity);
            db.ZonasEntrega.Add(zona);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/config/frete/{zona.Id}", Saida(zona));
        });

        zonas.MapPut("/{id:guid}", async (Guid id, ZonaEntregaEntrada entrada, AppDbContext db, CancellationToken ct) =>
        {
            var zona = await db.ZonasEntrega.FirstOrDefaultAsync(z => z.Id == id, ct);
            if (zona is null)
                return Results.NotFound();
            if (Aplicar(zona, entrada) is { } erro)
                return Results.Problem(erro, statusCode: StatusCodes.Status422UnprocessableEntity);
            await db.SaveChangesAsync(ct);
            return Results.Ok(Saida(zona));
        });

        zonas.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var zona = await db.ZonasEntrega.FirstOrDefaultAsync(z => z.Id == id, ct);
            if (zona is null)
                return Results.NotFound();
            db.ZonasEntrega.Remove(zona);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Consulta rápida para o vendedor, na conversa ou no card.
        app.MapGet("/frete", async (string cep, AppDbContext db, CancellationToken ct) =>
        {
            if (RegrasFrete.LerCep(cep) is not { } numero)
                return Results.Problem("Informe um CEP com 8 dígitos.", statusCode: StatusCodes.Status422UnprocessableEntity);
            var zona = RegrasFrete.Calcular(numero, await db.ZonasEntrega.AsNoTracking().ToListAsync(ct));
            return Results.Ok(new
            {
                cep = RegrasFrete.Formatar(numero),
                atende = zona is not null,
                regiao = zona?.Nome,
                valor = zona?.Valor,
                dias = zona?.Dias
            });
        })
        .WithTags("Frete")
        .RequireAuthorization(AuthEndpoints.PoliticaEquipe);
    }

    private static string? Aplicar(ZonaEntrega zona, ZonaEntregaEntrada entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada.Nome))
            return "Dê um nome para a região (ex.: Guarulhos).";
        var inicio = RegrasFrete.LerCep(entrada.CepInicio);
        var fim = RegrasFrete.LerCep(entrada.CepFim);
        if (RegrasFrete.Validar(inicio, fim, entrada.Valor, entrada.Dias) is { } erro)
            return erro;

        zona.Nome = entrada.Nome.Trim();
        zona.CepInicio = inicio!.Value;
        zona.CepFim = fim!.Value;
        zona.Valor = entrada.Valor;
        zona.Dias = entrada.Dias;
        return null;
    }

    private static ZonaEntregaSaida Saida(ZonaEntrega z) =>
        new(z.Id, z.Nome, RegrasFrete.Formatar(z.CepInicio), RegrasFrete.Formatar(z.CepFim), z.Valor, z.Dias);
}
