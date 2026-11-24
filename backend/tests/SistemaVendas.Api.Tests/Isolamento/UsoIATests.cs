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

public class RegrasUsoIATests
{
    [Theory]
    [InlineData(null, 1000, 1000)]
    [InlineData(50, 1000, 50)]
    [InlineData(0, 1000, 0)]
    [InlineData(null, 0, 0)]
    [InlineData(-3, 1000, 0)]
    public void Limite_da_loja_vence_o_padrao(int? daLoja, int padrao, int esperado) =>
        Assert.Equal(esperado, RegrasUsoIA.LimiteDaLoja(daLoja, padrao));

    [Theory]
    [InlineData(999, 1000, false)]
    [InlineData(1000, 1000, true)]
    [InlineData(50000, 0, false)]
    public void Esgota_so_com_limite(int usadas, int limite, bool esperado) => Assert.Equal(esperado, RegrasUsoIA.Esgotado(usadas, limite));

    [Fact]
    public void Dia_segue_o_fuso_da_loja()
    {
        // 02:00 UTC do dia 1º ainda é dia 30 (ou 31) em São Paulo.
        var agora = new DateTimeOffset(2026, 12, 1, 2, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 11, 30), RegrasUsoIA.Hoje(agora, "America/Sao_Paulo"));
        Assert.Equal(new DateOnly(2026, 11, 30), RegrasUsoIA.Hoje(agora, "fuso/que-nao-existe"));
        Assert.Equal(new DateOnly(2026, 11, 1), RegrasUsoIA.InicioDoMes(new DateOnly(2026, 11, 30)));
    }
}

/// <summary>A IA da plataforma conta o uso por loja e respeita o limite do mês de cada uma.</summary>
public class UsoIATests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private WebApplicationFactory<Program> Fabrica(GeminiFalso gemini) => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao)
        .UseSetting("IA:Gemini:ApiKey", "chave-de-teste")
        .UseSetting("IA:LimiteRespostasMes", "2")
        .ConfigureTestServices(s => s.AddHttpClient<GeminiAtendente>().ConfigurePrimaryHttpMessageHandler(() => gemini)));

    private static async Task<RespostaAtendente> ResponderAsync(WebApplicationFactory<Program> app, Guid lojaId)
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ILojaAtual>().Definir(lojaId);
        var atendente = scope.ServiceProvider.GetRequiredService<IAtendenteIA>();
        return await atendente.ResponderAsync("Loja", [new FalaConversa(true, "oi")], scope.ServiceProvider.GetRequiredService<IFerramentasAtendimento>(), default);
    }

    [FactComBanco]
    public async Task Conta_o_uso_por_loja_e_passa_para_regras_quando_o_limite_acaba()
    {
        var gemini = new GeminiFalso();
        await using var app = Fabrica(gemini);

        // Loja A: limite padrão (2). As duas primeiras saem da IA, a terceira por regras, sem chamar o Gemini.
        Assert.Equal("Oi! Como posso ajudar?", (await ResponderAsync(app, banco.A.Id)).Texto);
        Assert.Equal("Oi! Como posso ajudar?", (await ResponderAsync(app, banco.A.Id)).Texto);
        var terceira = await ResponderAsync(app, banco.A.Id);
        Assert.NotEqual("Oi! Como posso ajudar?", terceira.Texto);
        Assert.Equal(2, gemini.Chamadas);

        // Loja B (outro limite) não é afetada pelo uso da A.
        await using (var db = banco.Criar(banco.B.Id))
        {
            (await db.Lojas.SingleAsync(l => l.Id == banco.B.Id)).LimiteRespostasIAMes = 0; // sem limite
            await db.SaveChangesAsync();
        }
        for (var i = 0; i < 3; i++)
            Assert.Equal("Oi! Como posso ajudar?", (await ResponderAsync(app, banco.B.Id)).Texto);
        Assert.Equal(5, gemini.Chamadas);

        await using (var db = banco.Criar(banco.A.Id))
        {
            var hoje = await db.UsosIA.Where(u => u.Dia > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5)).SingleAsync();
            Assert.Equal(banco.A.Id, hoje.LojaId);
            Assert.Equal(2, hoje.Respostas);
            Assert.Equal(1, hoje.PorRegras);
            Assert.Equal(2, hoje.Chamadas);
            Assert.Equal(240, hoje.TokensEntrada);
            Assert.Equal(16, hoje.TokensSaida);
        }

        // O dono vê o uso do mês (o uso de 40 dias atrás não conta).
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = banco.A.EmailDono, senha = BancoDeTeste.Senha })).EnsureSuccessStatusCode();
        var uso = await client.GetFromJsonAsync<JsonElement>("/api/v1/config/ia");
        Assert.True(uso.GetProperty("ativa").GetBoolean());
        Assert.Equal(2, uso.GetProperty("respostas").GetInt32());
        Assert.Equal(2, uso.GetProperty("limite").GetInt32());
        Assert.True(uso.GetProperty("esgotado").GetBoolean());
    }
}
