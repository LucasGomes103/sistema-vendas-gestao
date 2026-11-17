namespace SistemaVendas.Domain.Entregas;

/// <summary>Endereço de entrega que o cliente passou na conversa, já numa linha só para o pedido.</summary>
public record EnderecoEntrega(int Cep, string Texto);

public static class RegrasEndereco
{
    /// <summary>
    /// Monta "Rua X, 123, apto 4 - Bairro, Cidade - UF". Sem rua ou sem número devolve null:
    /// endereço incompleto não serve para entregar, então o robô pergunta de novo.
    /// </summary>
    public static string? Montar(string? rua, string? numero, string? complemento, string? bairro, string? cidade)
    {
        rua = Limpar(rua);
        numero = Limpar(numero);
        if (rua is null || numero is null)
            return null;

        var linha = $"{rua}, {numero}";
        if (Limpar(complemento) is { } comp)
            linha += $", {comp}";
        var local = string.Join(", ", new[] { Limpar(bairro), Limpar(cidade) }.Where(p => p is not null));
        return local.Length == 0 ? linha : $"{linha} - {local}";
    }

    /// <summary>Endereço do cadastro do cliente vale para este CEP? Se o cliente mudou de CEP, não reaproveita.</summary>
    public static bool ValeParaCep(string? cepCadastrado, string? endereco, int cep) =>
        !string.IsNullOrWhiteSpace(endereco) && RegrasFrete.LerCep(cepCadastrado) == cep;

    /// <summary>Resposta do cliente que traz ao menos o número da casa ("120", "Rua das Flores, 120 apto 3"), e não só um CEP.</summary>
    public static bool PareceEndereco(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto) || !texto.Any(char.IsDigit))
            return false;
        // Só o CEP de novo não é endereço.
        return SemCep(texto).Any(char.IsDigit);
    }

    /// <summary>Tira o CEP (e a palavra "CEP") do texto: "Rua A, 10, CEP 07010-000" vira "Rua A, 10".</summary>
    public static string SemCep(string texto)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(texto, @"(?i)\b(cep)?[:\s]*(?<!\d)\d{5}[-.\s]?\d{3}(?!\d)", " ");
        return string.Join(' ', t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', ',', '-', '.');
    }

    /// <summary>
    /// Junta a resposta do cliente com a rua, o bairro e a cidade que o CEP deu ("Rua X, Bairro, Cidade - UF").
    /// Se o cliente já escreveu a rua, vale o que ele escreveu, mais bairro e cidade do CEP.
    /// </summary>
    public static string Completar(string respostaCliente, string? enderecoDoCep)
    {
        var resposta = Limpar(respostaCliente) ?? "";
        var partes = (enderecoDoCep ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (partes.Length == 0)
            return resposta;

        // Com 3 partes o CEP deu rua, bairro e cidade; com menos, só bairro e cidade (CEP geral da cidade).
        var rua = partes.Length >= 3 ? partes[0] : null;
        var local = string.Join(", ", partes.Skip(rua is null ? 0 : 1));
        var escreveuRua = rua is null || Normalizar(resposta).Contains(Normalizar(rua))
            || resposta.Any(char.IsLetter) && System.Text.RegularExpressions.Regex.IsMatch(Normalizar(resposta), @"\b(rua|r|av|avenida|travessa|tv|alameda|al|estrada|rodovia|praca)\b");
        var linha = escreveuRua ? resposta : $"{rua}, {resposta}";
        return Normalizar(linha).Contains(Normalizar(partes[^1])) ? linha : $"{linha} - {local}";
    }

    private static string Normalizar(string s)
    {
        var d = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static string? Limpar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        var t = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim(' ', ',', '-', '.');
        return t.Length == 0 ? null : t[..Math.Min(t.Length, 120)];
    }
}
