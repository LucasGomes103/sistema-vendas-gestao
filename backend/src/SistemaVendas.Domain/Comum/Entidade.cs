namespace SistemaVendas.Domain.Comum;

/// <summary>Base de toda entidade: id ordenado no tempo (UUID v7) e data de criação em UTC.</summary>
public abstract class Entidade
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset CriadoEm { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Entidade de negócio que pertence a uma loja (multi-loja desde o primeiro dia).</summary>
public abstract class EntidadeDaLoja : Entidade
{
    public Guid LojaId { get; set; }
}
