using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.IA;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>Gemini falso: responde sempre o mesmo texto, com o uso de tokens.</summary>
public sealed class GeminiFalso : HttpMessageHandler
{
    public int Chamadas;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref Chamadas);
        const string corpo = """
            {"candidates":[{"content":{"role":"model","parts":[{"text":"Oi! Como posso ajudar?"}]}}],
             "usageMetadata":{"promptTokenCount":120,"candidatesTokenCount":8,"totalTokenCount":128}}
            """;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") });
    }
}

public class RegrasLimiteIaTests
{
    [Theory]
    [InlineData(null, 1000, 1000)]
    [InlineData(50, 1000, 50)]
    [InlineData(0, 1000, 0)]
    [InlineData(null, 0, 0)]
    [InlineData(-3, 1000, 0)]
    public void Limite_da_loja_vence_o_padrao(int? daLoja, int padrao, int esperado) =>
        Assert.Equal(esperado, RegrasLimiteIa.LimiteDaLoja(daLoja, padrao));

    [Theory]
    [InlineData(999, 1000, false)]
    [InlineData(1000, 1000, true)]
    [InlineData(50000, 0, false)]
    public void Esgota_so_com_limite(int usadas, int limite, bool esperado) => Assert.Equal(esperado, RegrasLimiteIa.Esgotado(usadas, limite));

    [Fact]
    public void Mes_comeca_no_horario_de_brasilia()
    {
        // 02:00 UTC do dia 1º ainda é dia 30 em Brasília.
        Assert.Equal(new DateOnly(2026, 11, 1), RegrasLimiteIa.InicioDoMes(new DateTimeOffset(2026, 12, 1, 2, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 12, 1), RegrasLimiteIa.InicioDoMes(new DateTimeOffset(2026, 12, 1, 4, 0, 0, TimeSpan.Zero)));
    }
}

/// <summary>A IA da plataforma respeita o limite do mês de cada loja.</summary>
public class LimiteIaTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private WebApplicationFactory<Program> Fabrica(GeminiFalso gemini) => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao)
        .UseSetting("IA:Gemini:ApiKey", "chave-de-teste")
        .UseSetting("IA:LimiteRespostasMes", "2")
        .ConfigureTestServices(s => s.AddHttpClient<GeminiAtendente>().ConfigurePrimaryHttpMessageHandler(() => gemini)));

    /// <summary>Como o robô faz: responde e grava o gasto no uso_ia.</summary>
    private static async Task<RespostaAtendente> ResponderAsync(WebApplicationFactory<Program> app, Guid lojaId)
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ILojaAtual>().Definir(lojaId);
        var atendente = scope.ServiceProvider.GetRequiredService<IAtendenteIA>();
        var r = await atendente.ResponderAsync("Loja", [new FalaConversa(true, "oi")], scope.ServiceProvider.GetRequiredService<IFerramentasAtendimento>(), default);
        await RegistroUsoIa.RegistrarAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), lojaId, r.Consumo, DateTimeOffset.UtcNow, default);
        return r;
    }

    [FactComBanco]
    public async Task Passa_para_regras_quando_o_limite_do_mes_acaba_sem_afetar_outra_loja()
    {
        var gemini = new GeminiFalso();
        await using var app = Fabrica(gemini);

        // Loja A: limite padrão (2). As duas primeiras saem da IA, a terceira por regras, sem chamar o Gemini.
        Assert.Equal("Oi! Como posso ajudar?", (await ResponderAsync(app, banco.A.Id)).Texto);
        Assert.Equal("Oi! Como posso ajudar?", (await ResponderAsync(app, banco.A.Id)).Texto);
        var terceira = await ResponderAsync(app, banco.A.Id);
        Assert.NotEqual("Oi! Como posso ajudar?", terceira.Texto);
        Assert.Null(terceira.Consumo);
        Assert.Equal(2, gemini.Chamadas);

        // Loja B sem limite (0): segue na IA.
        await using (var db = banco.Criar(banco.B.Id))
        {
            (await db.Lojas.SingleAsync(l => l.Id == banco.B.Id)).LimiteRespostasIAMes = 0;
            await db.SaveChangesAsync();
        }
        for (var i = 0; i < 3; i++)
            Assert.Equal("Oi! Como posso ajudar?", (await ResponderAsync(app, banco.B.Id)).Texto);
        Assert.Equal(5, gemini.Chamadas);

        // O dono vê o uso do mês (a linha do seed conta como resposta por regras).
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = banco.A.EmailDono, senha = BancoDeTeste.Senha })).EnsureSuccessStatusCode();
        var uso = await client.GetFromJsonAsync<JsonElement>("/api/v1/config/ia");
        Assert.True(uso.GetProperty("ativa").GetBoolean());
        Assert.Equal(2, uso.GetProperty("respostas").GetInt32());
        Assert.Equal(2, uso.GetProperty("porRegras").GetInt32());
        Assert.Equal(2, uso.GetProperty("limite").GetInt32());
        Assert.True(uso.GetProperty("esgotado").GetBoolean());
    }
}
