using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaVendas.Application.Atendimento;

namespace SistemaVendas.Infrastructure.IA;

public class GeminiOptions
{
    public const string Secao = "IA:Gemini";

    public string? ApiKey { get; set; }

    /// <summary>Flash-Lite na camada gratuita (fase 1).</summary>
    public string Modelo { get; set; } = "gemini-flash-lite-latest";
}

/// <summary>
/// Atendente com Gemini e function calling. A IA só responde com dados vindos das ferramentas;
/// nome, telefone e endereço do cliente nunca são enviados ao modelo.
/// </summary>
public class GeminiAtendente(HttpClient http, IOptions<GeminiOptions> opcoes, AtendentePorRegras reserva, ILogger<GeminiAtendente> logger) : IAtendenteIA
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

        for (var rodada = 0; rodada < MaximoRodadas; rodada++)
        {
            var resposta = await ChamarAsync(nomeLoja, conteudos, ct);
            var conteudo = resposta["candidates"]?[0]?["content"] as JsonObject
                ?? throw new InvalidOperationException("Gemini não devolveu conteúdo.");
            var partes = conteudo["parts"]?.AsArray() ?? [];

            var chamadas = partes.Where(p => p?["functionCall"] is not null).Select(p => p!["functionCall"]!).ToList();
            if (chamadas.Count == 0)
            {
                var texto = string.Concat(partes.Select(p => p?["text"]?.GetValue<string>())).Trim();
                if (string.IsNullOrEmpty(texto))
                    texto = motivoVendedor is not null ? "Vou chamar um atendente para falar com você, um instante." : "Pode repetir, por favor?";
                return new RespostaAtendente(texto, motivoVendedor is not null, motivoVendedor);
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

        return new RespostaAtendente("Vou chamar um atendente para te ajudar, um instante.", true, motivoVendedor ?? "IA não concluiu a resposta");
    }

    private async Task<JsonObject> ChamarAsync(string nomeLoja, JsonArray conteudos, CancellationToken ct)
    {
        var o = opcoes.Value;
        var corpo = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = InstrucaoSistema(nomeLoja) }) },
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

    private static JsonObject Parte(string papel, JsonObject parte) =>
        new() { ["role"] = papel, ["parts"] = new JsonArray(parte) };

    private static string InstrucaoSistema(string nomeLoja) => $"""
        Você é o atendente virtual da {nomeLoja}, uma loja de peixes ornamentais, conversando pelo WhatsApp.
        Regras:
        - Responda em português do Brasil, curto e simpático (no máximo 3 frases), como num WhatsApp.
        - Preço e disponibilidade SÓ podem vir das ferramentas. Nunca invente espécie, preço ou quantidade.
        - Quando o cliente citar um peixe, use buscar_especie. Se não houver estoque, diga que no momento não tem.
        - Peixe em quarentena não aparece no estoque e não pode ser oferecido.
        - Use chamar_vendedor quando o cliente quiser reservar, comprar, pagar, saber frete ou entrega,
          pedir para falar com uma pessoa, reclamar, ou quando você não souber responder.
        - Não peça nem repita CPF, endereço ou outros dados pessoais.
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
              "name": "chamar_vendedor",
              "description": "Passa a conversa para um vendedor humano e pausa o atendimento automático.",
              "parameters": { "type": "object", "properties": { "motivo": { "type": "string", "description": "Resumo curto do que o cliente quer" } }, "required": ["motivo"] }
            }
          ]
        }]
        """;
}
