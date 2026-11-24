using System.Security.Claims;
using SistemaVendas.Infrastructure.Assinaturas;

namespace SistemaVendas.Api.Comum;

/// <summary>
/// Mensalidade atrasada além da carência: o painel da loja fica só para consulta. Passa leitura (GET), a tela de
/// assinatura (para pagar), entrar e sair, e o painel da plataforma. Fica antes dos endpoints, então vale para todas as
/// rotas, inclusive as que forem criadas depois.
/// </summary>
public static class BloqueioPorAtraso
{
    public const string Codigo = "assinatura_bloqueada";

    private static readonly string[] Liberados = ["/api/v1/assinatura", "/api/v1/auth/", "/api/v1/admin", "/api/v1/webhooks/", "/api/v1/cadastro"];

    public static async Task Middleware(HttpContext http, Func<Task> proximo)
    {
        var metodo = http.Request.Method;
        var leitura = HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo);
        var caminho = http.Request.Path.Value ?? "";
        if (!leitura && http.User.Identity?.IsAuthenticated == true
            && !Liberados.Any(l => caminho.StartsWith(l, StringComparison.OrdinalIgnoreCase))
            && Guid.TryParse(http.User.FindFirstValue(LojaAtualDaRequisicao.ClaimLoja), out var loja)
            && (await http.RequestServices.GetRequiredService<ServicoAssinatura>().EstadoAsync(loja, http.RequestAborted)).Bloqueada)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.Response.WriteAsJsonAsync(new
            {
                title = "Assinatura bloqueada",
                detail = "A mensalidade está atrasada: o painel está só para consulta e o robô está parado. Regularize em Assinatura (menu da conta).",
                status = 403,
                codigo = Codigo
            });
            return;
        }
        await proximo();
    }
}
