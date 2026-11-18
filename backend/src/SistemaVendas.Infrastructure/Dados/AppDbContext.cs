using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Caixa;
using SistemaVendas.Domain.Catalogo;
using SistemaVendas.Domain.Comum;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Domain.Vendas;

namespace SistemaVendas.Infrastructure.Dados;

public class AppDbContext(DbContextOptions<AppDbContext> options, ILojaAtual lojaAtual) : DbContext(options)
{
    public DbSet<Loja> Lojas => Set<Loja>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<ZonaEntrega> ZonasEntrega => Set<ZonaEntrega>();
    public DbSet<FretePorKm> FretesPorKm => Set<FretePorKm>();
    public DbSet<LocalCep> LocaisCep => Set<LocalCep>();
    public DbSet<Reserva> Reservas => Set<Reserva>();
    public DbSet<ItemReserva> ItensReserva => Set<ItemReserva>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<ItemDoPedido> ItensPedido => Set<ItemDoPedido>();
    public DbSet<LancamentoCaixa> LancamentosCaixa => Set<LancamentoCaixa>();
    public DbSet<FechamentoCaixa> FechamentosCaixa => Set<FechamentoCaixa>();
    public DbSet<Entrega> Entregas => Set<Entrega>();
    public DbSet<Especie> Especies => Set<Especie>();
    public DbSet<Foto> Fotos => Set<Foto>();
    public DbSet<ArquivoFoto> ArquivosFoto => Set<ArquivoFoto>();
    public DbSet<Tanque> Tanques => Set<Tanque>();
    public DbSet<Fornecedor> Fornecedores => Set<Fornecedor>();
    public DbSet<Lote> Lotes => Set<Lote>();
    public DbSet<MovimentoEstoque> Movimentos => Set<MovimentoEstoque>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Conversa> Conversas => Set<Conversa>();
    public DbSet<Mensagem> Mensagens => Set<Mensagem>();
    public DbSet<WebhookRecebido> WebhooksRecebidos => Set<WebhookRecebido>();
    public DbSet<CardEvento> CardEventos => Set<CardEvento>();
    public DbSet<ConfigFunil> ConfigsFunil => Set<ConfigFunil>();

    // Lido pelo filtro global a cada consulta.
    private Guid? LojaId => lojaAtual.LojaId;

    /// <summary>Loja da requisição (ou do worker) atual.</summary>
    public Guid? LojaAtualId => lojaAtual.LojaId;

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
        modelBuilder.Entity<ArquivoFoto>(e =>
        {
            e.ToTable("arquivo_foto");
            e.HasOne<Foto>().WithOne().HasForeignKey<ArquivoFoto>(x => x.FotoId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.FotoId).IsUnique();
        });
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

        modelBuilder.Entity<Cliente>(e =>
        {
            e.ToTable("cliente");
            e.HasIndex(x => new { x.LojaId, x.Telefone }).IsUnique();
            e.Property(x => x.Telefone).HasMaxLength(20);
        });

        modelBuilder.Entity<Conversa>(e =>
        {
            e.ToTable("conversa");
            e.HasOne<Cliente>().WithMany().HasForeignKey(x => x.ClienteId);
            e.HasIndex(x => new { x.LojaId, x.UltimaMensagemEm });
            e.Property(x => x.ValorEstimado).HasPrecision(10, 2);
            // Fila do motor de regras: só cards com ação pendente e robô ativo.
            e.HasIndex(x => x.ProximaAcaoEm).HasFilter("robo_pausado = false AND proxima_acao_em IS NOT NULL");
            // Quadro kanban por loja e etapa.
            e.HasIndex(x => new { x.LojaId, x.Etapa, x.UltimaMensagemEm });
            e.HasIndex(x => x.ResponsavelId).HasFilter("responsavel_id IS NOT NULL");
        });

        modelBuilder.Entity<CardEvento>(e =>
        {
            e.ToTable("card_evento");
            e.HasOne<Conversa>().WithMany().HasForeignKey(x => x.ConversaId);
            e.HasIndex(x => new { x.LojaId, x.CriadoEm });
        });

        modelBuilder.Entity<Reserva>(e =>
        {
            e.ToTable("reserva");
            e.Property(x => x.Total).HasPrecision(10, 2);
            e.HasOne<Conversa>().WithMany().HasForeignKey(x => x.ConversaId);
            e.HasMany(x => x.Itens).WithOne().HasForeignKey(x => x.ReservaId);
            e.HasIndex(x => new { x.ConversaId, x.Status });
            // Fila de vencimento: só as ativas.
            e.HasIndex(x => x.ExpiraEm).HasFilter("status = 'Ativa'");
        });

        modelBuilder.Entity<ItemReserva>(e =>
        {
            e.ToTable("item_reserva");
            e.Property(x => x.PrecoUnitario).HasPrecision(10, 2);
            e.HasOne<Lote>().WithMany().HasForeignKey(x => x.LoteId);
        });

        modelBuilder.Entity<Pedido>(e =>
        {
            e.ToTable("pedido");
            e.Property(x => x.Subtotal).HasPrecision(10, 2);
            e.Property(x => x.Frete).HasPrecision(10, 2);
            e.Property(x => x.Total).HasPrecision(10, 2);
            e.Property(x => x.Cep).HasMaxLength(8);
            e.Property(x => x.PixCopiaECola).HasMaxLength(512);
            e.Property(x => x.LinkCartao).HasMaxLength(512);
            e.Property(x => x.MpOrderId).HasMaxLength(64);
            e.Property(x => x.MpPagamentoId).HasMaxLength(64);
            e.Ignore(x => x.Aberto);
            e.HasIndex(x => new { x.LojaId, x.Numero }).IsUnique();
            e.HasIndex(x => x.VendedorId);
            e.HasIndex(x => new { x.LojaId, x.Status, x.CriadoEm });
            e.HasIndex(x => x.ConversaId);
            e.HasIndex(x => x.ReservaId);
            e.HasOne<Cliente>().WithMany().HasForeignKey(x => x.ClienteId);
            e.HasOne<Conversa>().WithMany().HasForeignKey(x => x.ConversaId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Reserva>().WithMany().HasForeignKey(x => x.ReservaId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Itens).WithOne().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItemDoPedido>(e =>
        {
            e.ToTable("item_pedido");
            e.Property(x => x.PrecoUnitario).HasPrecision(10, 2);
            e.HasOne<Especie>().WithMany().HasForeignKey(x => x.EspecieId);
        });

        modelBuilder.Entity<LancamentoCaixa>(e =>
        {
            e.ToTable("lancamento_caixa");
            e.Property(x => x.Valor).HasPrecision(12, 2);
            e.Property(x => x.Categoria).HasMaxLength(60);
            e.HasIndex(x => new { x.LojaId, x.Data });
            // Um pedido entra no caixa uma vez só.
            e.HasIndex(x => x.PedidoId).IsUnique().HasFilter("pedido_id IS NOT NULL");
            e.HasOne<Pedido>().WithMany().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FechamentoCaixa>(e =>
        {
            e.ToTable("fechamento_caixa");
            e.Property(x => x.Entradas).HasPrecision(12, 2);
            e.Property(x => x.Saidas).HasPrecision(12, 2);
            e.Property(x => x.Saldo).HasPrecision(12, 2);
            e.Property(x => x.DinheiroEsperado).HasPrecision(12, 2);
            e.Property(x => x.DinheiroContado).HasPrecision(12, 2);
            e.Property(x => x.Diferenca).HasPrecision(12, 2);
            e.HasIndex(x => new { x.LojaId, x.Data }).IsUnique();
        });

        modelBuilder.Entity<Entrega>(e =>
        {
            e.ToTable("entrega");
            e.Property(x => x.Janela).HasMaxLength(40);
            e.Property(x => x.Cep).HasMaxLength(8);
            e.Ignore(x => x.Aberta);
            e.HasIndex(x => x.PedidoId).IsUnique();
            e.HasIndex(x => new { x.LojaId, x.Status, x.Data });
            e.HasOne<Pedido>().WithMany().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.EntregadorId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ZonaEntrega>(e =>
        {
            e.ToTable("zona_entrega");
            e.Property(x => x.Valor).HasPrecision(10, 2);
            e.HasIndex(x => new { x.LojaId, x.CepInicio });
        });

        modelBuilder.Entity<FretePorKm>(e =>
        {
            e.ToTable("frete_por_km");
            e.HasIndex(x => x.LojaId).IsUnique();
            e.Property(x => x.KmInclusos).HasPrecision(6, 1);
            e.Property(x => x.KmMaximo).HasPrecision(6, 1);
            e.Property(x => x.ValorBase).HasPrecision(10, 2);
            e.Property(x => x.ValorPorKm).HasPrecision(10, 2);
        });

        modelBuilder.Entity<LocalCep>(e =>
        {
            e.ToTable("local_cep");
            e.HasIndex(x => x.Cep).IsUnique();
        });

        modelBuilder.Entity<ConfigFunil>(e =>
        {
            e.ToTable("config_funil");
            e.HasIndex(x => x.LojaId).IsUnique();
        });

        modelBuilder.Entity<Mensagem>(e =>
        {
            e.ToTable("mensagem");
            e.HasOne<Conversa>().WithMany().HasForeignKey(x => x.ConversaId);
            e.HasIndex(x => x.Wamid).IsUnique().HasFilter("wamid IS NOT NULL");
            e.HasIndex(x => new { x.ConversaId, x.CriadoEm });
            // Fila do outbox: só o que ainda precisa ser enviado.
            e.HasIndex(x => x.ProximaTentativaEm).HasFilter("status = 'Pendente'");
            e.Property(x => x.CustoEstimado).HasPrecision(10, 4);
        });

        modelBuilder.Entity<WebhookRecebido>(e =>
        {
            e.ToTable("webhook_recebido");
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => x.CriadoEm).HasFilter("processado_em IS NULL");
        });

        foreach (var entidade in modelBuilder.Model.GetEntityTypes().ToList())
        {
            // Enums gravados como texto para ficarem legíveis no banco.
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
        modelBuilder.Entity<Cliente>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<Conversa>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<Mensagem>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<CardEvento>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<ConfigFunil>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<ZonaEntrega>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<FretePorKm>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<Reserva>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<ItemReserva>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<Pedido>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<ItemDoPedido>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<LancamentoCaixa>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<FechamentoCaixa>().HasQueryFilter(x => x.LojaId == LojaId);
        modelBuilder.Entity<Entrega>().HasQueryFilter(x => x.LojaId == LojaId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Preenche loja_id automaticamente no que for criado durante a requisição.
        foreach (var entry in ChangeTracker.Entries<EntidadeDaLoja>().Where(e => e.State == EntityState.Added))
        {
            if (entry.Entity.LojaId == Guid.Empty && LojaId is { } loja)
                entry.Entity.LojaId = loja;
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
