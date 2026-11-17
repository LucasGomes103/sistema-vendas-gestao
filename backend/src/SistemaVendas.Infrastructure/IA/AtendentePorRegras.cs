using System.Globalization;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Domain.Vendas;

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
                $"Recebi seu pedido! Para calcular o frete, me envie o {AguardaEntrega.PedidoEndereco} completo: rua, número, complemento, bairro e CEP. Um atendente já confere tudo e confirma com você.",
                ChamarVendedor: true,
                MotivoVendedor: "Pedido pelo catálogo")
            {
                Reservar = await ItensDoPedidoAsync(ultima, ferramentas, ct)
            };

        if (PalavrasCatalogo.Any(p => $" {texto} ".Contains($" {p}")))
        {
            var link = await ferramentas.LinkCatalogoAsync(ct);
            if (link is not null)
                return new RespostaAtendente(
                    $"Aqui está o nosso catálogo com o que temos disponível agora: {link}\nÉ só escolher as quantidades e tocar em \"Enviar pedido pelo WhatsApp\".",
                    ChamarVendedor: false, MotivoVendedor: null);
        }

        // Respondeu o pedido de endereço: completa com rua, bairro e cidade do CEP e passa para o vendedor fechar.
        var anterior = historico.LastOrDefault(f => !f.DoCliente)?.Texto ?? "";
        if (anterior.Contains(AguardaEntrega.PedidoEndereco, StringComparison.OrdinalIgnoreCase) && RegrasEndereco.PareceEndereco(ultima)
            && historico.Where(f => f.DoCliente).Select(f => RegrasFrete.AcharCep(f.Texto)).LastOrDefault(c => c is not null) is { } cepEntrega)
        {
            var freteEntrega = await ferramentas.CalcularFreteAsync(cepEntrega.ToString("00000000"), ct);
            var endereco = RegrasEndereco.Completar(ultima, freteEntrega?.Endereco);
            var textos = await ferramentas.TextosAsync(ct);
            return new RespostaAtendente($"Anotei o {AguardaEntrega.PedidoEndereco}: {endereco}. {textos.ChamarVendedor}", ChamarVendedor: true,
                MotivoVendedor: freteEntrega?.Resumo ?? $"Calcular frete à mão: CEP {RegrasFrete.Formatar(cepEntrega)}")
            {
                Endereco = new EnderecoEntrega(cepEntrega, endereco)
            };
        }

        // Mandou o CEP: calcula pela distância, chama o vendedor (cliente quente) e pede o endereço de entrega.
        // Fora do raio ou CEP que o mapa não acha: não chuta valor, o vendedor calcula.
        if (RegrasFrete.AcharCep(ultima) is { } cep)
        {
            var frete = await ferramentas.CalcularFreteAsync(cep.ToString("00000000"), ct);

            // Mandou o endereço junto com o CEP: guarda tudo de uma vez, sem perguntar de novo.
            var semCep = RegrasEndereco.SemCep(ultima);
            if (RegrasEndereco.PareceEndereco(semCep) && semCep.Any(char.IsLetter))
            {
                var completo = RegrasEndereco.Completar(semCep, frete?.Endereco);
                var textos = await ferramentas.TextosAsync(ct);
                var inicio = frete?.ParaCliente ?? $"Anotei o CEP {RegrasFrete.Formatar(cep)}. Um atendente vai calcular a entrega.";
                return new RespostaAtendente($"{inicio}\n\nAnotei o {AguardaEntrega.PedidoEndereco}: {completo}. {textos.ChamarVendedor}", ChamarVendedor: true,
                    MotivoVendedor: frete?.Resumo ?? $"Calcular frete à mão: CEP {RegrasFrete.Formatar(cep)}")
                {
                    Endereco = new EnderecoEntrega(cep, completo)
                };
            }

            return frete is not null
                ? new RespostaAtendente($"{frete.ParaCliente}\n\n{PedirEndereco(frete.Endereco)}", ChamarVendedor: true, MotivoVendedor: frete.Resumo)
                : new RespostaAtendente($"Anotei o CEP {RegrasFrete.Formatar(cep)}. Um atendente vai calcular a entrega.\n\n{PedirEndereco(null)}", ChamarVendedor: true,
                    MotivoVendedor: $"Calcular frete à mão: CEP {RegrasFrete.Formatar(cep)}");
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

    /// <summary>Pede o que falta do endereço: com a rua do CEP, só o número e o complemento.</summary>
    public static string PedirEndereco(string? enderecoDoCep) =>
        enderecoDoCep is { Length: > 0 }
            ? $"Para a entrega, confirme o {AguardaEntrega.PedidoEndereco}: {enderecoDoCep}. Qual é o número e o complemento (apartamento, bloco)?"
            : $"Para a entrega, me envie o {AguardaEntrega.PedidoEndereco}: rua, número, complemento e bairro.";

    /// <summary>Casa cada linha do pedido da vitrine com a espécie de mesmo nome no catálogo.</summary>
    public static async Task<IReadOnlyList<ItemPedido>> ItensDoPedidoAsync(string mensagem, IFerramentasAtendimento ferramentas, CancellationToken ct)
    {
        var itens = new List<ItemPedido>();
        foreach (var (nome, quantidade) in LeitorPedidoVitrine.Ler(mensagem))
        {
            var alvo = FerramentasAtendimento.Normalizar(nome).Trim();
            var achada = (await ferramentas.BuscarEspecieAsync(nome, ct))
                .FirstOrDefault(e => FerramentasAtendimento.Normalizar(e.NomePopular).Trim() == alvo);
            if (achada is not null)
                itens.Add(new ItemPedido(achada.Id, quantidade));
        }
        return itens;
    }
}
