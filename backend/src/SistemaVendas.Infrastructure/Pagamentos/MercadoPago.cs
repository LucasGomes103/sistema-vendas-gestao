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
using SistemaVendas.Infrastructure.Seguranca;

namespace SistemaVendas.Infrastructure.Pagamentos;

/// <summary>
/// Configuração do Mercado Pago no servidor. O token de cada loja fica cifrado em credencial_loja (painel →
/// Configuração → Cartão). MercadoPago__AccessToken e MercadoPago__WebhookSecret, no Render, valem só para a loja
/// original (a mais antiga), como antes. Sem token, o cartão fica desligado e tudo segue só com Pix.
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
    /// que o Render preenche sozinho. Só para o painel mostrar a URL a cadastrar no Mercado Pago (Webhooks):
    /// {UrlPublicaApi}/api/v1/webhooks/mercadopago.
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

    public string? UrlWebhook => string.IsNullOrWhiteSpace(UrlPublicaApi) ? null : UrlPublicaApi.TrimEnd('/') + "/api/v1/webhooks/mercadopago";
}

public record LinkCartao(string OrderId, string Url);

/// <summary>
/// Order como o Mercado Pago informa (buscada com o nosso token: é a fonte confiável). Id é o da order
/// (ORD...); PagamentoId é o da transação aprovada (PAY...), quando há.
/// </summary>
public record PagamentoMp(string Id, string Status, string? StatusDetalhe, string? ReferenciaExterna, decimal Valor, string? Tipo, int? Parcelas,
    string? PagamentoId = null)
{
    /// <summary>Order "processed" é paga e creditada (status_detail "accredited").</summary>
    public bool Aprovado => Status == "processed";

    /// <summary>A referência que mandamos na order: o id do pedido (32 letras e números).</summary>
    public Guid? PedidoId => Guid.TryParseExact(ReferenciaExterna, "N", out var p) || Guid.TryParse(ReferenciaExterna, out p) ? p : null;
}

/// <summary>Cartão da loja pelo Mercado Pago. Cada chamada usa o token da loja dona do pedido.</summary>
public interface IMercadoPago
{
    /// <summary>A loja tem token do Mercado Pago (dela ou, na loja original, o do servidor).</summary>
    Task<bool> ConfiguradoAsync(Guid lojaId, CancellationToken ct);

    /// <summary>Cria a order do Checkout Pro do pedido (só cartão; Pix segue pela chave da loja). Vence com a reserva.</summary>
    Task<LinkCartao> CriarLinkAsync(Guid lojaId, Pedido pedido, string nomeLoja, CancellationToken ct);

    Task<PagamentoMp?> BuscarOrderAsync(Guid lojaId, string id, CancellationToken ct);
}

/// <summary>Checkout Pro pela API de Orders (POST/GET /v1/orders) do Mercado Pago.</summary>
public class MercadoPagoCliente(HttpClient http, IOptions<MercadoPagoOptions> opcoes, CredenciaisDaLoja credenciais,
    Assinaturas.ServicoLimites limites) : IMercadoPago
{
    private readonly MercadoPagoOptions _op = opcoes.Value;

    /// <summary>Tem token e o plano da loja inclui o cartão (o Essencial não inclui).</summary>
    public async Task<bool> ConfiguradoAsync(Guid lojaId, CancellationToken ct) =>
        await credenciais.MercadoPagoAsync(lojaId, ct) is not null && await limites.TemRecursoAsync(lojaId, Assinaturas.RecursoDoPlano.Cartao, ct);

    private async Task<string> TokenAsync(Guid lojaId, CancellationToken ct) =>
        (await credenciais.MercadoPagoAsync(lojaId, ct))?.Valor.AccessToken
        ?? throw new RegraException("Cartão desligado: a loja ainda não tem o token do Mercado Pago (Configuração → Cartão).");

    public async Task<LinkCartao> CriarLinkAsync(Guid lojaId, Pedido pedido, string nomeLoja, CancellationToken ct)
    {
        var token = await TokenAsync(lojaId, ct);
        var agora = DateTimeOffset.UtcNow;
        var corpo = MontarOrder(pedido, nomeLoja, _op, agora, excluirPix: true);
        var (ok, codigo, texto) = await CriarAsync(token, corpo, ct);
        if (!ok && codigo == 400)
        {
            // Se a conta não aceitar excluir o Pix do Mercado Pago, tenta de novo excluindo só o boleto.
            corpo = MontarOrder(pedido, nomeLoja, _op, agora, excluirPix: false);
            (ok, codigo, texto) = await CriarAsync(token, corpo, ct);
        }
        if (!ok)
            throw new RegraException($"O Mercado Pago recusou o link do cartão ({codigo}). Confira o token do Mercado Pago em Configuração → Cartão.");

        var order = JsonSerializer.Deserialize<OrderResposta>(texto)!;
        return new LinkCartao(order.Id, order.CheckoutUrl ?? throw new RegraException("O Mercado Pago não devolveu o link do cartão."));
    }

    private async Task<(bool Ok, int Codigo, string Texto)> CriarAsync(string token, Dictionary<string, object?> corpo, CancellationToken ct)
    {
        using var req = Requisicao(token, HttpMethod.Post, "v1/orders");
        req.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
        // Corpo com tamanho conhecido (sem chunked), como a API espera.
        req.Content = new StringContent(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");
        using var resp = await http.SendAsync(req, ct);
        return (resp.IsSuccessStatusCode, (int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
    }

    public async Task<PagamentoMp?> BuscarOrderAsync(Guid lojaId, string id, CancellationToken ct)
    {
        if (id.Length is 0 or > 64 || !id.All(char.IsAsciiLetterOrDigit))
            return null;
        using var req = Requisicao(await TokenAsync(lojaId, ct), HttpMethod.Get, $"v1/orders/{id}");
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        var o = (await resp.Content.ReadFromJsonAsync<OrderResposta>(ct))!;
        var pago = o.Transactions?.Payments?.FirstOrDefault(p => p.Status == "processed") ?? o.Transactions?.Payments?.FirstOrDefault();
        return new PagamentoMp(o.Id, o.Status ?? "", o.StatusDetail, o.ExternalReference, Valor(o.TotalPaidAmount),
            pago?.PaymentMethod?.Type, pago?.PaymentMethod?.Installments, pago?.Id);
    }

    private static decimal Valor(string? texto) =>
        decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private HttpRequestMessage Requisicao(string token, HttpMethod metodo, string caminho)
    {
        var req = new HttpRequestMessage(metodo, _op.UrlApi.TrimEnd('/') + "/" + caminho);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static string Dinheiro(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Corpo da order (público para teste). Exclui boleto e, quando dá, o Pix do Mercado Pago: o Pix continua pela
    /// chave da loja. O aviso de pagamento vai para a URL cadastrada no painel do Mercado Pago (Webhooks).
    /// </summary>
    public static Dictionary<string, object?> MontarOrder(Pedido pedido, string nomeLoja, MercadoPagoOptions op, DateTimeOffset agora, bool excluirPix = true)
    {
        var resumo = RegrasPedido.Resumo(pedido);
        var titulo = $"Pedido #{pedido.Numero} - {nomeLoja}";
        var corpo = new Dictionary<string, object?>
        {
            ["type"] = "online",
            ["processing_mode"] = "manual",
            ["total_amount"] = Dinheiro(pedido.Total),
            ["external_reference"] = pedido.Id.ToString("N"),
            ["items"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["external_code"] = pedido.Numero.ToString(CultureInfo.InvariantCulture),
                    ["title"] = titulo,
                    ["description"] = resumo.Length > 250 ? resumo[..250] : resumo,
                    ["quantity"] = 1,
                    ["unit_price"] = Dinheiro(pedido.Total)
                }
            },
            // Só o que a documentação do Checkout Pro (Orders) mostra: tirar boleto. Parcelas e o resto ficam no padrão
            // da conta (com "bank_transfer" e "max_installments" o botão Pagar do checkout não habilitava no teste).
            ["config"] = new Dictionary<string, object?>
            {
                ["payment_method"] = new Dictionary<string, object?> { ["not_allowed_types"] = new[] { "ticket" } }
            }
        };
        // Validade em duração ISO 8601 a partir de agora (ex.: PT20H30M), junto com a reserva.
        if (pedido.ExpiraEm is { } expira && expira > agora.AddMinutes(10))
        {
            var resta = expira - agora;
            corpo["expiration_time"] = $"PT{(int)resta.TotalHours}H{resta.Minutes}M";
        }
        return corpo;
    }

    private sealed record OrderResposta(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("status_detail")] string? StatusDetail,
        [property: JsonPropertyName("external_reference")] string? ExternalReference,
        [property: JsonPropertyName("total_paid_amount")] string? TotalPaidAmount,
        [property: JsonPropertyName("checkout_url")] string? CheckoutUrl,
        [property: JsonPropertyName("transactions")] Transacoes? Transactions);

    private sealed record Transacoes([property: JsonPropertyName("payments")] List<PagamentoOrder>? Payments);

    private sealed record PagamentoOrder(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("payment_method")] MetodoPagamento? PaymentMethod);

    private sealed record MetodoPagamento(
        [property: JsonPropertyName("type")] string? Type,
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
