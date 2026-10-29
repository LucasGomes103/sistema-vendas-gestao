using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Catalogo;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Lojas;

namespace SistemaVendas.Infrastructure.Dados;

/// <summary>Dados de exemplo para desenvolvimento local. Nomes, preços e quantidades são fictícios.</summary>
public static class SeedDesenvolvimento
{
    public const string SlugDemo = "demo";
    public static readonly Guid IdLojaDemo = Guid.Parse("0199f000-0000-7000-8000-000000000001");

    // Credenciais só de desenvolvimento local, documentadas no README.
    public const string EmailDonoDemo = "dono@demo.local";
    public const string SenhaDemo = "Demo@123";

    public static async Task ExecutarAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Lojas.AnyAsync(x => x.Slug == SlugDemo, ct))
            return;

        var loja = new Loja { Id = IdLojaDemo, Nome = "Loja Demo", Slug = SlugDemo };
        db.Lojas.Add(loja);

        var dono = new Usuario { LojaId = loja.Id, Nome = "Dono Demo", Email = EmailDonoDemo, Papel = Papel.Dono };
        dono.SenhaHash = new PasswordHasher<Usuario>().HashPassword(dono, SenhaDemo);
        db.Usuarios.Add(dono);

        var fornecedor = new Fornecedor { LojaId = loja.Id, Nome = "Fornecedor Exemplo", Telefone = "11999990000" };
        db.Fornecedores.Add(fornecedor);

        Tanque NovoTanque(string nome, int litros, TipoTanque tipo = TipoTanque.Venda) =>
            new() { LojaId = loja.Id, Nome = nome, Litros = litros, Tipo = tipo };

        Especie NovaEspecie(string popular, string cientifico, decimal tamanho, decimal preco) =>
            new() { LojaId = loja.Id, NomePopular = popular, NomeCientifico = cientifico, TamanhoCm = tamanho, Preco = preco };

        var t1 = NovoTanque("Tanque 1 · Bettas", 60);
        var t2 = NovoTanque("Tanque 2 · Corydoras", 120);
        var t3 = NovoTanque("Tanque 3 · Neons", 120);
        var t4 = NovoTanque("Tanque 4 · Ramirezi", 90);
        var t5 = NovoTanque("Tanque 5 · Discos", 200);
        var q1 = NovoTanque("Quarentena A", 60, TipoTanque.Quarentena);
        db.Tanques.AddRange(t1, t2, t3, t4, t5, q1);

        var betta = NovaEspecie("Betta halfmoon", "Betta splendens", 6, 25);
        var cory = NovaEspecie("Corydoras panda", "Corydoras panda", 4, 18);
        var neon = NovaEspecie("Neon cardinal", "Paracheirodon axelrodi", 3, 8);
        var ramirezi = NovaEspecie("Ramirezi (casal)", "Mikrogeophagus ramirezi", 4, 60);
        var disco = NovaEspecie("Disco azul", "Symphysodon aequifasciatus", 12, 180);
        var acara = NovaEspecie("Acará-bandeira", "Pterophyllum scalare", 8, 35);
        db.Especies.AddRange(betta, cory, neon, ramirezi, disco, acara);

        void Lote(Especie especie, Tanque tanque, int quantidade, int baixas = 0, StatusLote status = StatusLote.Liberado)
        {
            var lote = new Lote
            {
                LojaId = loja.Id,
                EspecieId = especie.Id,
                FornecedorId = fornecedor.Id,
                TanqueId = tanque.Id,
                QuantidadeInicial = quantidade,
                CustoUnitario = Math.Round(especie.Preco * 0.45m, 2),
                Status = status,
                QuarentenaAte = status == StatusLote.Quarentena ? DateTimeOffset.UtcNow.AddDays(5) : null
            };
            db.Lotes.Add(lote);
            db.Movimentos.Add(new MovimentoEstoque { LojaId = loja.Id, LoteId = lote.Id, TanqueId = tanque.Id, Tipo = TipoMovimento.Entrada, Quantidade = quantidade });
            if (baixas > 0)
                db.Movimentos.Add(new MovimentoEstoque { LojaId = loja.Id, LoteId = lote.Id, TanqueId = tanque.Id, Tipo = TipoMovimento.BaixaMorte, Quantidade = baixas, Observacao = "Exemplo" });
        }

        Lote(betta, t1, 12);
        Lote(cory, t2, 18);
        Lote(neon, t3, 43, baixas: 3);
        Lote(ramirezi, t4, 2);
        Lote(disco, t5, 4);
        Lote(acara, q1, 10, status: StatusLote.Quarentena);

        await db.SaveChangesAsync(ct);
    }
}
