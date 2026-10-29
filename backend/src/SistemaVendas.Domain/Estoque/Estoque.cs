using SistemaVendas.Domain.Comum;

namespace SistemaVendas.Domain.Estoque;

public enum TipoTanque
{
    Venda,
    Quarentena
}

public enum StatusLote
{
    Quarentena,
    Liberado,
    Encerrado
}

public enum TipoMovimento
{
    Entrada,
    BaixaMorte,
    Reserva,
    LiberaReserva,
    Venda,
    Transferencia,
    Ajuste
}

public class Tanque : EntidadeDaLoja
{
    public required string Nome { get; set; }
    public TipoTanque Tipo { get; set; } = TipoTanque.Venda;
    public int? Litros { get; set; }
    public bool Ativo { get; set; } = true;
}

public class Fornecedor : EntidadeDaLoja
{
    public required string Nome { get; set; }
    public string? Telefone { get; set; }
}

public class Lote : EntidadeDaLoja
{
    public Guid EspecieId { get; set; }
    public Guid? FornecedorId { get; set; }
    public Guid TanqueId { get; set; }
    public int QuantidadeInicial { get; set; }
    public decimal? CustoUnitario { get; set; }
    public DateTimeOffset RecebidoEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? QuarentenaAte { get; set; }
    public StatusLote Status { get; set; } = StatusLote.Quarentena;
}

/// <summary>
/// O estoque não é um número guardado: é a soma dos movimentos de cada lote.
/// Transferência gera dois movimentos (negativo na origem, positivo no destino);
/// ajuste aceita quantidade positiva ou negativa e exige observação.
/// </summary>
public class MovimentoEstoque : EntidadeDaLoja
{
    public Guid LoteId { get; set; }
    public Guid TanqueId { get; set; }
    public TipoMovimento Tipo { get; set; }
    public int Quantidade { get; set; }
    public Guid? PedidoId { get; set; }
    public Guid? UsuarioId { get; set; }
    public string? Observacao { get; set; }

    /// <summary>Efeito do movimento no saldo disponível, seguindo a view estoque_disponivel.</summary>
    public int EfeitoNoSaldo => Tipo switch
    {
        TipoMovimento.Entrada or TipoMovimento.LiberaReserva or TipoMovimento.Ajuste or TipoMovimento.Transferencia => Quantidade,
        TipoMovimento.BaixaMorte or TipoMovimento.Reserva or TipoMovimento.Venda => -Quantidade,
        _ => 0
    };
}
