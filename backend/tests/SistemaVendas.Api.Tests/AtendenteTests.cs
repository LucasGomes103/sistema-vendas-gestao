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
}
