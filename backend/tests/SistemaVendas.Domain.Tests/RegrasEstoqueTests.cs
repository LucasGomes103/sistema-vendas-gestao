using SistemaVendas.Domain.Estoque;

namespace SistemaVendas.Domain.Tests;

public class RegrasEstoqueTests
{
    private static readonly Guid Loja = Guid.CreateVersion7();
    private static readonly DateTimeOffset Agora = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static Tanque Tanque(TipoTanque tipo = TipoTanque.Venda) => new() { LojaId = Loja, Nome = "T", Tipo = tipo };

    private static Lote Lote() => new() { LojaId = Loja, EspecieId = Guid.CreateVersion7(), TanqueId = Guid.CreateVersion7(), QuantidadeInicial = 10 };

    [Fact]
    public void Lote_em_tanque_de_venda_entra_liberado()
    {
        var (lote, entrada) = RegrasEstoque.Receber(Loja, Guid.CreateVersion7(), Tanque(), null, 10, 5m, 0, Agora);

        Assert.Equal(StatusLote.Liberado, lote.Status);
        Assert.Null(lote.QuarentenaAte);
        Assert.Equal(10, entrada.EfeitoNoSaldo);
        Assert.Equal(lote.Id, entrada.LoteId);
    }

    [Fact]
    public void Lote_em_tanque_de_quarentena_entra_em_quarentena_por_7_dias()
    {
        var (lote, _) = RegrasEstoque.Receber(Loja, Guid.CreateVersion7(), Tanque(TipoTanque.Quarentena), null, 10, null, 0, Agora);

        Assert.Equal(StatusLote.Quarentena, lote.Status);
        Assert.Equal(Agora.AddDays(7), lote.QuarentenaAte);
    }

    [Fact]
    public void Dias_de_quarentena_colocam_o_lote_em_quarentena_mesmo_em_tanque_de_venda()
    {
        var (lote, _) = RegrasEstoque.Receber(Loja, Guid.CreateVersion7(), Tanque(), null, 10, null, 3, Agora);

        Assert.Equal(StatusLote.Quarentena, lote.Status);
        Assert.Equal(Agora.AddDays(3), lote.QuarentenaAte);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Entrada_exige_quantidade_positiva(int quantidade)
    {
        Assert.Throws<RegraException>(() => RegrasEstoque.Receber(Loja, Guid.CreateVersion7(), Tanque(), null, quantidade, null, 0, Agora));
    }

    [Fact]
    public void Baixa_nao_passa_do_saldo()
    {
        var erro = Assert.Throws<RegraException>(() => RegrasEstoque.Baixa(Lote(), Guid.CreateVersion7(), 3, 4, null, null));
        Assert.Contains("Só há 3", erro.Message);
    }

    [Fact]
    public void Baixa_reduz_o_saldo()
    {
        var baixa = RegrasEstoque.Baixa(Lote(), Guid.CreateVersion7(), 10, 2, "morreu no transporte", null);
        Assert.Equal(-2, baixa.EfeitoNoSaldo);
    }

    [Fact]
    public void Transferencia_gera_dois_movimentos_que_somam_zero()
    {
        var origem = Guid.CreateVersion7();
        var destino = Guid.CreateVersion7();

        var (saida, chegada) = RegrasEstoque.Transferir(Lote(), origem, destino, 10, 4, null);

        Assert.Equal(origem, saida.TanqueId);
        Assert.Equal(destino, chegada.TanqueId);
        Assert.Equal(-4, saida.EfeitoNoSaldo);
        Assert.Equal(4, chegada.EfeitoNoSaldo);
    }

    [Fact]
    public void Transferencia_para_o_mesmo_tanque_e_recusada()
    {
        var tanque = Guid.CreateVersion7();
        Assert.Throws<RegraException>(() => RegrasEstoque.Transferir(Lote(), tanque, tanque, 10, 1, null));
    }

    [Fact]
    public void Ajuste_exige_observacao()
    {
        Assert.Throws<RegraException>(() => RegrasEstoque.Ajuste(Lote(), Guid.CreateVersion7(), 10, -1, " ", null));
    }

    [Fact]
    public void Ajuste_nao_deixa_saldo_negativo()
    {
        Assert.Throws<RegraException>(() => RegrasEstoque.Ajuste(Lote(), Guid.CreateVersion7(), 2, -3, "contagem", null));
    }

    [Fact]
    public void Liberar_tira_o_lote_da_quarentena()
    {
        var lote = Lote();
        lote.Status = StatusLote.Quarentena;
        lote.QuarentenaAte = Agora;

        RegrasEstoque.Liberar(lote);

        Assert.Equal(StatusLote.Liberado, lote.Status);
        Assert.Null(lote.QuarentenaAte);
        Assert.Throws<RegraException>(() => RegrasEstoque.Liberar(lote));
    }
}
