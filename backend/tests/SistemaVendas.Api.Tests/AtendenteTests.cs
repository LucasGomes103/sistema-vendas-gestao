using SistemaVendas.Domain.Lojas;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Infrastructure.IA;

namespace SistemaVendas.Api.Tests;

public class AtendenteTests
{
    private sealed class CatalogoFalso : IFerramentasAtendimento
    {
        private readonly EspecieEncontrada[] _especies =
        [
            new(Guid.CreateVersion7(), "Ramirezi (casal)", "Mikrogeophagus ramirezi", 4, 60, 2),
            new(Guid.CreateVersion7(), "Disco azul", "Symphysodon aequifasciatus", 12, 180, 0)
        ];

        public Task<IReadOnlyList<EspecieEncontrada>> BuscarEspecieAsync(string texto, CancellationToken ct)
        {
            var t = FerramentasAtendimento.Normalizar(texto);
            IReadOnlyList<EspecieEncontrada> achadas = _especies
                .Where(e => FerramentasAtendimento.Normalizar(e.NomePopular).Split(' ').Any(p => p.Length >= 3 && t.Contains(p)))
                .ToList();
            return Task.FromResult(achadas);
        }

        public Task<int> ConsultarEstoqueAsync(Guid especieId, CancellationToken ct) =>
            Task.FromResult(_especies.First(e => e.Id == especieId).Disponivel);

        public Task<FreteCalculado?> CalcularFreteAsync(string cep, CancellationToken ct) =>
            Task.FromResult<FreteCalculado?>(cep.StartsWith("07")
                ? new FreteCalculado("07010-000", true, "a 5,3 km da loja", 15, 1) { Endereco = "Rua Dom Pedro II, Centro, Guarulhos - SP" }
                : cep.StartsWith("99")
                    ? new FreteCalculado("99999-999", false, "a 42,0 km da loja", null, null)
                    : null);

        public Task<string?> LinkCatalogoAsync(CancellationToken ct) => Task.FromResult<string?>("https://loja.test/demo");

        public Task<TextosProntos> TextosAsync(CancellationToken ct) =>
            Task.FromResult(new TextosProntos("Me envie seu CEP para o frete.", "Vou chamar um atendente."));
    }

    private static Task<RespostaAtendente> Perguntar(params string[] doCliente) =>
        new AtendentePorRegras().ResponderAsync("Loja Demo", doCliente.Select(t => new FalaConversa(true, t)).ToList(), new CatalogoFalso(), default);

    /// <summary>Conversa alternada: cliente, robô, cliente...</summary>
    private static Task<RespostaAtendente> Conversar(params string[] falas) =>
        new AtendentePorRegras().ResponderAsync("Loja Demo", falas.Select((t, i) => new FalaConversa(i % 2 == 0, t)).ToList(), new CatalogoFalso(), default);

    [Fact]
    public async Task Responde_preco_e_estoque_do_catalogo()
    {
        var r = await Perguntar("Oi, tem ramirezi?");

        Assert.Contains("Ramirezi", r.Texto);
        Assert.Contains("R$ 60,00", r.Texto);
        Assert.Contains("2 disponíveis", r.Texto);
        Assert.False(r.ChamarVendedor);
    }

    [Fact]
    public async Task Nao_oferece_o_que_esta_sem_estoque()
    {
        var r = await Perguntar("vocês têm disco azul?");
        Assert.Contains("sem estoque", r.Texto);
    }

    [Theory]
    [InlineData("quero reservar o casal")]
    [InlineData("qual o frete pro meu CEP?")]
    [InlineData("posso falar com uma pessoa?")]
    public async Task Cliente_quente_chama_o_vendedor(string texto)
    {
        var r = await Perguntar(texto);
        Assert.True(r.ChamarVendedor);
        Assert.NotNull(r.MotivoVendedor);
    }

    [Fact]
    public async Task Pedido_de_catalogo_recebe_o_link_da_vitrine()
    {
        var r = await Perguntar("Vocês tem catálogo?");
        Assert.Contains("https://loja.test/demo", r.Texto);
        Assert.False(r.ChamarVendedor);
    }

    [Fact]
    public async Task Pedido_montado_na_vitrine_chama_o_vendedor_e_pede_CEP()
    {
        var r = await Perguntar("🛒 Pedido pelo catálogo\n2x Ramirezi (casal)\nTotal: R$ 120,00");
        Assert.True(r.ChamarVendedor);
        Assert.Contains("CEP", r.Texto);
    }

    [Fact]
    public void Vendedor_que_assume_pausa_o_robo_e_devolver_libera()
    {
        var conversa = new Conversa();
        conversa.ReceberDoCliente(DateTimeOffset.UtcNow);
        Assert.True(conversa.AguardandoRobo);

        conversa.Assumir();
        conversa.ReceberDoCliente(DateTimeOffset.UtcNow);
        Assert.False(conversa.AguardandoRobo);

        conversa.DevolverAoRobo();
        conversa.ReceberDoCliente(DateTimeOffset.UtcNow);
        Assert.True(conversa.AguardandoRobo);
    }

    [Fact]
    public void Chamar_vendedor_marca_cliente_quente_e_pausa_o_robo()
    {
        var conversa = new Conversa();
        conversa.ChamarVendedor("quer reservar", DateTimeOffset.UtcNow);

        Assert.True(conversa.ChamouVendedor);
        Assert.True(conversa.RoboPausado);
        Assert.Equal("quer reservar", conversa.MotivoVendedor);
    }

    [Fact]
    public void Primeira_resposta_leva_o_link_do_catalogo()
    {
        var texto = CatalogoNaPrimeiraResposta.Aplicar("Olá! Qual peixe você procura?", "https://loja.test/ecsfish");
        Assert.StartsWith("Olá! Qual peixe você procura?", texto);
        Assert.EndsWith("https://loja.test/ecsfish", texto);
    }

    [Fact]
    public void Link_antigo_da_vitrine_vira_o_atual()
    {
        const string atual = "https://sistema-vendas-gestao.pages.dev/auto-pecas-marrone";
        var texto = LinkCatalogoAtual.Corrigir(
            "Claro! Aqui está o link: https://subtle-faun-9fb444.netlify.app/auto-pecas-marrone", atual);
        Assert.Equal($"Claro! Aqui está o link: {atual}", texto);
        // Outros links e o próprio link atual ficam como estão.
        Assert.Equal("Veja https://exemplo.com/outra", LinkCatalogoAtual.Corrigir("Veja https://exemplo.com/outra", atual));
        Assert.Equal($"Veja {atual}.", LinkCatalogoAtual.Corrigir($"Veja {atual}.", atual));
        Assert.Equal("https://x.app/auto-pecas-marrone-2", LinkCatalogoAtual.Corrigir("https://x.app/auto-pecas-marrone-2", atual));
    }

    [Fact]
    public void Link_do_catalogo_usa_o_emoji_do_tipo_de_loja()
    {
        var aquario = CatalogoNaPrimeiraResposta.Aplicar("Olá!", "https://loja.test/ecsfish", Vocabulario.Aquarismo.Emoji);
        Assert.Contains(Vocabulario.Aquarismo.Emoji + " Veja", aquario);
        var outra = CatalogoNaPrimeiraResposta.Aplicar("Olá!", "https://loja.test/pecas", Vocabulario.Geral("autopeças").Emoji);
        Assert.DoesNotContain(Vocabulario.Aquarismo.Emoji, outra);
        Assert.Contains(Vocabulario.Geral(null).Emoji + " Veja", outra);
    }

    [Fact]
    public void Primeira_resposta_nao_repete_o_link_nem_inventa_sem_vitrine()
    {
        const string comLink = "Aqui está o catálogo: https://loja.test/ecsfish";
        Assert.Equal(comLink, CatalogoNaPrimeiraResposta.Aplicar(comLink, "https://loja.test/ecsfish"));
        Assert.Equal("Olá!", CatalogoNaPrimeiraResposta.Aplicar("Olá!", null));
    }

    [Fact]
    public async Task CEP_atendido_recebe_valor_e_prazo_e_chama_o_vendedor()
    {
        var r = await Perguntar("meu cep é 07010-000");
        Assert.Contains("R$ 15,00", r.Texto);
        Assert.Contains("1 dia útil", r.Texto);
        Assert.True(r.ChamarVendedor);
        Assert.Contains("5,3 km", r.MotivoVendedor);
    }

    [Fact]
    public async Task CEP_alem_do_raio_passa_para_o_atendente_sem_dar_preco()
    {
        var r = await Perguntar("99999999");
        Assert.Contains("atendente", r.Texto);
        Assert.DoesNotContain("R$", r.Texto);
        Assert.True(r.ChamarVendedor);
        Assert.Contains("42,0 km", r.MotivoVendedor);
    }

    [Fact]
    public async Task CEP_que_o_mapa_nao_acha_tambem_chama_o_vendedor()
    {
        var r = await Perguntar("meu cep é 01234-567");
        Assert.True(r.ChamarVendedor);
        Assert.Contains("01234-567", r.MotivoVendedor);
    }

    [Fact]
    public async Task Pedido_da_vitrine_vira_pedido_de_reserva_com_as_especies_do_catalogo()
    {
        var r = await Perguntar("🛒 *Pedido pelo catálogo*\n\n• 2x Ramirezi (casal) (R$ 60,00 cada) = R$ 120,00\n• 1x Peixe que não existe (R$ 5,00 cada) = R$ 5,00\n\n*Total: R$ 125,00* + frete");
        var item = Assert.Single(r.Reservar);
        Assert.Equal(2, item.Quantidade);
    }

    [Fact]
    public async Task Depois_do_frete_pede_o_numero_confirmando_a_rua_do_CEP()
    {
        var r = await Perguntar("meu cep é 07010-000");
        Assert.Contains("Rua Dom Pedro II", r.Texto);
        Assert.Contains("número", r.Texto);
        Assert.True(AguardaEntrega.PediuEndereco(r.Texto, enderecoCompleto: false));
        Assert.Null(r.Endereco);
    }

    [Fact]
    public async Task Numero_e_complemento_viram_endereco_completo_com_rua_bairro_e_cidade_do_CEP()
    {
        var pediu = await Perguntar("meu cep é 07010-000");
        var r = await Conversar("meu cep é 07010-000", pediu.Texto, "120, apto 34");

        Assert.NotNull(r.Endereco);
        Assert.Equal(7010000, r.Endereco!.Cep);
        Assert.Equal("Rua Dom Pedro II, 120, apto 34 - Centro, Guarulhos - SP", r.Endereco.Texto);
        Assert.Contains("Rua Dom Pedro II, 120, apto 34", r.Texto);
        Assert.True(r.ChamarVendedor);
        Assert.False(AguardaEntrega.PediuEndereco(r.Texto, enderecoCompleto: true));
    }

    [Fact]
    public async Task Sem_rua_do_CEP_o_cliente_escreve_o_endereco_todo()
    {
        var pediu = await Perguntar("meu cep é 01234-567");
        Assert.Contains("rua, número, complemento e bairro", pediu.Texto);

        var r = await Conversar("meu cep é 01234-567", pediu.Texto, "Rua das Flores 45 casa 2, Vila Nova");
        Assert.Equal("Rua das Flores 45 casa 2, Vila Nova", r.Endereco!.Texto);
        Assert.Contains("01234-567", r.MotivoVendedor);
    }

    [Fact]
    public async Task Pedido_da_vitrine_pede_o_endereco_completo_e_nao_so_o_CEP()
    {
        var r = await Perguntar("🛒 Pedido pelo catálogo\n2x Ramirezi (casal)\nTotal: R$ 120,00");
        Assert.Contains("rua, número, complemento, bairro e CEP", r.Texto);
    }

    [Fact]
    public async Task Endereco_e_CEP_na_mesma_mensagem_calcula_o_frete_e_guarda_o_endereco()
    {
        var r = await Perguntar("Rua Dom Pedro II, 120, apto 34, CEP 07010-000");
        Assert.Contains("R$ 15,00", r.Texto);
        Assert.Equal("Rua Dom Pedro II, 120, apto 34 - Centro, Guarulhos - SP", r.Endereco!.Texto);
        Assert.False(AguardaEntrega.PediuEndereco(r.Texto, enderecoCompleto: true));
        Assert.True(r.ChamarVendedor);
    }

    [Theory]
    [InlineData("Rua Dom Pedro II, 120", "Rua Dom Pedro II, Centro, Guarulhos - SP", "Rua Dom Pedro II, 120 - Centro, Guarulhos - SP")]
    [InlineData("Av. Brasil 900 bloco B", "Rua Dom Pedro II, Centro, Guarulhos - SP", "Av. Brasil 900 bloco B - Centro, Guarulhos - SP")]
    [InlineData("15", "Centro, Guarulhos - SP", "15 - Centro, Guarulhos - SP")]
    [InlineData("Rua A, 10 - Centro, Guarulhos - SP", "Rua A, Centro, Guarulhos - SP", "Rua A, 10 - Centro, Guarulhos - SP")]
    public void Completa_o_endereco_com_o_que_o_CEP_deu(string resposta, string doCep, string esperado) =>
        Assert.Equal(esperado, RegrasEndereco.Completar(resposta, doCep));

    [Theory]
    [InlineData("120", true)]
    [InlineData("Rua das Flores, 120 apto 3", true)]
    [InlineData("07010-000", false)]
    [InlineData("é aqui mesmo", false)]
    public void Reconhece_resposta_com_numero_da_casa(string texto, bool esperado) =>
        Assert.Equal(esperado, RegrasEndereco.PareceEndereco(texto));

    [Fact]
    public void Monta_endereco_e_recusa_sem_numero()
    {
        Assert.Equal("Rua A, 10, apto 2 - Centro, Guarulhos - SP", RegrasEndereco.Montar("Rua A", "10", "apto 2", "Centro", "Guarulhos - SP"));
        Assert.Equal("Rua A, 10", RegrasEndereco.Montar(" Rua  A ", "10", "", null, null));
        Assert.Null(RegrasEndereco.Montar("Rua A", " ", null, "Centro", null));
    }

    [Fact]
    public void Endereco_do_cadastro_so_vale_para_o_mesmo_CEP()
    {
        Assert.True(RegrasEndereco.ValeParaCep("07010000", "Rua A, 10", 7010000));
        Assert.False(RegrasEndereco.ValeParaCep("02464100", "Rua A, 10", 7010000));
        Assert.False(RegrasEndereco.ValeParaCep("07010000", null, 7010000));
    }

    [Fact]
    public void Robo_que_pediu_o_CEP_continua_na_conversa_ate_o_CEP_chegar()
    {
        var conversa = new Conversa();
        var pediu = "Recebi seu pedido! Para calcular o frete, me envie o seu CEP.";
        var semCep = new List<FalaConversa> { new(true, "🛒 Pedido pelo catálogo\n• 2x Betta") };

        Assert.True(AguardaEntrega.PediuCep(pediu, semCep));
        conversa.ChamarVendedor("Pedido pelo catálogo", DateTimeOffset.UtcNow, pausarRobo: false);
        Assert.True(conversa.ChamouVendedor);
        Assert.False(conversa.RoboPausado);

        // O cliente manda o CEP: o robô responde o frete (e só então pausa).
        conversa.ReceberDoCliente(DateTimeOffset.UtcNow);
        Assert.True(conversa.AguardandoRobo);

        var comCep = semCep.Append(new FalaConversa(true, "meu cep é 02464100")).ToList();
        Assert.False(AguardaEntrega.PediuCep("Entregamos no CEP 02464-100 ...", comCep));
    }
}
