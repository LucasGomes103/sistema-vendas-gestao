using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Caixa;
using SistemaVendas.Domain.Catalogo;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Domain.Vendas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>
/// Teste que precisa de um PostgreSQL de verdade. O servidor vem da variável TESTE_BANCO
/// (ex.: "Host=localhost;Username=postgres;Password=postgres"). No CI ela é obrigatória: sem ela o teste falha.
/// Na máquina de desenvolvimento, sem a variável, o teste é pulado.
/// </summary>
public sealed class FactComBancoAttribute : FactAttribute
{
    public FactComBancoAttribute()
    {
        if (BancoDeTeste.Servidor is null && Environment.GetEnvironmentVariable("CI") is null)
            Skip = "Defina TESTE_BANCO para rodar os testes com banco.";
    }
}

/// <summary>Loja fixa para montar um AppDbContext fora de uma requisição.</summary>
public sealed class LojaFixa(Guid? loja) : ILojaAtual
{
    private Guid? _loja = loja;
    public Guid? LojaId => _loja;
    public void Definir(Guid lojaId) => _loja = lojaId;
}

/// <summary>Dados de uma loja criada para o teste (ids das linhas que a outra loja nunca pode ver).</summary>
public record LojaDeTeste(Guid Id, string Slug, string EmailDono, Guid EspecieId, Guid TanqueId, Guid ConversaId, Guid PedidoId, Guid UsuarioId);

/// <summary>Cria um banco novo, aplica as migrações e popula duas lojas com uma linha de cada tabela.</summary>
public sealed class BancoDeTeste : IAsyncLifetime
{
    public const string Senha = "Teste@123";

    public static string? Servidor => Environment.GetEnvironmentVariable("TESTE_BANCO") is { Length: > 0 } s ? s : null;

    private string? _nome;

    public string Conexao { get; private set; } = "";
    public LojaDeTeste A { get; private set; } = null!;
    public LojaDeTeste B { get; private set; } = null!;
    public bool Disponivel => _nome is not null;

    public async Task InitializeAsync()
    {
        if (Servidor is null)
        {
            if (Environment.GetEnvironmentVariable("CI") is not null)
                throw new InvalidOperationException("No CI, TESTE_BANCO é obrigatória (serviço postgres do workflow).");
            return;
        }

        _nome = "sv_teste_" + Guid.NewGuid().ToString("N")[..12];
        var adm = new NpgsqlConnectionStringBuilder(Servidor) { Database = "postgres" };
        await using (var c = new NpgsqlConnection(adm.ConnectionString))
        {
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {_nome}", c);
            await cmd.ExecuteNonQueryAsync();
        }
        Conexao = new NpgsqlConnectionStringBuilder(Servidor) { Database = _nome }.ConnectionString;

        await using var db = Criar(null);
        await db.Database.MigrateAsync();
        A = await PopularAsync(db, "loja-a");
        B = await PopularAsync(db, "loja-b");
    }

    public async Task DisposeAsync()
    {
        if (_nome is null)
            return;
        NpgsqlConnection.ClearAllPools();
        var adm = new NpgsqlConnectionStringBuilder(Servidor) { Database = "postgres" };
        await using var c = new NpgsqlConnection(adm.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_nome} WITH (FORCE)", c);
        await cmd.ExecuteNonQueryAsync();
    }

    public AppDbContext Criar(Guid? loja) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Conexao).UseSnakeCaseNamingConvention().Options,
        new LojaFixa(loja));

    /// <summary>Uma loja com uma linha em cada tabela de negócio.</summary>
    private static async Task<LojaDeTeste> PopularAsync(AppDbContext db, string slug)
    {
        var loja = new Loja { Nome = "Loja " + slug, Slug = slug, PhoneNumberId = "pn-" + slug };
        db.Lojas.Add(loja);
        var l = loja.Id;

        var dono = new Usuario { LojaId = l, Nome = "Dono " + slug, Email = $"dono@{slug}.teste", Papel = Papel.Dono };
        dono.SenhaHash = new PasswordHasher<Usuario>().HashPassword(dono, Senha);
        db.Usuarios.Add(dono);

        var fornecedor = new Fornecedor { LojaId = l, Nome = "Fornecedor " + slug };
        var tanque = new Tanque { LojaId = l, Nome = "Tanque " + slug, Litros = 50 };
        var especie = new Especie { LojaId = l, NomePopular = "Peixe " + slug, Preco = 10 };
        var foto = new Foto { LojaId = l, EspecieId = especie.Id, Url = "https://exemplo/" + slug + ".jpg" };
        var lote = new Lote { LojaId = l, EspecieId = especie.Id, TanqueId = tanque.Id, FornecedorId = fornecedor.Id, QuantidadeInicial = 5, Status = StatusLote.Liberado };
        var movimento = new MovimentoEstoque { LojaId = l, LoteId = lote.Id, TanqueId = tanque.Id, Tipo = TipoMovimento.Entrada, Quantidade = 5 };
        db.AddRange(fornecedor, tanque, especie, foto, lote, movimento);

        var cliente = new Cliente { LojaId = l, Telefone = "5511900000" + (slug == "loja-a" ? "01" : "02") };
        var conversa = new Conversa { LojaId = l, ClienteId = cliente.Id, ResponsavelId = dono.Id };
        var mensagem = new Mensagem { LojaId = l, ConversaId = conversa.Id, Conteudo = "Olá " + slug };
        var evento = new CardEvento { LojaId = l, ConversaId = conversa.Id, ParaEtapa = default };
        var funil = new ConfigFunil { LojaId = l };
        db.AddRange(cliente, conversa, mensagem, evento, funil);

        var reserva = new Reserva { LojaId = l, ConversaId = conversa.Id };
        var itemReserva = new ItemReserva { LojaId = l, ReservaId = reserva.Id, EspecieId = especie.Id, Especie = especie.NomePopular, LoteId = lote.Id, TanqueId = tanque.Id, Quantidade = 1, PrecoUnitario = 10 };
        var pedido = new Pedido { LojaId = l, Numero = 1, ClienteId = cliente.Id, ConversaId = conversa.Id, Subtotal = 10, Total = 10, VendedorId = dono.Id };
        var itemPedido = new ItemDoPedido { LojaId = l, PedidoId = pedido.Id, EspecieId = especie.Id, Especie = especie.NomePopular, Quantidade = 1, PrecoUnitario = 10 };
        db.AddRange(reserva, itemReserva, pedido, itemPedido);

        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        db.AddRange(
            new LancamentoCaixa { LojaId = l, Tipo = TipoLancamento.Entrada, Categoria = "Outras entradas", Valor = 5, Data = hoje },
            new FechamentoCaixa { LojaId = l, Data = hoje.AddDays(-1) },
            new Entrega { LojaId = l, PedidoId = pedido.Id, ClienteId = cliente.Id, ConversaId = conversa.Id },
            new ZonaEntrega { LojaId = l, Nome = "Zona " + slug, CepInicio = 1000000, CepFim = 1999999 },
            new FretePorKm { LojaId = l },
            new UsoIa { LojaId = l, Dia = hoje, Respostas = 1, Chamadas = 1, TokensEntrada = 100, TokensSaida = 20 },
            new CredencialLoja { LojaId = l, Tipo = TipoCredencial.MercadoPago, Cifrado = "v1:teste", Resumo = "token " + slug });

        await db.SaveChangesAsync();
        return new LojaDeTeste(l, slug, dono.Email, especie.Id, tanque.Id, conversa.Id, pedido.Id, dono.Id);
    }
}
