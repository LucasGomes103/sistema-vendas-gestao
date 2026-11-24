using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.Email;
using SistemaVendas.Infrastructure.IA;
using SistemaVendas.Infrastructure.Seguranca;

namespace SistemaVendas.Infrastructure.Assinaturas;

/// <summary>
/// E-mails de cobrança e de limite para o dono da loja. Cada aviso tem uma chave e sai uma vez só (tabela
/// aviso_assinatura), mesmo que o worker rode de novo ou o Mercado Pago repita o aviso.
/// </summary>
public class AvisosAssinatura(AppDbContext db, IEnvioEmail email, ServicoAssinatura assinaturas, ServicoLimites limites,
    ServicoLimiteIa limiteIa, IOptions<PlataformaOptions> plataforma, ILogger<AvisosAssinatura> logger)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private string LinkAssinatura => plataforma.Value.UrlPainel.TrimEnd('/') + "/assinatura";

    private static string Data(DateTimeOffset? d) => d is { } x ? TimeZoneInfo.ConvertTime(x, Brasilia).ToString("dd/MM/yyyy", PtBr) : "-";

    /// <summary>Manda o aviso para os donos ativos da loja, se a chave ainda não foi usada. Devolve true se mandou agora.</summary>
    public async Task<bool> AvisarAsync(Guid lojaId, string chave, string assunto, string texto, CancellationToken ct)
    {
        if (await db.AvisosAssinatura.IgnoreQueryFilters().AnyAsync(a => a.LojaId == lojaId && a.Chave == chave, ct))
            return false;
        var aviso = new AvisoAssinatura { LojaId = lojaId, Chave = chave, Assunto = assunto };
        db.AvisosAssinatura.Add(aviso);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Outro processo mandou o mesmo aviso ao mesmo tempo: o índice único segura.
            db.Entry(aviso).State = EntityState.Detached;
            return false;
        }

        var loja = await db.Lojas.AsNoTracking().Where(l => l.Id == lojaId).Select(l => l.Nome).FirstAsync(ct);
        var donos = await db.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.LojaId == lojaId && u.Papel == Papel.Dono && u.Ativo).Select(u => u.Email).ToListAsync(ct);
        var corpo = $"Olá!\n\n{texto}\n\nVeja e resolva em: {LinkAssinatura}\n\nLoja: {loja}\nLAG CHAT";
        var enviado = false;
        foreach (var para in donos)
            enviado |= await email.EnviarAsync(para, $"{assunto} - {loja}", corpo, ct);
        aviso.Enviado = enviado;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Aviso {Chave} para a loja {Loja}: {Enviado}", chave, lojaId, enviado ? "enviado" : "não enviado (e-mail desligado)");
        return true;
    }

    /// <summary>E-mail logo depois de um aviso do Mercado Pago (pagamento confirmado, recusado ou cancelamento).</summary>
    public async Task AoCobrarAsync(Guid lojaId, ResultadoCobranca resultado, string idMp, CancellationToken ct)
    {
        var a = await db.Assinaturas.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.LojaId == lojaId, ct);
        if (a is null)
            return;
        var plano = Planos.De(a.Plano);
        switch (resultado)
        {
            case ResultadoCobranca.Paga:
                await AvisarAsync(lojaId, $"pago:{idMp}:{a.PagoAte:yyyyMMdd}", "Pagamento confirmado",
                    $"Recebemos a mensalidade do plano {plano.Nome}. A loja está paga até {Data(a.PagoAte)}. Obrigado!", ct);
                break;
            case ResultadoCobranca.Recusada:
                await AvisarAsync(lojaId, $"recusado:{idMp}", "Pagamento da mensalidade recusado",
                    $"O cartão recusou a mensalidade do plano {plano.Nome}. O Mercado Pago tenta de novo nos próximos dias. " +
                    "Se precisar, troque o cartão na sua conta do Mercado Pago ou assine de novo pela tela Assinatura.", ct);
                break;
            case ResultadoCobranca.Cancelada:
                await AvisarAsync(lojaId, $"cancelada:{idMp}", "Assinatura cancelada",
                    $"A assinatura do plano {plano.Nome} foi cancelada no Mercado Pago. A loja funciona até {Data(RegrasAssinatura.Vencimento(a))}; " +
                    "depois o robô para e o painel fica só para consulta. Para continuar, assine de novo pela tela Assinatura.", ct);
                break;
        }
    }

    /// <summary>Confere todas as lojas que pagam: fim do teste, vencimento, bloqueio e uso perto do limite.</summary>
    public async Task<int> VerificarTodasAsync(DateTimeOffset agora, CancellationToken ct)
    {
        var ids = await db.Assinaturas.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Status != StatusAssinatura.Isenta).Select(a => a.LojaId).ToListAsync(ct);
        var avisos = 0;
        foreach (var lojaId in ids)
        {
            try
            {
                avisos += await VerificarAsync(lojaId, agora, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning("Falha ao conferir avisos da loja {Loja}: {Erro}", lojaId, e.Message);
            }
        }
        return avisos;
    }

    public async Task<int> VerificarAsync(Guid lojaId, DateTimeOffset agora, CancellationToken ct)
    {
        var estado = await assinaturas.EstadoAsync(lojaId, ct, agora);
        if (estado is not { Assinatura: { } a, Plano: { } plano } || estado.Situacao == SituacaoAssinatura.Isenta)
            return 0;
        var n = 0;
        var vence = estado.Vencimento;

        switch (estado.Situacao)
        {
            case SituacaoAssinatura.Teste when vence is { } fim && fim - agora <= TimeSpan.FromDays(2) && a.MpStatus != "authorized":
                n += await AvisarAsync(lojaId, $"teste_acaba:{fim:yyyyMMdd}", "Seu teste grátis está acabando",
                    $"O teste grátis do plano {plano.Nome} acaba em {Data(fim)}. Para o robô não parar, assine pela tela Assinatura " +
                    $"(R$ {plano.Preco.ToString("N2", PtBr)} por mês no cartão, pelo Mercado Pago).", ct) ? 1 : 0;
                break;
            case SituacaoAssinatura.Carencia:
                n += await AvisarAsync(lojaId, $"carencia:{vence:yyyyMMdd}", "Mensalidade em aberto",
                    $"A mensalidade venceu em {Data(vence)}. Tudo continua funcionando até {Data(estado.BloqueiaEm)}; depois disso o robô " +
                    "para e o painel fica só para consulta até o pagamento.", ct) ? 1 : 0;
                break;
            case SituacaoAssinatura.Bloqueada when vence > agora.AddDays(-60):
                n += await AvisarAsync(lojaId, $"bloqueada:{vence:yyyyMMdd}", "Loja bloqueada por falta de pagamento",
                    "O robô parou de responder e o painel está só para consulta. As mensagens dos clientes continuam chegando. " +
                    "Assim que a mensalidade for paga, tudo volta na hora.", ct) ? 1 : 0;
                break;
        }

        if (estado.Situacao == SituacaoAssinatura.Bloqueada)
            return n;

        // Uso do mês perto do limite (80%) e no limite.
        var mes = ServicoLimites.InicioDoMes(agora).ToOffset(TimeSpan.FromHours(-3)).ToString("yyyyMM", CultureInfo.InvariantCulture);
        var conversas = await limites.ConversasDoMesAsync(lojaId, agora, ct);
        n += await AvisarLimiteAsync(lojaId, "conversas", mes, conversas, plano.Conversas,
            "as conversas do mês", "cliente novo vai direto para a equipe (o robô não responde) até virar o mês", ct);
        var ia = await limiteIa.ResumoAsync(lojaId, agora, ct);
        if (ia.Limite > 0)
            n += await AvisarLimiteAsync(lojaId, "ia", mes, ia.Respostas, ia.Limite,
                "as respostas com IA do mês", "o robô continua com as respostas prontas, mais simples, até virar o mês", ct);
        return n;
    }

    private async Task<int> AvisarLimiteAsync(Guid lojaId, string tipo, string mes, int usado, int limite, string oque, string depois, CancellationToken ct)
    {
        if (limite <= 0 || usado * 100 < limite * 80)
            return 0;
        var cheio = usado >= limite;
        var chave = $"{tipo}{(cheio ? 100 : 80)}:{mes}";
        var texto = cheio
            ? $"A loja usou todas {oque} ({usado.ToString("N0", PtBr)} de {limite.ToString("N0", PtBr)}). Agora {depois}. Para mais, troque de plano pela tela Assinatura."
            : $"A loja já usou {usado.ToString("N0", PtBr)} de {limite.ToString("N0", PtBr)} ({usado * 100 / limite}%) d{oque[1..]}. Quando acabar, {depois}.";
        return await AvisarAsync(lojaId, chave, cheio ? "Limite do plano atingido" : "Uso perto do limite do plano", texto, ct) ? 1 : 0;
    }
}
