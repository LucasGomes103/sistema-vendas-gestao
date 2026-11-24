using SistemaVendas.Domain.Comum;

namespace SistemaVendas.Domain.Lojas;

public enum PlanoId
{
    Essencial,
    Profissional,
    Completo
}

/// <summary>
/// Um plano da plataforma (proposta comercial aprovada pelo Lucas em 19/11/2026). Limites por mês:
/// "conversa" = cada cliente diferente que escreveu para a loja no mês. Produtos null = sem limite.
/// </summary>
public record Plano(
    PlanoId Id, string Nome, decimal Preco, int Conversas, int Usuarios, int? Produtos, int RespostasIa,
    bool Cartao, bool Entregas, bool DominioProprio);

/// <summary>Catálogo dos planos. Preço e limites ficam no código: mudar é um PR, que fica registrado.</summary>
public static class Planos
{
    public static readonly Plano Essencial = new(PlanoId.Essencial, "Essencial", 297m, 500, 2, 300, 2_500, Cartao: false, Entregas: false, DominioProprio: false);
    public static readonly Plano Profissional = new(PlanoId.Profissional, "Profissional", 497m, 1_500, 5, 2_000, 7_500, Cartao: true, Entregas: true, DominioProprio: false);
    public static readonly Plano Completo = new(PlanoId.Completo, "Completo", 797m, 4_000, 15, null, 20_000, Cartao: true, Entregas: true, DominioProprio: true);

    public static readonly IReadOnlyList<Plano> Todos = [Essencial, Profissional, Completo];

    /// <summary>Plano do teste grátis quando o cadastro não escolhe um.</summary>
    public static readonly PlanoId PadraoDoTeste = PlanoId.Profissional;

    public static Plano De(PlanoId id) => Todos.First(p => p.Id == id);
}

/// <summary>O que foi combinado com a loja. A situação do dia (em dia, carência, bloqueada) é calculada.</summary>
public enum StatusAssinatura
{
    /// <summary>Teste grátis de 7 dias, até TesteAte.</summary>
    Teste,
    /// <summary>Pagando pelo Mercado Pago; pago até PagoAte.</summary>
    Ativa,
    /// <summary>Cancelou a cobrança: usa até PagoAte (ou o fim do teste) e depois bloqueia.</summary>
    Cancelada,
    /// <summary>Cortesia dada pelo administrador, até CortesiaAte (vazio = sem fim).</summary>
    Cortesia,
    /// <summary>Não paga e não tem limite de plano (a loja original, ECS Fish, e as lojas de antes da cobrança).</summary>
    Isenta
}

/// <summary>Situação de hoje, calculada a partir da assinatura e da data.</summary>
public enum SituacaoAssinatura
{
    Isenta,
    Cortesia,
    Teste,
    EmDia,
    /// <summary>Venceu e ainda está nos dias de carência: tudo funciona, com aviso.</summary>
    Carencia,
    /// <summary>Passou a carência: o robô para e o painel fica só leitura até pagar.</summary>
    Bloqueada
}

/// <summary>Assinatura da loja com a plataforma (uma por loja).</summary>
public class Assinatura : EntidadeDaLoja
{
    public PlanoId Plano { get; set; } = Planos.PadraoDoTeste;
    public StatusAssinatura Status { get; set; } = StatusAssinatura.Teste;

    public DateTimeOffset? TesteAte { get; set; }
    public DateTimeOffset? PagoAte { get; set; }
    public DateTimeOffset? CortesiaAte { get; set; }
    public string? MotivoCortesia { get; set; }

    /// <summary>Assinatura recorrente (preapproval) no Mercado Pago da plataforma.</summary>
    public string? MpAssinaturaId { get; set; }

    /// <summary>Status do preapproval no Mercado Pago (pending, authorized, paused, cancelled).</summary>
    public string? MpStatus { get; set; }

    /// <summary>Cobranças aprovadas já creditadas da assinatura do Mercado Pago atual (cada uma vale um mês).</summary>
    public int CobrancasCreditadas { get; set; }

    /// <summary>E-mail da conta do Mercado Pago que paga.</summary>
    public string? EmailPagador { get; set; }

    public DateTimeOffset? CanceladaEm { get; set; }
    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Uma cobrança da mensalidade (aviso do Mercado Pago). Histórico e trava contra aviso repetido.</summary>
public class PagamentoAssinatura : EntidadeDaLoja
{
    /// <summary>Id da cobrança (authorized_payment) no Mercado Pago.</summary>
    public required string MpId { get; set; }
    public string? MpAssinaturaId { get; set; }
    public decimal Valor { get; set; }

    /// <summary>approved, rejected, pending... (status do pagamento dentro da cobrança).</summary>
    public required string Status { get; set; }
    public DateTimeOffset? AtualizadoEm { get; set; }
}

public static class RegrasAssinatura
{
    public const int DiasTeste = 7;

    /// <summary>Dias depois do vencimento em que tudo segue funcionando (o Mercado Pago tenta cobrar de novo por 10 dias).</summary>
    public const int DiasCarenciaPadrao = 7;

    /// <summary>Data até quando a loja está paga, em teste ou em cortesia. Null = sem vencimento.</summary>
    public static DateTimeOffset? Vencimento(Assinatura a) => a.Status switch
    {
        StatusAssinatura.Isenta => null,
        StatusAssinatura.Cortesia => a.CortesiaAte,
        StatusAssinatura.Teste => a.TesteAte,
        _ => Max(a.PagoAte, a.TesteAte)
    };

    public static SituacaoAssinatura Situacao(Assinatura? a, DateTimeOffset agora, int diasCarencia = DiasCarenciaPadrao)
    {
        if (a is null || a.Status == StatusAssinatura.Isenta)
            return SituacaoAssinatura.Isenta;
        var vence = Vencimento(a);
        if (vence is null || vence > agora)
            return a.Status switch
            {
                StatusAssinatura.Cortesia => SituacaoAssinatura.Cortesia,
                StatusAssinatura.Teste => SituacaoAssinatura.Teste,
                _ => SituacaoAssinatura.EmDia
            };
        return agora < vence.Value.AddDays(diasCarencia) ? SituacaoAssinatura.Carencia : SituacaoAssinatura.Bloqueada;
    }

    /// <summary>Último instante da carência (depois dele, bloqueia). Null quando não vence.</summary>
    public static DateTimeOffset? BloqueiaEm(Assinatura? a, int diasCarencia = DiasCarenciaPadrao) =>
        a is null ? null : Vencimento(a)?.AddDays(diasCarencia);

    /// <summary>Plano cujos limites valem para a loja. Null = isenta (sem limites de plano).</summary>
    public static Plano? PlanoComLimites(Assinatura? a) =>
        a is null || a.Status == StatusAssinatura.Isenta ? null : Planos.De(a.Plano);

    /// <summary>Assinatura do teste grátis de uma loja nova.</summary>
    public static Assinatura NovoTeste(Guid lojaId, PlanoId? plano, DateTimeOffset agora) => new()
    {
        LojaId = lojaId,
        Plano = plano ?? Planos.PadraoDoTeste,
        Status = StatusAssinatura.Teste,
        TesteAte = agora.AddDays(DiasTeste),
        AtualizadoEm = agora
    };

    /// <summary>
    /// Cobranças aprovadas chegaram: cada uma nova estende um mês, a partir do fim do período pago (ou do teste).
    /// Se a loja ficou muito tempo sem pagar, o mês novo conta a partir de hoje.
    /// </summary>
    public static void Creditar(Assinatura a, int cobrancasAprovadas, DateTimeOffset agora)
    {
        var novas = cobrancasAprovadas - a.CobrancasCreditadas;
        if (novas <= 0)
            return;
        var inicio = Max(a.PagoAte, a.TesteAte) ?? agora;
        if (inicio < agora.AddDays(-15))
            inicio = agora;
        a.PagoAte = inicio.AddMonths(novas);
        a.CobrancasCreditadas = cobrancasAprovadas;
        if (a.Status == StatusAssinatura.Teste || (a.Status == StatusAssinatura.Cancelada && a.MpStatus != "cancelled"))
            a.Status = StatusAssinatura.Ativa;
        a.AtualizadoEm = agora;
    }

    private static DateTimeOffset? Max(DateTimeOffset? x, DateTimeOffset? y) =>
        x is null ? y : y is null ? x : x > y ? x : y;
}

/// <summary>E-mail de cobrança ou de limite já mandado para a loja (a chave evita mandar o mesmo aviso duas vezes).</summary>
public class AvisoAssinatura : EntidadeDaLoja
{
    /// <summary>Ex.: "teste_acaba:20261201", "conversas80:202611", "pago:9001".</summary>
    public required string Chave { get; set; }
    public string? Assunto { get; set; }
    public bool Enviado { get; set; }
}
