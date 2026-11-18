using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Vendas;

namespace SistemaVendas.Domain.Tests;

public class ResponsavelConversaTests
{
    private static readonly Guid Ana = Guid.NewGuid(), Bia = Guid.NewGuid();
    private static readonly DateTimeOffset Agora = new(2026, 11, 18, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Quem_assume_primeiro_fica_com_a_conversa()
    {
        var c = new Conversa();
        c.Assumir(Agora, Ana);
        c.Assumir(Agora.AddMinutes(5), Bia);

        Assert.Equal(Ana, c.ResponsavelId);
        Assert.Equal(Agora, c.ResponsavelDesde);
        Assert.True(c.RoboPausado);
    }

    [Fact]
    public void Assumir_sem_usuario_nao_define_responsavel()
    {
        var c = new Conversa();
        c.Assumir(Agora);
        Assert.Null(c.ResponsavelId);
        Assert.False(c.ComOutro(Bia, ehDono: false));
    }

    [Fact]
    public void Outro_vendedor_nao_responde_mas_o_dono_sim()
    {
        var c = new Conversa();
        c.Assumir(Agora, Ana);

        Assert.False(c.ComOutro(Ana, ehDono: false));
        Assert.True(c.ComOutro(Bia, ehDono: false));
        Assert.False(c.ComOutro(Bia, ehDono: true));
    }

    [Fact]
    public void Dono_transfere_para_outro_vendedor()
    {
        var c = new Conversa();
        c.ChamarVendedor("quer reservar", Agora);
        c.Assumir(Agora, Ana);
        c.Transferir(Bia, Agora.AddHours(1));

        Assert.Equal(Bia, c.ResponsavelId);
        Assert.Equal(Agora.AddHours(1), c.ResponsavelDesde);
        Assert.False(LeadQuente.SemDono(c));
        Assert.True(c.ComOutro(Ana, ehDono: false));
    }

    [Fact]
    public void Tirar_o_responsavel_volta_a_ser_cliente_sem_dono()
    {
        var c = new Conversa();
        c.ChamarVendedor("quer reservar", Agora);
        c.Assumir(Agora, Ana);
        c.Transferir(null, Agora.AddHours(2));

        Assert.Null(c.ResponsavelId);
        Assert.True(LeadQuente.SemDono(c));
        Assert.Equal(Agora.AddHours(2), c.ChamouVendedorEm);
        Assert.True(c.RoboPausado);
    }

    [Fact]
    public void Devolver_ao_robo_tira_o_responsavel()
    {
        var c = new Conversa();
        c.Assumir(Agora, Ana);
        c.DevolverAoRobo();

        Assert.Null(c.ResponsavelId);
        Assert.False(c.RoboPausado);
    }

    [Fact]
    public void Pedido_fica_com_o_responsavel_da_conversa()
    {
        var reserva = new Reserva { LojaId = Guid.NewGuid(), ConversaId = Guid.NewGuid(), Status = StatusReserva.Ativa };
        var entrega = new DadosEntrega(TipoEntrega.Retirada, 0, null, null, null);

        Assert.Equal(Ana, RegrasPedido.CriarDaReserva(reserva, Guid.NewGuid(), 1, entrega, Bia, Ana).VendedorId);
        Assert.Equal(Bia, RegrasPedido.CriarDaReserva(reserva, Guid.NewGuid(), 2, entrega, Bia).VendedorId);
    }
}
