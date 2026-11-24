using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SistemaVendas.Application.Comum;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.WhatsApp;

namespace SistemaVendas.Api.Endpoints;

public record CodigoPareamentoEntrada(string Numero);

/// <summary>
/// Conexão do número na ponte não oficial (QR code), feita pelo painel: a ponte roda junto da API
/// e não fica exposta na internet, então o painel fala com ela por aqui. Só a loja da ponte
/// (WhatsApp__Local__LojaSlug) enxerga: o dono de outra loja nunca vê o QR nem desconecta o número da ECS Fish.
/// </summary>
public static class ConexaoWhatsAppEndpoints
{
    public static void MapConexaoWhatsAppEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/config/whatsapp/conexao").WithTags("Configuração").RequireAuthorization(AuthEndpoints.PoliticaDono);

        grupo.MapGet("/", (AppDbContext db, ILojaAtual loja, IHttpClientFactory fabrica, IOptions<WhatsAppOptions> opcoes, CancellationToken ct) =>
            ChamarPonte(db, loja, fabrica, opcoes.Value, HttpMethod.Get, "/estado", null, ct));

        grupo.MapPost("/codigo", (CodigoPareamentoEntrada entrada, AppDbContext db, ILojaAtual loja, IHttpClientFactory fabrica, IOptions<WhatsAppOptions> opcoes, CancellationToken ct) =>
            ChamarPonte(db, loja, fabrica, opcoes.Value, HttpMethod.Post, "/estado/codigo", new { numero = entrada.Numero }, ct));

        grupo.MapPost("/desconectar", (AppDbContext db, ILojaAtual loja, IHttpClientFactory fabrica, IOptions<WhatsAppOptions> opcoes, CancellationToken ct) =>
            ChamarPonte(db, loja, fabrica, opcoes.Value, HttpMethod.Post, "/estado/desconectar", new { }, ct));
    }

    private static async Task<IResult> ChamarPonte(AppDbContext db, ILojaAtual loja,
        IHttpClientFactory fabrica, WhatsAppOptions opcoes, HttpMethod metodo, string caminho, object? corpo, CancellationToken ct)
    {
        var slug = opcoes.UsaPonteLocal ? await db.Lojas.Where(l => l.Id == loja.LojaId).Select(l => l.Slug).FirstOrDefaultAsync(ct) : null;
        if (!opcoes.PonteDaLoja(slug))
            return metodo == HttpMethod.Get
                ? Results.Ok(new { disponivel = false, status = "A loja usa a API oficial da Meta." })
                : Results.Problem("A conexão por QR code não está disponível para esta loja.", statusCode: StatusCodes.Status409Conflict);

        using var req = new HttpRequestMessage(metodo, $"{opcoes.Local.Url.TrimEnd('/')}{caminho}");
        req.Headers.Add("x-segredo", opcoes.Local.Segredo);
        if (corpo is not null)
            req.Content = JsonContent.Create(corpo);

        try
        {
            var http = fabrica.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            using var resp = await http.SendAsync(req, ct);
            var json = await resp.Content.ReadFromJsonAsync<JsonObject>(ct) ?? [];
            if (!resp.IsSuccessStatusCode)
                return Results.Problem(json["erro"]?.GetValue<string>() ?? "A ponte do WhatsApp recusou o pedido.", statusCode: StatusCodes.Status409Conflict);

            json["disponivel"] = true;
            return Results.Ok(json);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Logo depois de uma atualização a ponte ainda está subindo.
            return Results.Ok(new { disponivel = true, conectado = false, status = "ponte iniciando, aguarde alguns segundos" });
        }
    }
}
