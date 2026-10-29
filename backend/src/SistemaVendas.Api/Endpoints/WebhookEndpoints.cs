using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.WhatsApp;

namespace SistemaVendas.Api.Endpoints;

/// <summary>Mensagem recebida pela ponte local.</summary>
public record MensagemLocal(string De, string? Nome, string Id, string Texto, long Timestamp);

public static class WebhookEndpoints
{
    public const string LimiteWebhook = "webhook";

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

        // Ponte local (QR code, não oficial): converte para o formato da Meta e entra na mesma fila.
        grupo.MapPost("/local", async (MensagemLocal m, HttpRequest req, IOptions<WhatsAppOptions> opcoes, AppDbContext db, CancellationToken ct) =>
        {
            var local = opcoes.Value.Local;
            if (!opcoes.Value.UsaPonteLocal || string.IsNullOrEmpty(local.Segredo))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(req.Headers["x-segredo"].ToString()), Encoding.UTF8.GetBytes(local.Segredo)))
                return Results.Unauthorized();

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
}
