using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Domain.Vendas;
using SistemaVendas.Infrastructure.Pagamentos;
using SistemaVendas.Infrastructure.Seguranca;
using SistemaVendas.Infrastructure.WhatsApp;

namespace SistemaVendas.Api.Tests.Isolamento;

/// <summary>Mercado Pago falso: guarda o token de cada chamada e devolve a order que o teste mandar.</summary>
public sealed class MercadoPagoFalso : HttpMessageHandler
{
    public List<string> Tokens { get; } = [];
    public Func<string, string>? Order { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Tokens.Add(request.Headers.Authorization?.Parameter ?? "");
        var id = request.RequestUri!.Segments[^1];
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Order?.Invoke(id) ?? "{}", Encoding.UTF8, "application/json")
        });
    }
}

/// <summary>
/// Cada loja usa o próprio token do Mercado Pago e do WhatsApp. O token do servidor (Render) vale só para a loja
/// original (a mais antiga, A); uma loja nova nunca cai na conta dela.
/// </summary>
public class IntegracoesPorLojaTests(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private const string TokenServidor = "APP_USR-token-do-servidor-0000";
    private const string TokenLojaB = "APP_USR-token-da-loja-b-1111";
    private static readonly string Chave = Convert.ToBase64String(Enumerable.Range(10, 32).Select(i => (byte)i).ToArray());

    private WebApplicationFactory<Program> Fabrica(MercadoPagoFalso? mp = null, string? lojaOriginal = null) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseEnvironment("Testing")
            .UseSetting("Banco:MigrarAoIniciar", "false")
            .UseSetting("ConnectionStrings:Banco", banco.Conexao)
            .UseSetting("Plataforma:ChaveMestra", Chave)
            .UseSetting("MercadoPago:AccessToken", TokenServidor)
            .UseSetting("WhatsApp:Provedor", "Local")
            .UseSetting("WhatsApp:Local:LojaSlug", "loja-a")
            .UseSetting("Plataforma:LojaOriginal", lojaOriginal);
        if (mp is not null)
            b.ConfigureTestServices(s => s.AddHttpClient<IMercadoPago, MercadoPagoCliente>().ConfigurePrimaryHttpMessageHandler(() => mp));
    });

    private static string OrderPaga(string id, Guid pedidoId) =>
        $$"""{"id":"{{id}}","status":"processed","status_detail":"accredited","external_reference":"{{pedidoId:N}}","total_paid_amount":"10.00"}""";

    [FactComBanco]
    public async Task Token_do_servidor_vale_so_para_a_loja_original()
    {
        await using var app = Fabrica();
        using var scope = app.Services.CreateScope();
        var credenciais = scope.ServiceProvider.GetRequiredService<CredenciaisDaLoja>();

        Assert.Equal(banco.A.Id, await credenciais.LojaOriginalAsync(default));
        var a = await credenciais.MercadoPagoAsync(banco.A.Id, default);
        Assert.Equal(TokenServidor, a?.Valor.AccessToken);
        Assert.Equal(OrigemCredencial.Servidor, a?.Origem);

        // Loja B sem token próprio: cartão desligado, nunca o token do servidor.
        Assert.Null(await credenciais.MercadoPagoAsync(banco.B.Id, default));
        Assert.False(await scope.ServiceProvider.GetRequiredService<IMercadoPago>().ConfiguradoAsync(banco.B.Id, default));
    }

    [FactComBanco]
    public async Task Loja_original_pode_ser_escolhida_pelo_slug()
    {
        await using var app = Fabrica(lojaOriginal: "loja-b");
        using var scope = app.Services.CreateScope();
        var credenciais = scope.ServiceProvider.GetRequiredService<CredenciaisDaLoja>();
        Assert.Equal(banco.B.Id, await credenciais.LojaOriginalAsync(default));
        Assert.Null(await credenciais.MercadoPagoAsync(banco.A.Id, default));
    }

    [FactComBanco]
    public async Task Aviso_do_mercado_pago_usa_o_token_da_loja_do_pedido_e_ignora_pedido_de_outra_loja()
    {
        var mp = new MercadoPagoFalso();
        await using var app = Fabrica(mp);
        using (var scope = app.Services.CreateScope())
        {
            var cofre = scope.ServiceProvider.GetRequiredService<ServicoCredenciais>();
            await cofre.SalvarAsync(banco.B.Id, new CredencialMercadoPago(TokenLojaB), "token …1111", default);
        }
        await using (var db = banco.Criar(banco.B.Id))
        {
            var pedido = await db.Pedidos.SingleAsync(p => p.Id == banco.B.PedidoId);
            pedido.MpOrderId = "ORDB1";
            await db.SaveChangesAsync();
        }

        // Uma order da conta da loja B que aponta para o pedido da loja A: não pode confirmar nada na loja A.
        mp.Order = id => OrderPaga(id, banco.A.PedidoId);
        var client = app.CreateClient();
        var resp = await client.PostAsync("/api/v1/webhooks/mercadopago?type=order&data.id=ORDB1", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal([TokenLojaB], mp.Tokens);
        await using (var db = banco.Criar(banco.A.Id))
            Assert.NotEqual(StatusPedido.Pago, (await db.Pedidos.SingleAsync(p => p.Id == banco.A.PedidoId)).Status);

        using var scope2 = app.Services.CreateScope();
        Assert.True(await scope2.ServiceProvider.GetRequiredService<IMercadoPago>().ConfiguradoAsync(banco.B.Id, default));
        // Os outros testes desta classe contam com a loja B sem token.
        await scope2.ServiceProvider.GetRequiredService<ServicoCredenciais>().RemoverAsync<CredencialMercadoPago>(banco.B.Id, default);
    }

    [FactComBanco]
    public async Task Aviso_de_order_desconhecida_de_loja_sem_token_nao_chama_o_mercado_pago()
    {
        var mp = new MercadoPagoFalso();
        await using var app = Fabrica(mp);
        // Order que nenhum pedido guarda: só a loja original (token do servidor) pode consultá-la, nunca a B.
        var resp = await app.CreateClient().PostAsync("/api/v1/webhooks/mercadopago?type=order&data.id=ORDXYZ", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.All(mp.Tokens, t => Assert.Equal(TokenServidor, t));
    }

    [FactComBanco]
    public async Task Ponte_local_envia_so_pela_loja_dela()
    {
        await using var app = Fabrica();
        using var scope = app.Services.CreateScope();
        var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppCliente>();

        // A loja da ponte (A) sai pela ponte; a loja B, sem token da Meta, fica simulada (nunca pelo número da A).
        Assert.False(await whatsApp.SimuladoAsync(banco.A.Id, default));
        Assert.True(await whatsApp.SimuladoAsync(banco.B.Id, default));
        var wamid = await whatsApp.EnviarTextoAsync(banco.B.Id, "pn-loja-b", "5511900000002", "oi", default);
        Assert.StartsWith("sim.", wamid);
    }
}
