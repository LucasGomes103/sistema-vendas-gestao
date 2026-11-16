using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Funil;

namespace SistemaVendas.Domain.Metricas;

public enum Agrupamento
{
    Dia,
    Semana,
    Mes
}

/// <summary>Intervalo de dias (no fuso da loja), com os dois extremos incluídos.</summary>
public record Periodo(DateOnly De, DateOnly Ate)
{
    public const int MaximoDias = 366;

    public int Dias => Ate.DayNumber - De.DayNumber + 1;

    public static Periodo Criar(DateOnly? de, DateOnly? ate, DateOnly hoje)
    {
        var fim = ate ?? hoje;
        var inicio = de ?? fim.AddDays(-29);
        if (inicio > fim)
            throw new RegraException("A data inicial precisa ser antes da final.");
        var p = new Periodo(inicio, fim);
        if (p.Dias > MaximoDias)
            throw new RegraException("Escolha um período de no máximo um ano.");
        return p;
    }

    /// <summary>Mesmo número de dias, logo antes: base para comparar.</summary>
    public Periodo Anterior() => new(De.AddDays(-Dias), De.AddDays(-1));

    public bool Contem(DateOnly dia) => dia >= De && dia <= Ate;

    /// <summary>Início do primeiro dia e início do dia seguinte ao último, em UTC.</summary>
    public (DateTimeOffset Inicio, DateTimeOffset Fim) EmUtc(TimeZoneInfo fuso) => (InicioDoDia(De, fuso), InicioDoDia(Ate.AddDays(1), fuso));

    public static DateTimeOffset InicioDoDia(DateOnly dia, TimeZoneInfo fuso)
    {
        var local = dia.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, fuso.GetUtcOffset(local)).ToUniversalTime();
    }
}

/// <summary>Contas das métricas e relatórios. Tudo puro: quem chama traz os dados do banco.</summary>
public static class RegrasMetricas
{
    /// <summary>Até 31 dias mostra por dia; até ~4 meses por semana; acima disso por mês.</summary>
    public static Agrupamento AgrupamentoPadrao(Periodo p) => p.Dias switch
    {
        <= 31 => Agrupamento.Dia,
        <= 120 => Agrupamento.Semana,
        _ => Agrupamento.Mes
    };

    /// <summary>Primeiro dia do grupo: o próprio dia, a segunda-feira da semana ou o dia 1 do mês.</summary>
    public static DateOnly InicioDoGrupo(DateOnly dia, Agrupamento a) => a switch
    {
        Agrupamento.Semana => dia.AddDays(-(((int)dia.DayOfWeek + 6) % 7)),
        Agrupamento.Mes => new DateOnly(dia.Year, dia.Month, 1),
        _ => dia
    };

    /// <summary>Todos os grupos do período, inclusive os sem venda (para o gráfico não ter buracos).</summary>
    public static List<DateOnly> Grupos(Periodo p, Agrupamento a)
    {
        var grupos = new List<DateOnly>();
        for (var g = InicioDoGrupo(p.De, a); g <= p.Ate; g = a switch
             {
                 Agrupamento.Semana => g.AddDays(7),
                 Agrupamento.Mes => g.AddMonths(1),
                 _ => g.AddDays(1)
             })
            grupos.Add(g);
        return grupos;
    }

    /// <summary>Parte de um total em %, com uma casa. Zero quando não há base.</summary>
    public static decimal Percentual(decimal parte, decimal total) =>
        total <= 0 ? 0 : Math.Round(parte * 100m / total, 1, MidpointRounding.AwayFromZero);

    public static decimal Media(decimal soma, int quantidade) =>
        quantidade <= 0 ? 0 : Math.Round(soma / quantidade, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Giro do estoque no período: quantas vezes o estoque médio foi vendido. Estoque médio é a média
    /// entre o saldo no começo e no fim. Sem estoque médio, não há giro (null).
    /// </summary>
    public static decimal? Giro(int vendidos, int saldoInicio, int saldoFim)
    {
        var medio = (Math.Max(0, saldoInicio) + Math.Max(0, saldoFim)) / 2m;
        return medio <= 0 ? null : Math.Round(vendidos / medio, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Ordem das etapas no caminho até a venda. Follow-up e Perdido não são degraus.</summary>
    public static readonly EtapaCard[] DegrausFunil =
        [EtapaCard.Novo, EtapaCard.RoboAtendendo, EtapaCard.Quente, EtapaCard.AguardandoPagamento, EtapaCard.Pago, EtapaCard.Entregue];

    /// <summary>Degrau de cada etapa. Conferir pagamento conta como aguardando pagamento.</summary>
    public static int Degrau(EtapaCard etapa) => etapa switch
    {
        EtapaCard.Novo => 0,
        EtapaCard.RoboAtendendo => 1,
        EtapaCard.Quente => 2,
        EtapaCard.AguardandoPagamento or EtapaCard.ConferirPagamento => 3,
        EtapaCard.Pago => 4,
        EtapaCard.Entregue => 5,
        _ => -1
    };

    /// <summary>
    /// Funil: para cada degrau, quantas conversas chegaram até ele ou além. Recebe, por conversa,
    /// todas as etapas por onde ela passou.
    /// </summary>
    public static List<(EtapaCard Etapa, int Conversas)> Funil(IEnumerable<IEnumerable<EtapaCard>> etapasPorConversa)
    {
        var maximos = etapasPorConversa.Select(e => e.Select(Degrau).DefaultIfEmpty(0).Max()).Select(m => Math.Max(0, m)).ToList();
        return DegrausFunil.Select((etapa, i) => (etapa, maximos.Count(m => m >= i))).ToList();
    }

    public const string OrigemVitrine = "Pedido pela vitrine";
    public const string OrigemFollowup = "Recuperada por follow-up";
    public const string OrigemDireta = "Atendimento direto";
    public const string OrigemSemConversa = "Sem conversa";

    /// <summary>De onde veio a venda: vitrine ganha de follow-up, que ganha do atendimento direto.</summary>
    public static string Origem(bool temConversa, bool veioDaVitrine, int followupsEnviados) =>
        !temConversa ? OrigemSemConversa
        : veioDaVitrine ? OrigemVitrine
        : followupsEnviados > 0 ? OrigemFollowup
        : OrigemDireta;

    /// <summary>Variação em % do atual sobre o anterior. Null quando o anterior é zero.</summary>
    public static decimal? Variacao(decimal atual, decimal anterior) =>
        anterior == 0 ? null : Math.Round((atual - anterior) * 100m / anterior, 1, MidpointRounding.AwayFromZero);
}
