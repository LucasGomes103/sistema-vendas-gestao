using SistemaVendas.Domain.Atendimento;

namespace SistemaVendas.Domain.Tests;

public class LeadQuenteTests
{
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly TimeOnly Abre = new(9, 0), Fecha = new(20, 0);

    // 15h em São Paulo (loja aberta) e 23h (fechada).
    private static readonly DateTimeOffset Tarde = new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Noite = new(2026, 11, 10, 2, 0, 0, TimeSpan.Zero);

    private static Conversa Quente(DateTimeOffset chamadoEm)
    {
        var c = new Conversa();
        c.ChamarVendedor("quer reservar", chamadoEm);
        return c;
    }

    [Fact]
    public void Recem_chamado_nao_gera_aviso()
    {
        var c = Quente(Tarde);
        Assert.True(LeadQuente.SemDono(c));
        Assert.Null(LeadQuente.Avancar(c, Tarde.AddMinutes(10), Fuso, Abre, Fecha));
        Assert.Equal(0, c.AvisosQuente);
    }

    [Fact]
    public void Aos_15_min_so_sobe_o_aviso_no_painel()
    {
        var c = Quente(Tarde);
        Assert.Null(LeadQuente.Avancar(c, Tarde.AddMinutes(16), Fuso, Abre, Fecha));
        Assert.Equal(1, c.AvisosQuente);
    }

    [Fact]
    public void Com_1_hora_avisa_o_cliente_uma_vez_so()
    {
        var c = Quente(Tarde);
        var texto = LeadQuente.Avancar(c, Tarde.AddMinutes(61), Fuso, Abre, Fecha);
        Assert.Contains("em instantes", texto);
        Assert.Equal(2, c.AvisosQuente);
        Assert.Null(LeadQuente.Avancar(c, Tarde.AddMinutes(90), Fuso, Abre, Fecha));
    }

    [Fact]
    public void Fora_do_horario_promete_o_horario_de_abertura()
    {
        var c = Quente(Noite.AddHours(-1));
        var texto = LeadQuente.Avancar(c, Noite, Fuso, Abre, Fecha);
        Assert.Contains("09:00", texto);
    }

    [Fact]
    public void Vendedor_que_assume_tira_o_cliente_do_sem_dono()
    {
        var c = Quente(Tarde);
        c.Assumir(Tarde.AddMinutes(5));
        Assert.False(LeadQuente.SemDono(c));
        Assert.Null(LeadQuente.Avancar(c, Tarde.AddHours(2), Fuso, Abre, Fecha));
        Assert.Equal(0, LeadQuente.Nivel(c, Tarde.AddHours(2)));
    }

    [Fact]
    public void Devolver_ao_robo_zera_os_avisos()
    {
        var c = Quente(Tarde);
        LeadQuente.Avancar(c, Tarde.AddMinutes(61), Fuso, Abre, Fecha);
        c.DevolverAoRobo();
        Assert.Equal(0, c.AvisosQuente);
        Assert.Null(c.AtendidoEm);
    }
}
