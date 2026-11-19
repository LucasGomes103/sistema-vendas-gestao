using SistemaVendas.Domain.Comum;

namespace SistemaVendas.Domain.Lojas;

public enum TipoCredencial
{
    WhatsApp,
    MercadoPago
}

/// <summary>
/// Segredo de uma integração da loja (token da Meta, token do Mercado Pago), guardado cifrado com a chave mestra
/// da plataforma. O texto aberto nunca vai para o banco nem volta para o painel: o painel só vê o Resumo.
/// </summary>
public class CredencialLoja : EntidadeDaLoja
{
    public TipoCredencial Tipo { get; set; }

    /// <summary>JSON da credencial cifrado (AES-GCM), no formato "v1:base64".</summary>
    public required string Cifrado { get; set; }

    /// <summary>O que o painel pode mostrar, sem segredo (ex.: "token …a1b2, número 1234").</summary>
    public string? Resumo { get; set; }

    public DateTimeOffset AtualizadoEm { get; set; } = DateTimeOffset.UtcNow;
}
