using System.Globalization;
using System.Text.RegularExpressions;
using SistemaVendas.Domain.Comum;
using SistemaVendas.Domain.Estoque;

namespace SistemaVendas.Domain.Vendas;

public enum StatusPedido
{
    /// <summary>Pedido montado e Pix enviado (ou a enviar); o peixe segue reservado.</summary>
    AguardandoPagamento,
    /// <summary>Cliente disse que pagou ou mandou comprovante: o vendedor confere na conta.</summary>
    ConferirPagamento,
    Pago,
    Cancelado
}

public enum TipoEntrega
{
    Entrega,
    Retirada
}

public enum FormaPagamento
{
    Pix,
    Dinheiro,
    Cartao
}

/// <summary>
/// Pedido nascido de uma reserva de 24 h. O peixe continua reservado até o pagamento: pago, a reserva
/// vira venda (sai do estoque); cancelado ou vencido, o peixe volta.
/// </summary>
public class Pedido : EntidadeDaLoja
{
    /// <summary>Número curto por loja (#1, #2...), usado com o cliente e como identificador do Pix.</summary>
    public int Numero { get; set; }
    public Guid ClienteId { get; set; }
    public Guid? ConversaId { get; set; }
    public Guid? ReservaId { get; set; }
    public StatusPedido Status { get; set; } = StatusPedido.AguardandoPagamento;

    public TipoEntrega TipoEntrega { get; set; }
    public string? Cep { get; set; }
    public string? Endereco { get; set; }

    public decimal Subtotal { get; set; }
    public decimal Frete { get; set; }
    public decimal Total { get; set; }

    /// <summary>Até quando o peixe fica separado (vencimento da reserva).</summary>
    public DateTimeOffset? ExpiraEm { get; set; }

    public string? PixCopiaECola { get; set; }
    public DateTimeOffset? PixEnviadoEm { get; set; }
    public DateTimeOffset? ComprovanteEm { get; set; }

    /// <summary>Link do Mercado Pago (Checkout Pro) para o cliente pagar no cartão; vence junto com a reserva.</summary>
    public string? LinkCartao { get; set; }
    /// <summary>Id da order no Mercado Pago (o link acima).</summary>
    public string? MpOrderId { get; set; }
    /// <summary>Id do pagamento aprovado no Mercado Pago: quando preenchido, o pagamento foi confirmado sozinho.</summary>
    public string? MpPagamentoId { get; set; }

    public FormaPagamento? FormaPagamento { get; set; }
    public DateTimeOffset? PagoEm { get; set; }
    public Guid? ConfirmadoPor { get; set; }

    public DateTimeOffset? CanceladoEm { get; set; }
    public string? MotivoCancelamento { get; set; }

    public Guid? CriadoPor { get; set; }
    /// <summary>Vendedor que fica com a venda: o responsável pela conversa quando o pedido foi feito (ou quem o criou).</summary>
    public Guid? VendedorId { get; set; }
    public string? Observacao { get; set; }

    public List<ItemDoPedido> Itens { get; set; } = [];

    public bool Aberto => Status is StatusPedido.AguardandoPagamento or StatusPedido.ConferirPagamento;
}

public class ItemDoPedido : EntidadeDaLoja
{
    public Guid PedidoId { get; set; }
    public Guid EspecieId { get; set; }
    public required string Especie { get; set; }
    public int Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
}

public record DadosEntrega(TipoEntrega Tipo, decimal Frete, string? Cep, string? Endereco, string? Observacao);

public static partial class RegrasPedido
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Monta o pedido a partir da reserva ativa, com os itens somados por espécie.</summary>
    public static Pedido CriarDaReserva(Reserva reserva, Guid clienteId, int numero, DadosEntrega entrega, Guid? usuarioId,
        Guid? vendedorId = null)
    {
        if (reserva.Status != StatusReserva.Ativa)
            throw new RegraException("A reserva já foi encerrada; separe os itens de novo para montar o pedido.");
        if (entrega.Frete < 0)
            throw new RegraException("O frete não pode ser negativo.");

        var cep = entrega.Cep is { Length: > 0 } c ? new string(c.Where(char.IsDigit).ToArray()) : null;
        if (cep is { Length: not 8 })
            throw new RegraException("CEP deve ter 8 dígitos.");
        var endereco = string.IsNullOrWhiteSpace(entrega.Endereco) ? null : entrega.Endereco.Trim();
        if (entrega.Tipo == TipoEntrega.Entrega && endereco is null)
            throw new RegraException("Informe o endereço de entrega.");

        var pedido = new Pedido
        {
            LojaId = reserva.LojaId,
            Numero = numero,
            ClienteId = clienteId,
            ConversaId = reserva.ConversaId,
            ReservaId = reserva.Id,
            TipoEntrega = entrega.Tipo,
            Cep = entrega.Tipo == TipoEntrega.Entrega ? cep : null,
            Endereco = entrega.Tipo == TipoEntrega.Entrega ? endereco : null,
            Frete = entrega.Tipo == TipoEntrega.Entrega ? entrega.Frete : 0,
            ExpiraEm = reserva.ExpiraEm,
            CriadoPor = usuarioId,
            VendedorId = vendedorId ?? usuarioId,
            Observacao = string.IsNullOrWhiteSpace(entrega.Observacao) ? null : entrega.Observacao.Trim()
        };
        pedido.Itens = reserva.Itens
            .GroupBy(i => (i.EspecieId, i.Especie, i.PrecoUnitario))
            .Select(g => new ItemDoPedido
            {
                LojaId = reserva.LojaId, PedidoId = pedido.Id, EspecieId = g.Key.EspecieId, Especie = g.Key.Especie,
                Quantidade = g.Sum(i => i.Quantidade), PrecoUnitario = g.Key.PrecoUnitario
            })
            .ToList();
        pedido.Subtotal = pedido.Itens.Sum(i => i.Quantidade * i.PrecoUnitario);
        pedido.Total = pedido.Subtotal + pedido.Frete;
        return pedido;
    }

    /// <summary>Cliente avisou que pagou: o pedido espera o vendedor conferir. Devolve se mudou.</summary>
    public static bool ComprovanteRecebido(Pedido pedido, DateTimeOffset agora)
    {
        if (pedido.Status != StatusPedido.AguardandoPagamento)
            return false;
        pedido.Status = StatusPedido.ConferirPagamento;
        pedido.ComprovanteEm = agora;
        return true;
    }

    public static void ConfirmarPagamento(Pedido pedido, FormaPagamento forma, DateTimeOffset agora, Guid? usuarioId)
    {
        if (pedido.Status == StatusPedido.Pago)
            throw new RegraException("Este pedido já está pago.");
        if (pedido.Status == StatusPedido.Cancelado)
            throw new RegraException("Este pedido foi cancelado.");
        pedido.Status = StatusPedido.Pago;
        pedido.FormaPagamento = forma;
        pedido.PagoEm = agora;
        pedido.ConfirmadoPor = usuarioId;
    }

    public static void Cancelar(Pedido pedido, string motivo, DateTimeOffset agora)
    {
        if (pedido.Status == StatusPedido.Pago)
            throw new RegraException("Pedido pago não pode ser cancelado por aqui.");
        if (pedido.Status == StatusPedido.Cancelado)
            throw new RegraException("Este pedido já foi cancelado.");
        pedido.Status = StatusPedido.Cancelado;
        pedido.CanceladoEm = agora;
        pedido.MotivoCancelamento = motivo;
    }

    [GeneratedRegex(@"\b(paguei|comprovante|transferi|pix (feito|enviado|realizado|pago)|fiz o pix|mandei o pix|enviei o pix|ja fiz|ta pago|esta pago)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FalaDePagamento();

    /// <summary>Foto ou PDF (o comprovante) ou frase como "paguei", "segue o comprovante", "fiz o pix".</summary>
    public static bool PareceComprovante(string tipo, string texto) =>
        EhArquivo(tipo) || FalaDePagamento().IsMatch(SemAcento(texto));

    /// <summary>Foto ou PDF: o próprio comprovante.</summary>
    public static bool EhArquivo(string tipo) => tipo is "image" or "document";

    /// <summary>Cliente disse que pagou, mas não mandou o arquivo: pede o comprovante.</summary>
    public static string MensagemPedirComprovante(Pedido pedido) =>
        $"Obrigado! 🙌 Para confirmarmos o pedido #{pedido.Numero}, envie o *comprovante do Pix* aqui na conversa (print da tela ou PDF). " +
        "Um atendente confere na conta e confirma o pagamento por aqui.";

    /// <summary>Chegou foto ou PDF: avisa que um atendente vai conferir (a confirmação é manual).</summary>
    public static string MensagemComprovanteRecebido(Pedido pedido) =>
        $"Recebemos o seu comprovante do pedido #{pedido.Numero}! 🙌 Um atendente confere na conta e confirma o pagamento por aqui em instantes.";

    /// <summary>
    /// Mensagem de cobrança: itens, frete, total e como pagar. O código Pix (quando há) vai sozinho na
    /// mensagem seguinte, para o cliente copiar com um toque longo sem levar texto junto. Com o link do
    /// cartão, o cliente escolhe: Pix (comprovante, confirmação manual) ou cartão (confirmação automática).
    /// </summary>
    public static string MensagemCobranca(Pedido pedido, string? nomeRecebedor, TimeZoneInfo fuso)
    {
        var linhas = new List<string> { $"🧾 *Pedido #{pedido.Numero}*" };
        linhas.AddRange(pedido.Itens.Select(i => $"• {i.Quantidade}x {i.Especie} = {(i.Quantidade * i.PrecoUnitario).ToString("C", PtBr)}"));
        linhas.Add(pedido.TipoEntrega == TipoEntrega.Entrega
            ? $"Frete: {pedido.Frete.ToString("C", PtBr)}"
            : "Retirada na loja");
        linhas.Add($"*Total: {pedido.Total.ToString("C", PtBr)}*");
        linhas.Add("");
        // Sem link do cartão, a cobrança é sempre por Pix (como antes).
        var temPix = pedido.PixCopiaECola is not null || pedido.LinkCartao is null;
        if (temPix)
            linhas.Add($"{(pedido.LinkCartao is null ? "Para pagar" : "💠 *Pix*")}, copie o código da próxima mensagem e cole no app do seu banco em *Pix copia e cola*{(nomeRecebedor is { Length: > 0 } n ? $" (recebedor: {n})" : "")}. {PedidoDoComprovante}");
        if (pedido.LinkCartao is { } link)
        {
            if (temPix)
                linhas.Add("");
            linhas.Add($"💳 *Cartão de crédito ou débito*: pague por este link do Mercado Pago (pode parcelar). A confirmação chega aqui sozinha.\n{link}");
        }
        if (pedido.ExpiraEm is { } expira)
            linhas.Add($"\nSeu pedido fica separado até {TimeZoneInfo.ConvertTime(expira, fuso):dd/MM 'às' HH:mm}.");
        return string.Join('\n', linhas);
    }

    /// <summary>Sem aviso automático de Pix: o cliente manda o comprovante e um atendente confirma.</summary>
    public const string PedidoDoComprovante =
        "Depois de pagar, envie o *comprovante* aqui na conversa (print ou PDF). Um atendente confere e confirma o seu pagamento.";

    public static string MensagemPago(Pedido pedido) =>
        pedido.TipoEntrega == TipoEntrega.Entrega
            ? $"✅ Pagamento do pedido #{pedido.Numero} confirmado! Já vamos combinar a entrega com você."
            : $"✅ Pagamento do pedido #{pedido.Numero} confirmado! Seu pedido está separado para a retirada na loja.";

    /// <summary>Pagamento do cartão aprovado, mas a reserva tinha vencido: o vendedor resolve com o cliente.</summary>
    public static string MensagemCartaoForaDoPrazo(Pedido pedido) =>
        $"Recebemos o pagamento no cartão do pedido #{pedido.Numero}! 🙌 Um atendente confere a separação do seu pedido e fala com você por aqui.";

    /// <summary>
    /// Pagamento aprovado no Mercado Pago bate com o pedido? Aceita só valor igual ou maior que o total
    /// (com juros do parcelamento o cliente paga mais).
    /// </summary>
    public static bool ValorDoCartaoConfere(Pedido pedido, decimal valorPago) => valorPago + 0.009m >= pedido.Total;

    public static string Resumo(Pedido pedido) =>
        string.Join(", ", pedido.Itens.Select(i => $"{i.Quantidade}x {i.Especie}"));

    private static string SemAcento(string texto)
    {
        var d = texto.Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }
}
