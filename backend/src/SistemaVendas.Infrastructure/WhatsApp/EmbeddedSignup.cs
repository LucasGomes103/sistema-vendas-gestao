using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SistemaVendas.Infrastructure.WhatsApp;

/// <summary>
/// Botão "Conectar WhatsApp" (Embedded Signup da Meta) com o app da PLATAFORMA (conta do Lucas, Tech Provider).
/// Meta for Developers → app da plataforma → WhatsApp → Embedded Signup: crie a configuração (Facebook Login for
/// Business) e copie o Configuration ID.
/// </summary>
public class EmbeddedSignupOptions
{
    public const string Secao = "WhatsApp:EmbeddedSignup";

    /// <summary>App ID do app da plataforma na Meta (WhatsApp__EmbeddedSignup__AppId).</summary>
    public string? AppId { get; set; }

    /// <summary>Configuration ID do Facebook Login for Business (WhatsApp__EmbeddedSignup__ConfigId).</summary>
    public string? ConfigId { get; set; }

    public bool Configurado => !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(ConfigId);
}

/// <summary>Resultado da conexão: o número que a Meta mostra, ou o erro para o dono.</summary>
public record NumeroConectado(string Token, string PhoneNumberId, string WabaId, string? Numero, string Pin);

/// <summary>
/// Depois que o dono termina o Embedded Signup no navegador, o painel manda o código e os ids da conta. Aqui: troca o
/// código pelo token da empresa (business integration token), inscreve o app da plataforma nos webhooks da conta do
/// WhatsApp da loja (as mensagens chegam em /api/v1/webhooks/whatsapp), registra o número na Cloud API com um PIN
/// novo e confere o número.
/// </summary>
public class ConexaoWhatsApp(HttpClient http, IOptions<WhatsAppOptions> wa, IOptions<EmbeddedSignupOptions> signup, ILogger<ConexaoWhatsApp> logger)
{
    private string Graph => $"https://graph.facebook.com/{wa.Value.ApiVersion}/";

    /// <summary>O app secret é o mesmo que confere a assinatura dos webhooks (WhatsApp__AppSecret).</summary>
    public bool Disponivel => signup.Value.Configurado && !string.IsNullOrWhiteSpace(wa.Value.AppSecret);

    public async Task<(NumeroConectado? Conectado, string? Erro)> ConectarAsync(string code, string phoneNumberId, string wabaId, CancellationToken ct)
    {
        if (!Disponivel)
            return (null, "O botão do WhatsApp ainda não foi configurado na plataforma.");
        try
        {
            // 1. Código do Embedded Signup → token da empresa (vale enquanto a loja não remover o app).
            var url = $"{Graph}oauth/access_token?client_id={Uri.EscapeDataString(signup.Value.AppId!)}"
                + $"&client_secret={Uri.EscapeDataString(wa.Value.AppSecret!)}&code={Uri.EscapeDataString(code)}";
            using var tokenResp = await http.GetAsync(url, ct);
            var tokenTexto = await tokenResp.Content.ReadAsStringAsync(ct);
            if (!tokenResp.IsSuccessStatusCode)
                return Falha("trocar o código", tokenResp, tokenTexto, "A Meta não liberou o acesso. Tente conectar de novo.");
            var token = JsonDocument.Parse(tokenTexto).RootElement.GetProperty("access_token").GetString()!;

            // 2. Inscreve o app da plataforma nos webhooks da conta do WhatsApp da loja.
            using (var inscricao = await PostAsync($"{wabaId}/subscribed_apps", token, null, ct))
                if (!inscricao.IsSuccessStatusCode)
                    return Falha("inscrever os webhooks", inscricao, await inscricao.Content.ReadAsStringAsync(ct), "A Meta não deixou receber as mensagens desta conta. Tente de novo.");

            // 3. Registra o número na Cloud API. O PIN (verificação em duas etapas) fica guardado com o token.
            var pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");
            using (var registro = await PostAsync($"{phoneNumberId}/register", token, new { messaging_product = "whatsapp", pin }, ct))
                if (!registro.IsSuccessStatusCode)
                    return Falha("registrar o número", registro, await registro.Content.ReadAsStringAsync(ct),
                        "A Meta não registrou o número. Se ele ainda está no aplicativo do WhatsApp, apague a conta do app no celular (ou use o coexistência) e tente de novo.");

            // 4. Confere o número (e pega o número exibido para o resumo).
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{Graph}{phoneNumberId}?fields=display_phone_number");
            req.Headers.Authorization = new("Bearer", token);
            using var info = await http.SendAsync(req, ct);
            string? numero = null;
            if (info.IsSuccessStatusCode && JsonDocument.Parse(await info.Content.ReadAsStringAsync(ct)).RootElement.TryGetProperty("display_phone_number", out var n))
                numero = n.GetString();
            return (new NumeroConectado(token, phoneNumberId, wabaId, numero, pin), null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(e, "Embedded Signup falhou");
            return (null, "Não deu para falar com a Meta agora. Tente de novo em instantes.");
        }
    }

    private async Task<HttpResponseMessage> PostAsync(string caminho, string token, object? corpo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Graph + caminho);
        req.Headers.Authorization = new("Bearer", token);
        if (corpo is not null)
            req.Content = JsonContent.Create(corpo);
        return await http.SendAsync(req, ct);
    }

    private (NumeroConectado?, string?) Falha(string etapa, HttpResponseMessage resp, string corpo, string paraODono)
    {
        // O corpo de erro da Meta não traz token: só o motivo.
        logger.LogWarning("Embedded Signup: a Meta recusou {Etapa} ({Status}): {Corpo}", etapa, (int)resp.StatusCode, corpo[..Math.Min(400, corpo.Length)]);
        return (null, paraODono);
    }
}
