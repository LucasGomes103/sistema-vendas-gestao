using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Domain.Vendas;

namespace SistemaVendas.Infrastructure.IA;

public class GeminiOptions
{
    public const string Secao = "IA:Gemini";

    public string? ApiKey { get; set; }

    /// <summary>Flash-Lite na camada gratuita (fase 1).</summary>
    public string Modelo { get; set; } = "gemini-flash-lite-latest";
}

/// <summary>
/// Atendente com Gemini e function calling. A IA só responde com dados vindos das ferramentas.
/// Nome, telefone e cadastro do cliente nunca são enviados ao modelo: ele vê só o texto da conversa
/// (inclusive o endereço de entrega, quando o próprio cliente o escreve).
/// </summary>
public class GeminiAtendente(HttpClient http, IOptions<GeminiOptions> opcoes, AtendentePorRegras reserva, ILogger<GeminiAtendente> logger,
    IOptions<Pagamentos.MercadoPagoOptions> mercadoPago) : IAtendenteIA
{
    private const int MaximoRodadas = 4;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public string Nome => $"Gemini ({opcoes.Value.Modelo})";

    public async Task<RespostaAtendente> ResponderAsync(string nomeLoja, IReadOnlyList<FalaConversa> historico, IFerramentasAtendimento ferramentas, CancellationToken ct)
    {
        try
        {
            return await ConversarAsync(nomeLoja, historico, ferramentas, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Limite da camada grátis ou instabilidade: o cliente não fica sem resposta.
            logger.LogWarning("Gemini falhou, usando respostas por regras: {Erro}", e.Message);
            return await reserva.ResponderAsync(nomeLoja, historico, ferramentas, ct);
        }
    }

    private async Task<RespostaAtendente> ConversarAsync(string nomeLoja, IReadOnlyList<FalaConversa> historico, IFerramentasAtendimento ferramentas, CancellationToken ct)
    {
        var conteudos = new JsonArray();
        foreach (var fala in historico)
            conteudos.Add(Parte(fala.DoCliente ? "user" : "model", new JsonObject { ["text"] = fala.Texto }));

        string? motivoVendedor = null;
        var reservar = new List<ItemPedido>();
        EnderecoEntrega? endereco = null;
        var textos = await ferramentas.TextosAsync(ct);

        for (var rodada = 0; rodada < MaximoRodadas; rodada++)
        {
            var resposta = await ChamarAsync(nomeLoja, textos, conteudos, ct);
            var conteudo = resposta["candidates"]?[0]?["content"] as JsonObject
                ?? throw new InvalidOperationException("Gemini não devolveu conteúdo.");
            var partes = conteudo["parts"]?.AsArray() ?? [];

            var chamadas = partes.Where(p => p?["functionCall"] is not null).Select(p => p!["functionCall"]!).ToList();
            if (chamadas.Count == 0)
            {
                var texto = string.Concat(partes.Select(p => p?["text"]?.GetValue<string>())).Trim();
                if (string.IsNullOrEmpty(texto))
                    texto = motivoVendedor is not null ? "Vou chamar um atendente para falar com você, um instante." : "Pode repetir, por favor?";
                return new RespostaAtendente(texto, motivoVendedor is not null, motivoVendedor) { Reservar = reservar, Endereco = endereco };
            }

            conteudos.Add(conteudo.DeepClone());
            var respostas = new JsonArray();
            foreach (var chamada in chamadas)
            {
                var nome = chamada["name"]!.GetValue<string>();
                var args = chamada["args"] as JsonObject ?? [];
                JsonNode resultado;

                switch (nome)
                {
                    case "buscar_especie":
                        var achadas = await ferramentas.BuscarEspecieAsync(args["texto"]?.GetValue<string>() ?? "", ct);
                        resultado = new JsonObject
                        {
                            ["especies"] = new JsonArray(achadas.Select(e => (JsonNode)new JsonObject
                            {
                                ["especie_id"] = e.Id.ToString(),
                                ["nome_popular"] = e.NomePopular,
                                ["nome_cientifico"] = e.NomeCientifico,
                                ["tamanho_cm"] = e.TamanhoCm,
                                ["preco"] = e.Preco.ToString("C", PtBr),
                                ["disponivel"] = e.Disponivel
                            }).ToArray())
                        };
                        break;

                    case "consultar_estoque":
                        resultado = Guid.TryParse(args["especie_id"]?.GetValue<string>(), out var id)
                            ? new JsonObject { ["disponivel"] = await ferramentas.ConsultarEstoqueAsync(id, ct) }
                            : new JsonObject { ["erro"] = "especie_id inválido; use buscar_especie antes" };
                        break;

                    case "calcular_frete":
                        var frete = await ferramentas.CalcularFreteAsync(args["cep"]?.GetValue<string>() ?? "", ct);
                        resultado = frete is null
                            ? new JsonObject { ["sem_calculo"] = true, ["instrucao"] = "Não dê valor. Diga que um atendente vai calcular a entrega e use chamar_vendedor com o CEP no motivo." }
                            : new JsonObject
                            {
                                ["cep"] = frete.Cep,
                                ["atende"] = frete.Atende,
                                ["regiao"] = frete.Regiao,
                                ["valor"] = frete.Valor?.ToString("C", PtBr),
                                ["prazo"] = frete.Atende ? frete.Prazo : null,
                                ["resposta_sugerida"] = frete.ParaCliente,
                                ["endereco_do_cep"] = frete.Endereco,
                                ["resumo_para_vendedor"] = frete.Resumo
                            };
                        break;

                    case "criar_reserva":
                        foreach (var item in args["itens"]?.AsArray() ?? [])
                        {
                            if (Guid.TryParse(item?["especie_id"]?.GetValue<string>(), out var especieId)
                                && item?["quantidade"] is { } q && int.TryParse(q.ToString(), out var quantidade) && quantidade > 0)
                                reservar.Add(new ItemPedido(especieId, quantidade));
                        }
                        motivoVendedor ??= "Cliente quer reservar";
                        resultado = reservar.Count == 0
                            ? new JsonObject { ["erro"] = "informe itens com especie_id (de buscar_especie) e quantidade" }
                            : new JsonObject
                            {
                                ["ok"] = true,
                                ["instrucao"] = "O sistema separa os peixes por 24 h e acrescenta a confirmação com os itens no fim da sua resposta. Não repita a lista; diga só que vai separar e que um atendente confirma o pagamento."
                            };
                        break;

                    case "salvar_endereco":
                        var cepEndereco = RegrasFrete.LerCep(args["cep"]?.GetValue<string>())
                            ?? historico.Where(f => f.DoCliente).Select(f => RegrasFrete.AcharCep(f.Texto)).LastOrDefault(c => c is not null);
                        var linha = RegrasEndereco.Montar(Texto(args, "rua"), Texto(args, "numero"), Texto(args, "complemento"), Texto(args, "bairro"), Texto(args, "cidade"));
                        if (cepEndereco is null)
                            resultado = new JsonObject { ["erro"] = "sem CEP: peça o CEP antes do endereço" };
                        else if (linha is null)
                            resultado = new JsonObject { ["erro"] = "faltam a rua ou o número: pergunte ao cliente o que falta" };
                        else
                        {
                            endereco = new EnderecoEntrega(cepEndereco.Value, linha);
                            resultado = new JsonObject
                            {
                                ["ok"] = true,
                                ["endereco"] = linha,
                                ["instrucao"] = "Confirme o endereço com o cliente em uma frase e diga que um atendente vai fechar o pedido. Não peça mais nenhum dado."
                            };
                        }
                        break;

                    case "link_catalogo":
                        resultado = new JsonObject { ["link"] = await ferramentas.LinkCatalogoAsync(ct) };
                        break;

                    case "chamar_vendedor":
                        motivoVendedor = args["motivo"]?.GetValue<string>() ?? "Cliente quer falar com uma pessoa";
                        resultado = new JsonObject { ["ok"] = true, ["instrucao"] = "Avise o cliente que um atendente vai responder em instantes." };
                        break;

                    default:
                        resultado = new JsonObject { ["erro"] = $"ferramenta desconhecida: {nome}" };
                        break;
                }

                respostas.Add(new JsonObject { ["functionResponse"] = new JsonObject { ["name"] = nome, ["response"] = resultado } });
            }
            conteudos.Add(new JsonObject { ["role"] = "user", ["parts"] = respostas });
        }

        return new RespostaAtendente("Vou chamar um atendente para te ajudar, um instante.", true, motivoVendedor ?? "IA não concluiu a resposta") { Reservar = reservar, Endereco = endereco };
    }

    private async Task<JsonObject> ChamarAsync(string nomeLoja, TextosProntos textos, JsonArray conteudos, CancellationToken ct)
    {
        var o = opcoes.Value;
        var corpo = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = InstrucaoSistema(nomeLoja, textos, mercadoPago.Value.Configurado) }) },
            ["contents"] = conteudos.DeepClone(),
            ["tools"] = JsonNode.Parse(Ferramentas),
            ["generationConfig"] = new JsonObject { ["temperature"] = 0.3, ["maxOutputTokens"] = 400 }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{o.Modelo}:generateContent")
        {
            Content = JsonContent.Create(corpo)
        };
        req.Headers.Add("x-goog-api-key", o.ApiKey);

        using var resp = await http.SendAsync(req, ct);
        var texto = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini respondeu {(int)resp.StatusCode}: {texto[..Math.Min(300, texto.Length)]}");

        return JsonNode.Parse(texto)!.AsObject();
    }

    private static string? Texto(JsonObject args, string campo) =>
        args[campo] is JsonValue v ? v.ToString() : null;

    private static JsonObject Parte(string papel, JsonObject parte) =>
        new() { ["role"] = papel, ["parts"] = new JsonArray(parte) };

    private static string InstrucaoSistema(string nomeLoja, TextosProntos textos, bool aceitaCartao) => $"""
        Você é o atendente virtual da {nomeLoja}, uma loja de peixes ornamentais, conversando pelo WhatsApp.
        Regras:
        - Responda em português do Brasil, curto e simpático (no máximo 3 frases), como num WhatsApp.
        - Preço e disponibilidade SÓ podem vir das ferramentas. Nunca invente espécie, preço ou quantidade.
        - Quando o cliente citar um peixe, use buscar_especie. Se não houver estoque, diga que no momento não tem.
        - Peixe em quarentena não aparece no estoque e não pode ser oferecido.
        - Se pedirem catálogo, lista, fotos ou "o que vocês têm", use link_catalogo e envie o link completo,
          explicando que lá dá para escolher as quantidades e enviar o pedido pelo WhatsApp.
        - Mensagem que contém "{PedidoCatalogo.Marcador}" é um pedido montado na vitrine: confira cada item com
          buscar_especie, use criar_reserva com os itens e quantidades, peça o endereço completo de entrega com CEP e use chamar_vendedor
          com o motivo "Pedido pelo catálogo".
        - Quando o cliente disser que quer reservar ou comprar uma quantidade de um peixe com estoque, use criar_reserva
          (especie_id vindo de buscar_especie) e depois chamar_vendedor. A reserva vale 24 h.
        - Frete e entrega: se o cliente ainda não mandou o CEP, peça o endereço completo de entrega numa mensagem só:
          rua, número, complemento, bairro e CEP. Nunca peça só o CEP. Quando o CEP chegar, use calcular_frete,
          informe valor e prazo e use chamar_vendedor com o resumo_para_vendedor no motivo. Se atende vier false
          (além do raio de entrega da loja) ou vier sem_calculo, não dê valor: diga que um atendente vai ver a entrega
          e use chamar_vendedor. Nunca invente valor de frete ou prazo.
        - Endereço de entrega: se o cliente já mandou rua e número junto com o CEP, use salvar_endereco na mesma resposta
          do frete e não pergunte de novo. Se mandou só o CEP, depois do frete, na mesma resposta, peça o que falta. Se calcular_frete trouxe
          endereco_do_cep, mostre a rua e o bairro dele para o cliente confirmar e pergunte só o número e o complemento
          (apartamento, bloco, casa dos fundos). Sem endereco_do_cep, peça rua, número, complemento e bairro.
          Quando o cliente responder, use salvar_endereco com o CEP, a rua, o número, o complemento, o bairro e a cidade
          (do que ele escreveu e do endereco_do_cep). Se faltar o número, pergunte de novo. Se o cliente for retirar
          na loja, não peça endereço.
        - Ao chamar o vendedor, use este texto da loja: "{textos.ChamarVendedor}".
        - Use chamar_vendedor quando o cliente quiser reservar, comprar ou pagar, pedir para falar com uma pessoa,
          reclamar, ou quando você não souber responder.
        - O único dado pessoal que você pode pedir é o endereço de entrega, e só quando houver entrega.
          Nunca peça CPF, RG, e-mail, data de nascimento ou dados de cartão.
        - Formas de pagamento: {(aceitaCartao
            ? "Pix ou cartão de crédito (pode parcelar) e débito. Depois que o atendente fecha o pedido, o cliente recebe o código Pix e um link do Mercado Pago para pagar no cartão."
            : "Pix. Se perguntarem de cartão, diga que um atendente vê a possibilidade e use chamar_vendedor.")}
          Nunca mande link de pagamento por conta própria.
        """;

    private const string Ferramentas = """
        [{
          "functionDeclarations": [
            {
              "name": "buscar_especie",
              "description": "Busca espécies do catálogo da loja por nome popular ou científico. Devolve preço e quantidade disponível para venda.",
              "parameters": { "type": "object", "properties": { "texto": { "type": "string", "description": "Nome ou parte do nome do peixe" } }, "required": ["texto"] }
            },
            {
              "name": "consultar_estoque",
              "description": "Quantidade disponível agora de uma espécie, já descontando reservas e quarentena.",
              "parameters": { "type": "object", "properties": { "especie_id": { "type": "string" } }, "required": ["especie_id"] }
            },
            {
              "name": "calcular_frete",
              "description": "Valor e prazo do frete para um CEP, pela distância de carro desde a loja (base até alguns km, depois por km, até o raio máximo).",
              "parameters": { "type": "object", "properties": { "cep": { "type": "string", "description": "CEP com 8 dígitos" } }, "required": ["cep"] }
            },
            {
              "name": "salvar_endereco",
              "description": "Guarda o endereço de entrega que o cliente informou, para o atendente não precisar perguntar de novo.",
              "parameters": {
                "type": "object",
                "properties": {
                  "cep": { "type": "string", "description": "CEP com 8 dígitos" },
                  "rua": { "type": "string" },
                  "numero": { "type": "string", "description": "Número da casa ou do prédio" },
                  "complemento": { "type": "string", "description": "Apartamento, bloco, fundos; vazio se não tiver" },
                  "bairro": { "type": "string" },
                  "cidade": { "type": "string", "description": "Cidade - UF" }
                },
                "required": ["cep", "rua", "numero"]
              }
            },
            {
              "name": "criar_reserva",
              "description": "Separa peixes para o cliente por 24 h (sai do disponível até o vendedor confirmar a venda ou a reserva vencer).",
              "parameters": {
                "type": "object",
                "properties": {
                  "itens": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": { "especie_id": { "type": "string" }, "quantidade": { "type": "integer" } },
                      "required": ["especie_id", "quantidade"]
                    }
                  }
                },
                "required": ["itens"]
              }
            },
            {
              "name": "link_catalogo",
              "description": "Link da vitrine da loja, com fotos, preços e o que está disponível. O cliente monta o pedido lá.",
              "parameters": { "type": "object", "properties": {} }
            },
            {
              "name": "chamar_vendedor",
              "description": "Passa a conversa para um vendedor humano e pausa o atendimento automático.",
              "parameters": { "type": "object", "properties": { "motivo": { "type": "string", "description": "Resumo curto do que o cliente quer" } }, "required": ["motivo"] }
            }
          ]
        }]
        """;
}
