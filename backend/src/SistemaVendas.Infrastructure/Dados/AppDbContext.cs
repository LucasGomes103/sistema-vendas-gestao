using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Catalogo;
using SistemaVendas.Domain.Comum;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Lojas;

namespace SistemaVendas.Infrastructure.Dados;

public class AppDbContext(DbContextOptions<AppDbContext> options, ILojaAtual lojaAtual) : DbContext(options)
{
    public DbSet<Loja> Lojas => Set<Loja>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Especie> Especies => Set<Especie>();
    public DbSet<Foto> Fotos => Set<Foto>();
    public DbSet<Tanque> Tanques => Set<Tanque>();
    public DbSet<Fornecedor> Fornecedores => Set<Fornecedor>();
    public DbSet<Lote> Lotes => Set<Lote>();
    public DbSet<MovimentoEstoque> Movimentos => Set<MovimentoEstoque>();

    // Lido pelo filtro global a cada consulta.
    private Guid? LojaId => lojaAtual.LojaId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.Entity<Loja>(e =>
        {
            e.ToTable("loja");
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Nome).HasMaxLength(120);
            e.Property(x => x.Slug).HasMaxLength(60);
        });

        modelBuilder.Entity<Usuario>(e =>
        {
            e.ToTable("usuario");
            e.HasIndex(x => x.Email).IsUnique(); // login por e-mail, único entre lojas
        });

        modelBuilder.Entity<Especie>(e =>
        {
            e.ToTable("especie");
            e.Property(x => x.Preco).HasPrecision(10, 2);
            e.Property(x => x.TamanhoCm).HasPrecision(6, 1);
            e.HasIndex(x => x.NomePopular).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasMany(x => x.Fotos).WithOne().HasForeignKey(x => x.EspecieId);
        });

        modelBuilder.Entity<Foto>(e => e.ToTable("foto"));
        modelBuilder.Entity<Tanque>(e => e.ToTable("tanque"));
        modelBuilder.Entity<Fornecedor>(e => e.ToTable("fornecedor"));

        modelBuilder.Entity<Lote>(e =>
        {
            e.ToTable("lote");
            e.Property(x => x.CustoUnitario).HasPrecision(10, 2);
            e.HasOne<Especie>().WithMany().HasForeignKey(x => x.EspecieId);
            e.HasOne<Tanque>().WithMany().HasForeignKey(x => x.TanqueId);
            e.HasOne<Fornecedor>().WithMany().HasForeignKey(x => x.FornecedorId);
        });

        modelBuilder.Entity<MovimentoEstoque>(e =>
        {
            e.ToTable("movimento_estoque");
            e.Ignore(x => x.EfeitoNoSaldo);
            e.HasOne<Lote>().WithMany().HasForeignKey(x => x.LoteId);
            e.HasOne<Tanque>().WithMany().HasForeignKey(x => x.TanqueId).OnDelete(DeleteBehavior.Restrict);
        });

        foreach (var entidade in modelBuilder.Model.GetEntityTypes().ToList())
        {
            // Enums gravados como texto para ficarem legÃ­veis no banco.
            foreach (var prop in entidade.GetProperties().Where(p => p.ClrType.IsEnum))
                prop.SetProviderClrType(typeof(string));

            if (typeof(EntidadeDaLoja).IsAssignableFrom(entidade.ClrType))
            {
                modelBuilder.Entity(entidade.ClrType)
                    .HasOne(typeof(Loja)).WithMany()
                    .HasForeignKey(nameof(EntidadeDaLoja.LojaId))
                    .OnDelete(DeleteBehavior.Restrict);
            }
        }

        // Isolamento por loja: nenhuma consulta enxerga dados de outra loja.
        modelBuilder.Entity<Usuario>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<Especie>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<Foto>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<Tanque>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<Fornecedor>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<Lote>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<MovimentoEstoque>().HasQueryFilter(x => x.LojaId == LojaId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Preenche loja_id automaticamente no que for criado durante a requisiÃ§Ã£o.
        foreach (var entry in ChangeTracker.Entries<EntidadeDaLoja>().Where(e => e.State == EntityState.Added))
        {
            if (entry.Entity.LojaId == Guid.Empty && LojaId is { } loja)
                entry.Entity.LojaId = loja;
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
