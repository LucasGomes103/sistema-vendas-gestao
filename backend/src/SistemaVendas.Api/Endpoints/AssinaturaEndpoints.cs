using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Assinaturas;
<<<<<<< HEAD
using SistemaVendas.Infrastructure.IA;
=======
>>>>>>> origin/main

namespace SistemaVendas.Api.Endpoints;

public record PlanoSaida(string Id, string Nome, decimal Preco, int Conversas, int Usuarios, int? Produtos, int RespostasIa,
    bool Cartao, bool Entregas, bool DominioProprio)
{
    public static PlanoSaida De(Plano p) =>
        new(p.Id.ToString(), p.Nome, p.Preco, p.Conversas, p.Usuarios, p.Produtos, p.RespostasIa, p.Cartao, p.Entregas, p.DominioProprio);
}

<<<<<<< HEAD
/// <summary>Uso do mês (conversas, IA) e de agora (usuários e produtos ativos) com os limites do plano (null = sem limite).</summary>
public record UsoDoPlanoSaida(int Conversas, int? LimiteConversas, int Usuarios, int? LimiteUsuarios, int Produtos, int? LimiteProdutos,
    int RespostasIa, int LimiteRespostasIa);

=======
>>>>>>> origin/main
/// <summary>Assinatura da loja como o painel mostra. Plano null = isenta (sem limites de plano).</summary>
public record AssinaturaSaida(
    string Situacao, string Status, PlanoSaida? Plano, DateTimeOffset? TesteAte, DateTimeOffset? PagoAte, DateTimeOffset? CortesiaAte,
    DateTimeOffset? Vencimento, DateTimeOffset? BloqueiaEm, int DiasCarencia, bool LojaOriginal, string? MpStatus,
    IReadOnlyList<PlanoSaida> Planos);

/// <summary>Assinatura da loja com a plataforma: plano, teste grátis, vencimento e situação.</summary>
public static class AssinaturaEndpoints
{
    public static void MapAssinaturaEndpoints(this IEndpointRouteBuilder app)
    {
        // Todos da equipe leem (o painel mostra a faixa de teste, carência ou bloqueio); só o dono muda.
        var grupo = app.MapGroup("/assinatura").WithTags("Assinatura").RequireAuthorization(AuthEndpoints.PoliticaEntregas);

        grupo.MapGet("/", async (ILojaAtual loja, ServicoAssinatura assinaturas, CancellationToken ct) =>
            Saida(await assinaturas.EstadoAsync(loja.LojaId!.Value, ct), assinaturas.DiasCarencia));
<<<<<<< HEAD

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
=======
>>>>>>> origin/main
    }

    public static AssinaturaSaida Saida(EstadoAssinatura e, int diasCarencia) =>
        new(e.Situacao.ToString(), (e.Assinatura?.Status ?? StatusAssinatura.Isenta).ToString(),
            e.Plano is null ? null : PlanoSaida.De(e.Plano),
            e.Assinatura?.TesteAte, e.Assinatura?.PagoAte, e.Assinatura?.CortesiaAte, e.Vencimento, e.BloqueiaEm, diasCarencia,
            e.LojaOriginal, e.Assinatura?.MpStatus, Planos.Todos.Select(PlanoSaida.De).ToList());
}
