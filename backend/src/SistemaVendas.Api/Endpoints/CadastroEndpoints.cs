using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SistemaVendas.Api.Comum;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.Email;
using SistemaVendas.Infrastructure.Seguranca;

namespace SistemaVendas.Api.Endpoints;

public record CadastroEntrada(string? NomeLoja, string? Slug, string? Segmento, string? NomeDono, string? Email, string? Senha, bool AceiteTermos);

public record CadastroSaida(string Email, bool EmailEnviado);

public record SlugSaida(string Slug, bool Disponivel, string? Erro);

public record ConfirmarEmailEntrada(string? Token);

public record ReenviarEntrada(string? Email);

/// <summary>
/// Cadastro público: a loja nasce "aguardando confirmação" com o dono, e só entra no painel depois que o dono abre
/// o link enviado por e-mail. O link vale 48 h e só o hash do código fica no banco.
/// </summary>
public static class CadastroEndpoints
{
    public static void MapCadastroEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/cadastro").WithTags("Cadastro").RequireRateLimiting(AuthEndpoints.LimiteLogin);

        grupo.MapGet("/slug/{slug}", async (string slug, AppDbContext db, CancellationToken ct) =>
        {
            var s = slug.Trim().ToLowerInvariant();
            if (RegrasCadastro.ValidarSlug(s) is { } erro)
                return new SlugSaida(s, false, erro);
            var usado = await db.Lojas.AnyAsync(l => l.Slug == s, ct);
            return new SlugSaida(s, !usado, usado ? "Este endereço já é de outra loja." : null);
        });

        grupo.MapPost("/", async (CadastroEntrada e, AppDbContext db, IEnvioEmail email, IOptions<PlataformaOptions> plataforma,
            ILoggerFactory logs, CancellationToken ct) =>
        {
            var nomeLoja = e.NomeLoja?.Trim() ?? "";
            var nomeDono = e.NomeDono?.Trim() ?? "";
            var emailDono = e.Email?.Trim().ToLowerInvariant() ?? "";
            var slug = (string.IsNullOrWhiteSpace(e.Slug) ? RegrasCadastro.SugerirSlug(nomeLoja) : e.Slug.Trim()).ToLowerInvariant();
            var segmento = string.IsNullOrWhiteSpace(e.Segmento) ? null : e.Segmento.Trim();

            string? erro = null;
            if (nomeLoja.Length is < 2 or > 120) erro = "Informe o nome da loja.";
            else if (nomeDono.Length is < 2 or > 120) erro = "Informe o seu nome.";
            else if (!RegrasCadastro.EmailValido(emailDono)) erro = "Informe um e-mail válido.";
            else if (segmento is { Length: > 60 }) erro = "Tipo de loja muito longo.";
            else if (!e.AceiteTermos) erro = "Para criar a conta, aceite os termos de uso e a política de privacidade.";
            else erro = RegrasCadastro.ValidarSlug(slug) ?? RegrasUsuario.ValidarSenha(e.Senha);
            if (erro is not null)
                return Results.Problem(erro, statusCode: StatusCodes.Status422UnprocessableEntity);

            if (await db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == emailDono, ct))
                return Results.Problem("Este e-mail já tem cadastro. Entre pelo login (ou peça um link novo de confirmação).", statusCode: StatusCodes.Status409Conflict);
            if (await db.Lojas.AnyAsync(l => l.Slug == slug, ct))
                return Results.Problem("Este endereço já é de outra loja. Escolha outro.", statusCode: StatusCodes.Status409Conflict);

            var loja = new Loja { Nome = nomeLoja, Slug = slug, Segmento = segmento, Status = StatusLoja.AguardandoConfirmacao };
            var dono = new Usuario { LojaId = loja.Id, Nome = nomeDono, Email = emailDono, Papel = Papel.Dono };
            dono.SenhaHash = new PasswordHasher<Usuario>().HashPassword(dono, e.Senha!);
            db.Lojas.Add(loja);
            db.Usuarios.Add(dono);
            var codigo = NovoToken(db, dono.Id);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Dois cadastros ao mesmo tempo com o mesmo e-mail ou endereço: o índice único segura.
                return Results.Problem("Este e-mail ou endereço acabou de ser usado. Confira e tente de novo.", statusCode: StatusCodes.Status409Conflict);
            }

            var enviado = await EnviarConfirmacaoAsync(email, plataforma.Value, dono, loja, codigo, ct);
            logs.CreateLogger("Cadastro").LogInformation("Loja {Slug} cadastrada pelo site (e-mail enviado: {Enviado}).", slug, enviado);
            return Results.Created($"/api/v1/public/{slug}/catalogo", new CadastroSaida(emailDono, enviado));
        });

        // Abre o link do e-mail: confirma, ativa a loja e já entra no painel.
        grupo.MapPost("/confirmar", async (ConfirmarEmailEntrada e, AppDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var agora = DateTimeOffset.UtcNow;
            var hash = Hash(e.Token ?? "");
            var token = await db.TokensEmail.FirstOrDefaultAsync(t => t.Hash == hash, ct);
            if (token is null || token.UsadoEm is not null || token.ExpiraEm < agora)
                return Results.Problem("Link inválido ou vencido. Peça um novo na tela de entrar.", statusCode: StatusCodes.Status410Gone);

            var dono = await db.Usuarios.IgnoreQueryFilters().FirstAsync(u => u.Id == token.UsuarioId, ct);
            var loja = await db.Lojas.FirstAsync(l => l.Id == dono.LojaId, ct);
            if (loja.Status == StatusLoja.Suspensa)
                return Results.Problem("Esta loja está suspensa. Fale com o suporte.", statusCode: StatusCodes.Status403Forbidden);

            token.UsadoEm = agora;
            dono.EmailConfirmadoEm ??= agora;
            if (loja.Status == StatusLoja.AguardandoConfirmacao)
                loja.Status = StatusLoja.Ativa;
            await db.SaveChangesAsync(ct);

            await Sessao.EntrarAsync(http, dono, loja);
            return Results.Ok(new { loja = loja.Nome, slug = loja.Slug });
        });

        // Sempre responde igual, para não revelar quais e-mails têm cadastro.
        grupo.MapPost("/reenviar", async (ReenviarEntrada e, AppDbContext db, IEnvioEmail email, IOptions<PlataformaOptions> plataforma, CancellationToken ct) =>
        {
            var emailDono = e.Email?.Trim().ToLowerInvariant() ?? "";
            var dono = await db.Usuarios.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == emailDono && u.Papel == Papel.Dono && u.EmailConfirmadoEm == null, ct);
            var loja = dono is null ? null : await db.Lojas.FirstOrDefaultAsync(l => l.Id == dono.LojaId && l.Status == StatusLoja.AguardandoConfirmacao, ct);
            if (dono is not null && loja is not null)
            {
                // No máximo um link novo a cada 2 minutos por pessoa.
                var recente = await db.TokensEmail.AnyAsync(t => t.UsuarioId == dono.Id && t.CriadoEm > DateTimeOffset.UtcNow.AddMinutes(-2), ct);
                if (!recente)
                {
                    var codigo = NovoToken(db, dono.Id);
                    await db.SaveChangesAsync(ct);
                    await EnviarConfirmacaoAsync(email, plataforma.Value, dono, loja, codigo, ct);
                }
            }
            return Results.Accepted();
        });
    }

    private static string Hash(string codigo) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(codigo)));

    /// <summary>Cria o token e devolve o código aberto (vai só no e-mail).</summary>
    private static string NovoToken(AppDbContext db, Guid usuarioId)
    {
        var codigo = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.TokensEmail.Add(new TokenEmail { UsuarioId = usuarioId, Hash = Hash(codigo), ExpiraEm = DateTimeOffset.UtcNow + RegrasCadastro.ValidadeConfirmacao });
        return codigo;
    }

    private static Task<bool> EnviarConfirmacaoAsync(IEnvioEmail email, PlataformaOptions plataforma, Usuario dono, Loja loja, string codigo, CancellationToken ct)
    {
        var link = $"{plataforma.UrlPainel.TrimEnd('/')}/confirmar-email?token={codigo}";
        var texto = $"""
            Olá, {dono.Nome}!

            Recebemos o cadastro da loja "{loja.Nome}". Para ativar a conta e entrar no painel, abra o link abaixo
            (vale por 48 horas):

            {link}

            Se não foi você, ignore este e-mail: sem a confirmação, a conta não é ativada.
            """;
        return email.EnviarAsync(dono.Email, $"Confirme o cadastro da {loja.Nome}", texto, ct);
    }
}
