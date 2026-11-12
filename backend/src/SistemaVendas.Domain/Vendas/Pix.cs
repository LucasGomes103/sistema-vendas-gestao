using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SistemaVendas.Domain.Estoque;

namespace SistemaVendas.Domain.Vendas;

public enum TipoChavePix
{
    Cpf,
    Cnpj,
    Telefone,
    Email,
    Aleatoria
}

/// <summary>
/// Pix com a chave comum da loja, sem provedor de pagamento (custo zero). Gera o "Pix copia e cola"
/// (BR Code estático do Banco Central) já com o valor e o número do pedido; o cliente cola no app do banco.
/// Não há aviso automático de pagamento: o vendedor confere na conta e confirma com um toque.
/// </summary>
public static partial class RegrasPix
{
    [GeneratedRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")]
    private static partial Regex ChaveAleatoria();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Email();

    /// <summary>
    /// Confere a chave e devolve no formato que o Pix espera: CPF e CNPJ só dígitos, telefone +55DDDNÚMERO,
    /// e-mail em minúsculas, aleatória como veio. Telefone precisa do DDD; um número de 11 dígitos sem
    /// +55 é lido como CPF se o dígito verificador bater, senão como celular.
    /// </summary>
    public static (string Chave, TipoChavePix Tipo) NormalizarChave(string? chave)
    {
        var texto = (chave ?? "").Trim();
        if (texto.Length == 0)
            throw new RegraException("Informe a chave Pix.");

        if (ChaveAleatoria().IsMatch(texto.ToLowerInvariant()))
            return (texto.ToLowerInvariant(), TipoChavePix.Aleatoria);
        if (texto.Contains('@'))
        {
            var email = texto.ToLowerInvariant();
            if (!Email().IsMatch(email) || email.Length > 77)
                throw new RegraException("E-mail da chave Pix inválido.");
            return (email, TipoChavePix.Email);
        }

        var digitos = new string(texto.Where(char.IsDigit).ToArray());
        var comMais = texto.StartsWith('+');

        if (!comMais && digitos.Length == 14 && CnpjValido(digitos))
            return (digitos, TipoChavePix.Cnpj);
        if (!comMais && digitos.Length == 11 && CpfValido(digitos))
            return (digitos, TipoChavePix.Cpf);

        // Telefone: aceita com ou sem 55 na frente.
        if (digitos.StartsWith("55") && digitos.Length is 12 or 13)
            return ("+" + digitos, TipoChavePix.Telefone);
        if (digitos.Length is 10 or 11 && !comMais)
            return ("+55" + digitos, TipoChavePix.Telefone);

        throw new RegraException("Chave Pix não reconhecida. Use CPF, CNPJ, celular com DDD, e-mail ou chave aleatória.");
    }

    /// <summary>
    /// Monta o BR Code (padrão EMV do Banco Central). Nome e cidade do recebedor vão sem acento e em
    /// maiúsculas, cortados nos limites do padrão (25 e 15 letras). O identificador (txid) leva só
    /// letras e números, até 25.
    /// </summary>
    public static string CopiaECola(string chave, string nomeRecebedor, string cidade, decimal? valor, string? identificador)
    {
        var (chaveNormal, _) = NormalizarChave(chave);
        var nome = Limpar(nomeRecebedor, 25);
        var cid = Limpar(cidade, 15);
        if (nome.Length == 0)
            throw new RegraException("Informe o nome de quem recebe o Pix.");
        if (cid.Length == 0)
            throw new RegraException("Informe a cidade de quem recebe o Pix.");
        if (valor is <= 0)
            throw new RegraException("O valor do Pix precisa ser maior que zero.");

        var txid = new string((identificador ?? "").Where(char.IsAsciiLetterOrDigit).Take(25).ToArray());
        if (txid.Length == 0)
            txid = "***";

        var sb = new StringBuilder();
        sb.Append(Campo("00", "01"));
        sb.Append(Campo("26", Campo("00", "br.gov.bcb.pix") + Campo("01", chaveNormal)));
        sb.Append(Campo("52", "0000"));
        sb.Append(Campo("53", "986"));
        if (valor is { } v)
            sb.Append(Campo("54", v.ToString("0.00", CultureInfo.InvariantCulture)));
        sb.Append(Campo("58", "BR"));
        sb.Append(Campo("59", nome));
        sb.Append(Campo("60", cid));
        sb.Append(Campo("62", Campo("05", txid)));
        sb.Append("6304");
        sb.Append(Crc16(sb.ToString()).ToString("X4"));
        return sb.ToString();
    }

    private static string Campo(string id, string valor)
    {
        if (valor.Length > 99)
            throw new RegraException("Dado do Pix grande demais.");
        return id + valor.Length.ToString("00") + valor;
    }

    /// <summary>Tira acentos e símbolos que os bancos recusam; mantém letras, números e espaço.</summary>
    public static string Limpar(string? texto, int maximo)
    {
        var decomposto = (texto ?? "").Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsAsciiLetterOrDigit(c) || c == ' ')
                sb.Append(char.ToUpperInvariant(c));
        }
        var limpo = Regex.Replace(sb.ToString(), " {2,}", " ").Trim();
        return limpo.Length > maximo ? limpo[..maximo].TrimEnd() : limpo;
    }

    /// <summary>CRC16-CCITT (polinômio 0x1021, início 0xFFFF), exigido no fim do BR Code.</summary>
    public static ushort Crc16(string texto)
    {
        ushort crc = 0xFFFF;
        foreach (var b in Encoding.UTF8.GetBytes(texto))
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    private static bool CpfValido(string d)
    {
        if (d.Distinct().Count() == 1)
            return false;
        int Digito(int n)
        {
            var soma = 0;
            for (var i = 0; i < n; i++)
                soma += (d[i] - '0') * (n + 1 - i);
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
        return Digito(9) == d[9] - '0' && Digito(10) == d[10] - '0';
    }

    private static bool CnpjValido(string d)
    {
        if (d.Distinct().Count() == 1)
            return false;
        int Digito(int n)
        {
            int[] pesos = n == 12 ? [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2] : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
            var soma = 0;
            for (var i = 0; i < n; i++)
                soma += (d[i] - '0') * pesos[i];
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }
        return Digito(12) == d[12] - '0' && Digito(13) == d[13] - '0';
    }
}
