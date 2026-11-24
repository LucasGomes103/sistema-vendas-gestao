using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.Seguranca;

namespace SistemaVendas.Infrastructure.Assinaturas;

/// <summary>O que aconteceu com um aviso de cobrança: para o log e para os e-mails.</summary>
public enum ResultadoCobranca
{
    Ignorado,
    Atualizada,
    Paga,
    Recusada,
    Cancelada
}

/// <summary>
/// Mensalidade pelo Mercado Pago da plataforma: assinar, trocar de plano, cancelar e processar os avisos. Os avisos só
/// dizem "olhe a assinatura X": status e valores vêm sempre da API do Mercado Pago com o token da plataforma.
/// </summary>
public class ServicoCobranca(AppDbContext db, ICobrancaMercadoPago mp, CredenciaisDaLoja credenciais, IOptions<AssinaturasOptions> opcoes,
    IOptions<PlataformaOptions> plataforma, ILogger<ServicoCobranca> logger)
{
    public bool Disponivel => mp.Configurado;

    private string UrlRetorno => opcoes.Value.MercadoPago.UrlRetorno is { Length: > 0 } u ? u : plataforma.Value.UrlPainel.TrimEnd('/') + "/assinatura";

    public static string Motivo(Plano plano, string nomeLoja)
    {
        var texto = $"LAG CHAT - Plano {plano.Nome} - {nomeLoja}";
        return texto.Length > 120 ? texto[..120] : texto;
    }

    /// <summary>
    /// Assina (ou troca de plano). Com assinatura já autorizada no Mercado Pago, só muda o valor das próximas
    /// cobranças e o plano vale na hora (devolve null). Senão cria uma assinatura nova e devolve o link para pagar.
    /// </summary>
    public async Task<string?> AssinarAsync(Guid lojaId, PlanoId planoId, string emailPagador, CancellationToken ct)
    {
        if (await credenciais.LojaOriginalAsync(ct) == lojaId)
            throw new RegraException("Esta é a loja original da plataforma: ela não paga mensalidade.");
        var a = await db.Assinaturas.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.LojaId == lojaId, ct);
        if (a is null || a.Status == StatusAssinatura.Isenta)
            throw new RegraException("Esta loja está isenta da mensalidade. Para mudar, fale com o suporte.");
        if (!mp.Configurado)
            throw new RegraException("A cobrança da mensalidade ainda não foi ligada na plataforma. Fale com o suporte.");

        var plano = Planos.De(planoId);
        var nomeLoja = await db.Lojas.Where(l => l.Id == lojaId).Select(l => l.Nome).FirstAsync(ct);
        var agora = DateTimeOffset.UtcNow;

        // Já paga pelo cartão cadastrado: troca o valor das próximas cobranças.
        if (a.MpAssinaturaId is { } atual && await mp.BuscarAsync(atual, ct) is { Status: "authorized" })
        {
            await mp.AlterarValorAsync(atual, Motivo(plano, nomeLoja), plano.Preco, ct);
            logger.LogInformation("Loja {Loja}: plano trocado de {De} para {Para} na assinatura {Mp}", lojaId, a.Plano, planoId, atual);
            a.Plano = planoId;
            a.MpStatus = "authorized";
            a.AtualizadoEm = agora;
            await db.SaveChangesAsync(ct);
            return null;
        }

        // Assinatura anterior que ficou sem pagar (ou cancelada): sai, para não cobrar duas vezes.
        if (a.MpAssinaturaId is { } antiga && a.MpStatus is not "cancelled")
        {
            try
            {
                await mp.CancelarAsync(antiga, ct);
            }
            catch (Exception e) when (e is RegraException or HttpRequestException)
            {
                logger.LogWarning("Não consegui cancelar a assinatura antiga {Mp} da loja {Loja}: {Erro}", antiga, lojaId, e.Message);
            }
        }

        // Ainda no teste grátis (ou com mês pago): a primeira cobrança sai quando ele acaba.
        var pagoAte = new[] { a.Status == StatusAssinatura.Teste ? a.TesteAte : null, a.PagoAte }.Max();
        var inicio = pagoAte > agora.AddHours(1) ? pagoAte : null;
        var nova = await mp.CriarAsync(Motivo(plano, nomeLoja), lojaId, emailPagador, plano.Preco, inicio, UrlRetorno, ct);
        a.MpAssinaturaId = nova.Id;
        a.MpStatus = string.IsNullOrEmpty(nova.Status) ? "pending" : nova.Status;
        a.CobrancasCreditadas = 0;
        a.EmailPagador = emailPagador;
        a.Plano = planoId;
        a.CanceladaEm = null;
        if (a.Status == StatusAssinatura.Cancelada)
            a.Status = StatusAssinatura.Ativa;
        a.AtualizadoEm = agora;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Loja {Loja}: assinatura {Mp} criada no plano {Plano}", lojaId, nova.Id, planoId);
        return nova.UrlPagamento ?? throw new RegraException("O Mercado Pago não devolveu o link de pagamento.");
    }

    /// <summary>Cancela a cobrança. A loja segue funcionando até o fim do que já pagou (ou do teste).</summary>
    public async Task CancelarAsync(Guid lojaId, CancellationToken ct)
    {
        var a = await db.Assinaturas.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.LojaId == lojaId, ct)
            ?? throw new RegraException("Esta loja não tem assinatura.");
        if (a.MpAssinaturaId is { } id && a.MpStatus != "cancelled")
            await mp.CancelarAsync(id, ct);
        a.MpStatus = a.MpAssinaturaId is null ? a.MpStatus : "cancelled";
        if (a.Status is StatusAssinatura.Ativa or StatusAssinatura.Teste)
            a.Status = StatusAssinatura.Cancelada;
        a.CanceladaEm = DateTimeOffset.UtcNow;
        a.AtualizadoEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Lê a assinatura no Mercado Pago e atualiza a da loja (aviso "subscription_preapproval" ou o botão "Já paguei").</summary>
    public async Task<(ResultadoCobranca Resultado, Guid? Loja)> SincronizarAsync(string mpId, CancellationToken ct)
    {
        var mpa = await mp.BuscarAsync(mpId, ct);
        if (mpa?.LojaId is not { } lojaId)
            return (ResultadoCobranca.Ignorado, null);
        var a = await db.Assinaturas.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.LojaId == lojaId, ct);
        // Só a assinatura atual da loja manda: uma antiga (trocada por outra) não mexe em nada.
        if (a is null || a.MpAssinaturaId != mpa.Id)
            return (ResultadoCobranca.Ignorado, lojaId);

        var antes = (a.Status, a.PagoAte);
        a.MpStatus = mpa.Status;
        if (mpa.Status == "cancelled" && a.Status == StatusAssinatura.Ativa)
        {
            a.Status = StatusAssinatura.Cancelada;
            a.CanceladaEm ??= DateTimeOffset.UtcNow;
        }
        var aprovadas = await AprovadasAsync(a.LojaId, mpa.Id, ct);
        RegrasAssinatura.Creditar(a, Math.Max(aprovadas, mpa.CobrancasPagas), DateTimeOffset.UtcNow);
        a.AtualizadoEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var resultado = a.PagoAte != antes.PagoAte ? ResultadoCobranca.Paga
            : a.Status == StatusAssinatura.Cancelada && antes.Status != StatusAssinatura.Cancelada ? ResultadoCobranca.Cancelada
            : ResultadoCobranca.Atualizada;
        logger.LogInformation("Assinatura {Mp} da loja {Loja}: {Status} ({Resultado})", mpa.Id, lojaId, mpa.Status, resultado);
        return (resultado, lojaId);
    }

    /// <summary>Uma cobrança mensal (aviso "subscription_authorized_payment"): guarda e, se aprovada, estende um mês.</summary>
    public async Task<(ResultadoCobranca Resultado, Guid? Loja)> ProcessarCobrancaAsync(string cobrancaId, CancellationToken ct)
    {
        var c = await mp.BuscarCobrancaAsync(cobrancaId, ct);
        if (c?.AssinaturaId is not { } mpAssinatura)
            return (ResultadoCobranca.Ignorado, null);
        var a = await db.Assinaturas.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.MpAssinaturaId == mpAssinatura, ct);
        if (a is null)
        {
            logger.LogWarning("Cobrança {Id} de uma assinatura desconhecida ({Mp}): ignorada", c.Id, mpAssinatura);
            return (ResultadoCobranca.Ignorado, null);
        }

        var status = c.Aprovada ? "approved" : c.StatusPagamento ?? c.Status;
        var registro = await db.PagamentosAssinatura.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.MpId == c.Id, ct);
        var mudou = registro is null || registro.Status != status;
        if (registro is null)
            db.PagamentosAssinatura.Add(new PagamentoAssinatura { LojaId = a.LojaId, MpId = c.Id, MpAssinaturaId = mpAssinatura, Valor = c.Valor, Status = status, AtualizadoEm = DateTimeOffset.UtcNow });
        else
        {
            registro.Status = status;
            registro.Valor = c.Valor;
            registro.AtualizadoEm = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        if (!mudou)
            return (ResultadoCobranca.Ignorado, a.LojaId);
        if (!c.Aprovada)
        {
            logger.LogWarning("Cobrança {Id} da loja {Loja} não aprovada: {Status}", c.Id, a.LojaId, status);
            return (status is "rejected" or "cancelled" or "recycling" ? ResultadoCobranca.Recusada : ResultadoCobranca.Atualizada, a.LojaId);
        }

        var antes = a.PagoAte;
        RegrasAssinatura.Creditar(a, await AprovadasAsync(a.LojaId, mpAssinatura, ct), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Cobrança {Id} da loja {Loja} aprovada: pago até {Ate}", c.Id, a.LojaId, a.PagoAte);
        return (a.PagoAte != antes ? ResultadoCobranca.Paga : ResultadoCobranca.Atualizada, a.LojaId);
    }

    private Task<int> AprovadasAsync(Guid lojaId, string mpAssinatura, CancellationToken ct) =>
        db.PagamentosAssinatura.IgnoreQueryFilters().CountAsync(p => p.LojaId == lojaId && p.MpAssinaturaId == mpAssinatura && p.Status == "approved", ct);
}
