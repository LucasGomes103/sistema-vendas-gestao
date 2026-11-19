using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Lojas;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>Administrador da plataforma (dono da loja A, listado em Plataforma:Admins) e suspensão de loja.</summary>
public class AdminTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private WebApplicationFactory<Program> Fabrica() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao)
        .UseSetting("Plataforma:Admins", $"outro@x.com, {banco.A.EmailDono.ToUpperInvariant()}"));

    private static async Task<HttpResponseMessage> EntrarAsync(HttpClient client, string email) =>
        await client.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = BancoDeTeste.Senha });

    [FactComBanco]
    public async Task So_o_administrador_ve_a_lista_de_lojas()
    {
        await using var app = Fabrica();
        var dono = app.CreateClient();
        (await EntrarAsync(dono, banco.B.EmailDono)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await dono.GetAsync("/api/v1/admin/lojas")).StatusCode);
        Assert.False((await dono.GetFromJsonAsync<JsonElement>("/api/v1/auth/eu")).GetProperty("adminPlataforma").GetBoolean());

        var admin = app.CreateClient();
        (await EntrarAsync(admin, banco.A.EmailDono)).EnsureSuccessStatusCode();
        Assert.True((await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/eu")).GetProperty("adminPlataforma").GetBoolean());
        var lojas = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/lojas");
        var b = lojas.EnumerateArray().Single(l => l.GetProperty("slug").GetString() == banco.B.Slug);
        Assert.Equal(1, b.GetProperty("usuarios").GetInt32());
        Assert.Equal(1, b.GetProperty("produtos").GetInt32());
        Assert.Equal(1, b.GetProperty("pedidos30Dias").GetInt32());
        Assert.Equal(banco.B.EmailDono, b.GetProperty("emailDono").GetString());
        // Só números: nada de mensagem ou telefone de cliente.
        Assert.DoesNotContain("Olá", lojas.GetRawText());
        Assert.DoesNotContain("5511900000", lojas.GetRawText());
    }

    [FactComBanco]
    public async Task Loja_suspensa_perde_a_sessao_o_login_e_a_vitrine_e_volta_ao_reativar()
    {
        await using var app = Fabrica();
        var admin = app.CreateClient();
        (await EntrarAsync(admin, banco.A.EmailDono)).EnsureSuccessStatusCode();
        var donoB = app.CreateClient();
        (await EntrarAsync(donoB, banco.B.EmailDono)).EnsureSuccessStatusCode();

        // O administrador não suspende a própria loja.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/admin/lojas/{banco.A.Id}/suspender", new { motivo = "x" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/v1/admin/lojas/{banco.B.Id}/suspender", new { motivo = "Mensalidade atrasada" })).StatusCode);
        await using (var db = banco.Criar(null))
        {
            var loja = await db.Lojas.SingleAsync(l => l.Id == banco.B.Id);
            Assert.Equal(StatusLoja.Suspensa, loja.Status);
            Assert.Equal("Mensalidade atrasada", loja.MotivoSuspensao);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await EntrarAsync(app.CreateClient(), banco.B.EmailDono)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.CreateClient().GetAsync($"/api/v1/public/{banco.B.Slug}/catalogo")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/admin/lojas/{banco.B.Id}/ativar", null)).StatusCode);
        (await EntrarAsync(app.CreateClient(), banco.B.EmailDono)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await app.CreateClient().GetAsync($"/api/v1/public/{banco.B.Slug}/catalogo")).StatusCode);
    }
}
