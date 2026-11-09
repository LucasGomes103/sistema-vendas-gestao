using SistemaVendas.Domain.Comum;

namespace SistemaVendas.Domain.Catalogo;

public class Especie : EntidadeDaLoja
{
    public required string NomePopular { get; set; }
    public string? NomeCientifico { get; set; }
    public decimal? TamanhoCm { get; set; }
    public decimal Preco { get; set; }
    public string? Observacoes { get; set; }
    public bool Ativo { get; set; } = true;

    public List<Foto> Fotos { get; set; } = [];
}

public class Foto : EntidadeDaLoja
{
    public Guid EspecieId { get; set; }
    public required string Url { get; set; }
    public int Ordem { get; set; }
}

/// <summary>
/// Bytes da foto, guardados no próprio banco (fase 1, sem custo de armazenamento). Ficam numa tabela à parte
/// para a listagem de espécies não carregar imagem. O painel reduz a foto antes de enviar.
/// </summary>
public class ArquivoFoto : Entidade
{
    public Guid FotoId { get; set; }
    public required byte[] Conteudo { get; set; }
    public required string Tipo { get; set; }
}

public static class RegrasFoto
{
    /// <summary>Depois de reduzida no navegador, uma foto fica bem abaixo disso.</summary>
    public const int TamanhoMaximo = 1_500_000;

    public static readonly string[] TiposAceitos = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>Lê um "data:image/jpeg;base64,..." e confere tipo e tamanho. Devolve erro ou os bytes.</summary>
    public static (byte[]? Bytes, string? Tipo, string? Erro) LerDataUrl(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:", StringComparison.Ordinal))
            return (null, null, "Envie a foto como imagem.");
        var virgula = dataUrl.IndexOf(',');
        var cabecalho = virgula > 0 ? dataUrl[5..virgula] : "";
        if (!cabecalho.EndsWith(";base64", StringComparison.Ordinal))
            return (null, null, "Formato de imagem inválido.");
        var tipo = cabecalho[..^7];
        if (!TiposAceitos.Contains(tipo))
            return (null, null, "Use uma foto JPG, PNG ou WebP.");

        byte[] bytes;
        try { bytes = Convert.FromBase64String(dataUrl[(virgula + 1)..]); }
        catch (FormatException) { return (null, null, "Formato de imagem inválido."); }

        if (bytes.Length == 0)
            return (null, null, "A foto está vazia.");
        if (bytes.Length > TamanhoMaximo)
            return (null, null, "A foto é grande demais (máximo 1,5 MB).");
        return (bytes, tipo, null);
    }
}
