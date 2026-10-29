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
}

/// <summary>Mensagem montada pela vitrine quando o cliente toca em "Enviar pedido pelo WhatsApp".</summary>
public static class PedidoCatalogo
{
    /// <summary>A vitrine começa a mensagem com este texto; o robô reconhece e trata como pedido.</summary>
    public const string Marcador = "Pedido pelo catálogo";

    public static bool EhPedido(string texto) => texto.Contains(Marcador, StringComparison.OrdinalIgnoreCase);
}

public record RespostaAtendente(string Texto, bool ChamarVendedor, string? MotivoVendedor);

/// <summary>Atendente do WhatsApp. Trocar de provedor de IA é trocar a implementação.</summary>
public interface IAtendenteIA
{
    /// <summary>Nome do provedor, para o painel mostrar o que está respondendo.</summary>
    string Nome { get; }

    Task<RespostaAtendente> ResponderAsync(string nomeLoja, IReadOnlyList<FalaConversa> historico, IFerramentasAtendimento ferramentas, CancellationToken ct);
}
