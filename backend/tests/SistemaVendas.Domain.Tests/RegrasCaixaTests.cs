using SistemaVendas.Domain.Caixa;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Vendas;

namespace SistemaVendas.Domain.Tests;

public class RegrasCaixaTests
{
    private static readonly Guid Loja = Guid.CreateVersion7();
    private static readonly DateOnly Dia = new(2026, 11, 12);

    private static LancamentoCaixa L(TipoLancamento t, decimal v, FormaPagamento f) =>
        RegrasCaixa.Lancar(Loja, t, "Outros", v, null, f, Dia, false, null);

    [Fact]
    public void Resumo_soma_entradas_saidas_e_o_dinheiro_da_gaveta()
    {
        var r = RegrasCaixa.Resumir([
            L(TipoLancamento.Entrada, 100, FormaPagamento.Pix),
            L(TipoLancamento.Entrada, 50, FormaPagamento.Dinheiro),
            L(TipoLancamento.Saida, 20, FormaPagamento.Dinheiro),
            L(TipoLancamento.Saida, 30, FormaPagamento.Pix)
        ]);

        Assert.Equal(150, r.Entradas);
        Assert.Equal(50, r.Saidas);
        Assert.Equal(100, r.Saldo);
        Assert.Equal(30, r.DinheiroEsperado);
        Assert.Equal(100, r.PorForma.Single(f => f.Forma == FormaPagamento.Pix).Entradas);
    }

    [Fact]
    public void Fechamento_mostra_a_diferenca_do_dinheiro_contado()
    {
        var f = RegrasCaixa.Fechar(Loja, Dia, [L(TipoLancamento.Entrada, 80, FormaPagamento.Dinheiro)], 75, null, false, null);
        Assert.Equal(80, f.DinheiroEsperado);
        Assert.Equal(-5, f.Diferenca);
        Assert.Throws<RegraException>(() => RegrasCaixa.Fechar(Loja, Dia, [], null, null, true, null));
    }

    [Fact]
    public void Dia_fechado_ou_valor_zerado_nao_aceita_lancamento()
    {
        Assert.Throws<RegraException>(() => RegrasCaixa.Lancar(Loja, TipoLancamento.Saida, "Energia", 10, null, FormaPagamento.Pix, Dia, true, null));
        Assert.Throws<RegraException>(() => RegrasCaixa.Lancar(Loja, TipoLancamento.Saida, "Energia", 0, null, FormaPagamento.Pix, Dia, false, null));
        Assert.Throws<RegraException>(() => RegrasCaixa.Lancar(Loja, TipoLancamento.Saida, " ", 10, null, FormaPagamento.Pix, Dia, false, null));
    }

    [Fact]
    public void Venda_paga_entra_com_o_total_e_a_forma_do_pedido()
    {
        var p = new Pedido { LojaId = Loja, Numero = 4, Total = 65, FormaPagamento = FormaPagamento.Dinheiro };
        p.Itens.Add(new ItemDoPedido { Especie = "Betta", Quantidade = 2, PrecoUnitario = 25 });
        var l = RegrasCaixa.DaVenda(p, Dia);
        Assert.Equal(TipoLancamento.Entrada, l.Tipo);
        Assert.Equal(65, l.Valor);
        Assert.Equal(FormaPagamento.Dinheiro, l.Forma);
        Assert.Equal(p.Id, l.PedidoId);
        Assert.Contains("Pedido #4", l.Descricao);
    }

    [Fact]
    public void Hoje_segue_o_fuso_da_loja()
    {
        var sp = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        // 01:30 UTC do dia 13 ainda é dia 12 em São Paulo.
        Assert.Equal(Dia, RegrasCaixa.Hoje(new DateTimeOffset(2026, 11, 13, 1, 30, 0, TimeSpan.Zero), sp));
    }
}
