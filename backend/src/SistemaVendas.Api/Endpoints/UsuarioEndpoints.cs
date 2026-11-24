using SistemaVendas.Infrastructure.Assinaturas;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Api.Comum;
using SistemaVendas.Application.Usuarios;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Endpoints;

/// <summary>Gerenciamento da equipe da loja: só o dono cria, edita, redefine senha e exclui.</summary>
public static class UsuarioEndpoints
{
    public static void MapUsuarioEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/usuarios").WithTags("Usuários").RequireAuthorization(AuthEndpoints.PoliticaDono);

        grupo.MapGet("/", async (AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var eu = IdDe(user);
            var usuarios = await db.Usuarios.AsNoTracking().OrderByDescending(u => u.Ativo).ThenBy(u => u.Nome).ToListAsync(ct);
            return usuarios.Select(u => Saida(u, eu));
        });

        grupo.MapPost("/", async (UsuarioNovo entrada, IValidator<UsuarioNovo> validator, AppDbContext db, ServicoLimites limites, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var validacao = await validator.ValidateAsync(entrada, ct);
            if (!validacao.IsValid)
                return Results.ValidationProblem(validacao.ToDictionary());
            if (await ErroDoPlanoAsync(limites, LojaDe(user), entrada.Papel, maisUmAtivo: true, ct) is { } limite)
                return Results.Problem(limite, statusCode: StatusCodes.Status422UnprocessableEntity);

            var email = Normalizar(entrada.Email);
            if (await EmailEmUsoAsync(db, email, null, ct))
                return EmailEmUso();

            var usuario = new Usuario { Nome = entrada.Nome.Trim(), Email = email, Papel = entrada.Papel };
            usuario.SenhaHash = new PasswordHasher<Usuario>().HashPassword(usuario, entrada.Senha);
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/usuarios/{usuario.Id}", Saida(usuario, IdDe(user)));
        });

        grupo.MapPut("/{id:guid}", async (Guid id, UsuarioEdicao entrada, IValidator<UsuarioEdicao> validator, AppDbContext db, ServicoLimites limites, HttpContext http, CancellationToken ct) =>
        {
            var validacao = await validator.ValidateAsync(entrada, ct);
            if (!validacao.IsValid)
                return Results.ValidationProblem(validacao.ToDictionary());

            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (usuario is null)
                return Results.NotFound();

            var eu = IdDe(http.User);
            if (RegrasUsuario.ValidarAlteracao(usuario, entrada.Papel, entrada.Ativo, eu, await DonosAtivosAsync(db, ct)) is { } erro)
                return Results.Problem(erro, statusCode: StatusCodes.Status422UnprocessableEntity);

            // Reativar alguém conta como mais um usuário ativo no plano; virar entregador exige o recurso de entregas.
            if (await ErroDoPlanoAsync(limites, usuario.LojaId, usuario.Papel == entrada.Papel ? null : entrada.Papel,
                    maisUmAtivo: entrada.Ativo && !usuario.Ativo, ct) is { } limite)
                return Results.Problem(limite, statusCode: StatusCodes.Status422UnprocessableEntity);

            var email = Normalizar(entrada.Email);
            if (email != usuario.Email && await EmailEmUsoAsync(db, email, usuario.Id, ct))
                return EmailEmUso();

            usuario.Nome = entrada.Nome.Trim();
            usuario.Email = email;
            usuario.Papel = entrada.Papel;
            usuario.Ativo = entrada.Ativo;
            if (!usuario.Ativo || usuario.Papel == Papel.Entregador)
                await LiberarConversasAsync(db, usuario.Id, ct);
            await db.SaveChangesAsync(ct);

            // Editou a si mesmo: o cookie passa a mostrar o nome e o e-mail novos.
            if (usuario.Id == eu)
            {
                var loja = await db.Lojas.AsNoTracking().FirstAsync(l => l.Id == usuario.LojaId, ct);
                await Sessao.EntrarAsync(http, usuario, loja);
            }

            return Results.Ok(Saida(usuario, eu));
        });

        // O dono define uma senha nova para alguém da equipe (esqueceu a senha, por exemplo).
        // A própria senha se troca em /auth/senha, confirmando a atual.
        grupo.MapPost("/{id:guid}/senha", async (Guid id, RedefinicaoDeSenha entrada, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (usuario is null)
                return Results.NotFound();

            if (usuario.Id == IdDe(user))
                return Results.Problem("Para trocar a sua própria senha, use \"Trocar minha senha\" no menu da conta.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            if (RegrasUsuario.ValidarSenha(entrada.NovaSenha) is { } erro)
                return Results.Problem(erro, statusCode: StatusCodes.Status422UnprocessableEntity);

            usuario.SenhaHash = new PasswordHasher<Usuario>().HashPassword(usuario, entrada.NovaSenha);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Nenhuma tabela tem chave estrangeira para usuário (o histórico guarda só o id), então a exclusão é definitiva
        // e o e-mail fica livre para um cadastro novo. Para afastar alguém sem apagar, use "Ativo = false".
        grupo.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (usuario is null)
                return Results.NotFound();

            if (RegrasUsuario.ValidarExclusao(usuario, IdDe(user), await DonosAtivosAsync(db, ct)) is { } erro)
                return Results.Problem(erro, statusCode: StatusCodes.Status422UnprocessableEntity);

            db.Usuarios.Remove(usuario);
            await LiberarConversasAsync(db, usuario.Id, ct);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    /// <summary>Quem saiu da equipe (bloqueado, excluído ou virou entregador) deixa as conversas sem responsável, para outro pegar.</summary>
    private static async Task LiberarConversasAsync(AppDbContext db, Guid usuarioId, CancellationToken ct)
    {
        var agora = DateTimeOffset.UtcNow;
        foreach (var c in await db.Conversas.Where(c => c.ResponsavelId == usuarioId).ToListAsync(ct))
            c.Transferir(null, agora);
    }

    private static Guid IdDe(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static string Normalizar(string email) => email.Trim().ToLowerInvariant();

    private static Guid LojaDe(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(LojaAtualDaRequisicao.ClaimLoja)!);

    /// <summary>Limites do plano: número de usuários ativos e o papel de entregador (só com entregas no plano).</summary>
    private static async Task<string?> ErroDoPlanoAsync(ServicoLimites limites, Guid lojaId, Papel? papelNovo, bool maisUmAtivo, CancellationToken ct)
    {
        if (papelNovo == Papel.Entregador && !await limites.TemRecursoAsync(lojaId, RecursoDoPlano.Entregas, ct))
            return "O plano da loja não inclui entregas, então não tem o papel de entregador. Troque de plano em Assinatura.";
        return maisUmAtivo ? await limites.ErroMaisUmUsuarioAsync(lojaId, ct) : null;
    }

    private static Task<int> DonosAtivosAsync(AppDbContext db, CancellationToken ct) =>
        db.Usuarios.CountAsync(u => u.Papel == Papel.Dono && u.Ativo, ct);

    // O e-mail é único entre todas as lojas (é o login), por isso a busca ignora o filtro por loja.
    private static Task<bool> EmailEmUsoAsync(AppDbContext db, string email, Guid? exceto, CancellationToken ct) =>
        db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == email && u.Id != exceto, ct);

    private static IResult EmailEmUso() =>
        Results.Problem("Já existe um usuário com esse e-mail.", statusCode: StatusCodes.Status409Conflict);

    private static UsuarioSaida Saida(Usuario u, Guid eu) => new(u.Id, u.Nome, u.Email, u.Papel, u.Ativo, u.CriadoEm, u.Id == eu);
}
