using SistemaVendas.Domain.Entregas;

namespace SistemaVendas.Domain.Tests;

public class RegrasFreteTests
{
    private static ZonaEntrega Zona(string nome, int inicio, int fim, decimal valor) =>
        new() { Nome = nome, CepInicio = inicio, CepFim = fim, Valor = valor, Dias = 1 };

    [Theory]
    [InlineData("07010-000", 7010000)]
    [InlineData("07010000", 7010000)]
    [InlineData(" 07.010-000 ", 7010000)]
    public void Le_CEP_em_varios_formatos(string texto, int esperado) => Assert.Equal(esperado, RegrasFrete.LerCep(texto));

    [Theory]
    [InlineData("0701000")]
    [InlineData("")]
    [InlineData(null)]
    public void CEP_sem_8_digitos_e_invalido(string? texto) => Assert.Null(RegrasFrete.LerCep(texto));

    [Theory]
    [InlineData("meu cep é 07010-000, vocês entregam?", 7010000)]
    [InlineData("cep 07010 000", 7010000)]
    [InlineData("quero 2 ramirezi", null)]
    [InlineData("liga 11987654321", null)]
    public void Acha_CEP_no_meio_da_mensagem(string texto, int? esperado) => Assert.Equal(esperado, RegrasFrete.AcharCep(texto));

    [Fact]
    public void Faixa_mais_especifica_vence()
    {
        var zonas = new[] { Zona("Grande SP", 1000000, 9999999, 25), Zona("Guarulhos", 7000000, 7399999, 15) };
        Assert.Equal("Guarulhos", RegrasFrete.Calcular(7010000, zonas)?.Nome);
        Assert.Equal("Grande SP", RegrasFrete.Calcular(5000000, zonas)?.Nome);
        Assert.Null(RegrasFrete.Calcular(20000000, zonas));
    }

    [Fact]
    public void Limites_da_faixa_estao_incluidos()
    {
        var zonas = new[] { Zona("Guarulhos", 7000000, 7399999, 15) };
        Assert.NotNull(RegrasFrete.Calcular(7000000, zonas));
        Assert.NotNull(RegrasFrete.Calcular(7399999, zonas));
    }

    [Fact]
    public void Formata_com_hifen() => Assert.Equal("07010-000", RegrasFrete.Formatar(7010000));

    [Fact]
    public void Valida_faixa() 
    {
        Assert.NotNull(RegrasFrete.Validar(7399999, 7000000, 10, 1));
        Assert.NotNull(RegrasFrete.Validar(null, 7000000, 10, 1));
        Assert.NotNull(RegrasFrete.Validar(7000000, 7399999, -1, 1));
        Assert.Null(RegrasFrete.Validar(7000000, 7399999, 15, 1));
    }
}
