using SistemaVendas.Domain.Estoque;

namespace SistemaVendas.Domain.Tests;

public class MovimentoEstoqueTests
{
    [Theory]
    [InlineData(TipoMovimento.Entrada, 10, 10)]
    [InlineData(TipoMovimento.LiberaReserva, 2, 2)]
    [InlineData(TipoMovimento.BaixaMorte, 3, -3)]
    [InlineData(TipoMovimento.Reserva, 2, -2)]
    [InlineData(TipoMovimento.Venda, 1, -1)]
    [InlineData(TipoMovimento.Ajuste, -4, -4)]
    [InlineData(TipoMovimento.Transferencia, -5, -5)]
    public void Efeito_no_saldo_segue_a_regra_dos_movimentos(TipoMovimento tipo, int quantidade, int esperado)
    {
        var movimento = new MovimentoEstoque { Tipo = tipo, Quantidade = quantidade };
        Assert.Equal(esperado, movimento.EfeitoNoSaldo);
    }

    [Fact]
    public void Reserva_paga_fecha_a_soma()
    {
        // entrada de 10, reserva de 2, reserva paga vira libera_reserva (+2) e venda (-2)
        var movimentos = new[]
        {
            new MovimentoEstoque { Tipo = TipoMovimento.Entrada, Quantidade = 10 },
            new MovimentoEstoque { Tipo = TipoMovimento.Reserva, Quantidade = 2 },
            new MovimentoEstoque { Tipo = TipoMovimento.LiberaReserva, Quantidade = 2 },
            new MovimentoEstoque { Tipo = TipoMovimento.Venda, Quantidade = 2 }
        };

        Assert.Equal(8, movimentos.Sum(m => m.EfeitoNoSaldo));
    }
}
