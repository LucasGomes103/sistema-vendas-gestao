using SistemaVendas.Domain.Lojas;
namespace SistemaVendas.Application.Atendimento;

/// <summary>Uma fala do histórico, sem dados pessoais: só quem falou e o texto.</summary>
public record FalaConversa(bool DoCliente, string Texto);

/// <summary>Detalhes: campos opcionais do produto ("Tamanho: M · Cor: preto").</summary>
public record EspecieEncontrada(Guid Id, string NomePopular, string? NomeCientifico, decimal? TamanhoCm, decimal Preco, int Disponivel, string? Detalhes = null);

/// <summary>
/// Ferramentas que o backend expõe para a IA. A IA conversa, mas não inventa dados:
/// preço e estoque só vêm daqui.
/// </summary>
public interface IFerramentasAtendimento
{
    Task<IReadOnlyList<EspecieEncontrada>> BuscarEspecieAsync(string texto, CancellationToken ct);
    Task<int> ConsultarEstoqueAsync(Guid especieId, CancellationToken ct);

    /// <summary>Link da vitrine pública da loja, onde o cliente vê o que está disponível e monta o pedido.</summary>
    Task<string?> LinkCatalogoAsync(CancellationToken ct);

    /// <summary>Frete pela tabela de faixas de CEP da loja. Null se o CEP for inválido ou a loja ainda não tiver tabela de frete.</summary>
    Task<FreteCalculado?> CalcularFreteAsync(string cep, CancellationToken ct);

    /// <summary>Respostas prontas configuradas pela loja.</summary>
    Task<TextosProntos> TextosAsync(CancellationToken ct);
}

/// <summary>Resultado do frete: Atende = false quando o CEP está fora de todas as faixas da loja.</summary>
public record FreteCalculado(string Cep, bool Atende, string? Regiao, decimal? Valor, int? Dias)
{
    private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Rua, bairro e cidade que o CEP deu ("Rua X, Bairro, Cidade - UF"), para confirmar com o cliente e pedir só o número.</summary>
    public string? Endereco { get; init; }

    public string Prazo => Dias switch { 0 => "no mesmo dia", 1 => "1 dia útil", _ => $"{Dias} dias úteis" };

    /// <summary>Resumo para o vendedor no card: "Frete CEP 07010-000: R$ 15,00 (Guarulhos)".</summary>
    public string Resumo => Atende
        ? $"Frete CEP {Cep}: {Valor!.Value.ToString("C", PtBr)} ({Regiao}, {Prazo})"
        : $"CEP {Cep} além do raio de entrega{(Regiao is null ? "" : $" ({Regiao})")}: combinar a entrega";

    /// <summary>Resposta ao cliente.</summary>
    public string ParaCliente => Atende
        ? $"Entregamos no CEP {Cep} ({Regiao}): o frete fica {Valor!.Value.ToString("C", PtBr)}, com prazo de {Prazo}."
        : $"O CEP {Cep} fica fora da área em que calculo o frete sozinho{(Regiao is null ? "" : $" ({Regiao})")}, então um atendente vai ver a entrega para você.";
}

/// <summary>
/// Textos da loja para o robô. CartaoLigado: a loja tem Mercado Pago, a cobrança leva link do cartão.
/// Vocabulario: palavras do tipo de loja (sem ele, aquarismo, como a ECS Fish).
/// </summary>
public record TextosProntos(string Frete, string ChamarVendedor, bool CartaoLigado = false, Vocabulario? Vocabulario = null)
{
    public Vocabulario Termos => Vocabulario ?? Vocabulario.Aquarismo;
}

/// <summary>Mensagem montada pela vitrine quando o cliente toca em "Enviar pedido pelo WhatsApp".</summary>
public static class PedidoCatalogo
{
    /// <summary>A vitrine começa a mensagem com este texto; o robô reconhece e trata como pedido.</summary>
    public const string Marcador = "Pedido pelo catálogo";

    public static bool EhPedido(string texto) => texto.Contains(Marcador, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Resposta do atendente. Reservar: o que o cliente quer separar; o robô cria a reserva de 24 h
/// conferindo o estoque e acrescenta o resultado ao texto.
/// </summary>
public record RespostaAtendente(string Texto, bool ChamarVendedor, string? MotivoVendedor)
{
    public IReadOnlyList<SistemaVendas.Domain.Vendas.ItemPedido> Reservar { get; init; } = [];

    /// <summary>Endereço de entrega que o cliente passou: fica no cadastro dele e preenche o pedido.</summary>
    public SistemaVendas.Domain.Entregas.EnderecoEntrega? Endereco { get; init; }
}

/// <summary>Primeira resposta a um cliente: já leva o link da vitrine, para ele ver o que tem e montar o pedido.</summary>
public static class CatalogoNaPrimeiraResposta
{
    public static string Aplicar(string texto, string? link)
    {
        if (string.IsNullOrWhiteSpace(link) || texto.Contains(link, StringComparison.OrdinalIgnoreCase))
            return texto;
        return $"{texto.TrimEnd()}\n\n🐠 Veja o que temos disponível agora e monte seu pedido: {link}";
    }
}

/// <summary>Atendente do WhatsApp. Trocar de provedor de IA é trocar a implementação.</summary>
public interface IAtendenteIA
{
    /// <summary>Nome do provedor, para o painel mostrar o que está respondendo.</summary>
    string Nome { get; }

    Task<RespostaAtendente> ResponderAsync(string nomeLoja, IReadOnlyList<FalaConversa> historico, IFerramentasAtendimento ferramentas, CancellationToken ct);
}

/// <summary>
/// Dados de entrega que o robô ainda está colhendo. Enquanto ele pede o CEP ou o endereço, chama o
/// vendedor sem pausar, senão o cliente responde e ninguém continua a conversa.
/// </summary>
public static class AguardaEntrega
{
    /// <summary>Texto que o robô usa para pedir o endereço; o atendente por regras reconhece a resposta por ele.</summary>
    public const string PedidoEndereco = "endereço de entrega";

    public static bool PediuCep(string respostaDoRobo, IReadOnlyList<FalaConversa> historico) =>
        respostaDoRobo.Contains("CEP", StringComparison.OrdinalIgnoreCase)
        && !historico.Any(f => f.DoCliente && SistemaVendas.Domain.Entregas.RegrasFrete.AcharCep(f.Texto) is not null);

    public static bool PediuEndereco(string respostaDoRobo, bool enderecoCompleto) =>
        !enderecoCompleto
        && (respostaDoRobo.Contains("endereço", StringComparison.OrdinalIgnoreCase)
            || respostaDoRobo.Contains("número", StringComparison.OrdinalIgnoreCase));
}
