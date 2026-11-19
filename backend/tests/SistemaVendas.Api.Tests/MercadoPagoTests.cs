using System.Security.Cryptography;
using System.Text;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SistemaVendas.Domain.Vendas;
using SistemaVendas.Infrastructure.Pagamentos;

namespace SistemaVendas.Api.Tests;

public class MercadoPagoTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static string Hmac(string segredo, string texto) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(texto)));

    [Fact]
    public void Assinatura_do_webhook_confere_com_o_manifesto_do_mercado_pago()
    {
        var v1 = Hmac("segredo", "id:123456;request-id:abc-1;ts:1704908010;");

        Assert.True(AssinaturaMercadoPago.Valida($"ts=1704908010,v1={v1}", "abc-1", "123456", "segredo"));
        // id da order (ORD...) entra em minúsculas no manifesto
        var v1Order = Hmac("segredo", "id:ord01abc;request-id:abc-1;ts:1704908010;");
        Assert.True(AssinaturaMercadoPago.Valida($"ts=1704908010,v1={v1Order}", "abc-1", "ORD01ABC", "segredo"));
        Assert.False(AssinaturaMercadoPago.Valida($"ts=1704908010,v1={v1}", "abc-1", "999", "segredo"));
        Assert.False(AssinaturaMercadoPago.Valida($"ts=1704908010,v1={v1}", "abc-1", "123456", "outro"));
        Assert.False(AssinaturaMercadoPago.Valida("", "abc-1", "123456", "segredo"));
    }

    [Fact]
    public void Order_so_aceita_cartao_vence_com_a_reserva_e_leva_o_pedido()
    {
        var agora = DateTimeOffset.UtcNow;
        var pedido = new Pedido { LojaId = Guid.NewGuid(), Numero = 9, Total = 132.5m, ExpiraEm = agora.AddHours(20).AddMinutes(30) };
        var op = new MercadoPagoOptions { AccessToken = "APP_USR-x" };

        var json = System.Text.Json.JsonSerializer.Serialize(MercadoPagoCliente.MontarOrder(pedido, "ECS Fish", op, agora));

        Assert.Contains("\"type\":\"online\"", json);
        Assert.Contains($"\"external_reference\":\"{pedido.Id:N}\"", json);
        Assert.Contains("\"total_amount\":\"132.50\"", json);
        Assert.Contains("\"unit_price\":\"132.50\"", json);
        Assert.Contains("\"not_allowed_types\":[\"ticket\"]", json);
        Assert.DoesNotContain("max_installments", json);
        Assert.Contains("\"expiration_time\":\"PT20H30M\"", json);
    }

    [Fact]
    public void Sem_pix_excluido_fica_so_o_boleto_fora()
    {
        var op = new MercadoPagoOptions { AccessToken = "APP_USR-x" };
        var json = System.Text.Json.JsonSerializer.Serialize(MercadoPagoCliente.MontarOrder(new Pedido { Numero = 1, Total = 10 }, "Loja", op, DateTimeOffset.UtcNow, excluirPix: false));

        Assert.Contains("\"not_allowed_types\":[\"ticket\"]", json);
        Assert.DoesNotContain("expiration_time", json);
    }

    [Fact]
    public void Referencia_externa_traz_o_pedido_e_processed_e_pago()
    {
        var pedido = Guid.NewGuid();

        var order = new PagamentoMp("ORD1", "processed", "accredited", pedido.ToString("N"), 10, "credit_card", 1, "PAY1");
        Assert.Equal(pedido, order.PedidoId);
        Assert.True(order.Aprovado);
        Assert.False(new PagamentoMp("ORD1", "created", "created", "x", 0, null, null).Aprovado);
        Assert.Null(new PagamentoMp("ORD1", "processed", null, "outra-coisa", 10, null, null).PedidoId);
    }

    [Fact]
    public void Url_do_webhook_para_cadastrar_no_painel()
    {
        Assert.Equal("https://api.exemplo.com/api/v1/webhooks/mercadopago", new MercadoPagoOptions { UrlPublicaApi = "https://api.exemplo.com/" }.UrlWebhook);
    }

    [Fact]
    public async Task Webhook_de_outro_topico_e_ignorado_sem_consultar_nada()
    {
        var client = factory.WithWebHostBuilder(b => b
                .UseEnvironment("Testing")
                .UseSetting("ConnectionStrings:Banco", "Host=localhost;Database=nao_usado"))
            .CreateClient();

        var resposta = await client.PostAsync("/api/v1/webhooks/mercadopago?type=payment&data.id=123", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }
}
