using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
}
