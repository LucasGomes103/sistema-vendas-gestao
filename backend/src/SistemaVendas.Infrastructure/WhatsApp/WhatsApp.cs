using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaVendas.Application.Atendimento;

namespace SistemaVendas.Infrastructure.WhatsApp;

/// <summary>Configuração da WhatsApp Cloud API. Os segredos vêm de variáveis de ambiente ou user-secrets.</summary>
public class WhatsAppOptions
{
    public const string Secao = "WhatsApp";

    public string ApiVersion { get; set; } = "v25.0";

    /// <summary>Prefixo do phone_number_id usado pelo simulador do painel.</summary>
    public const string PrefixoSimulador = "sim-";

    /// <summary>Prefixo do phone_number_id das mensagens que chegam pela ponte local (não oficial).</summary>
    public const string PrefixoLocal = "local-";

    /// <summary>"Meta" (Cloud API oficial) ou "Local" (ponte por QR code, só para testes e demonstrações).</summary>
    public string Provedor { get; set; } = "Meta";

    public LocalOptions Local { get; set; } = new();

    public bool UsaPonteLocal => string.Equals(Provedor, "Local", StringComparison.OrdinalIgnoreCase);

    public class LocalOptions
    {
        public string Url { get; set; } = "http://localhost:5050";

        /// <summary>Segredo compartilhado entre a API e a ponte local.</summary>
        public string? Segredo { get; set; }

        /// <summary>Loja que atende pelo número conectado na ponte.</summary>
        public string LojaSlug { get; set; } = "demo";
    }

    /// <summary>Token de acesso do app na Meta. Vazio = envio simulado.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Segredo do app, usado para validar o X-Hub-Signature-256 dos webhooks.</summary>
    public string? AppSecret { get; set; }

    /// <summary>Texto combinado com a Meta na verificação do webhook (hub.verify_token).</summary>
    public string? VerifyToken { get; set; }
}

public static class AssinaturaWebhook
{
    /// <summary>Confere o cabeçalho "sha256=..." (HMAC SHA-256 do corpo cru com o segredo do app).</summary>
    public static bool Valida(string? cabecalho, byte[] corpo, string segredo)
    {
        const string prefixo = "sha256=";
        if (string.IsNullOrEmpty(cabecalho) || !cabecalho.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
            return false;

        byte[] recebido;
        try
        {
            recebido = Convert.FromHexString(cabecalho[prefixo.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var esperado = HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), corpo);
        return CryptographicOperations.FixedTimeEquals(esperado, recebido);
    }
}

public class WhatsAppCloudCliente(HttpClient http, IOptions<WhatsAppOptions> opcoes) : IWhatsAppCliente
{
    public bool Simulado => false;

    public async Task<string> EnviarTextoAsync(string phoneNumberId, string para, string texto, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(phoneNumberId))
            throw new InvalidOperationException("A loja ainda não tem número do WhatsApp configurado.");

        var o = opcoes.Value;
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{o.ApiVersion}/{phoneNumberId}/messages")
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                to = para,
                type = "text",
                text = new { preview_url = false, body = texto }
            })
        };
        req.Headers.Authorization = new("Bearer", o.AccessToken);

        using var resp = await http.SendAsync(req, ct);
        var corpo = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Meta respondeu {(int)resp.StatusCode}: {corpo}");

        using var json = JsonDocument.Parse(corpo);
        return json.RootElement.GetProperty("messages")[0].GetProperty("id").GetString()!;
    }
}

/// <summary>
/// Envio pela ponte local (QR code, não oficial). Só para testes e demonstrações, com número reserva:
/// o WhatsApp pode banir números que usam conexões não oficiais.
/// </summary>
public class WhatsAppLocalCliente(HttpClient http, IOptions<WhatsAppOptions> opcoes) : IWhatsAppCliente
{
    public bool Simulado => false;

    public async Task<string> EnviarTextoAsync(string phoneNumberId, string para, string texto, CancellationToken ct)
    {
        var local = opcoes.Value.Local;
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{local.Url.TrimEnd('/')}/enviar")
        {
            Content = JsonContent.Create(new { para, texto })
        };
        req.Headers.Add("x-segredo", local.Segredo);

        using var resp = await http.SendAsync(req, ct);
        var corpo = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Ponte local respondeu {(int)resp.StatusCode}: {corpo}");

        using var json = JsonDocument.Parse(corpo);
        return "local." + json.RootElement.GetProperty("id").GetString();
    }
}

/// <summary>Usado em desenvolvimento enquanto não há token da Meta: registra e devolve um wamid falso.</summary>
public class WhatsAppSimulado(ILogger<WhatsAppSimulado> logger) : IWhatsAppCliente
{
    public bool Simulado => true;

    public Task<string> EnviarTextoAsync(string phoneNumberId, string para, string texto, CancellationToken ct)
    {
        logger.LogInformation("[WhatsApp simulado] envio para {Para}: {Texto}", Domain.Atendimento.RegrasAtendimento.MascararTelefone(para), texto);
        return Task.FromResult($"sim.{Guid.CreateVersion7():N}");
    }
}
