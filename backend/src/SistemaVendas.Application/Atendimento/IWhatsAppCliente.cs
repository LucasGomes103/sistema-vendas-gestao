namespace SistemaVendas.Application.Atendimento;

/// <summary>
/// Envio pela WhatsApp Cloud API. Fica atrás de uma interface para trocar a implementação
/// (Meta de verdade ou simulador local) sem mexer nas regras de negócio.
/// </summary>
public interface IWhatsAppCliente
{
    /// <summary>True quando o envio é simulado (sem token da Meta configurado).</summary>
    bool Simulado { get; }

    /// <summary>Envia texto livre e devolve o wamid da Meta.</summary>
    Task<string> EnviarTextoAsync(string phoneNumberId, string para, string texto, CancellationToken ct);
}
