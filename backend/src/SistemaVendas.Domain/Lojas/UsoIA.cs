using SistemaVendas.Domain.Comum;

namespace SistemaVendas.Domain.Lojas;

/// <summary>
/// Uso da IA da plataforma por loja e por dia (fuso da loja). A IA é paga pela plataforma (conta do Lucas), então
/// cada loja tem um limite de respostas por mês; acima dele o robô segue com as respostas por regras.
/// </summary>
public class UsoIA : EntidadeDaLoja
{
    public DateOnly Dia { get; set; }

    /// <summary>Respostas do robô feitas pela IA.</summary>
    public int Respostas { get; set; }

    /// <summary>Chamadas ao modelo (uma resposta pode precisar de até 4, por causa das ferramentas).</summary>
    public int Chamadas { get; set; }

    public long TokensEntrada { get; set; }
    public long TokensSaida { get; set; }

    /// <summary>Respostas por regras: limite do mês atingido ou IA fora do ar.</summary>
    public int PorRegras { get; set; }
}

public static class RegrasUsoIA
{
    /// <summary>Limite que vale para a loja: o dela, senão o padrão da plataforma. 0 = sem limite.</summary>
    public static int LimiteDaLoja(int? limiteLoja, int padrao) => Math.Max(0, limiteLoja ?? padrao);

    public static bool Esgotado(int usadas, int limite) => limite > 0 && usadas >= limite;

    /// <summary>Primeiro dia do mês da data (para somar o uso do mês).</summary>
    public static DateOnly InicioDoMes(DateOnly dia) => new(dia.Year, dia.Month, 1);

    /// <summary>Hoje no fuso da loja (fuso inválido: São Paulo).</summary>
    public static DateOnly Hoje(DateTimeOffset agora, string? fuso)
    {
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(fuso) ? "America/Sao_Paulo" : fuso);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(agora, tz).DateTime);
    }
}
