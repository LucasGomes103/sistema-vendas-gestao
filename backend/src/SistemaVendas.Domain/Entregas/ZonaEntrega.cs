using SistemaVendas.Domain.Comum;

namespace SistemaVendas.Domain.Entregas;

/// <summary>
/// Faixa de CEP atendida pela loja, com valor e prazo do frete (fase 1: tabela própria, sem transportadora).
/// Ex.: Guarulhos 07000-000 a 07399-999, R$ 15, 1 dia.
/// </summary>
public class ZonaEntrega : EntidadeDaLoja
{
    public required string Nome { get; set; }

    /// <summary>CEP inicial da faixa, só os 8 dígitos.</summary>
    public int CepInicio { get; set; }

    /// <summary>CEP final da faixa (inclusive), só os 8 dígitos.</summary>
    public int CepFim { get; set; }

    public decimal Valor { get; set; }

    /// <summary>Prazo em dias úteis (0 = no mesmo dia).</summary>
    public int Dias { get; set; }
}

public static class RegrasFrete
{
    /// <summary>Lê um CEP digitado de qualquer jeito ("07010-000", "07010000", "cep 07010 000"). Devolve null se não tiver 8 dígitos.</summary>
    public static int? LerCep(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        var digitos = new string(texto.Where(char.IsDigit).ToArray());
        return digitos.Length == 8 ? int.Parse(digitos) : null;
    }

    /// <summary>Procura um CEP no meio de uma mensagem ("meu cep é 07010-000, entrega?").</summary>
    public static int? AcharCep(string? mensagem)
    {
        if (string.IsNullOrWhiteSpace(mensagem))
            return null;
        var m = System.Text.RegularExpressions.Regex.Match(mensagem, @"(?<!\d)(\d{5})[-.\s]?(\d{3})(?!\d)");
        return m.Success ? int.Parse(m.Groups[1].Value + m.Groups[2].Value) : null;
    }

    public static string Formatar(int cep) => cep.ToString("00000000").Insert(5, "-");

    /// <summary>Zona que atende o CEP. Se faixas se sobrepõem, vale a mais estreita (a mais específica).</summary>
    public static ZonaEntrega? Calcular(int cep, IEnumerable<ZonaEntrega> zonas) =>
        zonas.Where(z => cep >= z.CepInicio && cep <= z.CepFim)
            .OrderBy(z => z.CepFim - z.CepInicio)
            .FirstOrDefault();

    /// <summary>Erro de cadastro da faixa, ou null.</summary>
    public static string? Validar(int? inicio, int? fim, decimal valor, int dias)
    {
        if (inicio is null || fim is null)
            return "Informe o CEP inicial e o final com 8 dígitos.";
        if (inicio > fim)
            return "O CEP inicial precisa ser menor ou igual ao final.";
        if (valor < 0)
            return "O valor do frete não pode ser negativo.";
        if (dias is < 0 or > 60)
            return "O prazo precisa ficar entre 0 e 60 dias.";
        return null;
    }
}
