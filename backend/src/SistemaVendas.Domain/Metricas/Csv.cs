using System.Globalization;
using System.Text;

namespace SistemaVendas.Domain.Metricas;

/// <summary>
/// CSV no jeito que o Excel em português abre direto: separador ";", vírgula decimal e BOM UTF-8
/// (sem ele os acentos saem quebrados).
/// </summary>
public static class Csv
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static byte[] Gerar(IEnumerable<string> cabecalho, IEnumerable<IEnumerable<object?>> linhas)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', cabecalho.Select(Campo)));
        foreach (var linha in linhas)
            sb.AppendLine(string.Join(';', linha.Select(Valor)));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    public static string Valor(object? v) => v switch
    {
        null => "",
        decimal d => d.ToString("0.00", PtBr),
        double d => d.ToString("0.##", PtBr),
        DateOnly d => d.ToString("dd/MM/yyyy", PtBr),
        DateTimeOffset d => d.ToString("dd/MM/yyyy HH:mm", PtBr),
        bool b => b ? "Sim" : "Não",
        IFormattable f => f.ToString(null, PtBr),
        _ => Campo(v.ToString() ?? "")
    };

    /// <summary>Aspas quando o texto tem ";", aspas ou quebra de linha. Texto começando com =, +, - ou @ ganha um apóstrofo
    /// para o Excel não tratar como fórmula.</summary>
    public static string Campo(string texto)
    {
        if (texto.Length > 0 && texto[0] is '=' or '+' or '-' or '@')
            texto = "'" + texto;
        return texto.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{texto.Replace("\"", "\"\"")}\"" : texto;
    }
}
