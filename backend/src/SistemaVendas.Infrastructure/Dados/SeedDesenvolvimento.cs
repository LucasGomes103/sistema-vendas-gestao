using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Catalogo;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Lojas;

namespace SistemaVendas.Infrastructure.Dados;

/// <summary>Dados de exemplo para desenvolvimento local. Nomes e preços são fictícios.</summary>
public static class SeedDesenvolvimento
{
    public const string SlugDemo = "demo";

    /// <summary>Id fixo da loja demo, usado como Loja:IdPadrao em appsettings.Development.json.</summary>
    public static readonly Guid IdLojaDemo = Guid.Parse("0199f000-0000-7000-8000-000000000001");

    public static async Task ExecutarAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Lojas.AnyAsync(x => x.Slug == SlugDemo, ct))
            return;

        var loja = new Loja { Id = IdLojaDemo, Nome = "Loja Demo", Slug = SlugDemo };
        db.Lojas.Add(loja);

        var tanque = new Tanque { LojaId = loja.Id, Nome = "Tanque 1", Litros = 120 };
        var quarentena = new Tanque { LojaId = loja.Id, Nome = "Quarentena A", Tipo = TipoTanque.Quarentena, Litros = 60 };
        db.Tanques.AddRange(tanque, quarentena);

        var especies = new[]
        {
            new Especie { LojaId = loja.Id, NomePopular = "Ramirezi", NomeCientifico = "Mikrogeophagus ramirezi", TamanhoCm = 4, Preco = 35 },
            new Especie { LojaId = loja.Id, NomePopular = "Betta", NomeCientifico = "Betta splendens", TamanhoCm = 6, Preco = 25 },
            new Especie { LojaId = loja.Id, NomePopular = "Neon", NomeCientifico = "Paracheirodon innesi", TamanhoCm = 3, Preco = 6 }
        };
        db.Especies.AddRange(especies);

        foreach (var (especie, qtd) in especies.Zip(new[] { 8, 5, 40 }))
        {
            var lote = new Lote
            {
                LojaId = loja.Id,
                EspecieId = especie.Id,
                TanqueId = tanque.Id,
                QuantidadeInicial = qtd,
                Status = StatusLote.Liberado
            };
            db.Lotes.Add(lote);
            db.Movimentos.Add(new MovimentoEstoque
            {
                LojaId = loja.Id,
                LoteId = lote.Id,
                TanqueId = tanque.Id,
                Tipo = TipoMovimento.Entrada,
                Quantidade = qtd
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
