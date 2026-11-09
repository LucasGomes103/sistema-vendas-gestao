namespace SistemaVendas.Application.Atendimento;

/// <summary>Uma fala do histórico, sem dados pessoais: só quem falou e o texto.</summary>
public record FalaConversa(bool DoCliente, string Texto);

public record EspecieEncontrada(Guid Id, string NomePopular, string? NomeCientifico, decimal? TamanhoCm, decimal Preco, int Disponivel);

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

public record TextosProntos(string Frete, string ChamarVendedor);

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
