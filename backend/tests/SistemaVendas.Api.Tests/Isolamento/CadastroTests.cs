using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Email;

namespace SistemaVendas.Api.Tests.Isolamento;

public sealed class EmailFalso : IEnvioEmail
{
    public List<(string Para, string Assunto, string Texto)> Enviados { get; } = [];
    public bool Configurado => true;

    public Task<bool> EnviarAsync(string para, string assunto, string texto, CancellationToken ct)
    {
        lock (Enviados) Enviados.Add((para, assunto, texto));
        return Task.FromResult(true);
    }

    public string UltimoToken(string para)
    {
        var texto = Enviados.Last(e => e.Para == para).Texto;
        var i = texto.IndexOf("token=", StringComparison.Ordinal) + 6;
        return new string(texto[i..].TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
    }
}

public class RegrasCadastroTests
{
    [Theory]
    [InlineData("Aquário do Zé", "aquario-do-ze")]
    [InlineData("  Moda & Cia!! ", "moda-cia")]
    [InlineData("Pet--Shop 2", "pet-shop-2")]
    public void Sugere_endereco_a_partir_do_nome(string nome, string esperado) =>
        Assert.Equal(esperado, RegrasCadastro.SugerirSlug(nome));

    [Theory]
    [InlineData("ab")]
    [InlineData("Maiuscula")]
    [InlineData("com espaco")]
    [InlineData("-comeca")]
    [InlineData("dois--hifens")]
    [InlineData("painel")]
    [InlineData("api")]
    public void Recusa_endereco_invalido_ou_reservado(string slug) => Assert.NotNull(RegrasCadastro.ValidarSlug(slug));

    [Fact]
    public void Aceita_endereco_valido() => Assert.Null(RegrasCadastro.ValidarSlug("aquario-do-ze-2"));

    [Theory]
    [InlineData("a@b.co", true)]
    [InlineData("nome.sobrenome@empresa.com.br", true)]
    [InlineData("sem-arroba.com", false)]
    [InlineData("dois@@x.com", false)]
    [InlineData("a@semponto", false)]
    [InlineData("com espaco@x.com", false)]
    public void Confere_email(string email, bool valido) => Assert.Equal(valido, RegrasCadastro.EmailValido(email));
}

public class CadastroTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private readonly EmailFalso _email = new();

    private WebApplicationFactory<Program> Fabrica() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao)
        .UseSetting("Plataforma:UrlPainel", "https://painel.teste/painel")
        .ConfigureTestServices(s => s.AddSingleton<IEnvioEmail>(_email)));

    private static object Entrada(string slug, string email, string nome = "Loja Nova") =>
        new { nomeLoja = nome, slug, segmento = "Roupas", nomeDono = "Maria", email, senha = "Senha1234", aceiteTermos = true };

    [FactComBanco]
    public async Task Cadastro_so_entra_depois_de_confirmar_o_email_e_a_loja_nova_comeca_vazia()
    {
        await using var app = Fabrica();
        var client = app.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/v1/cadastro", Entrada("moda-da-maria", "Maria@Exemplo.com"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.Contains("https://painel.teste/painel/confirmar-email?token=", _email.Enviados.Single(e => e.Para == "maria@exemplo.com").Texto);

        // Sem confirmar, não entra.
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "maria@exemplo.com", senha = "Senha1234" });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Contains("email_nao_confirmado", await login.Content.ReadAsStringAsync());

        // Link errado não confirma; o certo confirma e já entra.
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync("/api/v1/cadastro/confirmar", new { token = "inventado" })).StatusCode);
        var token = _email.UltimoToken("maria@exemplo.com");
        var confirmar = await client.PostAsJsonAsync("/api/v1/cadastro/confirmar", new { token });
        Assert.Equal(HttpStatusCode.OK, confirmar.StatusCode);
        var eu = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/eu");
        Assert.Equal("moda-da-maria", eu.GetProperty("lojaSlug").GetString());
        Assert.Equal("Dono", eu.GetProperty("papel").GetString());

        // O link vale uma vez só.
        Assert.Equal(HttpStatusCode.Gone, (await client.PostAsJsonAsync("/api/v1/cadastro/confirmar", new { token })).StatusCode);

        // A loja nova não enxerga nada das outras.
        foreach (var rota in new[] { "/api/v1/especies", "/api/v1/pedidos", "/api/v1/conversas", "/api/v1/tanques" })
        {
            var corpo = await client.GetStringAsync(rota);
            Assert.DoesNotContain("loja-a", corpo);
            Assert.DoesNotContain("loja-b", corpo);
        }
        var usuarios = await client.GetFromJsonAsync<JsonElement>("/api/v1/usuarios");
        Assert.Equal(1, usuarios.GetArrayLength());

        await using var db = banco.Criar(null);
        var loja = await db.Lojas.SingleAsync(l => l.Slug == "moda-da-maria");
        Assert.Equal(StatusLoja.Ativa, loja.Status);
        Assert.Equal("Roupas", loja.Segmento);
        Assert.DoesNotContain(await db.TokensEmail.Select(t => t.Hash).ToListAsync(), h => h == token);
    }

    [FactComBanco]
    public async Task Recusa_endereco_ou_email_ja_usados_e_endereco_reservado()
    {
        await using var app = Fabrica();
        var client = app.CreateClient();

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/cadastro", Entrada(banco.A.Slug, "outra@exemplo.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/cadastro", Entrada("livre-1", banco.A.EmailDono))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/v1/cadastro", Entrada("painel", "outra@exemplo.com"))).StatusCode);

        var slug = await client.GetFromJsonAsync<JsonElement>($"/api/v1/cadastro/slug/{banco.A.Slug}");
        Assert.False(slug.GetProperty("disponivel").GetBoolean());
        slug = await client.GetFromJsonAsync<JsonElement>("/api/v1/cadastro/slug/endereco-livre");
        Assert.True(slug.GetProperty("disponivel").GetBoolean());
    }

    [FactComBanco]
    public async Task Reenviar_nao_revela_quem_tem_cadastro()
    {
        await using var app = Fabrica();
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/cadastro/reenviar", new { email = "ninguem@exemplo.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/cadastro/reenviar", new { email = banco.A.EmailDono })).StatusCode);
        Assert.DoesNotContain(_email.Enviados, e => e.Para == "ninguem@exemplo.com" || e.Para == banco.A.EmailDono);
    }
}
