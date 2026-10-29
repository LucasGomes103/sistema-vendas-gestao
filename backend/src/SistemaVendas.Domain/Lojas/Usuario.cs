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
}
