using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Funil;

namespace SistemaVendas.Domain.Tests;

public class MotorFunilTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly ConfigFunil Cfg = new();

    // Quinta, 29/10/2026 às 14h em São Paulo (UTC-3).
    private static readonly DateTimeOffset Tarde = new(2026, 10, 29, 14, 0, 0, TimeSpan.FromHours(-3));

    private static ResultadoAcao Executar(Conversa c, DateTimeOffset agora) =>
        MotorFunil.Executar(c, Cfg, agora, SaoPaulo, new TimeOnly(9, 0), new TimeOnly(20, 0));

    private static Conversa ConversaRespondidaPeloRobo(DateTimeOffset msgCliente)
    {
        var c = new Conversa();
        c.ReceberDoCliente(msgCliente);
        MotorFunil.AoReceberDoCliente(c);
        MotorFunil.AoResponderLoja(c, Cfg, msgCliente.AddMinutes(1));
        return c;
    }

    [Fact]
    public void Resposta_da_loja_move_de_novo_para_robo_atendendo_e_agenda_2h()
    {
        var c = ConversaRespondidaPeloRobo(Tarde);

        Assert.Equal(EtapaCard.RoboAtendendo, c.Etapa);
        Assert.Equal(AcaoFunil.MoverParaFollowup, c.ProximaAcao);
        Assert.Equal(Tarde.AddMinutes(1).AddHours(2), c.ProximaAcaoEm);
    }

    [Fact]
    public void Ciclo_completo_sem_resposta_termina_em_perdido_com_motivo()
    {
        var c = ConversaRespondidaPeloRobo(Tarde);

        var r1 = Executar(c, c.ProximaAcaoEm!.Value);
        Assert.Equal(EtapaCard.FollowUp, c.Etapa);
        Assert.Null(r1.Mensagem);
        Assert.Equal(AcaoFunil.EnviarFollowup1, c.ProximaAcao);
        Assert.Equal(Tarde.AddHours(20), c.ProximaAcaoEm); // 20 h após a última mensagem do cliente

        // 20 h depois das 14h = 10h do dia seguinte: dentro do horário comercial.
        var r2 = Executar(c, c.ProximaAcaoEm!.Value);
        Assert.Equal(Cfg.TextoFollowup1, r2.Mensagem);
        Assert.Equal(1, c.FollowupsEnviados);
        Assert.Equal(AcaoFunil.EnviarFollowup2, c.ProximaAcao);

        var r3 = Executar(c, c.ProximaAcaoEm!.Value);
        Assert.Equal(Cfg.TextoFollowup2, r3.Mensagem);
        Assert.Equal(2, c.FollowupsEnviados);
        Assert.Equal(AcaoFunil.MarcarPerdido, c.ProximaAcao);

        var r4 = Executar(c, c.ProximaAcaoEm!.Value);
        Assert.Equal(EtapaCard.Perdido, c.Etapa);
        Assert.Equal(MotivoPerda.SemResposta, c.MotivoPerda);
        Assert.NotNull(r4.Mudanca);
        Assert.Null(c.ProximaAcao);
    }

    [Fact]
    public void Followup_fora_do_horario_e_reagendado_para_as_9h()
    {
        var c = new Conversa { Etapa = EtapaCard.FollowUp, ProximaAcao = AcaoFunil.EnviarFollowup1 };
        var meiaNoite = new DateTimeOffset(2026, 10, 29, 23, 30, 0, TimeSpan.FromHours(-3));

        var r = Executar(c, meiaNoite);

        Assert.Null(r.Mensagem);
        Assert.Equal(AcaoFunil.EnviarFollowup1, c.ProximaAcao);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.FromHours(-3)), c.ProximaAcaoEm);
    }

    [Fact]
    public void Cliente_que_responde_zera_os_followups_e_volta_ao_robo()
    {
        var c = new Conversa { Etapa = EtapaCard.FollowUp, FollowupsEnviados = 1, ProximaAcao = AcaoFunil.EnviarFollowup2, ProximaAcaoEm = Tarde };

        var mudanca = MotorFunil.AoReceberDoCliente(c);

        Assert.Equal(EtapaCard.RoboAtendendo, c.Etapa);
        Assert.Equal(0, c.FollowupsEnviados);
        Assert.Null(c.ProximaAcao);
        Assert.NotNull(mudanca);
    }

    [Fact]
    public void Vendedor_que_assumiu_pausa_as_regras()
    {
        var c = new Conversa { Etapa = EtapaCard.FollowUp, ProximaAcao = AcaoFunil.EnviarFollowup1, RoboPausado = true };

        var r = Executar(c, Tarde);

        Assert.Null(r.Mensagem);
        Assert.Equal(EtapaCard.FollowUp, c.Etapa);
    }

    [Fact]
    public void Cliente_quente_sai_do_funil_automatico()
    {
        var c = ConversaRespondidaPeloRobo(Tarde);

        MotorFunil.AoChamarVendedor(c, "quer reservar");

        Assert.Equal(EtapaCard.Quente, c.Etapa);
        Assert.Null(c.ProximaAcao);
    }

    [Fact]
    public void Perdido_sem_motivo_e_recusado()
    {
        Assert.Throws<InvalidOperationException>(() => MotorFunil.Mover(new Conversa(), EtapaCard.Perdido, null));
    }
}
