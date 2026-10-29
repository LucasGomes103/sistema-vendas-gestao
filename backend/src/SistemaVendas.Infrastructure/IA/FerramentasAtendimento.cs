using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Infrastructure.IA;

/// <summary>Ferramentas da IA sobre o catálogo e o estoque da loja atual.</summary>
public class FerramentasAtendimento(AppDbContext db) : IFerramentasAtendimento
{
    public async Task<IReadOnlyList<EspecieEncontrada>> BuscarEspecieAsync(string texto, CancellationToken ct)
    {
        var termos = Normalizar(texto).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 3).ToArray();
        if (termos.Length == 0)
            return [];

        // Catálogo de uma loja é pequeno: a comparação sem acento é feita em memória.
        var especies = await db.Especies.AsNoTracking().Where(e => e.Ativo).ToListAsync(ct);
        var disponivel = Saldos.DisponivelPorEspecie(await Saldos.PorLoteAsync(db, ct));

        return especies
            .Select(e => new { e, Nome = Normalizar($"{e.NomePopular} {e.NomeCientifico}") })
            .Where(x => termos.Any(t => x.Nome.Contains(t) || x.Nome.Split(' ').Any(p => p.Length >= 3 && t.StartsWith(p))))
            .OrderByDescending(x => termos.Count(t => x.Nome.Contains(t)))
            .Take(5)
            .Select(x => new EspecieEncontrada(x.e.Id, x.e.NomePopular, x.e.NomeCientifico, x.e.TamanhoCm, x.e.Preco, disponivel.GetValueOrDefault(x.e.Id)))
            .ToList();
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
