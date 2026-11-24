using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Slug da loja que usa as variáveis do servidor (MercadoPago__AccessToken, WhatsApp__AccessToken). Vazio: usa
    /// PrimeiraLoja__Slug, depois WhatsApp__Local__LojaSlug e, sem nenhum, a loja mais antiga do banco.
    /// </summary>
    public string? LojaOriginal { get; set; }

    /// <summary>Endereço do painel, usado nos links dos e-mails (confirmação de cadastro).</summary>
    public string UrlPainel { get; set; } = "https://sistema-vendas-gestao.pages.dev/painel";

    /// <summary>E-mails dos administradores da plataforma, separados por vírgula (Plataforma__Admins).</summary>
    public string? Admins { get; set; }

    public bool EhAdmin(string? email) =>
        !string.IsNullOrWhiteSpace(email) && (Admins ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Any(a => string.Equals(a.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Credencial da WhatsApp Cloud API de uma loja: colada pelo dono ou vinda do botão "Conectar WhatsApp" (Embedded
/// Signup), que guarda também a conta (WABA) e o PIN de duas etapas usado no registro do número.
/// </summary>
public record CredencialWhatsApp(string AccessToken)
{
    public bool ViaEmbeddedSignup { get; init; }
    public string? WabaId { get; init; }
    public string? Pin { get; init; }
}

/// <summary>
/// Credencial do Mercado Pago de uma loja: colada pelo dono ou vinda do botão "Conectar Mercado Pago" (OAuth, com
/// refresh token e validade; renovada sozinha antes de vencer).
/// </summary>
public record CredencialMercadoPago(string AccessToken, string? WebhookSecret = null, bool ModoTeste = false,
    string? RefreshToken = null, DateTimeOffset? ExpiraEm = null)
{
    /// <summary>Veio do OAuth: os avisos de pagamento chegam assinados com o segredo da aplicação da plataforma.</summary>
    public bool ViaOAuth { get; init; }

    /// <summary>Id da conta (user_id) no Mercado Pago, só para o painel mostrar.</summary>
    public string? ContaMp { get; init; }
}

/// <summary>
/// AES-256-GCM com a chave mestra. O texto associado (loja + tipo) amarra cada segredo à sua loja: copiar o valor
/// cifrado de uma loja para outra no banco não funciona.
/// </summary>
public class Cofre(IOptions<PlataformaOptions> opcoes, ILogger<Cofre>? logger = null)
{
    private const string Versao = "v1:";
    private readonly byte[]? _chave = Ler(opcoes.Value.ChaveMestra, logger);

    public bool Disponivel => _chave is not null;

    /// <summary>
    /// Chave errada não derruba nada: fica como "sem chave" (credenciais da loja desligadas, a loja original segue
    /// com as variáveis do servidor) e o erro aparece no log.
    /// </summary>
    public static byte[]? Ler(string? base64, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return null;
        try
        {
            var chave = Convert.FromBase64String(base64.Trim());
            if (chave.Length == 32)
                return chave;
            logger?.LogError("Plataforma__ChaveMestra tem {Bytes} bytes, precisa de 32 (resultado de openssl rand -base64 32). Credenciais das lojas desligadas.", chave.Length);
        }
        catch (FormatException)
        {
            logger?.LogError("Plataforma__ChaveMestra não é base64 válido: cole o RESULTADO de openssl rand -base64 32 (44 caracteres). Credenciais das lojas desligadas.");
        }
        return null;
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

/// <summary>De onde veio a credencial em uso: do cadastro da loja ou das variáveis do servidor (só a loja original).</summary>
public enum OrigemCredencial
{
    Loja,
    Servidor
}

public record CredencialEmUso<T>(T Valor, OrigemCredencial Origem);

/// <summary>
/// Credencial que cada integração usa para uma loja. Primeiro a da própria loja (credencial_loja). Sem ela, só a
/// loja original (a ECS Fish: Plataforma__LojaOriginal, PrimeiraLoja__Slug, WhatsApp__Local__LojaSlug ou a mais
/// antiga do banco) continua com as variáveis do servidor
/// (MercadoPago__AccessToken, WhatsApp__AccessToken), como antes. Uma loja nova nunca usa o token da plataforma:
/// o dinheiro do cartão cairia na conta de outra loja.
/// </summary>
public class CredenciaisDaLoja(ServicoCredenciais credenciais, AppDbContext db, IOptions<PlataformaOptions> plataforma,
    Microsoft.Extensions.Configuration.IConfiguration config,
    IOptions<Pagamentos.MercadoPagoOptions> mercadoPago, IOptions<WhatsApp.WhatsAppOptions> whatsApp)
{
    private Guid? _lojaOriginal;

    /// <summary>A loja que herdou as variáveis do servidor.</summary>
    public async Task<Guid?> LojaOriginalAsync(CancellationToken ct)
    {
        if (_lojaOriginal is not null)
            return _lojaOriginal;
        var wa = whatsApp.Value;
        var slug = new[] { plataforma.Value.LojaOriginal, config["PrimeiraLoja:Slug"], wa.UsaPonteLocal ? wa.Local.LojaSlug : null }
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim().ToLowerInvariant();
        if (slug is not null)
            _lojaOriginal = await db.Lojas.AsNoTracking().Where(l => l.Slug == slug).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
        return _lojaOriginal ??= await db.Lojas.AsNoTracking().OrderBy(l => l.CriadoEm).ThenBy(l => l.Id).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
    }

    public async Task<CredencialEmUso<CredencialMercadoPago>?> MercadoPagoAsync(Guid lojaId, CancellationToken ct)
    {
        if (await credenciais.LerAsync<CredencialMercadoPago>(lojaId, ct) is { } daLoja && !string.IsNullOrWhiteSpace(daLoja.AccessToken))
            return new(daLoja, OrigemCredencial.Loja);
        var o = mercadoPago.Value;
        if (o.Configurado && await LojaOriginalAsync(ct) == lojaId)
            return new(new CredencialMercadoPago(o.AccessToken!, o.WebhookSecret, o.ModoTeste), OrigemCredencial.Servidor);
        return null;
    }

    public async Task<CredencialEmUso<CredencialWhatsApp>?> WhatsAppAsync(Guid lojaId, CancellationToken ct)
    {
        if (await credenciais.LerAsync<CredencialWhatsApp>(lojaId, ct) is { } daLoja && !string.IsNullOrWhiteSpace(daLoja.AccessToken))
            return new(daLoja, OrigemCredencial.Loja);
        var token = whatsApp.Value.AccessToken;
        if (!string.IsNullOrWhiteSpace(token) && await LojaOriginalAsync(ct) == lojaId)
            return new(new CredencialWhatsApp(token), OrigemCredencial.Servidor);
        return null;
    }
}
