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
}
