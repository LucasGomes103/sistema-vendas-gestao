using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Vendas;

namespace SistemaVendas.Domain.Tests;

public class RegrasPixTests
{
    [Fact]
    public void Crc_confere_com_o_exemplo_do_manual_do_banco_central()
    {
        const string semCrc = "00020126580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-4266554400005204000053039865802BR5913Fulano de Tal6008BRASILIA62070503***6304";
        Assert.Equal("1D3D", RegrasPix.Crc16(semCrc).ToString("X4"));
    }

    [Fact]
    public void Copia_e_cola_leva_chave_valor_nome_cidade_e_numero_do_pedido()
    {
        var codigo = RegrasPix.CopiaECola("123e4567-e12b-12d1-a456-426655440000", "Peixaria São João", "São Paulo", 157.5m, "PEDIDO12");

        Assert.StartsWith("000201", codigo);
        Assert.Contains("0014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-426655440000", codigo);
        Assert.Contains("5406157.50", codigo);
        Assert.Contains("5917PEIXARIA SAO JOAO", codigo);
        Assert.Contains("6009SAO PAULO", codigo);
        Assert.Contains("62120508PEDIDO12", codigo);
        // Os 4 últimos caracteres são o CRC de todo o resto.
        Assert.Equal(RegrasPix.Crc16(codigo[..^4]).ToString("X4"), codigo[^4..]);
    }

    [Fact]
    public void Nome_e_cidade_longos_sao_cortados_no_limite_do_padrao()
    {
        var codigo = RegrasPix.CopiaECola("loja@ecsfish.com.br", "Aquário Encantado dos Peixes Ornamentais", "São José dos Campos", null, null);

        Assert.Contains("5925AQUARIO ENCANTADO DOS PEI", codigo);
        Assert.Contains("6015SAO JOSE DOS CA", codigo);
        Assert.Contains("0503***", codigo);
        Assert.DoesNotContain("54", codigo.Split("5303986")[1][..2]);
    }

    [Theory]
    [InlineData("529.982.247-25", "52998224725", TipoChavePix.Cpf)]
    [InlineData("11.222.333/0001-81", "11222333000181", TipoChavePix.Cnpj)]
    [InlineData("(11) 94462-0000", "+5511944620000", TipoChavePix.Telefone)]
    [InlineData("+55 11 94462-0000", "+5511944620000", TipoChavePix.Telefone)]
    [InlineData("Loja@ECSFish.com", "loja@ecsfish.com", TipoChavePix.Email)]
    [InlineData("123E4567-E12B-12D1-A456-426655440000", "123e4567-e12b-12d1-a456-426655440000", TipoChavePix.Aleatoria)]
    public void Normaliza_cada_tipo_de_chave(string entrada, string esperado, TipoChavePix tipo)
    {
        var (chave, t) = RegrasPix.NormalizarChave(entrada);
        Assert.Equal(esperado, chave);
        Assert.Equal(tipo, t);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("email@semponto")]
    public void Recusa_chave_que_nao_e_pix(string entrada) =>
        Assert.Throws<RegraException>(() => RegrasPix.NormalizarChave(entrada));
}

public class RegrasPedidoTests
{
    private static readonly Guid Loja = Guid.CreateVersion7();
    private static readonly Guid Especie = Guid.CreateVersion7();
    private static readonly DateTimeOffset Agora = new(2026, 11, 12, 15, 0, 0, TimeSpan.Zero);

    private static Reserva ReservaDe(int qtd1, int qtd2)
    {
        var r = new Reserva { LojaId = Loja, ConversaId = Guid.CreateVersion7(), ExpiraEm = Agora.AddHours(24) };
        r.Itens.Add(new ItemReserva { LojaId = Loja, ReservaId = r.Id, EspecieId = Especie, Especie = "Ramirezi", LoteId = Guid.NewGuid(), TanqueId = Guid.NewGuid(), Quantidade = qtd1, PrecoUnitario = 60 });
        r.Itens.Add(new ItemReserva { LojaId = Loja, ReservaId = r.Id, EspecieId = Especie, Especie = "Ramirezi", LoteId = Guid.NewGuid(), TanqueId = Guid.NewGuid(), Quantidade = qtd2, PrecoUnitario = 60 });
        r.Total = (qtd1 + qtd2) * 60;
        return r;
    }

    [Fact]
    public void Pedido_soma_os_lotes_por_especie_e_acrescenta_o_frete()
    {
        var p = RegrasPedido.CriarDaReserva(ReservaDe(2, 1), Guid.NewGuid(), 7, new DadosEntrega(TipoEntrega.Entrega, 15, "07010-000", "Rua A, 10", null), null);

        Assert.Single(p.Itens);
        Assert.Equal(3, p.Itens[0].Quantidade);
        Assert.Equal(180, p.Subtotal);
        Assert.Equal(195, p.Total);
        Assert.Equal("07010000", p.Cep);
        Assert.Equal(7, p.Numero);
        Assert.Equal(StatusPedido.AguardandoPagamento, p.Status);
        Assert.Equal(Agora.AddHours(24), p.ExpiraEm);
    }

    [Fact]
    public void Retirada_nao_cobra_frete_nem_guarda_endereco()
    {
        var p = RegrasPedido.CriarDaReserva(ReservaDe(1, 0), Guid.NewGuid(), 1, new DadosEntrega(TipoEntrega.Retirada, 20, null, "Rua A", null), null);
        Assert.Equal(0, p.Frete);
        Assert.Null(p.Endereco);
        Assert.Equal(60, p.Total);
    }

    [Fact]
    public void Entrega_sem_endereco_e_recusada() =>
        Assert.Throws<RegraException>(() => RegrasPedido.CriarDaReserva(ReservaDe(1, 0), Guid.NewGuid(), 1, new DadosEntrega(TipoEntrega.Entrega, 10, null, " ", null), null));

    [Fact]
    public void Reserva_encerrada_nao_vira_pedido()
    {
        var r = ReservaDe(1, 0);
        r.Status = StatusReserva.Expirada;
        Assert.Throws<RegraException>(() => RegrasPedido.CriarDaReserva(r, Guid.NewGuid(), 1, new DadosEntrega(TipoEntrega.Retirada, 0, null, null, null), null));
    }

    [Fact]
    public void Comprovante_leva_para_conferir_e_pagamento_fecha_o_pedido()
    {
        var p = RegrasPedido.CriarDaReserva(ReservaDe(1, 0), Guid.NewGuid(), 1, new DadosEntrega(TipoEntrega.Retirada, 0, null, null, null), null);

        Assert.True(RegrasPedido.ComprovanteRecebido(p, Agora));
        Assert.Equal(StatusPedido.ConferirPagamento, p.Status);
        Assert.False(RegrasPedido.ComprovanteRecebido(p, Agora)); // segundo aviso não muda nada

        RegrasPedido.ConfirmarPagamento(p, FormaPagamento.Pix, Agora, null);
        Assert.Equal(StatusPedido.Pago, p.Status);
        Assert.Throws<RegraException>(() => RegrasPedido.ConfirmarPagamento(p, FormaPagamento.Pix, Agora, null));
        Assert.Throws<RegraException>(() => RegrasPedido.Cancelar(p, "x", Agora));
    }

    [Theory]
    [InlineData("text", "paguei!", true)]
    [InlineData("text", "segue o comprovante", true)]
    [InlineData("text", "Já fiz o Pix", true)]
    [InlineData("text", "pix enviado", true)]
    [InlineData("image", "[image]", true)]
    [InlineData("document", "[document]", true)]
    [InlineData("text", "como faço para pagar?", false)]
    [InlineData("text", "vou pagar amanhã", false)]
    public void Reconhece_quando_o_cliente_diz_que_pagou(string tipo, string texto, bool esperado) =>
        Assert.Equal(esperado, RegrasPedido.PareceComprovante(tipo, texto));

    [Fact]
    public void Robo_pede_o_comprovante_e_avisa_que_a_confirmacao_e_manual()
    {
        var p = RegrasPedido.CriarDaReserva(ReservaDe(1, 0), Guid.NewGuid(), 7, new DadosEntrega(TipoEntrega.Retirada, 0, null, null, null), null);

        Assert.Contains("comprovante", RegrasPedido.MensagemPedirComprovante(p));
        Assert.Contains("#7", RegrasPedido.MensagemPedirComprovante(p));
        Assert.Contains("atendente confere", RegrasPedido.MensagemComprovanteRecebido(p));
        Assert.Contains("comprovante", RegrasPedido.MensagemCobranca(p, null, TimeZoneInfo.Utc));
        Assert.True(RegrasPedido.EhArquivo("image"));
        Assert.False(RegrasPedido.EhArquivo("text"));
    }

    [Fact]
    public void Cobranca_mostra_itens_total_e_o_codigo()
    {
        var p = RegrasPedido.CriarDaReserva(ReservaDe(2, 0), Guid.NewGuid(), 3, new DadosEntrega(TipoEntrega.Entrega, 12.5m, null, "Rua B", null), null);
        var texto = RegrasPedido.MensagemCobranca(p, "ECS FISH", TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

        Assert.Contains("Pedido #3", texto);
        Assert.Contains("2x Ramirezi", texto);
        Assert.Contains("Frete: R$ 12,50", texto);
        Assert.Contains("Total: R$ 132,50", texto);
        Assert.Contains("próxima mensagem", texto);
        Assert.Contains("13/11 às 12:00", texto);
    }

    [Fact]
    public void Cobranca_com_cartao_oferece_pix_e_o_link_do_mercado_pago()
    {
        var p = RegrasPedido.CriarDaReserva(ReservaDe(1, 0), Guid.NewGuid(), 4, new DadosEntrega(TipoEntrega.Retirada, 0, null, null, null), null);
        p.PixCopiaECola = "000201...";
        p.LinkCartao = "https://www.mercadopago.com.br/checkout/v1/redirect?pref_id=1";
        var texto = RegrasPedido.MensagemCobranca(p, "ECS FISH", TimeZoneInfo.Utc);

        Assert.Contains("*Pix*", texto);
        Assert.Contains("comprovante", texto);
        Assert.Contains("Cartão de crédito", texto);
        Assert.Contains(p.LinkCartao, texto);
    }

    [Fact]
    public void Cobranca_so_com_cartao_nao_fala_de_pix()
    {
        var p = RegrasPedido.CriarDaReserva(ReservaDe(1, 0), Guid.NewGuid(), 5, new DadosEntrega(TipoEntrega.Retirada, 0, null, null, null), null);
        p.LinkCartao = "https://mp/link";
        var texto = RegrasPedido.MensagemCobranca(p, null, TimeZoneInfo.Utc);

        Assert.DoesNotContain("Pix", texto);
        Assert.Contains("https://mp/link", texto);
    }

    [Fact]
    public void Valor_do_cartao_precisa_cobrir_o_total()
    {
        var p = RegrasPedido.CriarDaReserva(ReservaDe(1, 0), Guid.NewGuid(), 6, new DadosEntrega(TipoEntrega.Retirada, 0, null, null, null), null);

        Assert.True(RegrasPedido.ValorDoCartaoConfere(p, p.Total));
        Assert.True(RegrasPedido.ValorDoCartaoConfere(p, p.Total + 8.4m)); // juros do parcelamento
        Assert.False(RegrasPedido.ValorDoCartaoConfere(p, p.Total - 1m));
    }
}
