using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Assinaturas;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>Super admin vê e muda a assinatura das lojas: plano, cortesia, mais dias de teste, isentar, cobrar.</summary>
public class AdminAssinaturaTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private WebApplicationFactory<Program> Fabrica() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao)
        .UseSetting("Plataforma:Admins", banco.A.EmailDono)
        .ConfigureTestServices(s => s.AddSingleton<ICobrancaMercadoPago>(new CobrancaFalsa())));

    private static async Task<HttpClient> EntrarAsync(WebApplicationFactory<Program> app, string email)
    {
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = BancoDeTeste.Senha })).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<Assinatura?> LerAsync(Guid loja)
    {
        await using var db = banco.Criar(null);
        return await db.Assinaturas.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(a => a.LojaId == loja);
    }

    [FactComBanco]
    public async Task Admin_troca_plano_da_cortesia_estende_teste_e_isenta_com_auditoria()
    {
        await using var app = Fabrica();
        var donoB = await EntrarAsync(app, banco.B.EmailDono);
        var url = $"/api/v1/admin/lojas/{banco.B.Id}/assinatura";
        Assert.Equal(HttpStatusCode.Forbidden, (await donoB.PostAsJsonAsync(url, new { acao = "isentar" })).StatusCode);

        var admin = await EntrarAsync(app, banco.A.EmailDono);
        // Loja original não muda.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync($"/api/v1/admin/lojas/{banco.A.Id}/assinatura", new { acao = "isentar" })).StatusCode);

        // B era isenta (sem assinatura): escolher plano começa um teste de 7 dias.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(url, new { acao = "plano", plano = "Essencial" })).StatusCode);
        var a = await LerAsync(banco.B.Id);
        Assert.Equal((StatusAssinatura.Teste, PlanoId.Essencial), (a!.Status, a.Plano));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(url, new { acao = "estender_teste", dias = 10 })).StatusCode);
        Assert.InRange((await LerAsync(banco.B.Id))!.TesteAte!.Value, DateTimeOffset.UtcNow.AddDays(16.9), DateTimeOffset.UtcNow.AddDays(17.1));

        var ate = DateTimeOffset.UtcNow.AddMonths(2);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(url, new { acao = "cortesia", ate, plano = "Completo", motivo = "piloto" })).StatusCode);
        a = await LerAsync(banco.B.Id);
        Assert.Equal((StatusAssinatura.Cortesia, PlanoId.Completo, "piloto"), (a!.Status, a.Plano, a.MotivoCortesia));

        var lojas = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/lojas");
        var b = lojas.EnumerateArray().Single(l => l.GetProperty("id").GetGuid() == banco.B.Id);
        Assert.Equal("Completo", b.GetProperty("plano").GetString());
        Assert.Equal("Cortesia", b.GetProperty("situacaoAssinatura").GetString());
        var detalhe = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/lojas/{banco.B.Id}");
        Assert.Equal(20_000, detalhe.GetProperty("loja").GetProperty("limiteIaMes").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(url, new { acao = "isentar" })).StatusCode);
        Assert.Equal(StatusAssinatura.Isenta, (await LerAsync(banco.B.Id))!.Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsJsonAsync(url, new { acao = "qualquer" })).StatusCode);

        await using var db = banco.Criar(null);
        Assert.True(await db.AuditoriaAdmin.CountAsync(x => x.LojaAlvoId == banco.B.Id && x.Acao == AcaoAdmin.Assinatura) >= 4);
    }
}
