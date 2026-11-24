using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemaVendas.Infrastructure.Seguranca;
using SistemaVendas.Infrastructure.WhatsApp;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>Graph API falsa: troca de código, inscrição dos webhooks, registro e consulta do número.</summary>
public sealed class MetaFalsa : HttpMessageHandler
{
    public List<string> Chamadas { get; } = [];
    public string? RegistroCorpo;
    public bool RecusarRegistro;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var caminho = request.RequestUri!.AbsolutePath;
        Chamadas.Add($"{request.Method} {caminho}");
        string corpo;
        if (caminho.EndsWith("/oauth/access_token"))
        {
            Assert.Contains("code=AQ-codigo-da-meta", request.RequestUri.Query);
            Assert.Contains("client_secret=segredo-do-app", request.RequestUri.Query);
            corpo = """{"access_token":"EAA-token-da-empresa-loja-b","token_type":"bearer"}""";
        }
        else if (caminho.EndsWith("/subscribed_apps"))
        {
            Assert.Equal("EAA-token-da-empresa-loja-b", request.Headers.Authorization?.Parameter);
            corpo = """{"success":true}""";
        }
        else if (caminho.EndsWith("/register"))
        {
            RegistroCorpo = await request.Content!.ReadAsStringAsync(ct);
            if (RecusarRegistro)
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":{"message":"(#133005) Two step verification PIN Mismatch"}}""") };
            corpo = """{"success":true}""";
        }
        else
            corpo = """{"display_phone_number":"+55 11 95555-0000","id":"7770001"}""";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
    }
}

public class ConectarWhatsAppTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private static readonly string Chave = Convert.ToBase64String(Enumerable.Range(70, 32).Select(i => (byte)i).ToArray());

    private WebApplicationFactory<Program> Fabrica(MetaFalsa meta) => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao)
        .UseSetting("Plataforma:ChaveMestra", Chave)
        .UseSetting("WhatsApp:AppSecret", "segredo-do-app")
        .UseSetting("WhatsApp:EmbeddedSignup:AppId", "111222333")
        .UseSetting("WhatsApp:EmbeddedSignup:ConfigId", "444555666")
        .ConfigureTestServices(s => s.AddHttpClient<ConexaoWhatsApp>().ConfigurePrimaryHttpMessageHandler(() => meta)));

    private static async Task<HttpClient> EntrarAsync(WebApplicationFactory<Program> app, LojaDeTeste loja)
    {
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = loja.EmailDono, senha = BancoDeTeste.Senha })).EnsureSuccessStatusCode();
        return client;
    }

    [FactComBanco]
    public async Task Embedded_signup_liga_o_numero_na_loja_com_o_token_da_empresa()
    {
        var meta = new MetaFalsa();
        await using var app = Fabrica(meta);
        var client = await EntrarAsync(app, banco.B);

        var antes = await client.GetFromJsonAsync<JsonElement>("/api/v1/config/whatsapp-oficial");
        Assert.Equal("111222333", antes.GetProperty("embeddedSignup").GetProperty("appId").GetString());
        Assert.Equal("444555666", antes.GetProperty("embeddedSignup").GetProperty("configId").GetString());

        var resp = await client.PostAsJsonAsync("/api/v1/config/whatsapp-oficial/conectar",
            new { code = "AQ-codigo-da-meta", phoneNumberId = "7770001", wabaId = "8880001" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(["GET /v25.0/oauth/access_token", "POST /v25.0/8880001/subscribed_apps", "POST /v25.0/7770001/register", "GET /v25.0/7770001"], meta.Chamadas);
        var pin = JsonDocument.Parse(meta.RegistroCorpo!).RootElement.GetProperty("pin").GetString()!;
        Assert.Matches("^[0-9]{6}$", pin);

        await using (var db = banco.Criar(banco.B.Id))
        {
            var loja = await db.Lojas.SingleAsync(l => l.Id == banco.B.Id);
            Assert.Equal("7770001", loja.PhoneNumberId);
        }
        using (var scope = app.Services.CreateScope())
        {
            var cred = await scope.ServiceProvider.GetRequiredService<ServicoCredenciais>().LerAsync<CredencialWhatsApp>(banco.B.Id, default);
            Assert.Equal("EAA-token-da-empresa-loja-b", cred!.AccessToken);
            Assert.True(cred.ViaEmbeddedSignup);
            Assert.Equal("8880001", cred.WabaId);
            Assert.Equal(pin, cred.Pin);
        }
        var depois = await client.GetFromJsonAsync<JsonElement>("/api/v1/config/whatsapp-oficial");
        Assert.True(depois.GetProperty("configurado").GetBoolean());
        Assert.True(depois.GetProperty("viaEmbeddedSignup").GetBoolean());

        // O mesmo número não pode ir para a loja A.
        var outra = await EntrarAsync(app, banco.A);
        var conflito = await outra.PostAsJsonAsync("/api/v1/config/whatsapp-oficial/conectar",
            new { code = "AQ-codigo-da-meta", phoneNumberId = "7770001", wabaId = "8880001" });
        Assert.Equal(HttpStatusCode.Conflict, conflito.StatusCode);

        await client.DeleteAsync("/api/v1/config/whatsapp-oficial");
    }

    [FactComBanco]
    public async Task Registro_recusado_pela_meta_nao_liga_nada()
    {
        var meta = new MetaFalsa { RecusarRegistro = true };
        await using var app = Fabrica(meta);
        var client = await EntrarAsync(app, banco.A);
        var resp = await client.PostAsJsonAsync("/api/v1/config/whatsapp-oficial/conectar",
            new { code = "AQ-codigo-da-meta", phoneNumberId = "7770002", wabaId = "8880002" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        await using var db = banco.Criar(banco.A.Id);
        Assert.NotEqual("7770002", (await db.Lojas.SingleAsync(l => l.Id == banco.A.Id)).PhoneNumberId);
    }
}
