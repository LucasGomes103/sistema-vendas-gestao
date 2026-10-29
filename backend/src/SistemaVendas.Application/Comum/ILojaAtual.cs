namespace SistemaVendas.Application.Comum;

/// <summary>Loja do usuário da requisição. Alimenta o filtro global por loja_id.</summary>
public interface ILojaAtual
{
    Guid? LojaId { get; }
}
