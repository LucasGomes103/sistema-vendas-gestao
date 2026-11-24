using System.Text.Json.Nodes;
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

/// <summary>Conta as chamadas e os tokens de uma resposta do robô (um por escopo).</summary>
public class MedidorIA
{
    public int Chamadas { get; private set; }
    public long TokensEntrada { get; private set; }
    public long TokensSaida { get; private set; }

    /// <summary>A IA falhou e a resposta saiu pelas regras.</summary>
    public bool CaiuParaRegras { get; set; }

    /// <summary>Lê o usageMetadata da resposta do Gemini.</summary>
    public void Registrar(JsonObject? resposta)
    {
        Chamadas++;
        if (resposta?["usageMetadata"] is not JsonObject uso)
            return;
        TokensEntrada += Numero(uso["promptTokenCount"]);
        TokensSaida += Numero(uso["candidatesTokenCount"]) + Numero(uso["thoughtsTokenCount"]);
    }

    private static long Numero(JsonNode? n) => n is JsonValue v && v.TryGetValue<long>(out var x) ? x : 0;
}

/// <summary>Uso da IA de uma loja no mês, para o painel e o administrador.</summary>
public record ResumoUsoIA(int Respostas, int PorRegras, int Chamadas, long Tokens, int Limite, bool Esgotado);

public class ServicoUsoIA(AppDbContext db, IOptions<LimitesIAOptions> opcoes)
{
    public int LimitePadrao => Math.Max(0, opcoes.Value.LimiteRespostasMes);

    public async Task<ResumoUsoIA> ResumoAsync(Guid lojaId, DateTimeOffset agora, CancellationToken ct)
    {
        var loja = await db.Lojas.AsNoTracking().Where(l => l.Id == lojaId).Select(l => new { l.Fuso, l.LimiteRespostasIAMes }).FirstAsync(ct);
        var inicio = RegrasUsoIA.InicioDoMes(RegrasUsoIA.Hoje(agora, loja.Fuso));
        var uso = await db.UsosIA.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.LojaId == lojaId && u.Dia >= inicio)
            .GroupBy(_ => 1)
            .Select(g => new { R = g.Sum(u => u.Respostas), P = g.Sum(u => u.PorRegras), C = g.Sum(u => u.Chamadas), T = g.Sum(u => u.TokensEntrada + u.TokensSaida) })
            .FirstOrDefaultAsync(ct);
        var limite = RegrasUsoIA.LimiteDaLoja(loja.LimiteRespostasIAMes, LimitePadrao);
        var respostas = uso?.R ?? 0;
        return new(respostas, uso?.P ?? 0, uso?.C ?? 0, uso?.T ?? 0, limite, RegrasUsoIA.Esgotado(respostas, limite));
    }

    /// <summary>Respostas da IA de cada loja no mês corrente (fuso de São Paulo), para a tela Lojas.</summary>
    public async Task<Dictionary<Guid, int>> RespostasDoMesAsync(DateTimeOffset agora, CancellationToken ct)
    {
        var inicio = RegrasUsoIA.InicioDoMes(RegrasUsoIA.Hoje(agora, null));
        return await db.UsosIA.IgnoreQueryFilters().AsNoTracking().Where(u => u.Dia >= inicio)
            .GroupBy(u => u.LojaId).Select(g => new { g.Key, N = g.Sum(u => u.Respostas) })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);
    }

    /// <summary>Soma no dia da loja (insert ou update atômico: dois workers não perdem contagem).</summary>
    public Task RegistrarAsync(Guid lojaId, DateOnly dia, int respostas, int porRegras, MedidorIA medidor, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into uso_ia (id, loja_id, criado_em, dia, respostas, chamadas, tokens_entrada, tokens_saida, por_regras)
            values ({Guid.CreateVersion7()}, {lojaId}, {DateTimeOffset.UtcNow}, {dia}, {respostas}, {medidor.Chamadas}, {medidor.TokensEntrada}, {medidor.TokensSaida}, {porRegras})
            on conflict (loja_id, dia) do update set
              respostas = uso_ia.respostas + excluded.respostas,
              chamadas = uso_ia.chamadas + excluded.chamadas,
              tokens_entrada = uso_ia.tokens_entrada + excluded.tokens_entrada,
              tokens_saida = uso_ia.tokens_saida + excluded.tokens_saida,
              por_regras = uso_ia.por_regras + excluded.por_regras
            """, ct);
}

/// <summary>
/// IA da plataforma com medição e limite por loja. Dentro do limite do mês, responde com o Gemini e soma chamadas e
/// tokens no uso da loja; acima do limite (ou com o Gemini fora do ar) o robô segue com as respostas por regras, e o
/// cliente nunca fica sem resposta.
/// </summary>
public class AtendenteComLimite(GeminiAtendente ia, AtendentePorRegras regras, MedidorIA medidor, ServicoUsoIA uso,
    ILojaAtual lojaAtual, AppDbContext db, ILogger<AtendenteComLimite> logger) : IAtendenteIA
{
    public string Nome => ia.Nome;

    public async Task<RespostaAtendente> ResponderAsync(string nomeLoja, IReadOnlyList<FalaConversa> historico, IFerramentasAtendimento ferramentas, CancellationToken ct)
    {
        if (lojaAtual.LojaId is not { } lojaId)
            return await ia.ResponderAsync(nomeLoja, historico, ferramentas, ct);

        var agora = DateTimeOffset.UtcNow;
        var fuso = await db.Lojas.AsNoTracking().Where(l => l.Id == lojaId).Select(l => l.Fuso).FirstOrDefaultAsync(ct);
        var dia = RegrasUsoIA.Hoje(agora, fuso);
        var resumo = await uso.ResumoAsync(lojaId, agora, ct);
        if (resumo.Esgotado)
        {
            logger.LogInformation("Loja {Loja}: limite de {Limite} respostas da IA no mês atingido, respondendo por regras", lojaId, resumo.Limite);
            var porRegras = await regras.ResponderAsync(nomeLoja, historico, ferramentas, ct);
            await Registrar(lojaId, dia, 0, 1, ct);
            return porRegras;
        }

        var resposta = await ia.ResponderAsync(nomeLoja, historico, ferramentas, ct);
        await Registrar(lojaId, dia, medidor.CaiuParaRegras ? 0 : 1, medidor.CaiuParaRegras ? 1 : 0, ct);
        return resposta;
    }

    private async Task Registrar(Guid lojaId, DateOnly dia, int respostas, int porRegras, CancellationToken ct)
    {
        try
        {
            await uso.RegistrarAsync(lojaId, dia, respostas, porRegras, medidor, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A medição nunca derruba a resposta ao cliente.
            logger.LogWarning(e, "Não deu para registrar o uso da IA da loja {Loja}", lojaId);
        }
    }
}
