using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Infrastructure.Seguranca;

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

    /// <summary>
    /// A ponte por QR está ligada na plataforma (WhatsApp__Provedor=Local). Ela não faz parte do produto: só a loja da
    /// ponte (WhatsApp__Local__LojaSlug, a ECS Fish) usa, enquanto a Meta oficial não sai. Para desligar de vez, troque
    /// WhatsApp__Provedor para Meta no Render; as outras lojas usam a Meta (ou o simulador) de qualquer jeito.
    /// </summary>
    public bool UsaPonteLocal => string.Equals(Provedor, "Local", StringComparison.OrdinalIgnoreCase);

    /// <summary>Esta loja (pelo slug) é a que atende pela ponte por QR.</summary>
    public bool PonteDaLoja(string? slug) =>
        UsaPonteLocal && !string.IsNullOrWhiteSpace(slug) && string.Equals(slug.Trim(), Local.LojaSlug?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>"Local" só para a loja da ponte; as demais sempre "Meta".</summary>
    public string ProvedorDa(string? slug) => PonteDaLoja(slug) ? "Local" : "Meta";

    public class LocalOptions
    {
        public string Url { get; set; } = "http://localhost:5050";

        /// <summary>Segredo compartilhado entre a API e a ponte local.</summary>
        public string? Segredo { get; set; }

        /// <summary>Loja que atende pelo número conectado na ponte.</summary>
        public string LojaSlug { get; set; } = "demo";
    }

    /// <summary>
    /// Token de acesso na Meta, só para a loja original (a mais antiga). Cada loja nova tem o token dela, cifrado em
    /// credencial_loja. Sem token para a loja, o envio é simulado.
    /// </summary>
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

/// <summary>WhatsApp Cloud API com o token de cada loja. Loja sem token: envio simulado, como em desenvolvimento.</summary>
public class WhatsAppCloudCliente(HttpClient http, IOptions<WhatsAppOptions> opcoes, CredenciaisDaLoja credenciais, WhatsAppSimulado simulado) : IWhatsAppCliente
{
    public async Task<bool> SimuladoAsync(Guid lojaId, CancellationToken ct) => await credenciais.WhatsAppAsync(lojaId, ct) is null;

    public async Task<string> EnviarTextoAsync(Guid lojaId, string phoneNumberId, string para, string texto, CancellationToken ct)
    {
        if (await credenciais.WhatsAppAsync(lojaId, ct) is not { } credencial)
            return await simulado.EnviarTextoAsync(lojaId, phoneNumberId, para, texto, ct);
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
        req.Headers.Authorization = new("Bearer", credencial.Valor.AccessToken);

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
    public Task<bool> SimuladoAsync(Guid lojaId, CancellationToken ct) => Task.FromResult(false);

    public async Task<string> EnviarTextoAsync(Guid lojaId, string phoneNumberId, string para, string texto, CancellationToken ct)
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

/// <summary>Confere na Meta se o token enxerga o número (prova de que o número é da conta da loja).</summary>
public class VerificadorWhatsApp(HttpClient http, IOptions<WhatsAppOptions> opcoes)
{
    /// <summary>Devolve o número de telefone exibido pela Meta, ou o erro.</summary>
    public async Task<(string? Numero, string? Erro)> ConferirAsync(string phoneNumberId, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://graph.facebook.com/{opcoes.Value.ApiVersion}/{phoneNumberId}?fields=display_phone_number,verified_name");
        req.Headers.Authorization = new("Bearer", token);
        try
        {
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return (null, $"A Meta recusou o token para este número ({(int)resp.StatusCode}). Confira o Phone Number ID e o token.");
            using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return (json.RootElement.TryGetProperty("display_phone_number", out var n) ? n.GetString() : phoneNumberId, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, "Não deu para falar com a Meta agora. Tente de novo em instantes.");
        }
    }
}

/// <summary>
/// Escolhe por onde sai a mensagem de cada loja: a loja da ponte local (WhatsApp__Local__LojaSlug, só quando o
/// provedor é "Local") sai pela ponte; todas as outras pela Cloud API com o token delas (ou simulado, sem token).
/// Assim a ponte da ECS Fish nunca envia mensagem de outra loja.
/// </summary>
public class WhatsAppRoteador(IOptions<WhatsAppOptions> opcoes, Dados.AppDbContext db, IServiceProvider servicos) : IWhatsAppCliente
{
    private async Task<IWhatsAppCliente> ClienteAsync(Guid lojaId, CancellationToken ct)
    {
        var o = opcoes.Value;
        if (o.UsaPonteLocal && o.PonteDaLoja(await db.Lojas.Where(l => l.Id == lojaId).Select(l => l.Slug).FirstOrDefaultAsync(ct)))
            return servicos.GetRequiredService<WhatsAppLocalCliente>();
        return servicos.GetRequiredService<WhatsAppCloudCliente>();
    }

    public async Task<bool> SimuladoAsync(Guid lojaId, CancellationToken ct) => await (await ClienteAsync(lojaId, ct)).SimuladoAsync(lojaId, ct);

    public async Task<string> EnviarTextoAsync(Guid lojaId, string phoneNumberId, string para, string texto, CancellationToken ct) =>
        await (await ClienteAsync(lojaId, ct)).EnviarTextoAsync(lojaId, phoneNumberId, para, texto, ct);
}

/// <summary>Usado em desenvolvimento enquanto não há token da Meta: registra e devolve um wamid falso.</summary>
public class WhatsAppSimulado(ILogger<WhatsAppSimulado> logger) : IWhatsAppCliente
{
    public Task<bool> SimuladoAsync(Guid lojaId, CancellationToken ct) => Task.FromResult(true);

    public Task<string> EnviarTextoAsync(Guid lojaId, string phoneNumberId, string para, string texto, CancellationToken ct)
    {
        logger.LogInformation("[WhatsApp simulado] envio para {Para}: {Texto}", Domain.Atendimento.RegrasAtendimento.MascararTelefone(para), texto);
        return Task.FromResult($"sim.{Guid.CreateVersion7():N}");
    }
}
