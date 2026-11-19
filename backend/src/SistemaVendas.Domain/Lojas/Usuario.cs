using SistemaVendas.Domain.Comum;

namespace SistemaVendas.Domain.Lojas;

public enum Papel
{
    Dono,
    Vendedor,
    Entregador
}

public class Usuario : EntidadeDaLoja
{
    public required string Nome { get; set; }
    public required string Email { get; set; }
    public Papel Papel { get; set; }
    public bool Ativo { get; set; } = true;

    /// <summary>Hash da senha (PasswordHasher do ASP.NET Core Identity). A senha nunca é guardada.</summary>
    public string SenhaHash { get; set; } = "";

    /// <summary>Quando o dono confirmou o e-mail do cadastro. Nulo = ainda não confirmou e não entra.</summary>
    public DateTimeOffset? EmailConfirmadoEm { get; set; }
}

/// <summary>Link enviado por e-mail (confirmação de cadastro). Só o hash do código fica no banco.</summary>
public class TokenEmail : Comum.Entidade
{
    public Guid UsuarioId { get; set; }

    /// <summary>SHA-256 do código do link, em hexadecimal.</summary>
    public required string Hash { get; set; }

    public DateTimeOffset ExpiraEm { get; set; }
    public DateTimeOffset? UsadoEm { get; set; }
}
