namespace SistemaVendas.Application.Comum;

/// <summary>Loja do usuário da requisição. Alimenta o filtro global por loja_id.</summary>
public interface ILojaAtual
{
    Guid? LojaId { get; }

    /// <summary>
    /// Define a loja quando ela não vem do login: rotas públicas (pelo slug)
    /// e workers em segundo plano (pelo número do WhatsApp).
    /// </summary>
    void Definir(Guid lojaId);
}
