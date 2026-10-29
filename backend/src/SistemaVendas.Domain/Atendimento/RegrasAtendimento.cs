using System.Globalization;
using System.Text;

namespace SistemaVendas.Domain.Atendimento;

public static class RegrasAtendimento
{
    /// <summary>Esperas entre tentativas de envio; depois da última, a mensagem fica como falha.</summary>
    private static readonly TimeSpan[] Esperas =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2)
    ];

    public static int MaximoTentativas => Esperas.Length;

    /// <summary>Registra uma falha de envio: agenda a próxima tentativa com espera crescente ou desiste.</summary>
    public static void RegistrarFalha(Mensagem mensagem, string erro, DateTimeOffset agora)
    {
        mensagem.Tentativas++;
        mensagem.Erro = erro;

        if (mensagem.Tentativas >= Esperas.Length)
        {
            mensagem.Status = StatusMensagem.Falhou;
            mensagem.ProximaTentativaEm = null;
        }
        else
        {
            mensagem.ProximaTentativaEm = agora + Esperas[mensagem.Tentativas - 1];
        }
    }

    /// <summary>Telefone mascarado para logs (LGPD): 11*****4321.</summary>
    public static string MascararTelefone(string telefone)
    {
        var digitos = new string(telefone.Where(char.IsDigit).ToArray());
        if (digitos.Length <= 6)
            return new string('*', digitos.Length);

        // Ignora o DDI 55 para mostrar o DDD.
        var local = digitos.StartsWith("55") && digitos.Length > 11 ? digitos[2..] : digitos;
        return local[..2] + new string('*', local.Length - 6) + local[^4..];
    }

    /// <summary>"parar" ou "sair" desliga o marketing na hora.</summary>
    public static bool PedeParaParar(string texto)
    {
        var normalizado = SemAcento(texto).Trim().ToLowerInvariant().TrimEnd('.', '!');
        return normalizado is "parar" or "sair" or "pare" or "stop" or "cancelar";
    }

    private static string SemAcento(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }
}
