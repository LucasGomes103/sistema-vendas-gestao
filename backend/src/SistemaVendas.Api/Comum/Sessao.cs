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

        var carimbo = principal!.FindFirstValue(ClaimCarimbo);
        var valido = usuario is { Ativo: true }
            && usuario.Papel.ToString() == principal.FindFirstValue(ClaimTypes.Role)
            && (carimbo is null || carimbo == Carimbo(usuario)); // sessões antigas, sem carimbo, continuam até expirar

        if (!valido)
        {
            await RejeitarAsync(ctx);
            return;
        }

        ctx.Properties.Items[ChaveValidadoEm] = agora.ToString("O");
        ctx.ShouldRenew = true;
    }

    private static async Task RejeitarAsync(CookieValidatePrincipalContext ctx)
    {
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
