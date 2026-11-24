using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Comum;

/// <summary>
/// Cria o cookie de sessão e confere, de tempos em tempos, se o usuário ainda pode usá-lo.
/// Usuário excluído, desativado, com senha trocada ou com papel mudado perde a sessão em até 1 minuto.
/// </summary>
public static class Sessao
{
    public const string ClaimCarimbo = "carimbo";

    /// <summary>
    /// Modo suporte: o administrador da plataforma está vendo o painel de outra loja, só para ler. Guarda a loja dele,
    /// para voltar. Com esta claim, nada que muda dados passa (ver <see cref="SomenteLeitura"/>).
    /// </summary>
    public const string ClaimSuporteOrigem = "suporte_origem";
    private static readonly TimeSpan IntervaloUltimoAcesso = TimeSpan.FromMinutes(5);
    private const string ChaveValidadoEm = "validado_em";
    private static readonly TimeSpan IntervaloValidacao = TimeSpan.FromMinutes(1);

    public static async Task EntrarAsync(HttpContext http, Usuario usuario, Loja loja)
    {
        var identidade = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new Claim(ClaimTypes.Name, usuario.Nome),
            new Claim(ClaimTypes.Email, usuario.Email),
            new Claim(ClaimTypes.Role, usuario.Papel.ToString()),
            new Claim(LojaAtualDaRequisicao.ClaimLoja, usuario.LojaId.ToString()),
            new Claim("loja_nome", loja.Nome),
            new Claim("loja_slug", loja.Slug),
            new Claim(ClaimCarimbo, Carimbo(usuario))
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        http.User = new ClaimsPrincipal(identidade);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, http.User);
    }

    /// <summary>
    /// Administrador entra para ver o painel da loja <paramref name="alvo"/> como o dono vê, sem poder mudar nada.
    /// Continua sendo ele (id, nome, e-mail, carimbo): a sessão cai se a senha dele mudar, como sempre.
    /// </summary>
    public static async Task EntrarComoSuporteAsync(HttpContext http, Usuario admin, Loja alvo)
    {
        var identidade = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new Claim(ClaimTypes.Name, admin.Nome),
            new Claim(ClaimTypes.Email, admin.Email),
            new Claim(ClaimTypes.Role, nameof(Papel.Dono)),
            new Claim(LojaAtualDaRequisicao.ClaimLoja, alvo.Id.ToString()),
            new Claim("loja_nome", alvo.Nome),
            new Claim("loja_slug", alvo.Slug),
            new Claim(ClaimCarimbo, Carimbo(admin)),
            new Claim(ClaimSuporteOrigem, admin.LojaId.ToString())
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        http.User = new ClaimsPrincipal(identidade);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, http.User);
    }

    public static bool EmModoSuporte(ClaimsPrincipal user) => user.HasClaim(c => c.Type == ClaimSuporteOrigem);

    /// <summary>
    /// No modo suporte só passa leitura (GET), sair do modo e sair do sistema. Fica antes dos endpoints, então vale
    /// para todas as rotas, inclusive as que forem criadas depois.
    /// </summary>
    public static async Task SomenteLeitura(HttpContext http, Func<Task> proximo)
    {
        var metodo = http.Request.Method;
        var leitura = HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo);
        var caminho = http.Request.Path.Value ?? "";
        var liberado = caminho.EndsWith("/admin/suporte/sair", StringComparison.OrdinalIgnoreCase)
            || caminho.EndsWith("/auth/logout", StringComparison.OrdinalIgnoreCase);
        if (!leitura && !liberado && EmModoSuporte(http.User))
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            await http.Response.WriteAsJsonAsync(new
            {
                title = "Modo suporte: só leitura",
                detail = "Você está vendo esta loja como suporte e não pode mudar nada. Saia do modo suporte para voltar à sua loja.",
                status = 403,
                codigo = "modo_suporte"
            });
            return;
        }
        await proximo();
    }

    /// <summary>Muda sempre que a senha muda (deriva do hash, que tem sal novo a cada troca).</summary>
    public static string Carimbo(Usuario usuario) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(usuario.SenhaHash)))[..16];

    public static async Task ValidarAsync(CookieValidatePrincipalContext ctx)
    {
        var agora = DateTimeOffset.UtcNow;
        if (ctx.Properties.Items.TryGetValue(ChaveValidadoEm, out var valor)
            && DateTimeOffset.TryParse(valor, out var validadoEm)
            && agora - validadoEm < IntervaloValidacao)
            return;

        var principal = ctx.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            await RejeitarAsync(ctx);
            return;
        }

        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var usuario = await db.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ctx.HttpContext.RequestAborted);

        var lojaAtiva = usuario is not null && await db.Lojas.AsNoTracking()
            .AnyAsync(l => l.Id == usuario.LojaId && l.Status == StatusLoja.Ativa, ctx.HttpContext.RequestAborted);
        var carimbo = principal!.FindFirstValue(ClaimCarimbo);
        var suporte = EmModoSuporte(principal);
        // No modo suporte o papel na sessão é "Dono" da loja vista; quem garante que ainda é administrador é o e-mail.
        var plataforma = ctx.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Infrastructure.Seguranca.PlataformaOptions>>();
        var valido = lojaAtiva && usuario is { Ativo: true }
            && (suporte
                ? plataforma.CurrentValue.EhAdmin(usuario.Email) && principal.FindFirstValue(ClaimSuporteOrigem) == usuario.LojaId.ToString()
                : usuario.Papel.ToString() == principal.FindFirstValue(ClaimTypes.Role))
            && (carimbo is null || carimbo == Carimbo(usuario)); // sessões antigas, sem carimbo, continuam até expirar

        if (!valido)
        {
            await RejeitarAsync(ctx);
            return;
        }

        // Último acesso, para o administrador ver lojas paradas. No máximo uma gravação a cada 5 minutos.
        if (!suporte && (usuario!.UltimoAcessoEm is null || agora - usuario.UltimoAcessoEm >= IntervaloUltimoAcesso))
            await db.Usuarios.IgnoreQueryFilters().Where(u => u.Id == id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.UltimoAcessoEm, agora), ctx.HttpContext.RequestAborted);

        ctx.Properties.Items[ChaveValidadoEm] = agora.ToString("O");
        ctx.ShouldRenew = true;
    }

    private static async Task RejeitarAsync(CookieValidatePrincipalContext ctx)
    {
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
