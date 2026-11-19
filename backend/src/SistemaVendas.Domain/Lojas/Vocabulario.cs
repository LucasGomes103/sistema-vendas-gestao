using System.Globalization;
using System.Text;

namespace SistemaVendas.Domain.Lojas;

/// <summary>
/// Palavras que mudam conforme o tipo de loja. Por dentro o sistema continua chamando o produto de "espécie" e o lugar
/// do estoque de "tanque" (banco e código); o que o dono, a equipe e o cliente leem vem daqui.
/// Aquarismo (ECS Fish) fala de espécie, tanque e peixe; as outras lojas, de produto, local e item.
/// </summary>
public sealed record Vocabulario(
    string Chave,
    // Como o robô descreve a loja: "uma loja de peixes ornamentais".
    string DescricaoLoja,
    string Produto,
    string Produtos,
    // "a espécie" (feminino) ou "o produto": para "Nova espécie" / "Novo produto".
    bool ProdutoFeminino,
    // O que o cliente leva, no plural e em minúsculas: "peixes", "produtos".
    string Itens,
    string Local,
    string Locais,
    // Rótulo do segundo nome do produto: "Nome científico", "Marca ou modelo".
    string NomeSecundario,
    // Mostra tamanho em cm e litros do tanque (coisas de aquário).
    bool Aquario,
    // Nome do lugar que não vende (quarentena no aquarismo).
    string Isolamento,
    string Emoji)
{
    public static readonly Vocabulario Aquarismo = new(
        "aquarismo", "uma loja de peixes ornamentais", "Espécie", "Espécies", true, "peixes", "Tanque", "Tanques",
        "Nome científico", true, "Quarentena", "🐠");

    // Loja de qualquer outro tipo.
    public static Vocabulario Geral(string? segmento) => new(
        "geral", segmento is { Length: > 0 } s ? $"uma loja do ramo de {s.Trim().ToLowerInvariant()}" : "uma loja",
        "Produto", "Produtos", false, "produtos", "Local", "Locais", "Marca ou modelo", false, "Separado (não vende)", "🛍️");

    // Palavras que indicam loja de aquário no tipo de loja escrito no cadastro.
    private static readonly string[] SinaisAquario = ["aquar", "aquá", "peixe", "aquat", "aquát"];

    public static Vocabulario Para(string? segmento)
    {
        var s = SemAcento(segmento ?? "").ToLowerInvariant();
        return SinaisAquario.Any(p => s.Contains(SemAcento(p))) ? Aquarismo : Geral(segmento);
    }

    private static string SemAcento(string texto)
    {
        var sb = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }
}
