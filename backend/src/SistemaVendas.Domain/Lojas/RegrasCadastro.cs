using System.Globalization;
using System.Text;

namespace SistemaVendas.Domain.Lojas;

/// <summary>Regras do cadastro público de loja (site → loja + dono).</summary>
public static class RegrasCadastro
{
    public const int TamanhoMinimoSlug = 3;
    public const int TamanhoMaximoSlug = 40;

    /// <summary>Validade do link de confirmação do e-mail.</summary>
    public static readonly TimeSpan ValidadeConfirmacao = TimeSpan.FromHours(48);

    /// <summary>Caminhos do site que não podem virar endereço de vitrine.</summary>
    public static readonly HashSet<string> Reservados = new(StringComparer.Ordinal)
    {
        "painel", "api", "health", "privacidade", "termos", "assets", "admin", "administrador", "cadastro", "planos",
        "login", "entrar", "www", "static", "favicon", "robots", "sitemap", "app", "suporte", "ajuda", "blog", "contato",
        "conta", "loja", "lojas", "demo", "teste", "precos", "sistema", "plataforma", "webhooks", "public"
    };

    /// <summary>Endereço a partir do nome: "Aquário do Zé" → "aquario-do-ze".</summary>
    public static string SugerirSlug(string? nome)
    {
        var semAcento = new StringBuilder();
        foreach (var c in (nome ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                semAcento.Append(c);

        var slug = new StringBuilder();
        foreach (var c in semAcento.ToString().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }
        var texto = slug.ToString().Trim('-');
        return texto.Length > TamanhoMaximoSlug ? texto[..TamanhoMaximoSlug].TrimEnd('-') : texto;
    }

    /// <summary>Confere o endereço da vitrine. Devolve o erro ou null.</summary>
    public static string? ValidarSlug(string? slug)
    {
        if (string.IsNullOrEmpty(slug) || slug.Length < TamanhoMinimoSlug || slug.Length > TamanhoMaximoSlug)
            return $"O endereço precisa ter de {TamanhoMinimoSlug} a {TamanhoMaximoSlug} letras.";
        if (!slug.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') || slug.StartsWith('-') || slug.EndsWith('-') || slug.Contains("--"))
            return "Use só letras minúsculas sem acento, números e hífen (ex.: aquario-do-ze).";
        if (Reservados.Contains(slug))
            return "Este endereço é reservado. Escolha outro.";
        return null;
    }

    /// <summary>E-mail com cara de e-mail (o link de confirmação prova que é dele).</summary>
    public static bool EmailValido(string? email) =>
        email is { Length: >= 6 and <= 200 } && email.Count(c => c == '@') == 1 && email.IndexOf('@') > 0
        && email[(email.IndexOf('@') + 1)..].Contains('.') && !email.Any(char.IsWhiteSpace) && !email.EndsWith('.');
}
