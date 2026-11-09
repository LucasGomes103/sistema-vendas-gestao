using SistemaVendas.Domain.Lojas;

namespace SistemaVendas.Domain.Tests;

public class RegrasUsuarioTests
{
    private static Usuario Novo(Papel papel, bool ativo = true) => new() { Nome = "U", Email = "u@x.com", Papel = papel, Ativo = ativo };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc123")]
    [InlineData("somenteletras")]
    [InlineData("12345678")]
    public void Senha_fraca_e_recusada(string? senha) => Assert.NotNull(RegrasUsuario.ValidarSenha(senha));

    [Fact]
    public void Senha_com_8_caracteres_letra_e_numero_e_aceita() => Assert.Null(RegrasUsuario.ValidarSenha("peixe123"));

    [Fact]
    public void Nao_exclui_a_si_mesmo()
    {
        var eu = Novo(Papel.Dono);
        Assert.NotNull(RegrasUsuario.ValidarExclusao(eu, eu.Id, donosAtivos: 2));
    }

    [Fact]
    public void Nao_exclui_o_ultimo_dono_ativo()
    {
        var outro = Novo(Papel.Dono);
        Assert.NotNull(RegrasUsuario.ValidarExclusao(outro, Guid.NewGuid(), donosAtivos: 1));
    }

    [Fact]
    public void Exclui_vendedor_e_dono_quando_ha_outro()
    {
        Assert.Null(RegrasUsuario.ValidarExclusao(Novo(Papel.Vendedor), Guid.NewGuid(), donosAtivos: 1));
        Assert.Null(RegrasUsuario.ValidarExclusao(Novo(Papel.Dono), Guid.NewGuid(), donosAtivos: 2));
    }

    [Fact]
    public void Nao_desativa_nem_muda_o_proprio_papel()
    {
        var eu = Novo(Papel.Dono);
        Assert.NotNull(RegrasUsuario.ValidarAlteracao(eu, Papel.Dono, novoAtivo: false, eu.Id, donosAtivos: 2));
        Assert.NotNull(RegrasUsuario.ValidarAlteracao(eu, Papel.Vendedor, novoAtivo: true, eu.Id, donosAtivos: 2));
        Assert.Null(RegrasUsuario.ValidarAlteracao(eu, Papel.Dono, novoAtivo: true, eu.Id, donosAtivos: 1));
    }

    [Fact]
    public void Nao_rebaixa_nem_desativa_o_ultimo_dono()
    {
        var outro = Novo(Papel.Dono);
        Assert.NotNull(RegrasUsuario.ValidarAlteracao(outro, Papel.Vendedor, novoAtivo: true, Guid.NewGuid(), donosAtivos: 1));
        Assert.NotNull(RegrasUsuario.ValidarAlteracao(outro, Papel.Dono, novoAtivo: false, Guid.NewGuid(), donosAtivos: 1));
        Assert.Null(RegrasUsuario.ValidarAlteracao(outro, Papel.Vendedor, novoAtivo: true, Guid.NewGuid(), donosAtivos: 2));
    }

    [Fact]
    public void Promove_vendedor_a_dono()
    {
        Assert.Null(RegrasUsuario.ValidarAlteracao(Novo(Papel.Vendedor), Papel.Dono, novoAtivo: true, Guid.NewGuid(), donosAtivos: 1));
    }
}
