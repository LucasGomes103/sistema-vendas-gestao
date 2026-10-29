namespace SistemaVendas.Domain.Funil;

/// <summary>
/// Follow-up automático só sai dentro do horário da loja. O que vencer fora disso
/// é reagendado para o próximo início de expediente.
/// </summary>
public static class HorarioComercial
{
    public static DateTimeOffset ProximoHorarioPermitido(
        DateTimeOffset quando, TimeZoneInfo fuso, TimeOnly inicio, TimeOnly fim)
    {
        var local = TimeZoneInfo.ConvertTime(quando, fuso);
        var hora = TimeOnly.FromDateTime(local.DateTime);

        if (hora >= inicio && hora < fim)
            return quando;

        var dia = DateOnly.FromDateTime(local.DateTime);
        if (hora >= fim)
            dia = dia.AddDays(1);

        var alvoLocal = dia.ToDateTime(inicio);
        var offset = fuso.GetUtcOffset(alvoLocal);
        return new DateTimeOffset(alvoLocal, offset).ToUniversalTime();
    }
}
