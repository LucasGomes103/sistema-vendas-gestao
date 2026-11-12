using System.Globalization;
using SistemaVendas.Domain.Comum;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Vendas;

namespace SistemaVendas.Domain.Entregas;

public enum StatusEntrega
{
    /// <summary>Pedido pago, falta combinar dia e horário.</summary>
    AAgendar,
    Agendada,
    /// <summary>Só para entrega: o entregador saiu com o peixe.</summary>
    SaiuParaEntrega,
    /// <summary>Entregue ao cliente ou retirado na loja.</summary>
    Entregue
}

/// <summary>Entrega ou retirada de um pedido pago. Nasce junto com a confirmação do pagamento.</summary>
public class Entrega : EntidadeDaLoja
{
    public Guid PedidoId { get; set; }
    public int NumeroPedido { get; set; }
    public Guid ClienteId { get; set; }
    public Guid? ConversaId { get; set; }
    public TipoEntrega Tipo { get; set; }
    public string? Cep { get; set; }
    public string? Endereco { get; set; }

    public StatusEntrega Status { get; set; } = StatusEntrega.AAgendar;
    public DateOnly? Data { get; set; }

    /// <summary>Faixa de horário combinada com o cliente, em texto livre ("14h às 18h").</summary>
    public string? Janela { get; set; }
    public Guid? EntregadorId { get; set; }

    public DateTimeOffset? SaiuEm { get; set; }
    public DateTimeOffset? EntregueEm { get; set; }
    public Guid? EntreguePor { get; set; }
    public string? Observacao { get; set; }

    public bool Aberta => Status != StatusEntrega.Entregue;
}

public static class RegrasEntrega
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static Entrega DoPedido(Pedido pedido) => new()
    {
        LojaId = pedido.LojaId, PedidoId = pedido.Id, NumeroPedido = pedido.Numero, ClienteId = pedido.ClienteId,
        ConversaId = pedido.ConversaId, Tipo = pedido.TipoEntrega, Cep = pedido.Cep, Endereco = pedido.Endereco,
        Observacao = pedido.Observacao
    };

    public static void Agendar(Entrega e, DateOnly data, string? janela, Guid? entregadorId, DateOnly hoje)
    {
        if (e.Status == StatusEntrega.Entregue)
            throw new RegraException("Esta entrega já foi feita.");
        if (data < hoje)
            throw new RegraException("Escolha hoje ou um dia à frente.");
        var j = janela?.Trim();
        if (j is { Length: > 40 })
            throw new RegraException("Horário com no máximo 40 letras.");

        e.Data = data;
        e.Janela = string.IsNullOrEmpty(j) ? null : j;
        e.EntregadorId = e.Tipo == TipoEntrega.Entrega ? entregadorId : null;
        // Remarcar quem já saiu volta para agendada.
        e.Status = StatusEntrega.Agendada;
        e.SaiuEm = null;
    }

    public static void SaiuParaEntrega(Entrega e, DateTimeOffset agora, Guid? usuarioId)
    {
        if (e.Tipo != TipoEntrega.Entrega)
            throw new RegraException("Retirada na loja não sai para entrega.");
        if (e.Status == StatusEntrega.Entregue)
            throw new RegraException("Esta entrega já foi feita.");
        e.Status = StatusEntrega.SaiuParaEntrega;
        e.SaiuEm = agora;
        e.EntregadorId ??= usuarioId;
    }

    public static void MarcarEntregue(Entrega e, DateTimeOffset agora, Guid? usuarioId)
    {
        if (e.Status == StatusEntrega.Entregue)
            throw new RegraException("Esta entrega já foi marcada.");
        e.Status = StatusEntrega.Entregue;
        e.EntregueEm = agora;
        e.EntreguePor = usuarioId;
    }

    public static string MensagemAgendada(Entrega e)
    {
        var dia = e.Data!.Value.ToString("dddd, dd/MM", PtBr);
        var horario = e.Janela is null ? "" : $", {e.Janela}";
        return e.Tipo == TipoEntrega.Entrega
            ? $"📅 A entrega do pedido #{e.NumeroPedido} ficou para {dia}{horario}. Avisamos quando sair daqui!"
            : $"📅 Seu pedido #{e.NumeroPedido} estará separado para retirada na loja em {dia}{horario}.";
    }

    public static string MensagemSaiu(Entrega e) =>
        $"🚚 Seu pedido #{e.NumeroPedido} saiu para entrega! Ao receber, deixe o saquinho boiando no aquário por uns 20 minutos antes de soltar os peixes, para eles se acostumarem com a temperatura.";

    public static string MensagemEntregue(Entrega e) =>
        e.Tipo == TipoEntrega.Entrega
            ? $"✅ Pedido #{e.NumeroPedido} entregue. Obrigado pela compra! Qualquer dúvida sobre os peixes, é só chamar aqui. 🐠"
            : $"✅ Pedido #{e.NumeroPedido} retirado. Obrigado pela compra! Qualquer dúvida sobre os peixes, é só chamar aqui. 🐠";
}
