using SistemaVendas.Domain.Lojas;

namespace SistemaVendas.Domain.Tests;

public class LojaTests
{
    [Fact]
    public void Por_padrao_nao_atende_ninguem()
    {
        var loja = new Loja { Nome = "L", Slug = "l" };
        Assert.False(loja.PodeAtender("5511987654321"));
    }

    [Fact]
    public void Atende_so_quem_esta_na_lista()
    {
        var loja = new Loja { Nome = "L", Slug = "l", NumerosPermitidos = ["5511987654321"] };

        Assert.True(loja.PodeAtender("5511987654321"));
        Assert.False(loja.PodeAtender("5511900000000"));
    }

    [Fact]
    public void Atender_todos_libera_qualquer_numero()
    {
        var loja = new Loja { Nome = "L", Slug = "l", AtenderTodos = true };
        Assert.True(loja.PodeAtender("5511900000000"));
    }

    [Fact]
    public void Normaliza_numeros_com_mascara_e_sem_DDI()
    {
        var lista = Loja.NormalizarNumeros(["(11) 98765-4321", "5511987654321", "+55 11 91234-5678", "123"]);

        Assert.Equal(["5511987654321", "5511912345678"], lista);
    }
}
