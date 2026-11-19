using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Infrastructure.Seguranca;

/// <summary>Segredos da plataforma (conta do Lucas), só por variável de ambiente no Render.</summary>
public class PlataformaOptions
{
    public const string Secao = "Plataforma";

    /// <summary>
    /// Chave mestra que cifra as credenciais das lojas: 32 bytes em base64 (Plataforma__ChaveMestra).
    /// Gere com: openssl rand -base64 32. Trocar a chave invalida as credenciais já salvas.
    /// </summary>
    public string? ChaveMestra { get; set; }
}

/// <summary>Credencial da WhatsApp Cloud API de uma loja.</summary>
public record CredencialWhatsApp(string AccessToken);

/// <summary>Credencial do Mercado Pago de uma loja (hoje digitada pelo dono; na fase 2 vem do OAuth).</summary>
public record CredencialMercadoPago(string AccessToken, string? WebhookSecret = null, bool ModoTeste = false,
    string? RefreshToken = null, DateTimeOffset? ExpiraEm = null);

/// <summary>
/// AES-256-GCM com a chave mestra. O texto associado (loja + tipo) amarra cada segredo à sua loja: copiar o valor
/// cifrado de uma loja para outra no banco não funciona.
/// </summary>
public class Cofre(IOptions<PlataformaOptions> opcoes)
{
    private const string Versao = "v1:";
    private readonly byte[]? _chave = Ler(opcoes.Value.ChaveMestra);

    public bool Disponivel => _chave is not null;

    private static byte[]? Ler(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return null;
        try
        {
            var chave = Convert.FromBase64String(base64.Trim());
            return chave.Length == 32 ? chave : throw new InvalidOperationException("Plataforma:ChaveMestra precisa ter 32 bytes (openssl rand -base64 32).");
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Plataforma:ChaveMestra não é base64 válido (openssl rand -base64 32).");
        }
    }

    private byte[] Chave => _chave ?? throw new RegraException("Falta a chave mestra (Plataforma__ChaveMestra) no servidor: não dá para guardar credenciais.");

    public string Cifrar(string texto, string contexto)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var aberto = Encoding.UTF8.GetBytes(texto);
        var cifrado = new byte[aberto.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(Chave, 16))
            aes.Encrypt(nonce, aberto, cifrado, tag, Encoding.UTF8.GetBytes(contexto));
        return Versao + Convert.ToBase64String([.. nonce, .. tag, .. cifrado]);
    }

    /// <summary>Devolve null quando o valor não abre (chave trocada, contexto errado ou valor adulterado).</summary>
    public string? Decifrar(string valor, string contexto)
    {
        if (_chave is null || !valor.StartsWith(Versao, StringComparison.Ordinal))
            return null;
        try
        {
            var bytes = Convert.FromBase64String(valor[Versao.Length..]);
            if (bytes.Length < 28)
                return null;
            var aberto = new byte[bytes.Length - 28];
            using var aes = new AesGcm(_chave, 16);
            aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), aberto, Encoding.UTF8.GetBytes(contexto));
            return Encoding.UTF8.GetString(aberto);
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>Lê e grava as credenciais de uma loja, sempre pelo id da loja (funciona também nos workers).</summary>
public class ServicoCredenciais(AppDbContext db, Cofre cofre)
{
    public bool Disponivel => cofre.Disponivel;

    private static string Contexto(Guid lojaId, TipoCredencial tipo) => $"{lojaId:N}:{tipo}";

    private static TipoCredencial TipoDe<T>() =>
        typeof(T) == typeof(CredencialWhatsApp) ? TipoCredencial.WhatsApp
        : typeof(T) == typeof(CredencialMercadoPago) ? TipoCredencial.MercadoPago
        : throw new ArgumentException($"Credencial sem tipo: {typeof(T).Name}");

    // A loja vem explícita (o worker não tem login): por isso ignora o filtro, mas sempre filtra pelo id da loja.
    private Task<CredencialLoja?> BuscarAsync(Guid lojaId, TipoCredencial tipo, CancellationToken ct) =>
        db.Set<CredencialLoja>().IgnoreQueryFilters().FirstOrDefaultAsync(c => c.LojaId == lojaId && c.Tipo == tipo, ct);

    public async Task<T?> LerAsync<T>(Guid lojaId, CancellationToken ct) where T : class
    {
        var tipo = TipoDe<T>();
        var linha = await db.Set<CredencialLoja>().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(c => c.LojaId == lojaId && c.Tipo == tipo, ct);
        if (linha is null || cofre.Decifrar(linha.Cifrado, Contexto(lojaId, tipo)) is not { } json)
            return null;
        return JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>Resumo (sem segredo) das credenciais da loja, para o painel.</summary>
    public async Task<Dictionary<TipoCredencial, (string? Resumo, DateTimeOffset AtualizadoEm)>> ResumosAsync(Guid lojaId, CancellationToken ct) =>
        await db.Set<CredencialLoja>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.LojaId == lojaId)
            .ToDictionaryAsync(c => c.Tipo, c => (c.Resumo, c.AtualizadoEm), ct);

    public async Task SalvarAsync<T>(Guid lojaId, T valor, string? resumo, CancellationToken ct) where T : class
    {
        var tipo = TipoDe<T>();
        var cifrado = cofre.Cifrar(JsonSerializer.Serialize(valor), Contexto(lojaId, tipo));
        var linha = await BuscarAsync(lojaId, tipo, ct);
        if (linha is null)
            db.Add(new CredencialLoja { LojaId = lojaId, Tipo = tipo, Cifrado = cifrado, Resumo = resumo });
        else
        {
            linha.Cifrado = cifrado;
            linha.Resumo = resumo;
            linha.AtualizadoEm = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoverAsync<T>(Guid lojaId, CancellationToken ct) where T : class
    {
        if (await BuscarAsync(lojaId, TipoDe<T>(), ct) is { } linha)
        {
            db.Remove(linha);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>"…a1b2": os 4 últimos caracteres, para o dono reconhecer qual token está salvo.</summary>
    public static string Final(string segredo) => segredo.Length <= 8 ? "…" : "…" + segredo[^4..];
}
