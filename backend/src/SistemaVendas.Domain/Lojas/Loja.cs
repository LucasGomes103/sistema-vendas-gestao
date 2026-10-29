using SistemaVendas.Domain.Comum;

namespace SistemaVendas.Domain.Lojas;

public class Loja : Entidade
{
    public required string Nome { get; set; }

    /// <summary>Identificador usado na URL do catálogo público.</summary>
    public required string Slug { get; set; }

    /// <summary>phone_number_id da WhatsApp Cloud API (preenchido quando o número for conectado).</summary>
    public string? PhoneNumberId { get; set; }

    public string Fuso { get; set; } = "America/Sao_Paulo";
    public TimeOnly HorarioInicio { get; set; } = new(9, 0);
    public TimeOnly HorarioFim { get; set; } = new(20, 0);
}
