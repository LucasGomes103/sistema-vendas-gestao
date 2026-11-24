using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Estoque;

namespace SistemaVendas.Infrastructure.Assinaturas;

/// <summary>
/// Conta do Mercado Pago DA PLATAFORMA (do Lucas), que recebe a mensalidade das lojas
/// (Assinaturas__MercadoPago__AccessToken). Não confundir com o Mercado Pago de cada loja, que recebe as vendas dela.
/// </summary>
public class CobrancaMercadoPagoOptions
{
    /// <summary>Access Token de produção da conta do Lucas (ou de um vendedor de teste, para testar).</summary>
    public string? AccessToken { get; set; }

    /// <summary>Assinatura secreta dos webhooks da aplicação (Suas integrações → Webhooks). Vazio: usa MercadoPago__OAuth__WebhookSecret.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Para onde o Mercado Pago devolve o dono depois de assinar. Vazio: {Plataforma__UrlPainel}/assinatura.</summary>
    public string? UrlRetorno { get; set; }

    public string UrlApi { get; set; } = "https://api.mercadopago.com/";

    public bool Configurado => !string.IsNullOrWhiteSpace(AccessToken);
}

/// <summary>Assinatura recorrente (preapproval) como o Mercado Pago informa.</summary>
public record AssinaturaMp(string Id, string Status, string? ReferenciaExterna, decimal Valor, int CobrancasPagas, string? UrlPagamento)
{
    /// <summary>A referência que mandamos: o id da loja.</summary>
    public Guid? LojaId => Guid.TryParseExact(ReferenciaExterna, "N", out var l) || Guid.TryParse(ReferenciaExterna, out l) ? l : null;
}

/// <summary>Uma cobrança mensal (authorized_payment) da assinatura. StatusPagamento: approved, rejected...</summary>
public record CobrancaMp(string Id, string? AssinaturaId, string Status, string? StatusPagamento, decimal Valor)
{
    public bool Aprovada => StatusPagamento == "approved" || (Status == "processed" && StatusPagamento is null);
}

public interface ICobrancaMercadoPago
{
    bool Configurado { get; }

    Task<AssinaturaMp> CriarAsync(string motivo, Guid lojaId, string emailPagador, decimal valor, DateTimeOffset? inicio, string urlRetorno, CancellationToken ct);
    Task<AssinaturaMp?> BuscarAsync(string id, CancellationToken ct);
    Task AlterarValorAsync(string id, string motivo, decimal valor, CancellationToken ct);
    Task CancelarAsync(string id, CancellationToken ct);
    Task<CobrancaMp?> BuscarCobrancaAsync(string id, CancellationToken ct);
}

/// <summary>API de Assinaturas do Mercado Pago (POST/GET/PUT /preapproval, GET /authorized_payments/{id}).</summary>
public class CobrancaMercadoPago(HttpClient http, IOptions<AssinaturasOptions> opcoes) : ICobrancaMercadoPago
{
    private CobrancaMercadoPagoOptions Op => opcoes.Value.MercadoPago;

    public bool Configurado => Op.Configurado;

    /// <summary>Corpo da assinatura sem plano associado, com pagamento pendente (o dono paga no link). Público para teste.</summary>
    public static Dictionary<string, object?> MontarAssinatura(string motivo, Guid lojaId, string emailPagador, decimal valor, DateTimeOffset? inicio, string urlRetorno)
    {
        var recorrencia = new Dictionary<string, object?>
        {
            ["frequency"] = 1,
            ["frequency_type"] = "months",
            ["transaction_amount"] = valor,
            ["currency_id"] = "BRL"
        };
        // Ainda no teste grátis: a primeira cobrança sai quando o teste acaba.
        if (inicio is { } i)
            recorrencia["start_date"] = i.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return new()
        {
            ["reason"] = motivo,
            ["external_reference"] = lojaId.ToString("N"),
            ["payer_email"] = emailPagador,
            ["auto_recurring"] = recorrencia,
            ["back_url"] = urlRetorno,
            ["status"] = "pending"
        };
    }

    public async Task<AssinaturaMp> CriarAsync(string motivo, Guid lojaId, string emailPagador, decimal valor, DateTimeOffset? inicio, string urlRetorno, CancellationToken ct)
    {
        using var req = Requisicao(HttpMethod.Post, "preapproval");
        req.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
        req.Content = Json(MontarAssinatura(motivo, lojaId, emailPagador, valor, inicio, urlRetorno));
        using var resp = await http.SendAsync(req, ct);
        var texto = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new RegraException($"O Mercado Pago recusou a assinatura ({(int)resp.StatusCode}): {Mensagem(texto)}");
        return Ler(JsonSerializer.Deserialize<PreapprovalResposta>(texto)!);
    }

    public async Task<AssinaturaMp?> BuscarAsync(string id, CancellationToken ct)
    {
        if (!IdValido(id))
            return null;
        using var req = Requisicao(HttpMethod.Get, $"preapproval/{id}");
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        return Ler((await resp.Content.ReadFromJsonAsync<PreapprovalResposta>(ct))!);
    }

    public Task AlterarValorAsync(string id, string motivo, decimal valor, CancellationToken ct) =>
        AlterarAsync(id, new Dictionary<string, object?>
        {
            ["reason"] = motivo,
            ["auto_recurring"] = new Dictionary<string, object?> { ["transaction_amount"] = valor, ["currency_id"] = "BRL" }
        }, ct);

    public Task CancelarAsync(string id, CancellationToken ct) =>
        AlterarAsync(id, new Dictionary<string, object?> { ["status"] = "cancelled" }, ct);

    private async Task AlterarAsync(string id, Dictionary<string, object?> corpo, CancellationToken ct)
    {
        if (!IdValido(id))
            throw new RegraException("Assinatura do Mercado Pago inválida.");
        using var req = Requisicao(HttpMethod.Put, $"preapproval/{id}");
        req.Content = Json(corpo);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new RegraException($"O Mercado Pago recusou a mudança na assinatura ({(int)resp.StatusCode}): {Mensagem(await resp.Content.ReadAsStringAsync(ct))}");
    }

    public async Task<CobrancaMp?> BuscarCobrancaAsync(string id, CancellationToken ct)
    {
        if (!IdValido(id))
            return null;
        using var req = Requisicao(HttpMethod.Get, $"authorized_payments/{id}");
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        resp.EnsureSuccessStatusCode();
        var c = (await resp.Content.ReadFromJsonAsync<CobrancaResposta>(ct))!;
        return new CobrancaMp(Texto(c.Id) ?? id, c.PreapprovalId, c.Status ?? "", c.Payment?.Status, Numero(c.TransactionAmount));
    }

    private static AssinaturaMp Ler(PreapprovalResposta p) =>
        new(p.Id, p.Status ?? "", p.ExternalReference, Numero(p.AutoRecurring?.TransactionAmount),
            (int)Numero(p.Summarized?.ChargedQuantity), p.InitPoint);

    private static bool IdValido(string id) => id.Length is > 0 and <= 64 && id.All(char.IsAsciiLetterOrDigit);

    private HttpRequestMessage Requisicao(HttpMethod metodo, string caminho)
    {
        if (!Configurado)
            throw new RegraException("A cobrança da mensalidade ainda não foi configurada na plataforma.");
        var req = new HttpRequestMessage(metodo, Op.UrlApi.TrimEnd('/') + "/" + caminho);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Op.AccessToken);
        return req;
    }

    private static StringContent Json(object corpo) => new(JsonSerializer.Serialize(corpo), Encoding.UTF8, "application/json");

    /// <summary>Mensagem de erro do Mercado Pago, curta, para o log e o painel.</summary>
    private static string Mensagem(string texto)
    {
        try
        {
            using var doc = JsonDocument.Parse(texto);
            if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                return m.GetString()!;
        }
        catch (JsonException)
        {
        }
        return texto.Length > 200 ? texto[..200] : texto;
    }

    /// <summary>Valores vêm como número ou como texto ("24.50"), dependendo do campo.</summary>
    private static decimal Numero(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.Number } n => n.GetDecimal(),
        { ValueKind: JsonValueKind.String } s when decimal.TryParse(s.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) => v,
        _ => 0
    };

    private static string? Texto(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.String } s => s.GetString(),
        { ValueKind: JsonValueKind.Number } n => n.GetRawText(),
        _ => null
    };

    private sealed record PreapprovalResposta(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("external_reference")] string? ExternalReference,
        [property: JsonPropertyName("init_point")] string? InitPoint,
        [property: JsonPropertyName("auto_recurring")] Recorrencia? AutoRecurring,
        [property: JsonPropertyName("summarized")] Resumo? Summarized);

    private sealed record Recorrencia([property: JsonPropertyName("transaction_amount")] JsonElement? TransactionAmount);

    private sealed record Resumo([property: JsonPropertyName("charged_quantity")] JsonElement? ChargedQuantity);

    private sealed record CobrancaResposta(
        [property: JsonPropertyName("id")] JsonElement? Id,
        [property: JsonPropertyName("preapproval_id")] string? PreapprovalId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("transaction_amount")] JsonElement? TransactionAmount,
        [property: JsonPropertyName("payment")] PagamentoDaCobranca? Payment);

    private sealed record PagamentoDaCobranca([property: JsonPropertyName("status")] string? Status);
}
