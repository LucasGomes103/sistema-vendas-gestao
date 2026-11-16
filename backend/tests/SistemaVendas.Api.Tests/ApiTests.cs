using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SistemaVendas.Api.Tests;

public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Raiz_responde_com_o_nome_do_servico()
    {
        var client = factory.WithWebHostBuilder(b => b
                .UseEnvironment("Testing")
                .UseSetting("ConnectionStrings:Banco", "Host=localhost;Database=nao_usado"))
            .CreateClient();

        var resposta = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Contains("sistema-vendas-gestao", await resposta.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/v1/especies")]
    [InlineData("/api/v1/tanques")]
    [InlineData("/api/v1/auth/eu")]
    [InlineData("/api/v1/usuarios")]
    [InlineData("/api/v1/pedidos")]
    [InlineData("/api/v1/caixa")]
    [InlineData("/api/v1/entregas")]
    [InlineData("/api/v1/metricas")]
    [InlineData("/api/v1/metricas/exportar/pedidos")]
    [InlineData("/api/v1/config/pix")]
    public async Task Rotas_do_painel_exigem_login(string rota)
    {
        var client = factory.WithWebHostBuilder(b => b
                .UseEnvironment("Testing")
                .UseSetting("ConnectionStrings:Banco", "Host=localhost;Database=nao_usado"))
            .CreateClient();

        var resposta = await client.GetAsync(rota);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Simulador_existe_fora_de_desenvolvimento_e_exige_login()
    {
        var client = factory.WithWebHostBuilder(b => b
                .UseEnvironment("Production")
                .UseSetting("Banco:MigrarAoIniciar", "false")
                .UseSetting("ConnectionStrings:Banco", "Host=localhost;Database=nao_usado"))
            .CreateClient();

        var resposta = await client.PostAsync("/api/v1/dev/simular-mensagem", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }
}
