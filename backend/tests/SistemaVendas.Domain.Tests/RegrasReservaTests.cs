using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Vendas;

namespace SistemaVendas.Domain.Tests;

public class RegrasReservaTests
{
    private static readonly Guid Loja = Guid.CreateVersion7();
    private static readonly Guid Conversa = Guid.CreateVersion7();
    private static readonly EspecieParaReserva Ramirezi = new(Guid.CreateVersion7(), "Ramirezi", 60);
    private static readonly EspecieParaReserva Betta = new(Guid.CreateVersion7(), "Betta", 25);
    private static readonly Dictionary<Guid, EspecieParaReserva> Especies = new() { [Ramirezi.Id] = Ramirezi, [Betta.Id] = Betta };
    private static readonly DateTimeOffset Agora = new(2026, 11, 9, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Separa_do_lote_mais_antigo_primeiro_e_completa_com_o_seguinte()
    {
        var antigo = Guid.CreateVersion7();
        var novo = Guid.CreateVersion7();
        var saldos = new[] { new SaldoParaReserva(novo, Ramirezi.Id, Guid.NewGuid(), 10, Agora.AddDays(-1)), new SaldoParaReserva(antigo, Ramirezi.Id, Guid.NewGuid(), 2, Agora.AddDays(-10)) };

        var r = RegrasReserva.Criar(Loja, Conversa, [new ItemPedido(Ramirezi.Id, 3)], saldos, Especies, Agora);

        Assert.NotNull(r.Reserva);
        Assert.Equal([antigo, novo], r.Reserva!.Itens.Select(i => i.LoteId));
        Assert.Equal([2, 1], r.Reserva.Itens.Select(i => i.Quantidade));
        Assert.Equal(180, r.Reserva.Total);
        Assert.Equal(Agora.AddHours(24), r.Reserva.ExpiraEm);
        Assert.All(r.Movimentos, m => Assert.Equal(TipoMovimento.Reserva, m.Tipo));
        Assert.Empty(r.Faltas);
    }

    [Fact]
    public void Separa_o_que_tem_e_avisa_a_falta()
    {
        var saldos = new[] { new SaldoParaReserva(Guid.CreateVersion7(), Ramirezi.Id, Guid.NewGuid(), 1, Agora) };
        var r = RegrasReserva.Criar(Loja, Conversa, [new ItemPedido(Ramirezi.Id, 3), new ItemPedido(Betta.Id, 2)], saldos, Especies, Agora);

        Assert.Equal(1, r.Reserva!.Itens.Sum(i => i.Quantidade));
        Assert.Contains(r.Faltas, f => f.Contains("só havia 1 de 3"));
        Assert.Contains(r.Faltas, f => f.Contains("Betta") && f.Contains("sem estoque"));
    }

    [Fact]
    public void Sem_nada_em_estoque_nao_cria_reserva()
    {
        var r = RegrasReserva.Criar(Loja, Conversa, [new ItemPedido(Betta.Id, 1)], [], Especies, Agora);
        Assert.Null(r.Reserva);
        Assert.Empty(r.Movimentos);
        Assert.Single(r.Faltas);
    }

    [Fact]
    public void Confirmar_libera_e_registra_a_venda_e_o_saldo_cai_uma_vez_so()
    {
        var saldos = new[] { new SaldoParaReserva(Guid.CreateVersion7(), Ramirezi.Id, Guid.NewGuid(), 5, Agora) };
        var r = RegrasReserva.Criar(Loja, Conversa, [new ItemPedido(Ramirezi.Id, 2)], saldos, Especies, Agora);

        var fim = RegrasReserva.Encerrar(r.Reserva!, StatusReserva.Confirmada, Agora.AddHours(1), null);

        Assert.Equal(StatusReserva.Confirmada, r.Reserva!.Status);
        Assert.Equal(-2, r.Movimentos.Concat(fim).Sum(m => m.EfeitoNoSaldo));
    }

    [Fact]
    public void Expirar_devolve_tudo_ao_estoque()
    {
        var saldos = new[] { new SaldoParaReserva(Guid.CreateVersion7(), Ramirezi.Id, Guid.NewGuid(), 5, Agora) };
        var r = RegrasReserva.Criar(Loja, Conversa, [new ItemPedido(Ramirezi.Id, 2)], saldos, Especies, Agora);

        Assert.True(RegrasReserva.Vencida(r.Reserva!, Agora.AddHours(24)));
        var fim = RegrasReserva.Encerrar(r.Reserva!, StatusReserva.Expirada, Agora.AddHours(24), null);

        Assert.Equal(0, r.Movimentos.Concat(fim).Sum(m => m.EfeitoNoSaldo));
        Assert.Throws<RegraException>(() => RegrasReserva.Encerrar(r.Reserva!, StatusReserva.Cancelada, Agora, null));
    }

    [Fact]
    public void Le_o_pedido_da_vitrine()
    {
        var texto = "🛒 *Pedido pelo catálogo*\n\n• 2x Ramirezi (casal) (R$ 60,00 cada) = R$ 120,00\n• 10x Neon (R$ 3,50 cada) = R$ 35,00\n\n*Total: R$ 155,00* + frete";
        var itens = LeitorPedidoVitrine.Ler(texto);
        Assert.Equal([("Ramirezi (casal)", 2), ("Neon", 10)], itens);
    }
}
