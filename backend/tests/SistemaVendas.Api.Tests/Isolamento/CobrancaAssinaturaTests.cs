using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Assinaturas;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>Mercado Pago da plataforma de mentira: guarda as assinaturas e as cobranças em memória.</summary>
public sealed class CobrancaFalsa : ICobrancaMercadoPago
{
    public Dictionary<string, AssinaturaMp> Assinaturas { get; } = [];
    public Dictionary<string, CobrancaMp> Cobrancas { get; } = [];
    public List<string> Canceladas { get; } = [];
    public List<(string Id, decimal Valor)> Alteracoes { get; } = [];
    public DateTimeOffset? UltimoInicio { get; private set; }
    public bool Configurado => true;

    public Task<AssinaturaMp> CriarAsync(string motivo, Guid lojaId, string emailPagador, decimal valor, DateTimeOffset? inicio, string urlRetorno, CancellationToken ct)
    {
        var id = "pre" + Guid.NewGuid().ToString("N")[..12];
        UltimoInicio = inicio;
        var a = new AssinaturaMp(id, "pending", lojaId.ToString("N"), valor, 0, "https://mp.teste/checkout/" + id);
        lock (Assinaturas) Assinaturas[id] = a;
        return Task.FromResult(a);
    }

    public Task<AssinaturaMp?> BuscarAsync(string id, CancellationToken ct) => Task.FromResult(Assinaturas.GetValueOrDefault(id));

    public Task AlterarValorAsync(string id, string motivo, decimal valor, CancellationToken ct)
    {
        Alteracoes.Add((id, valor));
        Assinaturas[id] = Assinaturas[id] with { Valor = valor };
        return Task.CompletedTask;
    }

    public Task CancelarAsync(string id, CancellationToken ct)
    {
        Canceladas.Add(id);
        if (Assinaturas.TryGetValue(id, out var a))
            Assinaturas[id] = a with { Status = "cancelled" };
        return Task.CompletedTask;
    }

    public Task<CobrancaMp?> BuscarCobrancaAsync(string id, CancellationToken ct) => Task.FromResult(Cobrancas.GetValueOrDefault(id));

    public void Autorizar(string id) => Assinaturas[id] = Assinaturas[id] with { Status = "authorized" };
}

public class CobrancaAssinaturaTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private readonly CobrancaFalsa _mp = new();

    private WebApplicationFactory<Program> Fabrica() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao)
        .ConfigureTestServices(s => s.AddSingleton<ICobrancaMercadoPago>(_mp)));

    private async Task<(Guid Loja, string Email, DateTimeOffset TesteAte)> LojaEmTesteAsync()
    {
        await using var db = banco.Criar(null);
        var slug = "cobra-" + Guid.NewGuid().ToString("N")[..8];
        var loja = new Loja { Nome = "Loja " + slug, Slug = slug };
        var dono = new Usuario { LojaId = loja.Id, Nome = "Dono", Email = $"dono@{slug}.teste", Papel = Papel.Dono };
        dono.SenhaHash = new PasswordHasher<Usuario>().HashPassword(dono, BancoDeTeste.Senha);
        var assinatura = RegrasAssinatura.NovoTeste(loja.Id, null, DateTimeOffset.UtcNow.AddDays(-2));
        db.AddRange(loja, dono, assinatura);
        await db.SaveChangesAsync();
        // Lê de volta: o banco guarda microssegundos.
        return (loja.Id, dono.Email, (await LerAsync(loja.Id)).TesteAte!.Value);
    }

    private static async Task<HttpClient> EntrarAsync(WebApplicationFactory<Program> app, string email)
    {
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = BancoDeTeste.Senha })).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<Assinatura> LerAsync(Guid loja)
    {
        await using var db = banco.Criar(null);
        return await db.Assinaturas.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.LojaId == loja);
    }

    private static Task<HttpResponseMessage> AvisoAsync(HttpClient c, string tipo, string id) =>
        c.PostAsJsonAsync($"/api/v1/webhooks/assinatura?type={tipo}&data.id={id}", new { type = tipo, data = new { id } });

    [FactComBanco]
    public async Task Assina_no_teste_paga_e_cada_cobranca_aprovada_estende_um_mes_sem_contar_aviso_repetido()
    {
        var (loja, email, testeAte) = await LojaEmTesteAsync();
        await using var app = Fabrica();
        var dono = await EntrarAsync(app, email);

        var resp = await dono.PostAsJsonAsync("/api/v1/assinatura/assinar", new { plano = "Essencial", emailPagador = "Pagador@MP.com" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var url = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString();
        Assert.StartsWith("https://mp.teste/checkout/", url);
        var a = await LerAsync(loja);
        Assert.Equal(PlanoId.Essencial, a.Plano);
        Assert.Equal("pagador@mp.com", a.EmailPagador);
        // Ainda no teste: a primeira cobrança sai quando o teste acaba.
        Assert.Equal(testeAte, _mp.UltimoInicio);
        var mpId = a.MpAssinaturaId!;

        // Dono pagou: o Mercado Pago autoriza e cobra.
        _mp.Autorizar(mpId);
        _mp.Cobrancas["9001"] = new CobrancaMp("9001", mpId, "processed", "approved", 297m);
        var anonimo = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await AvisoAsync(anonimo, "subscription_authorized_payment", "9001")).StatusCode);
        a = await LerAsync(loja);
        Assert.Equal(StatusAssinatura.Ativa, a.Status);
        Assert.Equal(testeAte.AddMonths(1), a.PagoAte);

        // Aviso repetido e aviso da assinatura (que já conta 1 cobrança) não estendem de novo.
        await AvisoAsync(anonimo, "subscription_authorized_payment", "9001");
        _mp.Assinaturas[mpId] = _mp.Assinaturas[mpId] with { CobrancasPagas = 1 };
        await AvisoAsync(anonimo, "subscription_preapproval", mpId);
        Assert.Equal(testeAte.AddMonths(1), (await LerAsync(loja)).PagoAte);

        // Cobrança recusada não muda nada; a do mês seguinte aprovada soma mais um mês.
        _mp.Cobrancas["9002"] = new CobrancaMp("9002", mpId, "recycling", "rejected", 297m);
        await AvisoAsync(anonimo, "subscription_authorized_payment", "9002");
        Assert.Equal(testeAte.AddMonths(1), (await LerAsync(loja)).PagoAte);
        _mp.Cobrancas["9003"] = new CobrancaMp("9003", mpId, "processed", "approved", 297m);
        await AvisoAsync(anonimo, "subscription_authorized_payment", "9003");
        Assert.Equal(testeAte.AddMonths(2), (await LerAsync(loja)).PagoAte);

        // Troca de plano com a assinatura paga: muda só o valor das próximas cobranças, sem link novo.
        var troca = await dono.PostAsJsonAsync("/api/v1/assinatura/assinar", new { plano = "Completo", emailPagador = "pagador@mp.com" });
        Assert.Equal(JsonValueKind.Null, (await troca.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").ValueKind);
        Assert.Contains((mpId, 797m), _mp.Alteracoes);
        Assert.Equal(PlanoId.Completo, (await LerAsync(loja)).Plano);

        // Cancelar: para de cobrar e a loja usa até o fim do que pagou.
        Assert.Equal(HttpStatusCode.NoContent, (await dono.PostAsync("/api/v1/assinatura/cancelar", null)).StatusCode);
        a = await LerAsync(loja);
        Assert.Contains(mpId, _mp.Canceladas);
        Assert.Equal(StatusAssinatura.Cancelada, a.Status);
        Assert.Equal(SituacaoAssinatura.EmDia, RegrasAssinatura.Situacao(a, DateTimeOffset.UtcNow));
        Assert.Equal(SituacaoAssinatura.Bloqueada, RegrasAssinatura.Situacao(a, testeAte.AddMonths(2).AddDays(30)));
    }

    [FactComBanco]
    public async Task Link_sem_pagar_e_trocado_cancela_o_anterior_e_aviso_de_outra_assinatura_nao_mexe()
    {
        var (loja, email, _) = await LojaEmTesteAsync();
        await using var app = Fabrica();
        var dono = await EntrarAsync(app, email);
        await dono.PostAsJsonAsync("/api/v1/assinatura/assinar", new { plano = "Essencial", emailPagador = "p@mp.com" });
        var primeira = (await LerAsync(loja)).MpAssinaturaId!;
        await dono.PostAsJsonAsync("/api/v1/assinatura/assinar", new { plano = "Profissional", emailPagador = "p@mp.com" });
        var segunda = (await LerAsync(loja)).MpAssinaturaId!;
        Assert.NotEqual(primeira, segunda);
        Assert.Contains(primeira, _mp.Canceladas);

        // Cobrança da assinatura velha não credita.
        _mp.Cobrancas["8001"] = new CobrancaMp("8001", primeira, "processed", "approved", 297m);
        await AvisoAsync(app.CreateClient(), "subscription_authorized_payment", "8001");
        var a = await LerAsync(loja);
        Assert.Null(a.PagoAte);
        Assert.Equal(StatusAssinatura.Teste, a.Status);
    }

    [FactComBanco]
    public async Task Loja_original_e_isenta_nao_assinam()
    {
        await using var app = Fabrica();
        var donoA = await EntrarAsync(app, banco.A.EmailDono);
        var resp = await donoA.PostAsJsonAsync("/api/v1/assinatura/assinar", new { plano = "Essencial", emailPagador = "p@mp.com" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var donoB = await EntrarAsync(app, banco.B.EmailDono);
        resp = await donoB.PostAsJsonAsync("/api/v1/assinatura/assinar", new { plano = "Essencial", emailPagador = "p@mp.com" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("isenta", await resp.Content.ReadAsStringAsync());
    }
}
