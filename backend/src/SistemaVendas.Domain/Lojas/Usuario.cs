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
}
