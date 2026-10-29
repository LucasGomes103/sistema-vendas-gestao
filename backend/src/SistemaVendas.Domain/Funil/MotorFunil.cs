using SistemaVendas.Domain.Atendimento;

namespace SistemaVendas.Domain.Funil;

/// <summary>Mudança de etapa produzida pelo motor (vira um CardEvento).</summary>
public record MudancaEtapa(EtapaCard? De, EtapaCard Para, string? Detalhe);

/// <summary>Resultado de uma ação vencida: talvez uma mensagem a enviar e talvez uma mudança de etapa.</summary>
public record ResultadoAcao(string? Mensagem, MudancaEtapa? Mudanca);

/// <summary>
/// Regras de tempo do funil. Cada card guarda a próxima ação e quando ela vence; o FunilWorker executa
/// as vencidas e este motor agenda a seguinte. No máximo 2 follow-ups; se o cliente responde, o contador zera.
/// </summary>
public static class MotorFunil
{
    /// <summary>Etapas em que o funil automático trabalha. Daqui para frente, quem conduz é o vendedor.</summary>
    public static bool Automatica(EtapaCard etapa) =>
        etapa is EtapaCard.Novo or EtapaCard.RoboAtendendo or EtapaCard.FollowUp;

    /// <summary>Cliente escreveu: volta para "Robô atendendo" e cancela os lembretes.</summary>
    public static MudancaEtapa? AoReceberDoCliente(Conversa c)
    {
        var anterior = c.Etapa;
        c.FollowupsEnviados = 0;

        // Perdido que volta a falar é reaberto; etapas de venda (Quente em diante) não mudam.
        if (c.Etapa is EtapaCard.FollowUp or EtapaCard.Perdido)
        {
            c.Etapa = EtapaCard.RoboAtendendo;
            c.MotivoPerda = null;
        }

        if (Automatica(c.Etapa))
        {
            c.ProximaAcao = null;
            c.ProximaAcaoEm = null;
        }

        return anterior != c.Etapa ? new MudancaEtapa(anterior, c.Etapa, "Cliente respondeu") : null;
    }

    /// <summary>Loja respondeu (robô ou vendedor): começa a contar o tempo sem resposta do cliente.</summary>
    public static MudancaEtapa? AoResponderLoja(Conversa c, ConfigFunil cfg, DateTimeOffset agora)
    {
        var anterior = c.Etapa;
        if (c.Etapa == EtapaCard.Novo)
            c.Etapa = EtapaCard.RoboAtendendo;

        if (c.Etapa == EtapaCard.RoboAtendendo)
        {
            c.ProximaAcao = AcaoFunil.MoverParaFollowup;
            c.ProximaAcaoEm = agora.AddHours(cfg.HorasAteFollowup);
        }

        return anterior != c.Etapa ? new MudancaEtapa(anterior, c.Etapa, null) : null;
    }

    /// <summary>Cliente quente: o card vai para "Quente" e as regras automáticas param.</summary>
    public static MudancaEtapa? AoChamarVendedor(Conversa c, string motivo)
    {
        var anterior = c.Etapa;
        if (Automatica(c.Etapa))
            c.Etapa = EtapaCard.Quente;
        c.ProximaAcao = null;
        c.ProximaAcaoEm = null;
        return anterior != c.Etapa ? new MudancaEtapa(anterior, c.Etapa, motivo) : null;
    }

    /// <summary>Movimento manual no kanban.</summary>
    public static MudancaEtapa? Mover(Conversa c, EtapaCard para, MotivoPerda? motivo)
    {
        if (para == EtapaCard.Perdido && motivo is null)
            throw new InvalidOperationException("Nenhum card vai para Perdido sem motivo.");

        var anterior = c.Etapa;
        c.Etapa = para;
        c.MotivoPerda = para == EtapaCard.Perdido ? motivo : null;
        c.ProximaAcao = null;
        c.ProximaAcaoEm = null;
        return anterior != para ? new MudancaEtapa(anterior, para, motivo?.ToString()) : null;
    }

    /// <summary>Executa a ação vencida e agenda a próxima.</summary>
    public static ResultadoAcao Executar(Conversa c, ConfigFunil cfg, DateTimeOffset agora, TimeZoneInfo fuso, TimeOnly inicio, TimeOnly fim)
    {
        var acao = c.ProximaAcao;
        c.ProximaAcao = null;
        c.ProximaAcaoEm = null;

        // Vendedor assumiu ou o card saiu das etapas automáticas: nada a fazer.
        if (acao is null || c.RoboPausado || !Automatica(c.Etapa))
            return new ResultadoAcao(null, null);

        // Ação que envia mensagem fora do horário comercial é reagendada para o próximo início de expediente.
        if (acao is AcaoFunil.EnviarFollowup1 or AcaoFunil.EnviarFollowup2)
        {
            var permitido = HorarioComercial.ProximoHorarioPermitido(agora, fuso, inicio, fim);
            if (permitido > agora)
            {
                c.ProximaAcao = acao;
                c.ProximaAcaoEm = permitido;
                return new ResultadoAcao(null, null);
            }
        }

        switch (acao)
        {
            case AcaoFunil.MoverParaFollowup:
            {
                var anterior = c.Etapa;
                c.Etapa = EtapaCard.FollowUp;
                var baseFollowup = c.UltimaMsgClienteEm ?? agora;
                var quando = baseFollowup.AddHours(cfg.HorasFollowup1);
                c.ProximaAcao = AcaoFunil.EnviarFollowup1;
                c.ProximaAcaoEm = quando > agora ? quando : agora;
                return new ResultadoAcao(null, new MudancaEtapa(anterior, EtapaCard.FollowUp, $"Sem resposta há {cfg.HorasAteFollowup} h"));
            }

            case AcaoFunil.EnviarFollowup1:
                c.FollowupsEnviados = 1;
                c.ProximaAcao = AcaoFunil.EnviarFollowup2;
                c.ProximaAcaoEm = agora.AddDays(cfg.DiasFollowup2);
                return new ResultadoAcao(cfg.TextoFollowup1, null);

            case AcaoFunil.EnviarFollowup2:
                c.FollowupsEnviados = 2;
                c.ProximaAcao = AcaoFunil.MarcarPerdido;
                c.ProximaAcaoEm = agora.AddHours(cfg.HorasAtePerdido);
                return new ResultadoAcao(cfg.TextoFollowup2, null);

            case AcaoFunil.MarcarPerdido:
            {
                var anterior = c.Etapa;
                c.Etapa = EtapaCard.Perdido;
                c.MotivoPerda = Funil.MotivoPerda.SemResposta;
                return new ResultadoAcao(null, new MudancaEtapa(anterior, EtapaCard.Perdido, "Sem resposta após 2 follow-ups"));
            }

            default:
                return new ResultadoAcao(null, null);
        }
    }
}
