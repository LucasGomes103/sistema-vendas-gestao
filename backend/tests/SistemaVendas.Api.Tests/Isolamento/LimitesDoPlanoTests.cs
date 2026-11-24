using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Catalogo;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Assinaturas;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>Limites do plano: usuários, produtos, recursos (cartão, entregas), conversas do mês e IA.</summary>
public class LimitesDoPlanoTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private WebApplicationFactory<Program> Fabrica() => new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
        .UseEnvironment("Testing")
        .UseSetting("Banco:MigrarAoIniciar", "false")
        .UseSetting("ConnectionStrings:Banco", banco.Conexao));

    /// <summary>Loja nova (não é a original) com assinatura em dia no plano pedido.</summary>
    private async Task<(Guid Loja, string Email)> LojaNoPlanoAsync(PlanoId plano)
    {
        await using var db = banco.Criar(null);
        var slug = "plano-" + Guid.NewGuid().ToString("N")[..8];
        var loja = new Loja { Nome = "Loja " + slug, Slug = slug };
        var dono = new Usuario { LojaId = loja.Id, Nome = "Dono", Email = $"dono@{slug}.teste", Papel = Papel.Dono };
        dono.SenhaHash = new PasswordHasher<Usuario>().HashPassword(dono, BancoDeTeste.Senha);
        db.AddRange(loja, dono, new Assinatura
        {
            LojaId = loja.Id, Plano = plano, Status = StatusAssinatura.Ativa, PagoAte = DateTimeOffset.UtcNow.AddDays(20)
        });
        await db.SaveChangesAsync();
        return (loja.Id, dono.Email);
    }

    private static async Task<HttpClient> EntrarAsync(WebApplicationFactory<Program> app, string email)
    {
        var client = app.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = BancoDeTeste.Senha })).EnsureSuccessStatusCode();
        return client;
    }

    private static object Usuario(string email, string papel = "Vendedor") =>
        new { nome = "Pessoa", email, senha = "Senha1234", papel };

    [FactComBanco]
    public async Task Essencial_limita_usuarios_e_nao_tem_cartao_nem_entregas()
    {
        var (_, email) = await LojaNoPlanoAsync(PlanoId.Essencial);
        await using var app = Fabrica();
        var dono = await EntrarAsync(app, email);

        // Dono + 1 = 2 usuários (limite do Essencial). O terceiro é recusado.
        Assert.Equal(HttpStatusCode.Created, (await dono.PostAsJsonAsync("/api/v1/usuarios", Usuario($"v1-{Guid.NewGuid():N}@x.teste"))).StatusCode);
        var terceiro = await dono.PostAsJsonAsync("/api/v1/usuarios", Usuario($"v2-{Guid.NewGuid():N}@x.teste"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, terceiro.StatusCode);
        Assert.Contains("2 usuários", await terceiro.Content.ReadAsStringAsync());

        // Sem entregas no plano: nem a agenda, nem o papel de entregador.
        var entregas = await dono.GetAsync("/api/v1/entregas");
        Assert.Equal(HttpStatusCode.Forbidden, entregas.StatusCode);
        Assert.Contains("recurso_do_plano", await entregas.Content.ReadAsStringAsync());

        // Sem cartão no plano: não salva token.
        var cartao = await dono.PutAsJsonAsync("/api/v1/config/cartao", new { accessToken = "APP_USR-1234567890123456789012", modoTeste = true });
        Assert.Equal(HttpStatusCode.Forbidden, cartao.StatusCode);

        var uso = await dono.GetFromJsonAsync<JsonElement>("/api/v1/assinatura/uso");
        Assert.Equal(2, uso.GetProperty("usuarios").GetInt32());
        Assert.Equal(2, uso.GetProperty("limiteUsuarios").GetInt32());
        Assert.Equal(500, uso.GetProperty("limiteConversas").GetInt32());
        Assert.Equal(300, uso.GetProperty("limiteProdutos").GetInt32());
        Assert.Equal(2_500, uso.GetProperty("limiteRespostasIa").GetInt32());
    }

    [FactComBanco]
    public async Task Profissional_tem_entregas_e_loja_original_nao_tem_limite()
    {
        var (_, email) = await LojaNoPlanoAsync(PlanoId.Profissional);
        await using var app = Fabrica();
        var dono = await EntrarAsync(app, email);
        Assert.Equal(HttpStatusCode.OK, (await dono.GetAsync("/api/v1/entregas")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await dono.PostAsJsonAsync("/api/v1/usuarios", Usuario($"e-{Guid.NewGuid():N}@x.teste", "Entregador"))).StatusCode);

        // A loja A é a original (a mais antiga): isenta, sem limites.
        var donoA = await EntrarAsync(app, banco.A.EmailDono);
        var usoA = await donoA.GetFromJsonAsync<JsonElement>("/api/v1/assinatura/uso");
        Assert.Equal(JsonValueKind.Null, usoA.GetProperty("limiteConversas").ValueKind);
        var assinaturaA = await donoA.GetFromJsonAsync<JsonElement>("/api/v1/assinatura");
        Assert.Equal("Isenta", assinaturaA.GetProperty("situacao").GetString());
    }

    [FactComBanco]
    public async Task Produtos_param_no_limite_do_plano()
    {
        var (loja, email) = await LojaNoPlanoAsync(PlanoId.Essencial);
        await using (var db = banco.Criar(loja))
        {
            db.Especies.AddRange(Enumerable.Range(1, 300).Select(i => new Especie { LojaId = loja, NomePopular = $"Produto {i}", Preco = 1 }));
            await db.SaveChangesAsync();
        }
        await using var app = Fabrica();
        var dono = await EntrarAsync(app, email);
        var resp = await dono.PostAsJsonAsync("/api/v1/especies", new { nomePopular = "Mais um", preco = 10 });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("300 produtos", await resp.Content.ReadAsStringAsync());
    }

    [FactComBanco]
    public async Task Robo_atende_so_os_primeiros_clientes_do_mes_dentro_do_limite()
    {
        var (loja, _) = await LojaNoPlanoAsync(PlanoId.Essencial);
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-30);
        var clientes = new List<Guid>();
        await using (var db = banco.Criar(loja))
        {
            for (var i = 0; i < 501; i++)
            {
                var cliente = new Cliente { LojaId = loja, Telefone = $"55119{i:00000000}" };
                var conversa = new Conversa { LojaId = loja, ClienteId = cliente.Id };
                db.AddRange(cliente, conversa, new Mensagem
                {
                    LojaId = loja, ConversaId = conversa.Id, Direcao = DirecaoMensagem.Entrada, Autor = AutorMensagem.Cliente,
                    Conteudo = "oi", Status = StatusMensagem.Recebida, CriadoEm = inicio.AddSeconds(i)
                });
                clientes.Add(cliente.Id);
            }
            // Simulador não conta.
            var simCliente = new Cliente { LojaId = loja, Telefone = "5511888888888" };
            var simConversa = new Conversa { LojaId = loja, ClienteId = simCliente.Id, Simulada = true };
            db.AddRange(simCliente, simConversa, new Mensagem { LojaId = loja, ConversaId = simConversa.Id, Direcao = DirecaoMensagem.Entrada, Conteudo = "teste" });
            await db.SaveChangesAsync();
        }

        await using var app = Fabrica();
        using var escopo = app.Services.CreateScope();
        var limites = escopo.ServiceProvider.GetRequiredService<ServicoLimites>();
        var agora = DateTimeOffset.UtcNow;
        Assert.Equal(501, await limites.ConversasDoMesAsync(loja, agora, CancellationToken.None));
        Assert.True(await limites.ClienteDentroDoLimiteAsync(loja, clientes[0], agora, CancellationToken.None));
        Assert.True(await limites.ClienteDentroDoLimiteAsync(loja, clientes[499], agora, CancellationToken.None));
        Assert.False(await limites.ClienteDentroDoLimiteAsync(loja, clientes[500], agora, CancellationToken.None));
    }
}
