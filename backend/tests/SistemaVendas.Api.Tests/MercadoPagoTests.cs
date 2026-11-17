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
        Assert.False(AssinaturaMercadoPago.Valida($"ts=1704908010,v1={v1}", "abc-1", "999", "segredo"));
        Assert.False(AssinaturaMercadoPago.Valida($"ts=1704908010,v1={v1}", "abc-1", "123456", "outro"));
        Assert.False(AssinaturaMercadoPago.Valida("", "abc-1", "123456", "segredo"));
    }

    [Fact]
    public void Preferencia_so_aceita_cartao_vence_com_a_reserva_e_leva_o_pedido()
    {
        var agora = DateTimeOffset.UtcNow;
        var pedido = new Pedido { LojaId = Guid.NewGuid(), Numero = 9, Total = 132.5m, ExpiraEm = agora.AddHours(20) };
        var op = new MercadoPagoOptions { AccessToken = "APP_USR-x", UrlPublicaApi = "https://api.exemplo.com/" };

        var corpo = MercadoPagoCliente.MontarPreferencia(pedido, "ECS Fish", op, agora);
        var json = System.Text.Json.JsonSerializer.Serialize(corpo);

        Assert.Contains($"\"external_reference\":\"{pedido.LojaId}:{pedido.Id}\"", json);
        Assert.Contains("\"unit_price\":132.5", json);
        Assert.Contains("\"notification_url\":\"https://api.exemplo.com/api/v1/webhooks/mercadopago\"", json);
        Assert.Contains("bank_transfer", json); // Pix do MP fora: o Pix segue pela chave da loja
        Assert.Contains("ticket", json);
        Assert.Contains("\"expires\":true", json);
    }

    [Fact]
    public void Sem_https_nao_manda_endereco_de_aviso()
    {
        var op = new MercadoPagoOptions { AccessToken = "TEST-x", UrlPublicaApi = "http://localhost:5007" };
        var corpo = MercadoPagoCliente.MontarPreferencia(new Pedido { Numero = 1, Total = 10 }, "Loja", op, DateTimeOffset.UtcNow);

        Assert.False(corpo.ContainsKey("notification_url"));
        Assert.True(op.CredencialSandbox);
    }

    [Fact]
    public void Referencia_externa_traz_loja_e_pedido()
    {
        var (loja, pedido) = (Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal((loja, pedido), new PagamentoMp("1", "approved", null, $"{loja}:{pedido}", 10, "credit_card", 1).Pedido);
        Assert.Null(new PagamentoMp("1", "approved", null, "outra-coisa", 10, null, null).Pedido);
    }

    [Fact]
    public async Task Webhook_sem_cartao_configurado_responde_indisponivel()
    {
        var client = factory.WithWebHostBuilder(b => b
                .UseEnvironment("Testing")
                .UseSetting("ConnectionStrings:Banco", "Host=localhost;Database=nao_usado"))
            .CreateClient();

        var resposta = await client.PostAsync("/api/v1/webhooks/mercadopago?type=payment&data.id=1", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
    }
}
