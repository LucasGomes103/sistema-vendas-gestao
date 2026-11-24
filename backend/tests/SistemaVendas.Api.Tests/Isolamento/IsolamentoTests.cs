using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Comum;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>
/// Uma loja nunca vê nem mexe em dado de outra. É o pior erro possível num sistema com várias lojas,
/// por isso estes testes rodam no CI com um PostgreSQL de verdade.
/// </summary>
public class IsolamentoTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    [Fact]
    public void Toda_tabela_de_loja_tem_filtro_global()
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=nao_usado").UseSnakeCaseNamingConvention().Options,
            new LojaFixa(null));

        var semFiltro = db.Model.GetEntityTypes()
            .Where(t => typeof(EntidadeDaLoja).IsAssignableFrom(t.ClrType) && t.GetDeclaredQueryFilters().Count == 0)
            .Select(t => t.ClrType.Name)
            .ToList();

        Assert.Empty(semFiltro);
    }

    [FactComBanco]
    public async Task Cada_tabela_so_devolve_linhas_da_loja_atual()
    {
        await using var db = banco.Criar(banco.B.Id);
        var tipos = db.Model.GetEntityTypes().Where(t => typeof(EntidadeDaLoja).IsAssignableFrom(t.ClrType)).ToList();
        Assert.NotEmpty(tipos);

        var metodo = typeof(IsolamentoTests).GetMethod(nameof(LojasVisiveisAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var tipo in tipos)
        {
            var lojas = await (Task<List<Guid>>)metodo.MakeGenericMethod(tipo.ClrType).Invoke(null, [db])!;
            Assert.True(lojas.All(l => l == banco.B.Id), $"{tipo.ClrType.Name}: a loja B enxergou linha de outra loja.");
        }
    }

    [FactComBanco]
    public async Task Sem_loja_definida_nenhuma_tabela_devolve_nada()
    {
        await using var db = banco.Criar(null);
        Assert.Empty(await db.Especies.ToListAsync());
        Assert.Empty(await db.Pedidos.ToListAsync());
        Assert.Empty(await db.Usuarios.ToListAsync());
        Assert.Empty(await db.Conversas.ToListAsync());
        // Os dados existem, só não aparecem.
        Assert.True(await db.Especies.IgnoreQueryFilters().CountAsync() >= 2);
    }

    private static Task<List<Guid>> LojasVisiveisAsync<T>(AppDbContext db) where T : EntidadeDaLoja =>
        db.Set<T>().AsNoTracking().Select(x => x.LojaId).Distinct().ToListAsync();

    [FactComBanco]
    public async Task Painel_da_loja_B_nao_lista_dados_da_loja_A()
    {
        var client = await EntrarAsync(banco.B);
        string[] listas =
        [
            "/api/v1/especies", "/api/v1/tanques", "/api/v1/fornecedores", "/api/v1/conversas", "/api/v1/pedidos",
            "/api/v1/usuarios", "/api/v1/entregas", "/api/v1/caixa", "/api/v1/metricas", "/api/v1/conversas/atendentes",
            "/api/v1/conversas/alertas", "/api/v1/funil/cards", "/api/v1/reservas", "/api/v1/entregas/pessoas",
            "/api/v1/config/pix", "/api/v1/config/funil", "/api/v1/config/whatsapp", "/api/v1/metricas/exportar/pedidos",
            "/api/v1/config/ia"
        ];
        var deA = new[] { banco.A.Id, banco.A.EspecieId, banco.A.TanqueId, banco.A.ConversaId, banco.A.PedidoId, banco.A.UsuarioId }
            .Select(g => g.ToString()).Append("loja-a").ToArray();

        foreach (var rota in listas)
        {
            var resp = await client.GetAsync(rota);
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"{rota} respondeu {(int)resp.StatusCode}");
            var corpo = await resp.Content.ReadAsStringAsync();
            foreach (var marca in deA)
                Assert.False(corpo.Contains(marca, StringComparison.OrdinalIgnoreCase), $"{rota} mostrou dado da loja A ({marca}).");
        }
    }

    [FactComBanco]
    public async Task Painel_da_loja_B_nao_abre_nem_altera_dados_da_loja_A_pelo_id()
    {
        var client = await EntrarAsync(banco.B);
        var a = banco.A;

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/tanques/{a.TanqueId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/pedidos/{a.PedidoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/conversas/{a.ConversaId}/mensagens")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/conversas/{a.ConversaId}/mensagens", new { texto = "invasão" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/conversas/{a.ConversaId}/assumir", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/especies/{a.EspecieId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/tanques/{a.TanqueId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/usuarios/{a.UsuarioId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/pedidos/{a.PedidoId}/cancelar", new { motivo = "x" })).StatusCode);

        // Nada da loja A foi mexido.
        await using var db = banco.Criar(a.Id);
        Assert.True(await db.Especies.AnyAsync(e => e.Id == a.EspecieId));
        Assert.True(await db.Tanques.AnyAsync(t => t.Id == a.TanqueId));
        Assert.True(await db.Usuarios.AnyAsync(u => u.Id == a.UsuarioId));
        Assert.Equal(1, await db.Mensagens.CountAsync(m => m.ConversaId == a.ConversaId));
    }

    [FactComBanco]
    public async Task Vitrine_publica_mostra_so_a_loja_do_endereco()
    {
        var client = Fabrica().CreateClient();
        var corpo = await client.GetStringAsync($"/api/v1/public/{banco.B.Slug}/catalogo");
        Assert.DoesNotContain("loja-a", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(banco.A.EspecieId.ToString(), corpo);
    }

    private WebApplicationFactory<Program> Fabrica() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao));

    private async Task<HttpClient> EntrarAsync(LojaDeTeste loja)
    {
        var client = Fabrica().CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = loja.EmailDono, senha = BancoDeTeste.Senha });
        resp.EnsureSuccessStatusCode();
        return client;
    }
}
