using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Api.Comum;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Endpoints;

public record LoginEntrada(string Email, string Senha);

public record UsuarioLogado(Guid Id, string Nome, string Email, string Papel, Guid LojaId, string Loja, string LojaSlug);

public static class AuthEndpoints
{
    public const string PoliticaEquipe = "Equipe";
    public const string PoliticaDono = "Dono";
    public const string LimiteLogin = "login";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/auth").WithTags("Autenticação");

        grupo.MapPost("/login", async (LoginEntrada entrada, AppDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var email = entrada.Email.Trim().ToLowerInvariant();

            // O login acontece antes de existir loja na requisição, por isso ignora o filtro por loja.
            var usuario = await db.Usuarios.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == email && u.Ativo, ct);

            var hasher = new PasswordHasher<Usuario>();
            if (usuario is null || hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, entrada.Senha) == PasswordVerificationResult.Failed)
                return Results.Problem("E-mail ou senha inválidos.", statusCode: StatusCodes.Status401Unauthorized);

            var loja = await db.Lojas.AsNoTracking().FirstAsync(l => l.Id == usuario.LojaId, ct);

            var identidade = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new Claim(ClaimTypes.Name, usuario.Nome),
                new Claim(ClaimTypes.Email, usuario.Email),
                new Claim(ClaimTypes.Role, usuario.Papel.ToString()),
                new Claim(LojaAtualDaRequisicao.ClaimLoja, usuario.LojaId.ToString()),
                new Claim("loja_nome", loja.Nome),
                new Claim("loja_slug", loja.Slug)
            ], CookieAuthenticationDefaults.AuthenticationScheme);

            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identidade));
            return Results.Ok(DoPrincipal(new ClaimsPrincipal(identidade)));
        })
        .RequireRateLimiting(LimiteLogin);

        grupo.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        grupo.MapGet("/eu", (ClaimsPrincipal user) => Results.Ok(DoPrincipal(user)))
            .RequireAuthorization();
    }

    private static UsuarioLogado DoPrincipal(ClaimsPrincipal user) => new(
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        user.FindFirstValue(ClaimTypes.Name)!,
        user.FindFirstValue(ClaimTypes.Email)!,
        user.FindFirstValue(ClaimTypes.Role)!,
        Guid.Parse(user.FindFirstValue(LojaAtualDaRequisicao.ClaimLoja)!),
        user.FindFirstValue("loja_nome")!,
        user.FindFirstValue("loja_slug")!);
}
