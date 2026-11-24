using SistemaVendas.Domain.Lojas;

namespace SistemaVendas.Domain.Tests;

public class RegrasAssinaturaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Planos_seguem_a_proposta_aprovada()
    {
        Assert.Equal([297m, 497m, 797m], Planos.Todos.Select(p => p.Preco));
        Assert.Equal([500, 1_500, 4_000], Planos.Todos.Select(p => p.Conversas));
        Assert.Equal([2, 5, 15], Planos.Todos.Select(p => p.Usuarios));
        Assert.Equal([300, 2_000, (int?)null], Planos.Todos.Select(p => p.Produtos));
        Assert.False(Planos.Essencial.Cartao);
        Assert.False(Planos.Essencial.Entregas);
        Assert.True(Planos.Profissional.Cartao && Planos.Profissional.Entregas && !Planos.Profissional.DominioProprio);
        Assert.True(Planos.Completo.DominioProprio);
    }

    [Fact]
    public void Teste_gratis_dura_7_dias_e_depois_entra_na_carencia_e_bloqueia()
    {
        var a = RegrasAssinatura.NovoTeste(Guid.NewGuid(), null, Agora);

        Assert.Equal(PlanoId.Profissional, a.Plano);
        Assert.Equal(SituacaoAssinatura.Teste, RegrasAssinatura.Situacao(a, Agora.AddDays(6)));
        Assert.Equal(SituacaoAssinatura.Carencia, RegrasAssinatura.Situacao(a, Agora.AddDays(8)));
        Assert.Equal(SituacaoAssinatura.Bloqueada, RegrasAssinatura.Situacao(a, Agora.AddDays(7 + 7)));
        Assert.Equal(Agora.AddDays(14), RegrasAssinatura.BloqueiaEm(a));
    }

    [Fact]
    public void Sem_assinatura_ou_isenta_nunca_bloqueia_nem_tem_limite()
    {
        Assert.Equal(SituacaoAssinatura.Isenta, RegrasAssinatura.Situacao(null, Agora));
        var isenta = new Assinatura { Status = StatusAssinatura.Isenta, TesteAte = Agora.AddYears(-1) };
        Assert.Equal(SituacaoAssinatura.Isenta, RegrasAssinatura.Situacao(isenta, Agora));
        Assert.Null(RegrasAssinatura.PlanoComLimites(isenta));
    }

    [Fact]
    public void Cortesia_sem_data_nao_vence_e_com_data_vence()
    {
        var sempre = new Assinatura { Status = StatusAssinatura.Cortesia };
        Assert.Equal(SituacaoAssinatura.Cortesia, RegrasAssinatura.Situacao(sempre, Agora.AddYears(3)));

        var ate = new Assinatura { Status = StatusAssinatura.Cortesia, CortesiaAte = Agora };
        Assert.Equal(SituacaoAssinatura.Cortesia, RegrasAssinatura.Situacao(ate, Agora.AddDays(-1)));
        Assert.Equal(SituacaoAssinatura.Bloqueada, RegrasAssinatura.Situacao(ate, Agora.AddDays(30)));
    }

    [Fact]
    public void Primeira_cobranca_no_teste_conta_um_mes_a_partir_do_fim_do_teste()
    {
        var a = RegrasAssinatura.NovoTeste(Guid.NewGuid(), PlanoId.Essencial, Agora);

        RegrasAssinatura.Creditar(a, 1, Agora.AddDays(2));

        Assert.Equal(StatusAssinatura.Ativa, a.Status);
        Assert.Equal(Agora.AddDays(7).AddMonths(1), a.PagoAte);
        Assert.Equal(SituacaoAssinatura.EmDia, RegrasAssinatura.Situacao(a, Agora.AddDays(20)));
    }

    [Fact]
    public void Aviso_repetido_nao_estende_de_novo_e_cobranca_nova_soma_um_mes()
    {
        var a = new Assinatura { Status = StatusAssinatura.Ativa, PagoAte = Agora, CobrancasCreditadas = 3 };

        RegrasAssinatura.Creditar(a, 3, Agora);
        Assert.Equal(Agora, a.PagoAte);

        RegrasAssinatura.Creditar(a, 4, Agora.AddDays(1));
        Assert.Equal(Agora.AddMonths(1), a.PagoAte);
        Assert.Equal(4, a.CobrancasCreditadas);
    }

    [Fact]
    public void Pagamento_depois_de_muito_tempo_bloqueada_conta_a_partir_de_hoje()
    {
        var a = new Assinatura { Status = StatusAssinatura.Cancelada, PagoAte = Agora.AddMonths(-3), MpStatus = "authorized" };

        RegrasAssinatura.Creditar(a, 1, Agora);

        Assert.Equal(Agora.AddMonths(1), a.PagoAte);
        Assert.Equal(StatusAssinatura.Ativa, a.Status);
    }
}
