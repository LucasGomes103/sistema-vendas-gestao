using SistemaVendas.Domain.Funil;

namespace SistemaVendas.Domain.Tests;

public class HorarioComercialTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly TimeOnly Inicio = new(9, 0);
    private static readonly TimeOnly Fim = new(20, 0);

    // São Paulo está em UTC-3 (sem horário de verão).
    private static DateTimeOffset Local(int dia, int hora, int minuto = 0) =>
        new DateTimeOffset(2026, 10, dia, hora, minuto, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void Dentro_do_horario_mantem_o_momento()
    {
        var quando = Local(29, 14, 30);
        Assert.Equal(quando, HorarioComercial.ProximoHorarioPermitido(quando, SaoPaulo, Inicio, Fim));
    }

    [Fact]
    public void Depois_das_20h_vai_para_as_9h_do_dia_seguinte()
    {
        var resultado = HorarioComercial.ProximoHorarioPermitido(Local(29, 21, 15), SaoPaulo, Inicio, Fim);
        Assert.Equal(Local(30, 9), resultado);
    }

    [Fact]
    public void Antes_das_9h_vai_para_as_9h_do_mesmo_dia()
    {
        var resultado = HorarioComercial.ProximoHorarioPermitido(Local(29, 6, 0), SaoPaulo, Inicio, Fim);
        Assert.Equal(Local(29, 9), resultado);
    }
}
