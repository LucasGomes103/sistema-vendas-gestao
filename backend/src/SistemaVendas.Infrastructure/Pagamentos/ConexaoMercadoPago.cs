using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.Seguranca;

namespace SistemaVendas.Infrastructure.Pagamentos;

/// <summary>
/// Aplicação do Mercado Pago DA PLATAFORMA (conta do Lucas), usada no botão "Conectar Mercado Pago" (OAuth). Cada
/// loja autoriza a aplicação a criar cobranças na conta dela; o dinheiro cai na conta da loja.
/// Mercado Pago → Suas integrações → aplicação da plataforma → Credenciais de produção (Client ID e Client Secret)
/// e "URLs de redirecionamento" com a RedirectUri abaixo.
/// </summary>
public class MercadoPagoOAuthOptions
{
    public const string Secao = "MercadoPago:OAuth";

    /// <summary>Client ID (App ID) da aplicação da plataforma (MercadoPago__OAuth__ClientId).</summary>
    public string? ClientId { get; set; }

    /// <summary>Client Secret da aplicação da plataforma (MercadoPago__OAuth__ClientSecret).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Endereço de retorno cadastrado na aplicação. Vazio: {origem do Plataforma__UrlPainel}/api/v1/mercadopago/oauth/retorno
    /// (ex.: https://sistema-vendas-gestao.pages.dev/api/v1/mercadopago/oauth/retorno).
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>Assinatura secreta dos webhooks da aplicação da plataforma. Vazio: MercadoPago__WebhookSecret.</summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Pede credenciais de teste na troca do código (conta vendedora de teste).</summary>
    public bool TokenDeTeste { get; set; }

    public string UrlAutorizacao { get; set; } = "https://auth.mercadopago.com.br/authorization";

    /// <summary>Renova o token quando faltar menos que isso para vencer (o token vale 180 dias).</summary>
    public int DiasAntesDeRenovar { get; set; } = 30;

    public bool Configurado => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Resultado do retorno do Mercado Pago, para o painel mostrar.</summary>
public record ResultadoConexao(bool Ok, string? Erro = null);

/// <summary>
/// "Conectar Mercado Pago": monta o link de autorização, troca o código pelo token da loja e renova o token antes de
/// vencer. O token e o refresh token ficam cifrados em credencial_loja, como os outros segredos da loja.
/// </summary>
public class ConexaoMercadoPago(HttpClient http, IOptions<MercadoPagoOAuthOptions> oauth, IOptions<MercadoPagoOptions> mp,
    IOptions<PlataformaOptions> plataforma, IDataProtectionProvider protecao, ServicoCredenciais credenciais, AppDbContext db,
    ILogger<ConexaoMercadoPago> logger, TimeProvider? relogio = null)
{
    private readonly ITimeLimitedDataProtector _estado = protecao.CreateProtector("mercadopago-oauth-estado").ToTimeLimitedDataProtector();
    private DateTimeOffset Agora => (relogio ?? TimeProvider.System).GetUtcNow();

    public bool Disponivel => oauth.Value.Configurado && credenciais.Disponivel;

    public string RedirectUri
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(oauth.Value.RedirectUri))
                return oauth.Value.RedirectUri.Trim();
            var painel = new Uri(plataforma.Value.UrlPainel);
            return $"{painel.Scheme}://{painel.Authority}/api/v1/mercadopago/oauth/retorno";
        }
    }

    /// <summary>Endereço do painel para onde o navegador volta depois do retorno.</summary>
    public string VoltaAoPainel(ResultadoConexao r) =>
        plataforma.Value.UrlPainel.TrimEnd('/') + "/config?mercadopago=" + (r.Ok ? "conectado" : "erro&motivo=" + Uri.EscapeDataString(r.Erro ?? ""));

    /// <summary>Link do Mercado Pago onde o dono autoriza a plataforma. O "state" leva a loja, assinado e vale 15 min.</summary>
    public string LinkAutorizacao(Guid lojaId)
    {
        var o = oauth.Value;
        if (!Disponivel)
            throw new RegraException("O botão do Mercado Pago ainda não foi configurado na plataforma.");
        var estado = _estado.Protect(lojaId.ToString("N"), TimeSpan.FromMinutes(15));
        return $"{o.UrlAutorizacao}?client_id={Uri.EscapeDataString(o.ClientId!)}&response_type=code&platform_id=mp"
            + $"&state={Uri.EscapeDataString(estado)}&redirect_uri={Uri.EscapeDataString(RedirectUri)}";
    }

    /// <summary>Loja do "state", ou null se ele foi adulterado ou passou de 15 min.</summary>
    public Guid? LojaDoEstado(string? estado)
    {
        if (string.IsNullOrEmpty(estado))
            return null;
        try
        {
            return Guid.TryParseExact(_estado.Unprotect(estado), "N", out var loja) ? loja : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Volta do Mercado Pago: troca o código pelo token e guarda na loja do "state".</summary>
    public async Task<ResultadoConexao> ConcluirAsync(string? code, string? estado, string? erro, CancellationToken ct)
    {
        if (LojaDoEstado(estado) is not { } lojaId)
            return new(false, "O link de conexão venceu. Clique em Conectar Mercado Pago de novo.");
        if (!string.IsNullOrEmpty(erro) || string.IsNullOrEmpty(code))
            return new(false, "A conexão não foi autorizada no Mercado Pago.");
        if (!await db.Lojas.AnyAsync(l => l.Id == lojaId, ct))
            return new(false, "Loja não encontrada.");

        var token = await PedirTokenAsync(new Dictionary<string, object?>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri
        }, ct);
        if (token is null)
            return new(false, "O Mercado Pago não liberou o acesso. Tente conectar de novo.");

        await SalvarAsync(lojaId, token, segredoAntigo: null, ct);
        logger.LogInformation("Loja {Loja} conectou o Mercado Pago (conta {Usuario})", lojaId, token.UserId);
        return new(true);
    }

    /// <summary>Renova os tokens conectados pelo botão que vencem em menos de DiasAntesDeRenovar. Devolve quantos renovou.</summary>
    public async Task<int> RenovarVencendoAsync(CancellationToken ct)
    {
        if (!oauth.Value.Configurado || !credenciais.Disponivel)
            return 0;
        var lojas = await db.CredenciaisLoja.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Tipo == TipoCredencial.MercadoPago).Select(c => c.LojaId).ToListAsync(ct);
        var limite = Agora.AddDays(oauth.Value.DiasAntesDeRenovar);
        var renovados = 0;
        foreach (var lojaId in lojas)
        {
            var atual = await credenciais.LerAsync<CredencialMercadoPago>(lojaId, ct);
            if (atual is not { ViaOAuth: true, RefreshToken: { Length: > 0 } refresh } || atual.ExpiraEm > limite)
                continue;
            if (await RenovarAsync(lojaId, refresh, atual.WebhookSecret, ct))
                renovados++;
        }
        return renovados;
    }

    public async Task<bool> RenovarAsync(Guid lojaId, string refreshToken, string? segredo, CancellationToken ct)
    {
        var token = await PedirTokenAsync(new Dictionary<string, object?>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        }, ct);
        if (token is null)
        {
            logger.LogWarning("Não deu para renovar o token do Mercado Pago da loja {Loja}: o dono precisa conectar de novo", lojaId);
            return false;
        }
        await SalvarAsync(lojaId, token, segredo, ct);
        return true;
    }

    private async Task SalvarAsync(Guid lojaId, RespostaToken t, string? segredoAntigo, CancellationToken ct)
    {
        var expira = Agora.AddSeconds(t.ExpiresIn > 0 ? t.ExpiresIn : 180 * 24 * 3600);
        var teste = !t.LiveMode || oauth.Value.TokenDeTeste;
        var credencial = new CredencialMercadoPago(t.AccessToken, segredoAntigo, teste, t.RefreshToken, expira)
        {
            ViaOAuth = true,
            ContaMp = t.UserId?.ToString()
        };
        await credenciais.SalvarAsync(lojaId, credencial,
            $"conta {t.UserId} conectada pelo Mercado Pago" + (teste ? " (teste)" : ""), ct);
    }

    private async Task<RespostaToken?> PedirTokenAsync(Dictionary<string, object?> corpo, CancellationToken ct)
    {
        var o = oauth.Value;
        corpo["client_id"] = o.ClientId;
        corpo["client_secret"] = o.ClientSecret;
        if (o.TokenDeTeste)
            corpo["test_token"] = "true";
        try
        {
            using var resp = await http.PostAsJsonAsync(mp.Value.UrlApi.TrimEnd('/') + "/oauth/token", corpo, ct);
            var texto = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                // A resposta de erro não tem segredo (só o motivo, ex.: invalid_grant).
                logger.LogWarning("Mercado Pago recusou o token ({Status}): {Corpo}", (int)resp.StatusCode, texto[..Math.Min(300, texto.Length)]);
                return null;
            }
            var t = JsonSerializer.Deserialize<RespostaToken>(texto);
            return string.IsNullOrWhiteSpace(t?.AccessToken) ? null : t;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(e, "Mercado Pago fora do ar ao pedir o token");
            return null;
        }
    }

    private sealed record RespostaToken(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] long ExpiresIn,
        [property: JsonPropertyName("user_id")] long? UserId,
        [property: JsonPropertyName("live_mode")] bool LiveMode);
}
