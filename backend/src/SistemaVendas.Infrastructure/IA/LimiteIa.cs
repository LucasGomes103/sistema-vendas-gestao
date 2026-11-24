using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Infrastructure.IA;

/// <summary>Limites da IA da plataforma (seção "IA").</summary>
public class LimitesIAOptions
{
    public const string Secao = "IA";

    /// <summary>Respostas da IA por loja por mês, quando a loja não tem limite próprio (IA__LimiteRespostasMes). 0 = sem limite.</summary>
    public int LimiteRespostasMes { get; set; } = 1000;
}

/// <summary>Uso da IA de uma loja no mês. Respostas = respostas do robô que saíram da IA (sem as que caíram para as regras).</summary>
public record ResumoUsoIA(int Respostas, int PorRegras, int Limite, bool Esgotado);

/// <summary>Lê o uso do mês (tabela uso_ia, gravada pelo robô a cada resposta) e o limite de cada loja.</summary>
public class ServicoLimiteIa(AppDbContext db, IOptions<LimitesIAOptions> opcoes, Assinaturas.ServicoAssinatura assinaturas)
{
    public int LimitePadrao => Math.Max(0, opcoes.Value.LimiteRespostasMes);

    public async Task<ResumoUsoIA> ResumoAsync(Guid lojaId, DateTimeOffset agora, CancellationToken ct)
    {
        var limiteLoja = await db.Lojas.AsNoTracking().Where(l => l.Id == lojaId).Select(l => l.LimiteRespostasIAMes).FirstOrDefaultAsync(ct);
        var inicio = RegrasLimiteIa.InicioDoMes(agora);
        var uso = await db.UsosIa.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.LojaId == lojaId && u.Dia >= inicio)
            .GroupBy(_ => 1)
            .Select(g => new { Total = g.Sum(u => u.Respostas), ComIa = g.Sum(u => u.RespostasIa) })
            .FirstOrDefaultAsync(ct);
        var limite = RegrasLimiteIa.LimiteDaLoja(limiteLoja, (await assinaturas.EstadoAsync(lojaId, ct)).Plano, LimitePadrao);
        var respostas = uso?.ComIa ?? 0;
        // O resto saiu pelas regras (IA fora do ar ou limite atingido).
        return new(respostas, (uso?.Total ?? 0) - respostas, limite, RegrasLimiteIa.Esgotado(respostas, limite));
    }

    /// <summary>Limite de cada loja (para a tela Lojas): próprio, do plano ou o padrão. Loja original: sem plano.</summary>
    public int LimitePara(int? limiteLoja, Assinatura? assinatura, bool lojaOriginal) =>
        RegrasLimiteIa.LimiteDaLoja(limiteLoja, lojaOriginal ? null : RegrasAssinatura.PlanoComLimites(assinatura), LimitePadrao);

    /// <summary>Respostas com IA no mês por loja (para a tela Lojas).</summary>
    public async Task<Dictionary<Guid, int>> RespostasDoMesAsync(DateTimeOffset agora, CancellationToken ct)
    {
        var inicio = RegrasLimiteIa.InicioDoMes(agora);
        return await db.UsosIa.IgnoreQueryFilters().AsNoTracking().Where(u => u.Dia >= inicio)
            .GroupBy(u => u.LojaId).Select(g => new { g.Key, N = g.Sum(u => u.RespostasIa) })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
    }
}

/// <summary>
/// IA da plataforma com limite por loja. Dentro do limite do mês responde com o Gemini; acima dele o robô segue com as
/// respostas por regras (sem chamar o Gemini) até virar o mês, e o cliente nunca fica sem resposta. O gasto de cada
/// resposta continua sendo gravado pelo robô em uso_ia.
/// </summary>
public class AtendenteComLimite(GeminiAtendente ia, AtendentePorRegras regras, ServicoLimiteIa limite, ILojaAtual lojaAtual,
    ILogger<AtendenteComLimite> logger) : IAtendenteIA
{
    public string Nome => ia.Nome;

    public async Task<RespostaAtendente> ResponderAsync(string nomeLoja, IReadOnlyList<FalaConversa> historico, IFerramentasAtendimento ferramentas, CancellationToken ct)
    {
        if (lojaAtual.LojaId is { } lojaId && await limite.ResumoAsync(lojaId, DateTimeOffset.UtcNow, ct) is { Esgotado: true } r)
        {
            logger.LogInformation("Loja {Loja}: limite de {Limite} respostas da IA no mês atingido, respondendo por regras", lojaId, r.Limite);
            return await regras.ResponderAsync(nomeLoja, historico, ferramentas, ct);
        }
        return await ia.ResponderAsync(nomeLoja, historico, ferramentas, ct);
    }
}
