using Microsoft.EntityFrameworkCore;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Assinaturas;
using SistemaVendas.Infrastructure.IA;

namespace SistemaVendas.Api.Endpoints;

public record PlanoSaida(string Id, string Nome, decimal Preco, int Conversas, int Usuarios, int? Produtos, int RespostasIa,
    bool Cartao, bool Entregas, bool DominioProprio)
{
    public static PlanoSaida De(Plano p) =>
        new(p.Id.ToString(), p.Nome, p.Preco, p.Conversas, p.Usuarios, p.Produtos, p.RespostasIa, p.Cartao, p.Entregas, p.DominioProprio);
}

/// <summary>Uso do mês (conversas, IA) e de agora (usuários e produtos ativos) com os limites do plano (null = sem limite).</summary>
public record UsoDoPlanoSaida(int Conversas, int? LimiteConversas, int Usuarios, int? LimiteUsuarios, int Produtos, int? LimiteProdutos,
    int RespostasIa, int LimiteRespostasIa);

/// <summary>Assinatura da loja como o painel mostra. Plano null = isenta (sem limites de plano).</summary>
public record AssinaturaSaida(
    string Situacao, string Status, PlanoSaida? Plano, DateTimeOffset? TesteAte, DateTimeOffset? PagoAte, DateTimeOffset? CortesiaAte,
    DateTimeOffset? Vencimento, DateTimeOffset? BloqueiaEm, int DiasCarencia, bool LojaOriginal, string? MpStatus,
    IReadOnlyList<PlanoSaida> Planos)
{
    /// <summary>A plataforma já tem a conta do Mercado Pago que cobra a mensalidade.</summary>
    public bool CobrancaDisponivel { get; init; }
    public string? EmailPagador { get; init; }
}

public record AssinarEntrada(string? Plano, string? EmailPagador);

/// <summary>Url: link do Mercado Pago para pagar. Null: o plano já foi trocado na assinatura que estava paga.</summary>
public record AssinarSaida(string? Url);

/// <summary>Assinatura da loja com a plataforma: plano, teste grátis, vencimento e situação.</summary>
public static class AssinaturaEndpoints
{
    public static void MapAssinaturaEndpoints(this IEndpointRouteBuilder app)
    {
        // Todos da equipe leem (o painel mostra a faixa de teste, carência ou bloqueio); só o dono muda.
        var grupo = app.MapGroup("/assinatura").WithTags("Assinatura").RequireAuthorization(AuthEndpoints.PoliticaEntregas);

        grupo.MapGet("/", async (ILojaAtual loja, ServicoAssinatura assinaturas, ServicoCobranca cobranca, CancellationToken ct) =>
            Saida(await assinaturas.EstadoAsync(loja.LojaId!.Value, ct), assinaturas.DiasCarencia) with { CobrancaDisponivel = cobranca.Disponivel });

        // Assinar ou trocar de plano: devolve o link do Mercado Pago (o painel abre na mesma aba).
        grupo.MapPost("/assinar", async (AssinarEntrada e, ILojaAtual loja, ServicoCobranca cobranca, CancellationToken ct) =>
        {
            if (!Enum.TryParse<PlanoId>(e.Plano, true, out var plano))
                return Results.Problem("Escolha um plano.", statusCode: StatusCodes.Status422UnprocessableEntity);
            var email = e.EmailPagador?.Trim().ToLowerInvariant() ?? "";
            if (!RegrasCadastro.EmailValido(email))
                return Results.Problem("Informe o e-mail da conta do Mercado Pago que vai pagar.", statusCode: StatusCodes.Status422UnprocessableEntity);
            try
            {
                return Results.Ok(new AssinarSaida(await cobranca.AssinarAsync(loja.LojaId!.Value, plano, email, ct)));
            }
            catch (Domain.Estoque.RegraException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (HttpRequestException)
            {
                return Results.Problem("O Mercado Pago não respondeu. Tente de novo em alguns minutos.", statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);

        grupo.MapPost("/cancelar", async (ILojaAtual loja, ServicoCobranca cobranca, CancellationToken ct) =>
        {
            try
            {
                await cobranca.CancelarAsync(loja.LojaId!.Value, ct);
                return Results.NoContent();
            }
            catch (Domain.Estoque.RegraException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (HttpRequestException)
            {
                return Results.Problem("O Mercado Pago não respondeu. Tente de novo em alguns minutos.", statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);

        // Voltou do Mercado Pago (ou "Já paguei"): confere a assinatura lá, sem esperar o aviso.
        grupo.MapPost("/conferir", async (ILojaAtual loja, AppDbContext db, ServicoCobranca cobranca, CancellationToken ct) =>
        {
            var mpId = await db.Assinaturas.Where(a => a.LojaId == loja.LojaId).Select(a => a.MpAssinaturaId).FirstOrDefaultAsync(ct);
            if (mpId is null || !cobranca.Disponivel)
                return Results.NoContent();
            try
            {
                await cobranca.SincronizarAsync(mpId, ct);
            }
            catch (HttpRequestException)
            {
                return Results.Problem("O Mercado Pago não respondeu. Tente de novo em alguns minutos.", statusCode: StatusCodes.Status502BadGateway);
            }
            return Results.NoContent();
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);

        grupo.MapGet("/uso", async (ILojaAtual loja, ServicoAssinatura assinaturas, ServicoLimites limites, ServicoLimiteIa limiteIa, CancellationToken ct) =>
        {
            var lojaId = loja.LojaId!.Value;
            var agora = DateTimeOffset.UtcNow;
            var plano = (await assinaturas.EstadoAsync(lojaId, ct)).Plano;
            var ia = await limiteIa.ResumoAsync(lojaId, agora, ct);
            var uso = await limites.UsoAsync(lojaId, ia.Respostas, agora, ct);
            return new UsoDoPlanoSaida(uso.Conversas, plano?.Conversas, uso.Usuarios, plano?.Usuarios, uso.Produtos, plano?.Produtos,
                uso.RespostasIa, ia.Limite);
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);
    }

    public static AssinaturaSaida Saida(EstadoAssinatura e, int diasCarencia) =>
        new(e.Situacao.ToString(), (e.Assinatura?.Status ?? StatusAssinatura.Isenta).ToString(),
            e.Plano is null ? null : PlanoSaida.De(e.Plano),
            e.Assinatura?.TesteAte, e.Assinatura?.PagoAte, e.Assinatura?.CortesiaAte, e.Vencimento, e.BloqueiaEm, diasCarencia,
            e.LojaOriginal, e.Assinatura?.MpStatus, Planos.Todos.Select(PlanoSaida.De).ToList())
        {
            EmailPagador = e.Assinatura?.EmailPagador
        };
}
