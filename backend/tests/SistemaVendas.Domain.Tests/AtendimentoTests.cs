using SistemaVendas.Domain.Atendimento;

namespace SistemaVendas.Domain.Tests;

public class AtendimentoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 29, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Janela_de_24h_aberta_logo_apos_mensagem_do_cliente()
    {
        var conversa = new Conversa { UltimaMsgClienteEm = Agora.AddHours(-23) };
        Assert.True(conversa.DentroDaJanela(Agora));
    }

    [Fact]
    public void Janela_fecha_depois_de_24h()
    {
        Assert.False(new Conversa { UltimaMsgClienteEm = Agora.AddHours(-24) }.DentroDaJanela(Agora));
        Assert.False(new Conversa().DentroDaJanela(Agora));
    }

    [Fact]
    public void Falha_de_envio_agenda_nova_tentativa_com_espera_crescente()
    {
        var msg = new Mensagem { Conteudo = "oi", Status = StatusMensagem.Pendente };

        RegrasAtendimento.RegistrarFalha(msg, "timeout", Agora);
        var primeira = msg.ProximaTentativaEm!.Value - Agora;
        RegrasAtendimento.RegistrarFalha(msg, "timeout", Agora);
        var segunda = msg.ProximaTentativaEm!.Value - Agora;

        Assert.Equal(StatusMensagem.Pendente, msg.Status);
        Assert.True(segunda > primeira);
    }

    [Fact]
    public void Depois_do_limite_a_mensagem_fica_como_falha()
    {
        var msg = new Mensagem { Conteudo = "oi", Status = StatusMensagem.Pendente };
        for (var i = 0; i < RegrasAtendimento.MaximoTentativas; i++)
            RegrasAtendimento.RegistrarFalha(msg, "erro", Agora);

        Assert.Equal(StatusMensagem.Falhou, msg.Status);
        Assert.Null(msg.ProximaTentativaEm);
    }

    [Theory]
    [InlineData("5511987654321", "11*****4321")]
    [InlineData("11987654321", "11*****4321")]
    public void Telefone_mascarado_nos_logs(string telefone, string esperado)
    {
        Assert.Equal(esperado, RegrasAtendimento.MascararTelefone(telefone));
    }

    [Theory]
    [InlineData("parar", true)]
    [InlineData("SAIR", true)]
    [InlineData(" Parar! ", true)]
    [InlineData("quero parar de comprar em outra loja", false)]
    [InlineData("tem ramirezi?", false)]
    public void Parar_ou_sair_desliga_marketing(string texto, bool esperado)
    {
        Assert.Equal(esperado, RegrasAtendimento.PedeParaParar(texto));
    }
}
