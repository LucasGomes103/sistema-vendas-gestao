using SistemaVendas.Domain.Comum;

namespace SistemaVendas.Domain.Funil;

/// <summary>Próximo passo automático agendado para o card.</summary>
public enum AcaoFunil
{
    /// <summary>Cliente parou de responder: o card vai para Follow-up (sem mensagem).</summary>
    MoverParaFollowup,
    /// <summary>Lembrete livre, ainda dentro da janela de 24 h (grátis).</summary>
    EnviarFollowup1,
    /// <summary>Segundo lembrete (modelo de marketing na API oficial).</summary>
    EnviarFollowup2,
    /// <summary>Esgotou as regras: venda não realizada, motivo "sem resposta".</summary>
    MarcarPerdido
}

public enum OrigemEvento
{
    Regra,
    Ia,
    Usuario,
    Webhook
}

/// <summary>Histórico de mudanças de etapa. As métricas de conversão saem daqui.</summary>
public class CardEvento : EntidadeDaLoja
{
    public Guid ConversaId { get; set; }
    public EtapaCard? DeEtapa { get; set; }
    public EtapaCard ParaEtapa { get; set; }
    public OrigemEvento Origem { get; set; }
    public Guid? UsuarioId { get; set; }
    public string? Detalhe { get; set; }
}

/// <summary>
/// Prazos e textos do funil, por loja. Mudar "follow-up 2 em 3 dias" para "2 dias" é editar esta
/// configuração, não fazer deploy.
/// </summary>
public class ConfigFunil : EntidadeDaLoja
{
    /// <summary>Sem resposta do cliente por este tempo, o card vai para Follow-up.</summary>
    public int HorasAteFollowup { get; set; } = 2;

    /// <summary>Follow-up 1, contado a partir da última mensagem do cliente (fica dentro da janela de 24 h).</summary>
    public int HorasFollowup1 { get; set; } = 20;

    /// <summary>Follow-up 2, contado a partir do follow-up 1.</summary>
    public int DiasFollowup2 { get; set; } = 3;

    /// <summary>Depois do follow-up 2, sem resposta, o card vira Perdido.</summary>
    public int HorasAtePerdido { get; set; } = 24;

    public string TextoFollowup1 { get; set; } =
        "Oi! Passando para saber se ficou alguma dúvida sobre os peixes. Se quiser, já separo para você 🐟";

    public string TextoFollowup2 { get; set; } =
        "Oi! Chegaram novidades aqui na loja. Quer que eu te mande o catálogo atualizado?";

    /// <summary>Resposta pronta quando o cliente pergunta de frete (o vendedor calcula).</summary>
    public string TextoFrete { get; set; } =
        "Para calcular o frete, me envie o seu CEP, por favor. Um atendente já te responde com o valor e o prazo.";

    public string TextoChamarVendedor { get; set; } =
        "Perfeito! Vou chamar um atendente para finalizar com você. Ele já responde por aqui.";
}
