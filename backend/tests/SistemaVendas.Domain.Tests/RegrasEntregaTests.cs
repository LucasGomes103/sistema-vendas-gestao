using SistemaVendas.Domain.Entregas;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Vendas;

namespace SistemaVendas.Domain.Tests;

public class RegrasEntregaTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 12);
    private static readonly DateTimeOffset Agora = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    private static Entrega Nova(TipoEntrega tipo = TipoEntrega.Entrega) =>
        RegrasEntrega.DoPedido(new Pedido { LojaId = Guid.NewGuid(), Numero = 9, ClienteId = Guid.NewGuid(), TipoEntrega = tipo, Endereco = "Rua A, 1", Cep = "07010000" });

    [Fact]
    public void Nasce_a_agendar_com_os_dados_do_pedido()
    {
        var e = Nova();
        Assert.Equal(StatusEntrega.AAgendar, e.Status);
        Assert.Equal(9, e.NumeroPedido);
        Assert.Equal("Rua A, 1", e.Endereco);
    }

    [Fact]
    public void Agenda_sai_e_entrega()
    {
        var e = Nova();
        var entregador = Guid.NewGuid();
        RegrasEntrega.Agendar(e, Hoje.AddDays(1), " 14h às 18h ", entregador, Hoje);
        Assert.Equal(StatusEntrega.Agendada, e.Status);
        Assert.Equal("14h às 18h", e.Janela);
        Assert.Equal(entregador, e.EntregadorId);
        Assert.Contains("sexta-feira, 13/11, 14h às 18h", RegrasEntrega.MensagemAgendada(e));

        RegrasEntrega.SaiuParaEntrega(e, Agora, null);
        Assert.Equal(StatusEntrega.SaiuParaEntrega, e.Status);

        RegrasEntrega.MarcarEntregue(e, Agora, entregador);
        Assert.Equal(StatusEntrega.Entregue, e.Status);
        Assert.Throws<RegraException>(() => RegrasEntrega.MarcarEntregue(e, Agora, null));
        Assert.Throws<RegraException>(() => RegrasEntrega.Agendar(e, Hoje, null, null, Hoje));
    }

    [Fact]
    public void Nao_agenda_no_passado()
    {
        Assert.Throws<RegraException>(() => RegrasEntrega.Agendar(Nova(), Hoje.AddDays(-1), null, null, Hoje));
    }

    [Fact]
    public void Retirada_nao_sai_para_entrega_nem_tem_entregador()
    {
        var e = Nova(TipoEntrega.Retirada);
        RegrasEntrega.Agendar(e, Hoje, null, Guid.NewGuid(), Hoje);
        Assert.Null(e.EntregadorId);
        Assert.Throws<RegraException>(() => RegrasEntrega.SaiuParaEntrega(e, Agora, null));
        Assert.Contains("retirada", RegrasEntrega.MensagemAgendada(e));
    }

    [Fact]
    public void Quem_marca_a_saida_vira_o_entregador_se_nao_havia()
    {
        var e = Nova();
        var eu = Guid.NewGuid();
        RegrasEntrega.SaiuParaEntrega(e, Agora, eu);
        Assert.Equal(eu, e.EntregadorId);
    }
}
