using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SistemaVendas.Infrastructure.Pagamentos;
using SistemaVendas.Infrastructure.Seguranca;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>Endpoint /oauth/token falso do Mercado Pago: guarda os pedidos e devolve um token novo a cada chamada.</summary>
public sealed class OAuthMercadoPagoFalso : HttpMessageHandler
{
    public List<JsonElement> Pedidos { get; } = [];
    public bool Recusar { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Assert.EndsWith("/oauth/token", request.RequestUri!.AbsolutePath);
        var corpo = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
        Pedidos.Add(corpo);
        if (Recusar)
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"invalid_grant"}""") };
        var n = Pedidos.Count;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""
                {"access_token":"APP_USR-loja-oauth-{{n}}","token_type":"Bearer","expires_in":15552000,"scope":"offline_access read write",
                 "user_id":987654,"refresh_token":"TG-refresh-{{n}}","public_key":"APP_USR-pub","live_mode":true}
                """, Encoding.UTF8, "application/json")
        };
    }
}

public class ConectarMercadoPagoTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private static readonly string Chave = Convert.ToBase64String(Enumerable.Range(40, 32).Select(i => (byte)i).ToArray());
    private const string SegredoPlataforma = "segredo-da-aplicacao-da-plataforma";

    private WebApplicationFactory<Program> Fabrica(OAuthMercadoPagoFalso mp) => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao)
        .UseSetting("Plataforma:ChaveMestra", Chave)
        .UseSetting("Plataforma:UrlPainel", "https://painel.teste/painel")
        .UseSetting("MercadoPago:OAuth:ClientId", "1234567890")
        .UseSetting("MercadoPago:OAuth:ClientSecret", "segredo-do-cliente")
        .UseSetting("MercadoPago:OAuth:WebhookSecret", SegredoPlataforma)
        .ConfigureTestServices(s => s.AddHttpClient<ConexaoMercadoPago>().ConfigurePrimaryHttpMessageHandler(() => mp)));

    private static async Task<HttpClient> EntrarAsync(WebApplicationFactory<Program> app, LojaDeTeste loja)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = loja.EmailDono, senha = BancoDeTeste.Senha })).EnsureSuccessStatusCode();
        return client;
    }

    [FactComBanco]
    public async Task Botao_conecta_a_conta_da_loja_e_o_token_renova_sozinho()
    {
        var mp = new OAuthMercadoPagoFalso();
        await using var app = Fabrica(mp);
        var client = await EntrarAsync(app, banco.B);

        var antes = await client.GetFromJsonAsync<JsonElement>("/api/v1/config/cartao");
        Assert.True(antes.GetProperty("conectarDisponivel").GetBoolean());

        var link = (await (await client.PostAsJsonAsync("/api/v1/config/cartao/conectar", new { })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
        var uri = new Uri(link);
        var q = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("auth.mercadopago.com.br", uri.Host);
        Assert.Equal("1234567890", q["client_id"]);
        Assert.Equal("https://painel.teste/api/v1/mercadopago/oauth/retorno", q["redirect_uri"]);

        // Mercado Pago devolve o navegador com o código (sem login: a loja vem do state).
        var anonimo = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var volta = await anonimo.GetAsync($"/api/v1/mercadopago/oauth/retorno?code=TG-codigo&state={Uri.EscapeDataString(q["state"]!)}");
        Assert.Equal(HttpStatusCode.Redirect, volta.StatusCode);
        Assert.Equal("https://painel.teste/painel/config?mercadopago=conectado", volta.Headers.Location!.ToString());
        Assert.Equal("authorization_code", mp.Pedidos[0].GetProperty("grant_type").GetString());
        Assert.Equal("TG-codigo", mp.Pedidos[0].GetProperty("code").GetString());
        Assert.Equal("segredo-do-cliente", mp.Pedidos[0].GetProperty("client_secret").GetString());

        using (var scope = app.Services.CreateScope())
        {
            var emUso = await scope.ServiceProvider.GetRequiredService<CredenciaisDaLoja>().MercadoPagoAsync(banco.B.Id, default);
            Assert.Equal("APP_USR-loja-oauth-1", emUso!.Valor.AccessToken);
            Assert.True(emUso.Valor.ViaOAuth);
            Assert.Equal("TG-refresh-1", emUso.Valor.RefreshToken);
            Assert.True(emUso.Valor.ExpiraEm > DateTimeOffset.UtcNow.AddDays(170));
            // A loja A não ganhou nada.
            Assert.False((await scope.ServiceProvider.GetRequiredService<ServicoCredenciais>().LerAsync<CredencialMercadoPago>(banco.A.Id, default))?.ViaOAuth == true);
        }

        var depois = await client.GetFromJsonAsync<JsonElement>("/api/v1/config/cartao");
        Assert.True(depois.GetProperty("viaOAuth").GetBoolean());
        Assert.True(depois.GetProperty("assinaturaConferida").GetBoolean()); // segredo da aplicação da plataforma

        // Longe de vencer: não renova. Perto de vencer (dentro de 30 dias): renova com o refresh token.
        using (var scope = app.Services.CreateScope())
        {
            var conexao = scope.ServiceProvider.GetRequiredService<ConexaoMercadoPago>();
            Assert.Equal(0, await conexao.RenovarVencendoAsync(default));
        }
        using (var scope = app.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var daqui160Dias = new Relogio(DateTimeOffset.UtcNow.AddDays(160));
            var conexao = ActivatorUtilities.CreateInstance<ConexaoMercadoPago>(sp, new HttpClient(mp), daqui160Dias);
            Assert.Equal(1, await conexao.RenovarVencendoAsync(default));
            Assert.Equal("refresh_token", mp.Pedidos[^1].GetProperty("grant_type").GetString());
            Assert.Equal("TG-refresh-1", mp.Pedidos[^1].GetProperty("refresh_token").GetString());
            var renovado = await sp.GetRequiredService<ServicoCredenciais>().LerAsync<CredencialMercadoPago>(banco.B.Id, default);
            Assert.Equal("APP_USR-loja-oauth-2", renovado!.AccessToken);
            Assert.Equal("TG-refresh-2", renovado.RefreshToken);
        }

        await (await EntrarAsync(app, banco.B)).DeleteAsync("/api/v1/config/cartao");
    }

    [FactComBanco]
    public async Task Retorno_com_state_adulterado_ou_recusado_nao_salva_nada()
    {
        var mp = new OAuthMercadoPagoFalso();
        await using var app = Fabrica(mp);
        var anonimo = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var volta = await anonimo.GetAsync("/api/v1/mercadopago/oauth/retorno?code=TG-x&state=inventado");
        Assert.StartsWith("https://painel.teste/painel/config?mercadopago=erro", volta.Headers.Location!.ToString());
        Assert.Empty(mp.Pedidos);

        // State válido, mas o Mercado Pago recusa o código.
        var client = await EntrarAsync(app, banco.A);
        var link = (await (await client.PostAsJsonAsync("/api/v1/config/cartao/conectar", new { })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
        var state = HttpUtility.ParseQueryString(new Uri(link).Query)["state"]!;
        mp.Recusar = true;
        volta = await anonimo.GetAsync($"/api/v1/mercadopago/oauth/retorno?code=TG-y&state={Uri.EscapeDataString(state)}");
        Assert.StartsWith("https://painel.teste/painel/config?mercadopago=erro", volta.Headers.Location!.ToString());
        using var scope = app.Services.CreateScope();
        Assert.False((await scope.ServiceProvider.GetRequiredService<ServicoCredenciais>().LerAsync<CredencialMercadoPago>(banco.A.Id, default))?.ViaOAuth == true);
    }

    private sealed class Relogio(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
