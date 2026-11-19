using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Seguranca;

namespace SistemaVendas.Api.Tests.Isolamento;

public class CofreTests
{
    private static readonly string ChaveTeste = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    public static Cofre NovoCofre(string? chave = null) =>
        new(Options.Create(new PlataformaOptions { ChaveMestra = chave ?? ChaveTeste }));

    [Fact]
    public void Cifra_e_decifra_com_o_mesmo_contexto()
    {
        var cofre = NovoCofre();
        var cifrado = cofre.Cifrar("APP_USR-segredo", "loja:MercadoPago");
        Assert.StartsWith("v1:", cifrado);
        Assert.DoesNotContain("segredo", cifrado);
        Assert.Equal("APP_USR-segredo", cofre.Decifrar(cifrado, "loja:MercadoPago"));
    }

    [Fact]
    public void Mesmo_texto_cifra_diferente_a_cada_vez()
    {
        var cofre = NovoCofre();
        Assert.NotEqual(cofre.Cifrar("x", "c"), cofre.Cifrar("x", "c"));
    }

    [Fact]
    public void Nao_abre_com_outro_contexto_outra_chave_ou_valor_adulterado()
    {
        var cofre = NovoCofre();
        var cifrado = cofre.Cifrar("segredo", "loja-a:WhatsApp");

        Assert.Null(cofre.Decifrar(cifrado, "loja-b:WhatsApp"));
        Assert.Null(NovoCofre(Convert.ToBase64String(new byte[32])).Decifrar(cifrado, "loja-a:WhatsApp"));
        var adulterado = cifrado[..^2] + (cifrado[^2] == 'A' ? "B" : "A") + cifrado[^1];
        Assert.Null(cofre.Decifrar(adulterado, "loja-a:WhatsApp"));
        Assert.Null(cofre.Decifrar("lixo", "loja-a:WhatsApp"));
    }

    [Fact]
    public void Sem_chave_mestra_nao_cifra_e_nao_abre()
    {
        var cofre = new Cofre(Options.Create(new PlataformaOptions()));
        Assert.False(cofre.Disponivel);
        Assert.Throws<RegraException>(() => cofre.Cifrar("x", "c"));
        Assert.Null(cofre.Decifrar(NovoCofre().Cifrar("x", "c"), "c"));
    }

    [Fact]
    public void Chave_mestra_de_tamanho_errado_impede_a_subida()
    {
        Assert.Throws<InvalidOperationException>(() => NovoCofre(Convert.ToBase64String(new byte[16])));
        Assert.Throws<InvalidOperationException>(() => NovoCofre("não é base64"));
    }
}

public class CredenciaisNoBancoTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    [FactComBanco]
    public async Task Credencial_de_uma_loja_nao_abre_para_outra_e_nao_fica_aberta_no_banco()
    {
        var cofre = CofreTests.NovoCofre();
        await using (var db = banco.Criar(banco.A.Id))
            await new ServicoCredenciais(db, cofre).SalvarAsync(banco.A.Id, new CredencialWhatsApp("EAAG-token-da-loja-a"), "token …ja-a", default);

        await using (var db = banco.Criar(banco.B.Id))
        {
            var servico = new ServicoCredenciais(db, cofre);
            Assert.Null(await servico.LerAsync<CredencialWhatsApp>(banco.B.Id, default));
            Assert.Empty(await db.CredenciaisLoja.Where(c => c.Tipo == TipoCredencial.WhatsApp).ToListAsync());

            // Copiar o valor cifrado da loja A para a loja B direto no banco não adianta: não abre.
            var daA = await db.CredenciaisLoja.IgnoreQueryFilters().SingleAsync(c => c.LojaId == banco.A.Id && c.Tipo == TipoCredencial.WhatsApp);
            Assert.DoesNotContain("token-da-loja-a", daA.Cifrado);
            db.CredenciaisLoja.Add(new CredencialLoja { LojaId = banco.B.Id, Tipo = TipoCredencial.WhatsApp, Cifrado = daA.Cifrado });
            await db.SaveChangesAsync();
            Assert.Null(await servico.LerAsync<CredencialWhatsApp>(banco.B.Id, default));
            await servico.RemoverAsync<CredencialWhatsApp>(banco.B.Id, default);
        }

        await using (var db = banco.Criar(banco.A.Id))
            Assert.Equal("EAAG-token-da-loja-a", (await new ServicoCredenciais(db, cofre).LerAsync<CredencialWhatsApp>(banco.A.Id, default))?.AccessToken);
    }

    [FactComBanco]
    public async Task Login_continua_valido_depois_de_um_deploy_novo()
    {
        WebApplicationFactory<Program> Instancia() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Testing")
            .UseSetting("Banco:MigrarAoIniciar", "false")
            .UseSetting("ConnectionStrings:Banco", banco.Conexao));

        await using var antes = Instancia();
        var client = antes.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = banco.A.EmailDono, senha = BancoDeTeste.Senha });
        login.EnsureSuccessStatusCode();
        var cookie = login.Headers.GetValues("Set-Cookie").First().Split(';')[0];

        // Outra instância da API (como depois de um deploy), sem nada em memória: as chaves vêm do banco.
        await using var depois = Instancia();
        var outro = depois.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/eu");
        req.Headers.Add("Cookie", cookie);
        var eu = await outro.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, eu.StatusCode);
        await using var db = banco.Criar(null);
        Assert.NotEmpty(await db.DataProtectionKeys.ToListAsync());
    }
}
