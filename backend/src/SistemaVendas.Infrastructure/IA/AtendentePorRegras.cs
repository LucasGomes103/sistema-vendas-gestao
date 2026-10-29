using System.Globalization;
using SistemaVendas.Application.Atendimento;

namespace SistemaVendas.Infrastructure.IA;

/// <summary>
/// Atendente simples, sem IA, usado quando não há chave do Gemini (ou se ele falhar).
/// Responde preço e disponibilidade do que o cliente citar e chama o vendedor quando ele quer comprar.
/// </summary>
public class AtendentePorRegras : IAtendenteIA
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Sinais de cliente quente (quer comprar ou falar com alguém).</summary>
    private static readonly string[] SinaisQuente =
        ["reservar", "reserva", "comprar", "quero levar", "pix", "pagar", "frete", "entrega", "cep", "atendente", "pessoa", "humano", "vendedor"];

    private static readonly string[] PalavrasCatalogo = ["catalogo", "lista", "o que tem", "oque tem", "o que voces tem", "disponiveis", "fotos"];

    public string Nome => "Regras (sem IA)";

    public async Task<RespostaAtendente> ResponderAsync(string nomeLoja, IReadOnlyList<FalaConversa> historico, IFerramentasAtendimento ferramentas, CancellationToken ct)
    {
        var ultima = historico.LastOrDefault(f => f.DoCliente)?.Texto ?? "";
        var texto = FerramentasAtendimento.Normalizar(ultima);

        if (PedidoCatalogo.EhPedido(ultima))
            return new RespostaAtendente(
                "Recebi seu pedido! Para calcular o frete, me envie o seu CEP. Um atendente já confere tudo e confirma com você.",
                ChamarVendedor: true,
                MotivoVendedor: "Pedido pelo catálogo");

        if (PalavrasCatalogo.Any(p => $" {texto} ".Contains($" {p}")))
        {
            var link = await ferramentas.LinkCatalogoAsync(ct);
            if (link is not null)
                return new RespostaAtendente(
                    $"Aqui está o nosso catálogo com o que temos disponível agora: {link}\nÉ só escolher as quantidades e tocar em \"Enviar pedido pelo WhatsApp\".",
                    ChamarVendedor: false, MotivoVendedor: null);
        }

        var sinal = SinaisQuente.FirstOrDefault(s => $" {texto} ".Contains($" {s}"));
        if (sinal is not null)
        {
            var textos = await ferramentas.TextosAsync(ct);
            var frete = sinal is "frete" or "entrega" or "cep";
            return new RespostaAtendente(
                frete ? textos.Frete : textos.ChamarVendedor,
                ChamarVendedor: true,
                MotivoVendedor: frete ? "Cliente quer saber o frete" : $"Cliente falou em \"{sinal}\"");
        }

        var especies = await ferramentas.BuscarEspecieAsync(ultima, ct);
        if (especies.Count > 0)
        {
            var linhas = especies.Select(e => e.Disponivel > 0
                ? $"• {e.NomePopular}: {e.Preco.ToString("C", PtBr)} ({e.Disponivel} disponíveis)"
                : $"• {e.NomePopular}: no momento sem estoque");
            return new RespostaAtendente(
                $"Encontrei aqui:\n{string.Join('\n', linhas)}\n\nQuer reservar algum?",
                ChamarVendedor: false, MotivoVendedor: null);
        }

        var primeira = historico.Count(f => f.DoCliente) <= 1;
        return new RespostaAtendente(
            primeira
                ? $"Olá! Aqui é o atendimento da {nomeLoja}. Me diga qual peixe você procura que eu confiro preço e disponibilidade."
                : "Não encontrei esse peixe no catálogo. Pode me dizer o nome popular ou científico? Se preferir, peço para um atendente falar com você.",
            ChamarVendedor: false, MotivoVendedor: null);
    }
}
