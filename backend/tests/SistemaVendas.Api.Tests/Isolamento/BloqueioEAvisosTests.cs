using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Assinaturas;
using SistemaVendas.Infrastructure.Email;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>Carência, bloqueio por atraso (painel só leitura) e e-mails de cobrança e de limite.</summary>
public class BloqueioEAvisosTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private readonly EmailFalso _email = new();
    private readonly CobrancaFalsa _mp = new();

    private WebApplicationFactory<Program> Fabrica() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao)
        .UseSetting("Plataforma:Admins", banco.A.EmailDono)
        .ConfigureTestServices(s =>
        {
            s.AddSingleton<IEnvioEmail>(_email);
            s.AddSingleton<ICobrancaMercadoPago>(_mp);
        }));

    private async Task<(Guid Loja, string Email)> LojaAsync(Action<Assinatura> ajustar)
    {
        await using var db = banco.Criar(null);
        var slug = "bloq-" + Guid.NewGuid().ToString("N")[..8];
        var loja = new Loja { Nome = "Loja " + slug, Slug = slug };
        var dono = new Usuario { LojaId = loja.Id, Nome = "Dono", Email = $"dono@{slug}.teste", Papel = Papel.Dono };
        dono.SenhaHash = new PasswordHasher<Usuario>().HashPassword(dono, BancoDeTeste.Senha);
        var a = new Assinatura { LojaId = loja.Id, Plano = PlanoId.Essencial };
        ajustar(a);
        db.AddRange(loja, dono, a);
        await db.SaveChangesAsync();
        return (loja.Id, dono.Email);
    }

    private static async Task<HttpClient> EntrarAsync(WebApplicationFactory<Program> app, string email)
    {
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = BancoDeTeste.Senha })).EnsureSuccessStatusCode();
        return client;
    }

    [FactComBanco]
    public async Task Bloqueada_le_tudo_mas_nao_muda_nada_fora_da_assinatura()
    {
        var (_, email) = await LojaAsync(a => { a.Status = StatusAssinatura.Teste; a.TesteAte = DateTimeOffset.UtcNow.AddDays(-30); });
        await using var app = Fabrica();
        var dono = await EntrarAsync(app, email);

        Assert.Equal(HttpStatusCode.OK, (await dono.GetAsync("/api/v1/especies")).StatusCode);
        var criar = await dono.PostAsJsonAsync("/api/v1/especies", new { nomePopular = "Novo", preco = 10 });
        Assert.Equal(HttpStatusCode.Forbidden, criar.StatusCode);
        Assert.Contains("assinatura_bloqueada", await criar.Content.ReadAsStringAsync());

        // A tela de assinatura continua funcionando (para pagar) e dá para sair.
        Assert.Equal(HttpStatusCode.NoContent, (await dono.PostAsync("/api/v1/assinatura/conferir", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dono.PostAsJsonAsync("/api/v1/assinatura/assinar", new { plano = "Essencial", emailPagador = "p@mp.com" })).StatusCode);
    }

    [FactComBanco]
    public async Task Carencia_ainda_deixa_mudar()
    {
        var (_, email) = await LojaAsync(a => { a.Status = StatusAssinatura.Ativa; a.PagoAte = DateTimeOffset.UtcNow.AddDays(-2); });
        await using var app = Fabrica();
        var dono = await EntrarAsync(app, email);
        Assert.Equal(HttpStatusCode.Created, (await dono.PostAsJsonAsync("/api/v1/especies", new { nomePopular = "Novo", preco = 10 })).StatusCode);
    }

    [FactComBanco]
    public async Task Avisos_de_teste_acabando_e_mensalidade_em_aberto_saem_uma_vez_so()
    {
        var (lojaTeste, emailTeste) = await LojaAsync(a => { a.Status = StatusAssinatura.Teste; a.TesteAte = DateTimeOffset.UtcNow.AddDays(1); });
        var (lojaAtraso, emailAtraso) = await LojaAsync(a => { a.Status = StatusAssinatura.Ativa; a.PagoAte = DateTimeOffset.UtcNow.AddDays(-1); });
        await using var app = Fabrica();

        using (var escopo = app.Services.CreateScope())
        {
            var avisos = escopo.ServiceProvider.GetRequiredService<AvisosAssinatura>();
            Assert.Equal(1, await avisos.VerificarAsync(lojaTeste, DateTimeOffset.UtcNow, CancellationToken.None));
            Assert.Equal(1, await avisos.VerificarAsync(lojaAtraso, DateTimeOffset.UtcNow, CancellationToken.None));
            Assert.Equal(0, await avisos.VerificarAsync(lojaTeste, DateTimeOffset.UtcNow, CancellationToken.None));
        }
        Assert.Contains(_email.Enviados, e => e.Para == emailTeste && e.Assunto.StartsWith("Seu teste grátis está acabando"));
        Assert.Contains(_email.Enviados, e => e.Para == emailAtraso && e.Assunto.StartsWith("Mensalidade em aberto"));
        Assert.Single(_email.Enviados, e => e.Para == emailTeste);
    }

    [FactComBanco]
    public async Task Pagamento_confirmado_manda_email()
    {
        var (loja, email) = await LojaAsync(a => { a.Status = StatusAssinatura.Teste; a.TesteAte = DateTimeOffset.UtcNow.AddDays(3); });
        await using var app = Fabrica();
        var dono = await EntrarAsync(app, email);
        await dono.PostAsJsonAsync("/api/v1/assinatura/assinar", new { plano = "Essencial", emailPagador = "p@mp.com" });
        await using var db = banco.Criar(loja);
        var mpId = db.Assinaturas.Single().MpAssinaturaId!;
        _mp.Autorizar(mpId);
        _mp.Cobrancas["7001"] = new CobrancaMp("7001", mpId, "processed", "approved", 297m);
        await app.CreateClient().PostAsJsonAsync("/api/v1/webhooks/assinatura?type=subscription_authorized_payment&data.id=7001", new { });
        Assert.Contains(_email.Enviados, e => e.Para == email && e.Assunto.StartsWith("Pagamento confirmado"));
    }
}
