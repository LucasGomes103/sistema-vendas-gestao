using SistemaVendas.Domain.Entregas;

namespace SistemaVendas.Domain.Tests;

public class RegrasFreteKmTests
{
    private static readonly FretePorKm Cfg = new() { Ativo = true, KmInclusos = 2, ValorBase = 10, ValorPorKm = 2.5m, KmMaximo = 20 };

    [Theory]
    [InlineData(0.8, 10)]
    [InlineData(2.0, 10)]
    [InlineData(3.0, 12.5)]
    [InlineData(7.4, 23.5)]
    [InlineData(20.0, 55)]
    public void Base_ate_os_km_inclusos_e_valor_por_km_depois(double km, double esperado) =>
        Assert.Equal((decimal)esperado, RegrasFreteKm.Calcular(Cfg, (decimal)km));

    [Fact]
    public void Acima_do_maximo_nao_entrega() =>
        Assert.Null(RegrasFreteKm.Calcular(Cfg, 20.1m));

    [Fact]
    public void Km_arredonda_para_cima_em_uma_casa()
    {
        Assert.Equal(5.3m, RegrasFreteKm.Arredondar(5.21));
        Assert.Equal(5.2m, RegrasFreteKm.Arredondar(5.2));
    }

    [Fact]
    public void Linha_reta_entre_dois_pontos_conhecidos()
    {
        // Praça da Sé até o centro de Guarulhos: cerca de 17 km em linha reta.
        var km = RegrasFreteKm.LinhaReta(-23.5505, -46.6333, -23.4628, -46.5333);
        Assert.InRange(km, 13, 15);
    }

    [Theory]
    [InlineData(-1, 10, 2, 20, 1)]
    [InlineData(2, 10, 2, 0, 1)]
    [InlineData(30, 10, 2, 20, 1)]
    [InlineData(2, 10, 2, 20, 90)]
    public void Configuracao_invalida_e_recusada(decimal inclusos, decimal baseV, decimal porKm, decimal max, int dias) =>
        Assert.NotNull(RegrasFreteKm.Validar(inclusos, baseV, porKm, max, dias));
}
