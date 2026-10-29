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
