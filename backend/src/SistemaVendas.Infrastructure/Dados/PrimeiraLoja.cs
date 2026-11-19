using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SistemaVendas.Domain.Lojas;

namespace SistemaVendas.Infrastructure.Dados;

/// <summary>
/// Em produção, cria a loja e o usuário dono na primeira subida, a partir da configuração
/// (PrimeiraLoja:Nome, Slug, DonoNome, DonoEmail, DonoSenha). Depois disso não faz mais nada.
/// </summary>
public static class PrimeiraLoja
{
    public static async Task CriarSeNecessarioAsync(AppDbContext db, IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Lojas.AnyAsync(ct))
            return;

        var secao = config.GetSection("PrimeiraLoja");
        var nome = secao["Nome"];
        var slug = secao["Slug"];
        var email = secao["DonoEmail"]?.Trim().ToLowerInvariant();
        var senha = secao["DonoSenha"];

        if (string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha))
        {
            logger.LogWarning("Banco vazio e PrimeiraLoja não configurada: ninguém consegue entrar no painel ainda.");
            return;
        }

        var loja = new Loja { Nome = nome.Trim(), Slug = slug.Trim().ToLowerInvariant(), Segmento = secao["Segmento"] is { Length: > 0 } seg ? seg.Trim() : null };
        db.Lojas.Add(loja);

        var dono = new Usuario { LojaId = loja.Id, Nome = secao["DonoNome"] ?? "Dono", Email = email, Papel = Papel.Dono };
        dono.SenhaHash = new PasswordHasher<Usuario>().HashPassword(dono, senha);
        db.Usuarios.Add(dono);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Loja {Loja} criada com o usuário dono {Email}.", loja.Slug, email);
    }
}
