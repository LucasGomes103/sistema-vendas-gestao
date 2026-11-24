using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Infrastructure.Assinaturas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Application.Comum;
using SistemaVendas.Infrastructure.Pagamentos;
using SistemaVendas.Infrastructure.Seguranca;
using SistemaVendas.Infrastructure.WhatsApp;

namespace SistemaVendas.Api.Endpoints;

/// <summary>Mensagem recebida pela ponte local.</summary>
public record MensagemLocal(string De, string? Nome, string Id, string Texto, long Timestamp);

public static class WebhookEndpoints
{
    public const string LimiteWebhook = "webhook";

    private static IResult? ValidarPonte(HttpRequest req, WhatsAppOptions opcoes)
    {
        var segredo = opcoes.Local.Segredo;
        if (!opcoes.UsaPonteLocal || string.IsNullOrEmpty(segredo))
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(req.Headers["x-segredo"].ToString()), Encoding.UTF8.GetBytes(segredo))
            ? null
            : Results.Unauthorized();
    }

    public static void MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/webhooks").WithTags("Webhooks").RequireRateLimiting(LimiteWebhook);

        // Verificação feita pela Meta ao cadastrar a URL do webhook.
        grupo.MapGet("/whatsapp", (HttpRequest req, IOptions<WhatsAppOptions> opcoes) =>
        {
            var token = opcoes.Value.VerifyToken;
            return req.Query["hub.mode"] == "subscribe" && !string.IsNullOrEmpty(token) && req.Query["hub.verify_token"] == token
                ? Results.Text(req.Query["hub.challenge"].ToString())
                : Results.StatusCode(StatusCodes.Status403Forbidden);
        });

        // Recebimento: valida a assinatura antes de qualquer coisa, grava o corpo cru e responde 200 na hora.
        grupo.MapPost("/whatsapp", async (HttpRequest req, IOptions<WhatsAppOptions> opcoes, AppDbContext db, CancellationToken ct) =>
        {
            var segredo = opcoes.Value.AppSecret;
            if (string.IsNullOrEmpty(segredo))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms, ct);
            var corpo = ms.ToArray();

            if (!AssinaturaWebhook.Valida(req.Headers["X-Hub-Signature-256"], corpo, segredo))
                return Results.Unauthorized();

            db.WebhooksRecebidos.Add(new WebhookRecebido { Origem = "whatsapp", Payload = Encoding.UTF8.GetString(corpo) });
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // Aviso do Mercado Pago (API de Orders, cartão): ?type=order&data.id=ORD..., com x-signature. O aviso só
        // diz "olhe a order X": status, valor e pedido vêm da API do Mercado Pago com o token DA LOJA, então um aviso
        // falso não confirma nada. Com a assinatura secreta da loja, aviso com x-signature inválida é recusado.
        grupo.MapPost("/mercadopago", async (HttpRequest req, IMercadoPago mp, CredenciaisDaLoja credenciais,
            IOptions<MercadoPagoOAuthOptions> oauth, IOptions<MercadoPagoOptions> opcoesMp,
            ILojaAtual lojaAtual, AppDbContext db, ServicoPedidos pedidos, ILoggerFactory logs, CancellationToken ct) =>
        {
            var log = logs.CreateLogger("MercadoPago");
            var (tipo, id) = await LerAvisoMercadoPagoAsync(req, ct);
            // A aplicação da plataforma tem uma URL de webhook só: os avisos da mensalidade também podem chegar aqui.
            if (EhAvisoDeAssinatura(tipo))
                return await AvisoDeAssinaturaAsync(req, tipo!, id, req.HttpContext.RequestServices, log, ct);
            // "order" (e "orders_v2", nome que aparece em parte da documentação). Outros tópicos não interessam.
            if (tipo is not ("order" or "orders_v2") || string.IsNullOrEmpty(id))
                return Results.Ok();

            // A order não diz a loja: acha pelo pedido que guardou o id da order. Link antigo (trocado por "Enviar
            // cobrança" de novo) não está mais no pedido: só a loja original, com o token do servidor, cai aqui.
            var lojaId = await db.Pedidos.IgnoreQueryFilters().Where(p => p.MpOrderId == id).Select(p => (Guid?)p.LojaId).FirstOrDefaultAsync(ct)
                ?? await credenciais.LojaOriginalAsync(ct);
            if (lojaId is not { } loja || await credenciais.MercadoPagoAsync(loja, ct) is not { } credencial)
                return Results.Ok();

            // Loja conectada pelo botão: o aviso vem assinado com o segredo da aplicação da plataforma.
            var segredo = credencial.Valor.WebhookSecret
                ?? (credencial.Valor.ViaOAuth ? oauth.Value.WebhookSecret ?? opcoesMp.Value.WebhookSecret : null);
            var assinatura = req.Headers["x-signature"].ToString();
            if (!string.IsNullOrEmpty(segredo) && assinatura.Length > 0
                && !AssinaturaMercadoPago.Valida(assinatura, req.Headers["x-request-id"], req.Query["data.id"].FirstOrDefault() ?? id, segredo))
            {
                log.LogWarning("Aviso do Mercado Pago com assinatura inválida (order {Id})", id);
                return Results.Unauthorized();
            }

            var order = await mp.BuscarOrderAsync(loja, id, ct);
            if (order?.PedidoId is not { } pedidoId)
                return Results.Ok(); // order de fora do sistema (ou o teste do painel do MP)

            // O pedido da order tem de ser da mesma loja do token que a buscou.
            var lojaDoPedido = await db.Pedidos.IgnoreQueryFilters().Where(p => p.Id == pedidoId).Select(p => (Guid?)p.LojaId).FirstOrDefaultAsync(ct);
            if (lojaDoPedido != loja)
            {
                log.LogWarning("Order {Id} aponta para pedido de outra loja: ignorada", id);
                return Results.Ok();
            }
            lojaAtual.Definir(loja);
            var resultado = await pedidos.AoPagarNoCartaoAsync(pedidoId, order, ct);
            log.LogInformation("Mercado Pago order {Id} ({Status}): {Resultado}", order.Id, order.Status, resultado);
            return Results.Ok();
        });

        // Mensalidade das lojas (Mercado Pago DA PLATAFORMA): subscription_preapproval e subscription_authorized_payment.
        grupo.MapPost("/assinatura", async (HttpRequest req, ILoggerFactory logs, CancellationToken ct) =>
        {
            var (tipo, id) = await LerAvisoMercadoPagoAsync(req, ct);
            return EhAvisoDeAssinatura(tipo)
                ? await AvisoDeAssinaturaAsync(req, tipo!, id, req.HttpContext.RequestServices, logs.CreateLogger("Assinatura"), ct)
                : Results.Ok();
        });

        // Lista de quem a ponte local pode atender (configurada no painel). A ponte consulta a cada 30 s.
        grupo.MapGet("/local/permitidos", async (HttpRequest req, IOptions<WhatsAppOptions> opcoes, AppDbContext db, CancellationToken ct) =>
        {
            if (ValidarPonte(req, opcoes.Value) is { } erro)
                return erro;

            var loja = await db.Lojas.AsNoTracking().FirstOrDefaultAsync(l => l.Slug == opcoes.Value.Local.LojaSlug, ct);
            return loja is null
                ? Results.Ok(new { atenderTodos = false, numeros = Array.Empty<string>() })
                : Results.Ok(new { atenderTodos = loja.AtenderTodos, numeros = loja.NumerosPermitidos });
        });

        // Ponte local (QR code, não oficial): converte para o formato da Meta e entra na mesma fila.
        grupo.MapPost("/local", async (MensagemLocal m, HttpRequest req, IOptions<WhatsAppOptions> opcoes, AppDbContext db, CancellationToken ct) =>
        {
            var local = opcoes.Value.Local;
            if (ValidarPonte(req, opcoes.Value) is { } erro)
                return erro;

            // Segunda barreira: mesmo que a ponte esteja desatualizada, número fora da lista não entra no sistema.
            var loja = await db.Lojas.AsNoTracking().FirstOrDefaultAsync(l => l.Slug == local.LojaSlug, ct);
            if (loja is null || !loja.PodeAtender(m.De))
                return Results.NoContent();

            var payload = JsonSerializer.Serialize(new
            {
                entry = new[]
                {
                    new
                    {
                        changes = new[]
                        {
                            new
                            {
                                value = new
                                {
                                    metadata = new { phone_number_id = WhatsAppOptions.PrefixoLocal + local.LojaSlug },
                                    contacts = new[] { new { profile = new { name = m.Nome }, wa_id = m.De } },
                                    messages = new[]
                                    {
                                        new { from = m.De, id = "local." + m.Id, timestamp = m.Timestamp.ToString(), type = "text", text = new { body = m.Texto } }
                                    }
                                }
                            }
                        }
                    }
                }
            });

            db.WebhooksRecebidos.Add(new WebhookRecebido { Origem = "local", Payload = payload });
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }

    private static bool EhAvisoDeAssinatura(string? tipo) =>
        tipo is "subscription_preapproval" or "subscription_authorized_payment" or "preapproval" or "authorized_payment";

    /// <summary>
    /// Aviso da mensalidade: confere a assinatura (quando há segredo) e relê tudo na API do Mercado Pago com o token da
    /// plataforma. Um aviso falso não muda nada: no máximo faz o sistema ler de novo o que já está lá.
    /// </summary>
    private static async Task<IResult> AvisoDeAssinaturaAsync(HttpRequest req, string tipo, string? id, IServiceProvider sp, ILogger log, CancellationToken ct)
    {
        var cobranca = sp.GetRequiredService<ServicoCobranca>();
        if (string.IsNullOrEmpty(id) || !cobranca.Disponivel)
            return Results.Ok();
        var segredo = sp.GetRequiredService<IOptions<AssinaturasOptions>>().Value.MercadoPago.WebhookSecret
            ?? sp.GetRequiredService<IOptions<MercadoPagoOAuthOptions>>().Value.WebhookSecret;
        var assinatura = req.Headers["x-signature"].ToString();
        if (!string.IsNullOrEmpty(segredo) && assinatura.Length > 0
            && !AssinaturaMercadoPago.Valida(assinatura, req.Headers["x-request-id"], req.Query["data.id"].FirstOrDefault() ?? id, segredo))
        {
            log.LogWarning("Aviso de assinatura com assinatura inválida ({Tipo} {Id})", tipo, id);
            return Results.Unauthorized();
        }
        try
        {
            var (resultado, loja) = tipo.EndsWith("authorized_payment", StringComparison.Ordinal)
                ? await cobranca.ProcessarCobrancaAsync(id, ct)
                : await cobranca.SincronizarAsync(id, ct);
            log.LogInformation("Aviso de assinatura {Tipo} {Id}: {Resultado} (loja {Loja})", tipo, id, resultado, loja);
            if (loja is { } l && resultado is ResultadoCobranca.Paga or ResultadoCobranca.Recusada or ResultadoCobranca.Cancelada)
                await sp.GetRequiredService<AvisosAssinatura>().AoCobrarAsync(l, resultado, id, ct);
            return Results.Ok();
        }
        catch (HttpRequestException e)
        {
            // Responde erro para o Mercado Pago mandar o aviso de novo mais tarde.
            log.LogWarning("Não consegui ler {Tipo} {Id} no Mercado Pago: {Erro}", tipo, id, e.Message);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>Tipo ("order") e id do aviso, da URL ou do corpo JSON.</summary>
    private static async Task<(string? Tipo, string? Id)> LerAvisoMercadoPagoAsync(HttpRequest req, CancellationToken ct)
    {
        string? tipo = req.Query["type"].FirstOrDefault() ?? req.Query["topic"].FirstOrDefault();
        string? id = req.Query["data.id"].FirstOrDefault() ?? req.Query["id"].FirstOrDefault();
        if (tipo is not null && id is not null)
            return (tipo, id);
        try
        {
            using var doc = await JsonDocument.ParseAsync(req.Body, cancellationToken: ct);
            var raiz = doc.RootElement;
            if (raiz.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                tipo ??= t.GetString();
            else if (raiz.TryGetProperty("topic", out var tp) && tp.ValueKind == JsonValueKind.String)
                tipo ??= tp.GetString();
            if (raiz.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("id", out var di))
                id ??= di.ValueKind == JsonValueKind.Number ? di.GetRawText() : di.GetString();
        }
        catch (JsonException)
        {
        }
        return (tipo, id);
    }
}
