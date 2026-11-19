namespace SistemaVendas.Application.Atendimento;

/// <summary>
/// Envio pela WhatsApp Cloud API. Fica atrás de uma interface para trocar a implementação
/// (Meta de verdade ou simulador local) sem mexer nas regras de negócio.
/// </summary>
public interface IWhatsAppCliente
{
    /// <summary>True quando o envio da loja é simulado (sem token da Meta para ela).</summary>
    Task<bool> SimuladoAsync(Guid lojaId, CancellationToken ct);

    /// <summary>Envia texto livre pelo número da loja, com o token da loja, e devolve o wamid da Meta.</summary>
    Task<string> EnviarTextoAsync(Guid lojaId, string phoneNumberId, string para, string texto, CancellationToken ct);
}
