using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Vendas;

namespace SistemaVendas.Infrastructure.Pagamentos;

/// <summary>
/// Credenciais do Mercado Pago, só por variável de ambiente (Render) ou user-secrets, nunca no código:
/// MercadoPago__AccessToken e MercadoPago__WebhookSecret. Sem token, o cartão fica desligado e tudo
/// segue só com Pix.
/// </summary>
public class MercadoPagoOptions
{
    public const string Secao = "MercadoPago";

    /// <summary>Access Token da aplicação (de teste ou de produção).</summary>
    public string? AccessToken { get; set; }

    /// <summary>Assinatura secreta dos webhooks (Suas integrações → Webhooks). Com ela, aviso sem assinatura válida é recusado.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Endereço público da API (ex.: https://sistema-vendas-api.onrender.com). Vazio usa o RENDER_EXTERNAL_URL,
    /// que o Render preenche sozinho. O Mercado Pago avisa os pagamentos em {UrlPublicaApi}/api/v1/webhooks/mercadopago.
    /// </summary>
    public string? UrlPublicaApi { get; set; }

    /// <summary>
    /// Só informativo, para o painel mostrar "modo de teste": ligue enquanto usar as credenciais do vendedor de teste.
    /// </summary>
    public bool ModoTeste { get; set; }

    /// <summary>Endereço da API do Mercado Pago (troca só em teste local).</summary>
    public string UrlApi { get; set; } = "https://api.mercadopago.com/";

    /// <summary>Máximo de parcelas oferecidas no cartão.</summary>
    public int MaxParcelas { get; set; } = 12;

    public bool Configurado => !string.IsNullOrWhiteSpace(AccessToken);

    /// <summary>Credencial de teste antiga (TEST-...): o link usa o ambiente sandbox.</summary>
    public bool CredencialSandbox => AccessToken?.StartsWith("TEST-", StringComparison.Ordinal) == true;

    public string? UrlWebhook => string.IsNullOrWhiteSpace(UrlPublicaApi) ? null : UrlPublicaApi.TrimEnd('/') + "/api/v1/webhooks/mercadopago";
}

public record LinkCartao(string PreferenciaId, string Url);

/// <summary>Pagamento como o Mercado Pago informa (buscado com o nosso token: é a fonte confiável).</summary>
public record PagamentoMp(string Id, string Status, string? StatusDetalhe, string? ReferenciaExterna, decimal Valor, string? Tipo, int? Parcelas)
{
    public bool Aprovado => Status == "approved";

    /// <summary>A referência que mandamos na preferência: "{lojaId}:{pedidoId}".</summary>
    public (Guid LojaId, Guid PedidoId)? Pedido =>
        ReferenciaExterna?.Split(':') is [var l, var p] && Guid.TryParse(l, out var loja) && Guid.TryParse(p, out var pedido)
            ? (loja, pedido)
            : null;
}

public interface IMercadoPago
{
    bool Configurado { get; }

    /// <summary>Cria o link do Checkout Pro do pedido (só cartão; Pix segue pela chave da loja). Vence com a reserva.</summary>
    Task<LinkCartao> CriarLinkAsync(Pedido pedido, string nomeLoja, CancellationToken ct);

    Task<PagamentoMp?> BuscarPagamentoAsync(string id, CancellationToken ct);
}

public class MercadoPagoCliente(HttpClient http, IOptions<MercadoPagoOptions> opcoes) : IMercadoPago
{
    private readonly MercadoPagoOptions _op = opcoes.Value;

    public bool Configurado => _op.Configurado;

    public async Task<LinkCartao> CriarLinkAsync(Pedido pedido, string nomeLoja, CancellationToken ct)
    {
        var corpo = MontarPreferencia(pedido, nomeLoja, _op, DateTimeOffset.UtcNow);
        using var req = Requisicao(HttpMethod.Post, "checkout/preferences");
        // Corpo com tamanho conhecido (sem chunked), como a API espera.
        req.Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        var texto = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new RegraException($"O Mercado Pago recusou o link do cartão ({(int)resp.StatusCode}). Confira o MercadoPago__AccessToken no Render.");

        var pref = JsonSerializer.Deserialize<PreferenciaResposta>(texto)!;
        var url = _op.CredencialSandbox ? pref.SandboxInitPoint ?? pref.InitPoint : pref.InitPoint ?? pref.SandboxInitPoint;
        return new LinkCartao(pref.Id, url ?? throw new RegraException("O Mercado Pago não devolveu o link do cartão."));
    }

    public async Task<PagamentoMp?> BuscarPagamentoAsync(string id, CancellationToken ct)
    {
        if (!id.All(char.IsAsciiDigit))
            return null;
        using var req = Requisicao(HttpMethod.Get, $"v1/payments/{id}");
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        var p = (await resp.Content.ReadFromJsonAsync<PagamentoResposta>(ct))!;
        return new PagamentoMp(p.Id.ToString(CultureInfo.InvariantCulture), p.Status ?? "", p.StatusDetail, p.ExternalReference, p.TransactionAmount, p.PaymentTypeId, p.Installments);
    }

    private HttpRequestMessage Requisicao(HttpMethod metodo, string caminho)
    {
        if (!_op.Configurado)
            throw new RegraException("Cartão desligado: falta o MercadoPago__AccessToken no Render.");
        var req = new HttpRequestMessage(metodo, _op.UrlApi.TrimEnd('/') + "/" + caminho);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _op.AccessToken);
        return req;
    }

    /// <summary>Corpo da preferência (público para teste). Exclui boleto e Pix do Mercado Pago: o Pix continua pela chave da loja.</summary>
    public static Dictionary<string, object?> MontarPreferencia(Pedido pedido, string nomeLoja, MercadoPagoOptions op, DateTimeOffset agora)
    {
        var resumo = RegrasPedido.Resumo(pedido);
        var corpo = new Dictionary<string, object?>
        {
            ["items"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["id"] = pedido.Numero.ToString(CultureInfo.InvariantCulture),
                    ["title"] = $"Pedido #{pedido.Numero} - {nomeLoja}",
                    ["description"] = resumo.Length > 250 ? resumo[..250] : resumo,
                    ["quantity"] = 1,
                    ["currency_id"] = "BRL",
                    ["unit_price"] = pedido.Total
                }
            },
            ["external_reference"] = $"{pedido.LojaId}:{pedido.Id}",
            ["payment_methods"] = new Dictionary<string, object?>
            {
                ["excluded_payment_types"] = new[] { new { id = "ticket" }, new { id = "bank_transfer" }, new { id = "atm" } },
                ["installments"] = Math.Clamp(op.MaxParcelas, 1, 24)
            },
            // Só aprovado ou recusado, sem "em análise": o peixe não fica preso esperando.
            ["binary_mode"] = true
        };
        if (op.UrlWebhook is { } url && url.StartsWith("https://", StringComparison.Ordinal))
            corpo["notification_url"] = url;
        if (pedido.ExpiraEm is { } expira && expira > agora)
        {
            corpo["expires"] = true;
            corpo["expiration_date_from"] = agora.AddMinutes(-5).ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
            corpo["expiration_date_to"] = expira.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        }
        return corpo;
    }

    private sealed record PreferenciaResposta(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("init_point")] string? InitPoint,
        [property: JsonPropertyName("sandbox_init_point")] string? SandboxInitPoint);

    private sealed record PagamentoResposta(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("status_detail")] string? StatusDetail,
        [property: JsonPropertyName("external_reference")] string? ExternalReference,
        [property: JsonPropertyName("transaction_amount")] decimal TransactionAmount,
        [property: JsonPropertyName("payment_type_id")] string? PaymentTypeId,
        [property: JsonPropertyName("installments")] int? Installments);
}

/// <summary>
/// Confere o cabeçalho x-signature dos webhooks ("ts=...,v1=..."): HMAC-SHA256 da frase
/// "id:{data.id};request-id:{x-request-id};ts:{ts};" com a assinatura secreta.
/// </summary>
public static class AssinaturaMercadoPago
{
    public static bool Valida(string? xSignature, string? xRequestId, string? dataId, string segredo)
    {
        if (string.IsNullOrWhiteSpace(xSignature))
            return false;
        string? ts = null, v1 = null;
        foreach (var parte in xSignature.Split(','))
        {
            var kv = parte.Split('=', 2);
            if (kv.Length != 2) continue;
            var chave = kv[0].Trim();
            if (chave == "ts") ts = kv[1].Trim();
            else if (chave == "v1") v1 = kv[1].Trim();
        }
        if (ts is null || v1 is null)
            return false;

        var manifesto = new StringBuilder();
        if (!string.IsNullOrEmpty(dataId))
            manifesto.Append("id:").Append(dataId.ToLowerInvariant()).Append(';');
        if (!string.IsNullOrEmpty(xRequestId))
            manifesto.Append("request-id:").Append(xRequestId).Append(';');
        manifesto.Append("ts:").Append(ts).Append(';');

        var esperado = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(manifesto.ToString())));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(esperado), Encoding.ASCII.GetBytes(v1.ToLowerInvariant()));
    }
}
