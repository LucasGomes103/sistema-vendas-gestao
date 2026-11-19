using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Api.Comum;
using SistemaVendas.Application.Usuarios;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Endpoints;

public record LoginEntrada(string Email, string Senha);

public record UsuarioLogado(Guid Id, string Nome, string Email, string Papel, Guid LojaId, string Loja, string LojaSlug);

public static class AuthEndpoints
{
    public const string PoliticaEquipe = "Equipe";
    public const string PoliticaDono = "Dono";
    /// <summary>Equipe e entregador: a agenda de entregas.</summary>
    public const string PoliticaEntregas = "Entregas";
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
            if (loja.Status == StatusLoja.AguardandoConfirmacao)
                return Results.Problem("Confirme o seu e-mail pelo link que enviamos para entrar. Não chegou? Peça outro.",
                    statusCode: StatusCodes.Status403Forbidden, extensions: new Dictionary<string, object?> { ["codigo"] = "email_nao_confirmado" });
            if (loja.Status == StatusLoja.Suspensa)
                return Results.Problem("Esta loja está suspensa. Fale com o suporte.", statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?> { ["codigo"] = "loja_suspensa" });
            await Sessao.EntrarAsync(http, usuario, loja);
            return Results.Ok(DoPrincipal(http.User));
        })
        .RequireRateLimiting(LimiteLogin);

        grupo.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        grupo.MapGet("/eu", (ClaimsPrincipal user) => Results.Ok(DoPrincipal(user)))
            .RequireAuthorization();

        // Qualquer usuário troca a própria senha, confirmando a atual. A sessão é renovada com o carimbo novo.
        grupo.MapPost("/senha", async (TrocaDeSenha entrada, AppDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var id = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id && u.Ativo, ct);
            if (usuario is null)
                return Results.Unauthorized();

            var hasher = new PasswordHasher<Usuario>();
            if (hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, entrada.SenhaAtual ?? "") == PasswordVerificationResult.Failed)
                return Results.Problem("A senha atual não confere.", statusCode: StatusCodes.Status422UnprocessableEntity);

            if (RegrasUsuario.ValidarSenha(entrada.NovaSenha) is { } erro)
                return Results.Problem(erro, statusCode: StatusCodes.Status422UnprocessableEntity);

            usuario.SenhaHash = hasher.HashPassword(usuario, entrada.NovaSenha);
            await db.SaveChangesAsync(ct);

            var loja = await db.Lojas.AsNoTracking().FirstAsync(l => l.Id == usuario.LojaId, ct);
            await Sessao.EntrarAsync(http, usuario, loja);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .RequireRateLimiting(LimiteLogin);
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
