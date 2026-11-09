using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.Entregas;

namespace SistemaVendas.Api.Endpoints;

public record FreteKmEntrada(bool Ativo, string CepOrigem, decimal KmInclusos, decimal ValorBase, decimal ValorPorKm, decimal KmMaximo, int Dias);

public record FreteKmSaida(bool Ativo, string? CepOrigem, decimal KmInclusos, decimal ValorBase, decimal ValorPorKm, decimal KmMaximo, int Dias, bool Configurado);

/// <summary>Frete por km a partir da loja. O dono configura; o robô e a equipe consultam.</summary>
public static class FreteEndpoints
{
    public static void MapFreteEndpoints(this IEndpointRouteBuilder app)
    {
        var config = app.MapGroup("/config/frete").WithTags("Frete").RequireAuthorization(AuthEndpoints.PoliticaDono);

        config.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var cfg = await db.FretesPorKm.AsNoTracking().FirstOrDefaultAsync(ct);
            return cfg is null
                ? new FreteKmSaida(false, null, 2, 10, 2, 20, 1, false)
                : Saida(cfg);
        });

        config.MapPut("/", async (FreteKmEntrada entrada, AppDbContext db, IGeolocalizacao geo, CancellationToken ct) =>
        {
            if (RegrasFrete.LerCep(entrada.CepOrigem) is not { } cep)
                return Erro("Informe o CEP da loja com 8 dígitos.");
            if (RegrasFreteKm.Validar(entrada.KmInclusos, entrada.ValorBase, entrada.ValorPorKm, entrada.KmMaximo, entrada.Dias) is { } erro)
                return Erro(erro);

            var cfg = await db.FretesPorKm.FirstOrDefaultAsync(ct);
            if (cfg is null)
            {
                cfg = new FretePorKm();
                db.FretesPorKm.Add(cfg);
            }

            // Só procura a loja no mapa quando o CEP muda.
            if (cfg.CepOrigem != cep || (cfg.OrigemLat == 0 && cfg.OrigemLon == 0))
            {
                if (await geo.LocalizarAsync(cep, ct) is not { } origem)
                    return Erro($"Não encontrei o CEP {RegrasFrete.Formatar(cep)} no mapa. Confira o número ou tente de novo em alguns minutos.");
                cfg.CepOrigem = cep;
                cfg.OrigemLat = origem.Lat;
                cfg.OrigemLon = origem.Lon;
            }

            cfg.Ativo = entrada.Ativo;
            cfg.KmInclusos = entrada.KmInclusos;
            cfg.ValorBase = entrada.ValorBase;
            cfg.ValorPorKm = entrada.ValorPorKm;
            cfg.KmMaximo = entrada.KmMaximo;
            cfg.Dias = entrada.Dias;
            await db.SaveChangesAsync(ct);
            return Results.Ok(Saida(cfg));
        });

        // Consulta para o dono testar a configuração e para o vendedor, na conversa.
        app.MapGet("/frete", async (string cep, ServicoFrete frete, CancellationToken ct) =>
        {
            if (RegrasFrete.LerCep(cep) is not { } numero)
                return Erro("Informe um CEP com 8 dígitos.");
            if (await frete.ConfigAtivaAsync(ct) is null)
                return Erro("O frete por km ainda não está ligado. Configure em Configuração → Frete.");
            if (await frete.CalcularAsync(numero, ct) is not { } r)
                return Erro($"Não encontrei o CEP {RegrasFrete.Formatar(numero)} no mapa.");
            return Results.Ok(new { cep = r.Cep, atende = r.Atende, regiao = r.Regiao, valor = r.Valor, dias = r.Dias, resumo = r.Resumo });
        })
        .WithTags("Frete")
        .RequireAuthorization(AuthEndpoints.PoliticaEquipe);
    }

    private static IResult Erro(string mensagem) =>
        Results.Problem(mensagem, statusCode: StatusCodes.Status422UnprocessableEntity);

    private static FreteKmSaida Saida(FretePorKm c) =>
        new(c.Ativo, c.CepOrigem == 0 ? null : RegrasFrete.Formatar(c.CepOrigem), c.KmInclusos, c.ValorBase, c.ValorPorKm, c.KmMaximo, c.Dias, true);
}
