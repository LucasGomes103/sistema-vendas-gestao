using SistemaVendas.Domain.Comum;

namespace SistemaVendas.Domain.Lojas;

/// <summary>
/// Uso do robô por loja e por dia: quantas respostas deu, quantas chamadas fez à IA e quantos tokens gastou.
/// Base para o painel do administrador e para os limites por plano.
/// </summary>
public class UsoIa : EntidadeDaLoja
{
    public DateOnly Dia { get; set; }

    /// <summary>Respostas que o robô mandou (pela IA ou pelas regras).</summary>
    public int Respostas { get; set; }

    /// <summary>Respostas que saíram da IA (sem falha). É o que conta para o limite do mês da loja.</summary>
    public int RespostasIa { get; set; }

    /// <summary>Chamadas feitas à IA (uma resposta pode usar várias, quando consulta estoque, frete etc.).</summary>
    public int Chamadas { get; set; }
    public long TokensEntrada { get; set; }
    public long TokensSaida { get; set; }

    /// <summary>Vezes em que a IA falhou (limite grátis, instabilidade) e o robô respondeu pelas regras.</summary>
    public int Falhas { get; set; }
}

/// <summary>
/// O que o administrador da plataforma fez: editar, suspender, ativar, excluir loja, entrar para ver como a loja.
/// Não tem loja_id de propósito: o registro fica mesmo depois que a loja é excluída.
/// </summary>
public class AuditoriaAdmin : Entidade
{
    public required string AdminEmail { get; set; }
    public required string Acao { get; set; }
    public Guid? LojaAlvoId { get; set; }
    public string? LojaAlvoSlug { get; set; }
    public string? Detalhe { get; set; }
}

public static class AcaoAdmin
{
    public const string Editar = "editar";
    public const string Suspender = "suspender";
    public const string Ativar = "ativar";
    public const string Excluir = "excluir";
    public const string VerComoLoja = "ver_como_loja";
    public const string SairDaLoja = "sair_da_loja";
}

public static class RegrasLimiteIa
{
    /// <summary>Limite que vale para a loja: o dela, senão o padrão da plataforma. 0 = sem limite.</summary>
    public static int LimiteDaLoja(int? limiteLoja, int padrao) => Math.Max(0, limiteLoja ?? padrao);

    /// <summary>
    /// Limite que vale para a loja: o próprio (definido pelo administrador), senão o do plano, senão o padrão da
    /// plataforma (lojas isentas). 0 = sem limite.
    /// </summary>
    public static int LimiteDaLoja(int? limiteLoja, Plano? plano, int padrao) => Math.Max(0, limiteLoja ?? plano?.RespostasIa ?? padrao);

    public static bool Esgotado(int usadas, int limite) => limite > 0 && usadas >= limite;

    /// <summary>Primeiro dia do mês no horário de Brasília (o mesmo dia que o uso_ia grava).</summary>
    public static DateOnly InicioDoMes(DateTimeOffset agora)
    {
        var hoje = DateOnly.FromDateTime(agora.ToOffset(TimeSpan.FromHours(-3)).Date);
        return new(hoje.Year, hoje.Month, 1);
    }
}
