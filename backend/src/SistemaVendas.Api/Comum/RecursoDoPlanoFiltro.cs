using SistemaVendas.Application.Comum;
using SistemaVendas.Infrastructure.Assinaturas;

namespace SistemaVendas.Api.Comum;

/// <summary>Rotas de um recurso que nem todo plano tem (cartão, entregas): 403 "recurso_do_plano" quando falta.</summary>
public static class RecursoDoPlanoFiltro
{
    public const string Codigo = "recurso_do_plano";

    public static TBuilder ExigeRecurso<TBuilder>(this TBuilder builder, RecursoDoPlano recurso) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (ctx, proximo) =>
        {
            var sp = ctx.HttpContext.RequestServices;
            if (sp.GetRequiredService<ILojaAtual>().LojaId is { } loja
                && !await sp.GetRequiredService<ServicoLimites>().TemRecursoAsync(loja, recurso, ctx.HttpContext.RequestAborted))
                return Results.Problem($"O plano da loja não inclui {ServicoLimites.NomeDoRecurso(recurso)}. Troque de plano em Assinatura.",
                    statusCode: StatusCodes.Status403Forbidden, extensions: new Dictionary<string, object?> { ["codigo"] = Codigo });
            return await proximo(ctx);
        });
}
