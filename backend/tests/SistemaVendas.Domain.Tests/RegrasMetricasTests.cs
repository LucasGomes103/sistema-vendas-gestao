using System.Text;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Domain.Metricas;

namespace SistemaVendas.Domain.Tests;

public class RegrasMetricasTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 16);

    [Fact]
    public void Periodo_padrao_sao_os_ultimos_30_dias_e_o_anterior_tem_o_mesmo_tamanho()
    {
        var p = Periodo.Criar(null, null, Hoje);
        Assert.Equal(new DateOnly(2026, 10, 18), p.De);
        Assert.Equal(Hoje, p.Ate);
        Assert.Equal(30, p.Dias);

        var a = p.Anterior();
        Assert.Equal(new DateOnly(2026, 9, 18), a.De);
        Assert.Equal(new DateOnly(2026, 10, 17), a.Ate);
    }

    [Fact]
    public void Periodo_invertido_ou_maior_que_um_ano_e_recusado()
    {
        Assert.Throws<RegraException>(() => Periodo.Criar(Hoje, Hoje.AddDays(-1), Hoje));
        Assert.Throws<RegraException>(() => Periodo.Criar(Hoje.AddDays(-400), Hoje, Hoje));
    }

    [Fact]
    public void Dia_comeca_a_meia_noite_do_fuso_da_loja()
    {
        var fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var (inicio, fim) = new Periodo(Hoje, Hoje).EmUtc(fuso);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 3, 0, 0, TimeSpan.Zero), inicio);
        Assert.Equal(new DateTimeOffset(2026, 11, 17, 3, 0, 0, TimeSpan.Zero), fim);
    }

    [Fact]
    public void Agrupamento_padrao_depende_do_tamanho_do_periodo()
    {
        Assert.Equal(Agrupamento.Dia, RegrasMetricas.AgrupamentoPadrao(new Periodo(Hoje.AddDays(-30), Hoje)));
        Assert.Equal(Agrupamento.Semana, RegrasMetricas.AgrupamentoPadrao(new Periodo(Hoje.AddDays(-89), Hoje)));
        Assert.Equal(Agrupamento.Mes, RegrasMetricas.AgrupamentoPadrao(new Periodo(Hoje.AddDays(-200), Hoje)));
    }

    [Fact]
    public void Grupos_cobrem_o_periodo_sem_buracos()
    {
        // 16/11/2026 é segunda-feira.
        Assert.Equal(new DateOnly(2026, 11, 16), RegrasMetricas.InicioDoGrupo(new DateOnly(2026, 11, 22), Agrupamento.Semana));
        Assert.Equal(new DateOnly(2026, 11, 9), RegrasMetricas.InicioDoGrupo(new DateOnly(2026, 11, 15), Agrupamento.Semana));

        var semanas = RegrasMetricas.Grupos(new Periodo(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 16)), Agrupamento.Semana);
        Assert.Equal([new(2026, 10, 26), new(2026, 11, 2), new(2026, 11, 9), new(2026, 11, 16)], semanas);

        var meses = RegrasMetricas.Grupos(new Periodo(new DateOnly(2026, 9, 15), new DateOnly(2026, 11, 2)), Agrupamento.Mes);
        Assert.Equal([new(2026, 9, 1), new(2026, 10, 1), new(2026, 11, 1)], meses);

        Assert.Equal(7, RegrasMetricas.Grupos(new Periodo(Hoje.AddDays(-6), Hoje), Agrupamento.Dia).Count);
    }

    [Fact]
    public void Giro_e_vendido_sobre_estoque_medio()
    {
        Assert.Equal(1.5m, RegrasMetricas.Giro(30, 10, 30));
        Assert.Null(RegrasMetricas.Giro(5, 0, 0));
        Assert.Equal(0m, RegrasMetricas.Giro(0, 10, 10));
    }

    [Fact]
    public void Funil_conta_quem_chegou_ate_cada_degrau()
    {
        var funil = RegrasMetricas.Funil([
            [EtapaCard.Novo],
            [EtapaCard.Novo, EtapaCard.RoboAtendendo, EtapaCard.FollowUp, EtapaCard.Perdido],
            [EtapaCard.Novo, EtapaCard.RoboAtendendo, EtapaCard.Quente, EtapaCard.AguardandoPagamento, EtapaCard.ConferirPagamento],
            [EtapaCard.Novo, EtapaCard.Quente, EtapaCard.AguardandoPagamento, EtapaCard.Pago, EtapaCard.Entregue]
        ]);

        Assert.Equal(
            [(EtapaCard.Novo, 4), (EtapaCard.RoboAtendendo, 3), (EtapaCard.Quente, 2), (EtapaCard.AguardandoPagamento, 2), (EtapaCard.Pago, 1), (EtapaCard.Entregue, 1)],
            funil);
    }

    [Fact]
    public void Origem_da_venda()
    {
        Assert.Equal(RegrasMetricas.OrigemVitrine, RegrasMetricas.Origem(true, true, 2));
        Assert.Equal(RegrasMetricas.OrigemFollowup, RegrasMetricas.Origem(true, false, 1));
        Assert.Equal(RegrasMetricas.OrigemDireta, RegrasMetricas.Origem(true, false, 0));
        Assert.Equal(RegrasMetricas.OrigemSemConversa, RegrasMetricas.Origem(false, false, 0));
    }

    [Fact]
    public void Percentual_e_variacao()
    {
        Assert.Equal(14.8m, RegrasMetricas.Percentual(61, 412));
        Assert.Equal(0m, RegrasMetricas.Percentual(3, 0));
        Assert.Equal(50m, RegrasMetricas.Variacao(150, 100));
        Assert.Null(RegrasMetricas.Variacao(10, 0));
        Assert.Equal(92m, RegrasMetricas.Media(5612, 61));
    }

    [Fact]
    public void Csv_abre_no_excel_em_portugues()
    {
        var bytes = Csv.Gerar(["Espécie", "Receita"], [["Ramirezi; casal", 1234.5m], ["=SOMA(A1)", null]]);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var texto = Encoding.UTF8.GetString(bytes[3..]);
        Assert.Equal("Espécie;Receita\r\n\"Ramirezi; casal\";1234,50\r\n'=SOMA(A1);\r\n".Replace("\r\n", Environment.NewLine), texto);
    }
}
