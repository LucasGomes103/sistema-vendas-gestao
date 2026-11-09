using SistemaVendas.Application.Atendimento;
using SistemaVendas.Domain.Atendimento;
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
                ? new FreteCalculado("07010-000", true, "Guarulhos", 15, 1)
                : new FreteCalculado("99999-999", false, null, null, null));

        public Task<string?> LinkCatalogoAsync(CancellationToken ct) => Task.FromResult<string?>("https://loja.test/demo");

        public Task<TextosProntos> TextosAsync(CancellationToken ct) =>
            Task.FromResult(new TextosProntos("Me envie seu CEP para o frete.", "Vou chamar um atendente."));
    }

    private static Task<RespostaAtendente> Perguntar(params string[] doCliente) =>
        new AtendentePorRegras().ResponderAsync("Loja Demo", doCliente.Select(t => new FalaConversa(true, t)).ToList(), new CatalogoFalso(), default);

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
        Assert.Contains("Guarulhos", r.MotivoVendedor);
    }

    [Fact]
    public async Task CEP_fora_da_area_oferece_retirada()
    {
        var r = await Perguntar("99999999");
        Assert.Contains("retirar", r.Texto);
        Assert.True(r.ChamarVendedor);
    }

    [Fact]
    public async Task Pedido_da_vitrine_vira_pedido_de_reserva_com_as_especies_do_catalogo()
    {
        var r = await Perguntar("🛒 *Pedido pelo catálogo*\n\n• 2x Ramirezi (casal) (R$ 60,00 cada) = R$ 120,00\n• 1x Peixe que não existe (R$ 5,00 cada) = R$ 5,00\n\n*Total: R$ 125,00* + frete");
        var item = Assert.Single(r.Reservar);
        Assert.Equal(2, item.Quantidade);
    }
}
