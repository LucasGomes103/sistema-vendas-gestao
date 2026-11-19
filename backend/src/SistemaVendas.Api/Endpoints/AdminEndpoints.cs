using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Domain.Vendas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.Seguranca;

namespace SistemaVendas.Api.Endpoints;

public record LojaAdminSaida(
    Guid Id, string Nome, string Slug, string? Segmento, string Status, string? MotivoSuspensao, DateTimeOffset CriadoEm,
    string? Dono, string? EmailDono, bool EmailConfirmado, int Usuarios, int Produtos,
    int Conversas30Dias, int Pedidos30Dias, decimal Vendido30Dias, DateTimeOffset? UltimaMensagemEm,
    bool WhatsAppProprio, bool MercadoPagoProprio, bool LojaOriginal);

public record SuspenderEntrada(string? Motivo);

public record EditarLojaEntrada(string? Nome, string? Slug, string? Segmento, string? NomeDono, string? EmailDono);

/// <summary>Para excluir, o administrador digita o endereço (slug) da loja: evita apagar a loja errada.</summary>
public record ExcluirLojaEntrada(string? Confirmacao);

/// <summary>Administrador da plataforma: e-mail listado em Plataforma__Admins (conta do Lucas).</summary>
public class AdminPlataformaRequisito : IAuthorizationRequirement;

public class AdminPlataformaHandler(IOptionsMonitor<PlataformaOptions> opcoes) : AuthorizationHandler<AdminPlataformaRequisito>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminPlataformaRequisito requisito)
    {
        if (opcoes.CurrentValue.EhAdmin(context.User.FindFirstValue(ClaimTypes.Email)))
            context.Succeed(requisito);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Painel do administrador da plataforma: todas as lojas, uso dos últimos 30 dias, suspender, reativar e ativar à
/// mão (quando o e-mail de confirmação não chegou). Só lê números agregados: não abre conversa nem cliente de loja.
/// </summary>
public static class AdminEndpoints
{
    public const string PoliticaAdmin = "AdminPlataforma";

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/admin").WithTags("Administração da plataforma").RequireAuthorization(PoliticaAdmin);

        grupo.MapGet("/lojas", async (AppDbContext db, CredenciaisDaLoja credenciais, CancellationToken ct) =>
        {
            var desde = DateTimeOffset.UtcNow.AddDays(-30);
            var lojas = await db.Lojas.AsNoTracking().OrderByDescending(l => l.CriadoEm).ToListAsync(ct);
            var donos = (await db.Usuarios.IgnoreQueryFilters().AsNoTracking().Where(u => u.Papel == Papel.Dono)
                    .OrderBy(u => u.CriadoEm).Select(u => new { u.LojaId, u.Nome, u.Email, u.EmailConfirmadoEm }).ToListAsync(ct))
                .GroupBy(u => u.LojaId).ToDictionary(g => g.Key, g => g.First());
            var usuarios = await db.Usuarios.IgnoreQueryFilters().GroupBy(u => u.LojaId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
            var produtos = await db.Especies.IgnoreQueryFilters().Where(e => e.Ativo).GroupBy(e => e.LojaId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
            var conversas = await db.Conversas.IgnoreQueryFilters().Where(c => !c.Simulada && c.UltimaMensagemEm >= desde)
                .GroupBy(c => c.LojaId).Select(g => new { g.Key, N = g.Count(), Ultima = g.Max(c => c.UltimaMensagemEm) }).ToDictionaryAsync(x => x.Key, ct);
            var pedidos = await db.Pedidos.IgnoreQueryFilters().Where(p => p.CriadoEm >= desde)
                .GroupBy(p => p.LojaId)
                .Select(g => new { g.Key, N = g.Count(), Vendido = g.Where(p => p.Status == StatusPedido.Pago).Sum(p => p.Total) })
                .ToDictionaryAsync(x => x.Key, ct);
            var credenciaisPorLoja = await db.CredenciaisLoja.IgnoreQueryFilters().GroupBy(c => c.LojaId)
                .Select(g => new { g.Key, Tipos = g.Select(c => c.Tipo).ToList() }).ToDictionaryAsync(x => x.Key, x => x.Tipos, ct);
            var original = await credenciais.LojaOriginalAsync(ct);

            return lojas.Select(l =>
            {
                var dono = donos.GetValueOrDefault(l.Id);
                var conv = conversas.GetValueOrDefault(l.Id);
                var ped = pedidos.GetValueOrDefault(l.Id);
                var cred = credenciaisPorLoja.GetValueOrDefault(l.Id) ?? [];
                return new LojaAdminSaida(l.Id, l.Nome, l.Slug, l.Segmento, l.Status.ToString(), l.MotivoSuspensao, l.CriadoEm,
                    dono?.Nome, dono?.Email, dono?.EmailConfirmadoEm is not null, usuarios.GetValueOrDefault(l.Id), produtos.GetValueOrDefault(l.Id),
                    conv?.N ?? 0, ped?.N ?? 0, ped?.Vendido ?? 0, conv?.Ultima,
                    cred.Contains(TipoCredencial.WhatsApp), cred.Contains(TipoCredencial.MercadoPago), l.Id == original);
            }).ToList();
        });

        grupo.MapPost("/lojas/{id:guid}/suspender", async (Guid id, SuspenderEntrada e, AppDbContext db, ClaimsPrincipal user, ILoggerFactory logs, CancellationToken ct) =>
        {
            var loja = await db.Lojas.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (loja is null)
                return Results.NotFound();
            // Não deixa o administrador trancar a própria loja (e a si mesmo) do lado de fora.
            if (Guid.TryParse(user.FindFirstValue(Comum.LojaAtualDaRequisicao.ClaimLoja), out var propria) && propria == id)
                return Results.Problem("Você não pode suspender a sua própria loja.", statusCode: StatusCodes.Status422UnprocessableEntity);
            loja.Status = StatusLoja.Suspensa;
            loja.MotivoSuspensao = string.IsNullOrWhiteSpace(e.Motivo) ? null : e.Motivo.Trim()[..Math.Min(e.Motivo.Trim().Length, 300)];
            loja.SuspensaEm = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            logs.CreateLogger("Admin").LogWarning("Loja {Slug} suspensa por {Admin}: {Motivo}", loja.Slug, user.FindFirstValue(ClaimTypes.Email), loja.MotivoSuspensao);
            return Results.NoContent();
        });

        grupo.MapPut("/lojas/{id:guid}", async (Guid id, EditarLojaEntrada e, AppDbContext db, ClaimsPrincipal user, ILoggerFactory logs, CancellationToken ct) =>
        {
            var loja = await db.Lojas.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (loja is null)
                return Results.NotFound();
            var nome = e.Nome?.Trim() ?? "";
            var slug = e.Slug?.Trim().ToLowerInvariant() ?? "";
            var segmento = string.IsNullOrWhiteSpace(e.Segmento) ? null : e.Segmento.Trim();
            var nomeDono = e.NomeDono?.Trim() ?? "";
            var email = e.EmailDono?.Trim().ToLowerInvariant() ?? "";

            string? erro = null;
            if (nome.Length is < 2 or > 120) erro = "O nome da loja precisa ter de 2 a 120 letras.";
            else if (segmento is { Length: > 60 }) erro = "O tipo de loja pode ter até 60 letras.";
            else if (slug != loja.Slug) erro = RegrasCadastro.ValidarSlug(slug);
            if (erro is not null)
                return Results.Problem(erro, statusCode: StatusCodes.Status422UnprocessableEntity);
            if (slug != loja.Slug && await db.Lojas.AnyAsync(l => l.Slug == slug && l.Id != id, ct))
                return Results.Problem("Este endereço já é de outra loja.", statusCode: StatusCodes.Status409Conflict);

            var dono = await db.Usuarios.IgnoreQueryFilters().Where(u => u.LojaId == id && u.Papel == Papel.Dono)
                .OrderBy(u => u.CriadoEm).FirstOrDefaultAsync(ct);
            if (dono is not null)
            {
                if (nomeDono.Length is < 2 or > 120)
                    return Results.Problem("O nome do dono precisa ter de 2 a 120 letras.", statusCode: StatusCodes.Status422UnprocessableEntity);
                if (!RegrasCadastro.EmailValido(email))
                    return Results.Problem("Informe um e-mail válido para o dono.", statusCode: StatusCodes.Status422UnprocessableEntity);
                if (email != dono.Email && await db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == email && u.Id != dono.Id, ct))
                    return Results.Problem("Este e-mail já é de outro usuário.", statusCode: StatusCodes.Status409Conflict);
                dono.Nome = nomeDono;
                dono.Email = email;
            }

            var slugAntigo = loja.Slug;
            loja.Nome = nome;
            loja.Slug = slug;
            loja.Segmento = segmento;
            await db.SaveChangesAsync(ct);
            logs.CreateLogger("Admin").LogWarning("Loja {Slug} (antes {SlugAntigo}) editada por {Admin}", slug, slugAntigo, user.FindFirstValue(ClaimTypes.Email));
            return Results.NoContent();
        });

        // Apaga a loja e tudo dela (produtos, conversas, pedidos, usuários, credenciais). Não tem volta.
        grupo.MapPost("/lojas/{id:guid}/excluir", async (Guid id, ExcluirLojaEntrada e, AppDbContext db, CredenciaisDaLoja credenciais, ClaimsPrincipal user, ILoggerFactory logs, CancellationToken ct) =>
        {
            var loja = await db.Lojas.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
            if (loja is null)
                return Results.NotFound();
            if (Guid.TryParse(user.FindFirstValue(Comum.LojaAtualDaRequisicao.ClaimLoja), out var propria) && propria == id)
                return Results.Problem("Você não pode excluir a sua própria loja.", statusCode: StatusCodes.Status422UnprocessableEntity);
            if (await credenciais.LojaOriginalAsync(ct) == id)
                return Results.Problem("A loja original (que usa as chaves do servidor) não pode ser excluída por aqui.", statusCode: StatusCodes.Status422UnprocessableEntity);
            if (!string.Equals(e.Confirmacao?.Trim(), loja.Slug, StringComparison.Ordinal))
                return Results.Problem($"Para confirmar, digite o endereço da loja: {loja.Slug}", statusCode: StatusCodes.Status422UnprocessableEntity);

            var linhas = await ExclusaoDeLoja.ExcluirAsync(db, id, ct);
            logs.CreateLogger("Admin").LogWarning("Loja {Slug} EXCLUÍDA por {Admin} ({Linhas} linhas apagadas)", loja.Slug, user.FindFirstValue(ClaimTypes.Email), linhas);
            return Results.NoContent();
        });

        // Reativar uma suspensa, ou ativar à mão uma que não confirmou o e-mail.
        grupo.MapPost("/lojas/{id:guid}/ativar", async (Guid id, AppDbContext db, ClaimsPrincipal user, ILoggerFactory logs, CancellationToken ct) =>
        {
            var loja = await db.Lojas.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (loja is null)
                return Results.NotFound();
            loja.Status = StatusLoja.Ativa;
            loja.MotivoSuspensao = null;
            loja.SuspensaEm = null;
            await db.SaveChangesAsync(ct);
            logs.CreateLogger("Admin").LogWarning("Loja {Slug} ativada por {Admin}", loja.Slug, user.FindFirstValue(ClaimTypes.Email));
            return Results.NoContent();
        });
    }
}
