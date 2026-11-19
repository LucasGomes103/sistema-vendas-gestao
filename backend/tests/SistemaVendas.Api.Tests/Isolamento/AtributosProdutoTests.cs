using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SistemaVendas.Domain.Catalogo;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>Campos opcionais do produto (Tamanho, Cor, Marca...): salvos, mostrados no painel e na vitrine.</summary>
public class AtributosProdutoTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public void Lista_tira_vazios_e_repetidos_e_respeita_o_maximo()
    {
        var lista = RegrasAtributos.Normalizar(
        [
            new() { Nome = " Cor ", Valor = " Preto " }, new() { Nome = "cor", Valor = "Azul" }, new() { Nome = "Tamanho", Valor = "" },
            .. Enumerable.Range(1, 12).Select(i => new AtributoProduto { Nome = $"C{i}", Valor = "x" }),
        ]);
        Assert.Equal(RegrasAtributos.Maximo, lista.Count);
        Assert.Equal(("Cor", "Preto"), (lista[0].Nome, lista[0].Valor));
        Assert.DoesNotContain(lista, a => a.Nome == "Tamanho");
        Assert.Equal("Cor: Preto · Marca: Bosch", RegrasAtributos.Resumo([new() { Nome = "Cor", Valor = "Preto" }, new() { Nome = "Marca", Valor = "Bosch" }]));
        Assert.Null(RegrasAtributos.Resumo([]));
    }

    [FactComBanco]
    public async Task Campos_salvos_aparecem_no_painel_e_na_vitrine()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseEnvironment("Testing")
            .UseSetting("Banco:MigrarAoIniciar", "false")
            .UseSetting("ConnectionStrings:Banco", banco.Conexao));
        var dono = app.CreateClient();
        (await dono.PostAsJsonAsync("/api/v1/auth/login", new { email = banco.B.EmailDono, senha = BancoDeTeste.Senha })).EnsureSuccessStatusCode();

        var especies = await dono.GetFromJsonAsync<JsonElement>("/api/v1/especies");
        var atual = especies.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == banco.B.EspecieId);
        Assert.Empty(atual.GetProperty("atributos").EnumerateArray()); // produto antigo: lista vazia

        var corpo = new
        {
            nomePopular = atual.GetProperty("nomePopular").GetString(),
            nomeCientifico = (string?)null,
            tamanhoCm = (decimal?)null,
            preco = 10m,
            observacoes = (string?)null,
            atributos = new[] { new { nome = "Tamanho", valor = "M" }, new { nome = "Cor", valor = "Preto" }, new { nome = "cor", valor = "repetido" } },
        };
        Assert.Equal(HttpStatusCode.NoContent, (await dono.PutAsJsonAsync($"/api/v1/especies/{banco.B.EspecieId}", corpo)).StatusCode);

        especies = await dono.GetFromJsonAsync<JsonElement>("/api/v1/especies");
        var salvo = especies.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == banco.B.EspecieId).GetProperty("atributos");
        Assert.Equal(["Tamanho: M", "Cor: Preto"], salvo.EnumerateArray().Select(a => $"{a.GetProperty("nome").GetString()}: {a.GetProperty("valor").GetString()}"));

        var vitrine = await app.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/public/{banco.B.Slug}/catalogo");
        var item = vitrine.GetProperty("itens").EnumerateArray().SingleOrDefault(i => i.GetProperty("id").GetGuid() == banco.B.EspecieId);
        if (item.ValueKind != JsonValueKind.Undefined) // só aparece se tiver saldo
            Assert.Equal(2, item.GetProperty("atributos").GetArrayLength());

        var demais = new { corpo.nomePopular, preco = 10m, atributos = Enumerable.Range(1, 9).Select(i => new { nome = $"C{i}", valor = "x" }) };
        Assert.Equal(HttpStatusCode.BadRequest, (await dono.PutAsJsonAsync($"/api/v1/especies/{banco.B.EspecieId}", demais)).StatusCode);
    }
}
