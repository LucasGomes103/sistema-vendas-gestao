using SistemaVendas.Application.Atendimento;
using SistemaVendas.Domain.Caixa;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Domain.Vendas;
using SistemaVendas.Infrastructure.IA;

namespace SistemaVendas.Api.Tests;

/// <summary>Fase 4: o robô e as mensagens falam a língua do tipo de loja.</summary>
public class SegmentosTests
{
    [Theory]
    [InlineData("Aquarismo", "aquarismo")]
    [InlineData("Aquário e peixes", "aquarismo")]
    [InlineData("loja de PEIXES ornamentais", "aquarismo")]
    [InlineData("Roupas e acessórios", "geral")]
    [InlineData("Auto peças mecânica", "geral")]
    [InlineData(null, "geral")]
    public void Tipo_de_loja_escolhe_o_vocabulario(string? segmento, string chave) =>
        Assert.Equal(chave, Vocabulario.Para(segmento).Chave);

    private static string Prompt(string? segmento) =>
        GeminiAtendente.InstrucaoSistema("Loja X", new TextosProntos("frete", "chamo", false, Vocabulario.Para(segmento)), false);

    [Fact]
    public void Robo_de_aquario_fala_de_peixe_e_quarentena()
    {
        var p = Prompt("Aquarismo");
        Assert.Contains("loja de peixes ornamentais", p);
        Assert.Contains("quarentena", p);
        Assert.Contains("buscar_produto", p);
        Assert.Contains("peixes", GeminiAtendente.Ferramentas(Vocabulario.Aquarismo));
    }

    [Theory]
    [InlineData("Roupas e acessórios", "ramo de roupas e acessórios")]
    [InlineData("Auto peças", "ramo de auto peças")]
    public void Robo_de_outros_segmentos_nao_fala_de_peixe(string segmento, string descricao)
    {
        var p = Prompt(segmento);
        var ferramentas = GeminiAtendente.Ferramentas(Vocabulario.Para(segmento));
        Assert.Contains(descricao, p);
        foreach (var texto in new[] { p, ferramentas })
        {
            Assert.DoesNotContain("peixe", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("espécie", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("quarentena", texto, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("Marca ou modelo".ToLowerInvariant(), ferramentas);
        // As ferramentas continuam um JSON válido com as chaves trocadas.
        var json = System.Text.Json.Nodes.JsonNode.Parse(ferramentas)!.ToJsonString();
        Assert.Contains("produto_id", json);
    }

    [Fact]
    public void Mensagens_ao_cliente_seguem_o_tipo_de_loja()
    {
        var entrega = new Entrega { NumeroPedido = 7, Tipo = TipoEntrega.Entrega };
        Assert.Contains("saquinho", RegrasEntrega.MensagemSaiu(entrega, Vocabulario.Aquarismo));
        Assert.DoesNotContain("aquário", RegrasEntrega.MensagemSaiu(entrega, Vocabulario.Para("Roupas")));
        Assert.Contains("peixes", RegrasEntrega.MensagemEntregue(entrega, Vocabulario.Aquarismo));
        Assert.Contains("produtos", RegrasEntrega.MensagemEntregue(entrega, Vocabulario.Para("Roupas")));
        Assert.Contains("Compra de mercadoria", RegrasCaixa.CategoriasSaidaPara(Vocabulario.Para("Autopeças")));
        Assert.Contains("Compra de peixes", RegrasCaixa.CategoriasSaidaPara(Vocabulario.Aquarismo));
    }

    [Theory]
    [InlineData("Roupas e acessórios", "Tamanho")]
    [InlineData("Autopeças", "Aplicação (veículo e ano)")]
    [InlineData("Pet shop", "Peso")]
    [InlineData("Qualquer coisa", "Marca")]
    [InlineData("Aquarismo", "Temperatura")]
    public void Cada_ramo_sugere_os_campos_que_importam(string segmento, string campo) =>
        Assert.Contains(campo, Vocabulario.Para(segmento).CamposSugeridos!);
}
