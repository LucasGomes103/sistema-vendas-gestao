using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.Entregas;

namespace SistemaVendas.Infrastructure.IA;

public class VitrineOptions
{
    public const string Secao = "Vitrine";

    /// <summary>Endereço público da vitrine; o link de cada loja é {UrlBase}/{slug}.</summary>
    public string UrlBase { get; set; } = "http://localhost:4300";
}

/// <summary>Ferramentas da IA sobre o catálogo e o estoque da loja atual.</summary>
public class FerramentasAtendimento(AppDbContext db, ILojaAtual lojaAtual, IOptions<VitrineOptions> vitrine, ServicoFunil funil, ServicoFrete frete,
    Pagamentos.IMercadoPago mercadoPago) : IFerramentasAtendimento
{
    public async Task<TextosProntos> TextosAsync(CancellationToken ct)
    {
        var cfg = await funil.ConfigAsync(lojaAtual.LojaId!.Value, ct);
        var segmento = await db.Lojas.Where(l => l.Id == lojaAtual.LojaId).Select(l => l.Segmento).FirstOrDefaultAsync(ct);
        return new TextosProntos(cfg.TextoFrete, cfg.TextoChamarVendedor, await mercadoPago.ConfiguradoAsync(lojaAtual.LojaId!.Value, ct),
            Domain.Lojas.Vocabulario.Para(segmento));
    }

    public async Task<string?> LinkCatalogoAsync(CancellationToken ct)
    {
        var slug = await db.Lojas.Where(l => l.Id == lojaAtual.LojaId).Select(l => l.Slug).FirstOrDefaultAsync(ct);
        return slug is null ? null : $"{vitrine.Value.UrlBase.TrimEnd('/')}/{slug}";
    }

    public async Task<IReadOnlyList<EspecieEncontrada>> BuscarEspecieAsync(string texto, CancellationToken ct)
    {
        var termos = Normalizar(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 3).ToArray();
        if (termos.Length == 0)
            return [];

        // Catálogo de uma loja é pequeno: a comparação sem acento é feita em memória.
        var especies = await db.Especies.AsNoTracking().Where(e => e.Ativo).ToListAsync(ct);
        var disponivel = Saldos.DisponivelPorEspecie(await Saldos.PorLoteAsync(db, ct));

        return especies
            .Select(e => new { e, Nome = Normalizar($"{e.NomePopular} {e.NomeCientifico} {string.Join(' ', (e.Atributos ?? []).Select(a => a.Valor))}") })
            .Where(x => termos.Any(t => x.Nome.Contains(t) || x.Nome.Split(' ').Any(p => p.Length >= 3 && t.StartsWith(p))))
            .OrderByDescending(x => termos.Count(t => x.Nome.Contains(t)))
            .Take(5)
            .Select(x => new EspecieEncontrada(x.e.Id, x.e.NomePopular, x.e.NomeCientifico, x.e.TamanhoCm, x.e.Preco, disponivel.GetValueOrDefault(x.e.Id),
                Domain.Catalogo.RegrasAtributos.Resumo(x.e.Atributos)))
            .ToList();
    }

    public async Task<FreteCalculado?> CalcularFreteAsync(string cep, CancellationToken ct)
    {
        // Sem frete configurado ou CEP que os mapas não acham: null, e o vendedor calcula.
        return RegrasFrete.LerCep(cep) is { } numero ? await frete.CalcularAsync(numero, ct) : null;
    }

    public async Task<int> ConsultarEstoqueAsync(Guid especieId, CancellationToken ct) =>
        Saldos.DisponivelPorEspecie(await Saldos.PorLoteAsync(db, ct)).GetValueOrDefault(especieId);

    public static string Normalizar(string texto)
    {
        var decomposto = texto.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return sb.ToString();
    }
}
