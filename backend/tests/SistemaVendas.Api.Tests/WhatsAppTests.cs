using System.Security.Cryptography;
using System.Text;
using SistemaVendas.Infrastructure.WhatsApp;

namespace SistemaVendas.Api.Tests;

public class WhatsAppTests
{
    // Formato do webhook da WhatsApp Cloud API (mensagem de texto + status).
    private const string Payload = """
    {
      "object": "whatsapp_business_account",
      "entry": [{
        "id": "123",
        "changes": [{
          "field": "messages",
          "value": {
            "messaging_product": "whatsapp",
            "metadata": { "display_phone_number": "5511999990000", "phone_number_id": "PNID1" },
            "contacts": [{ "profile": { "name": "Marcos" }, "wa_id": "5511987654321" }],
            "messages": [{
              "from": "5511987654321",
              "id": "wamid.ABC",
              "timestamp": "1793300000",
              "type": "text",
              "text": { "body": "Tem ramirezi?" }
            }],
            "statuses": [{ "id": "wamid.XYZ", "status": "delivered", "timestamp": "1793300001", "recipient_id": "5511987654321" }]
          }
        }]
      }]
    }
    """;

    [Fact]
    public void Le_mensagem_e_status_do_payload_da_Meta()
    {
        var (mensagens, status) = ProcessadorWebhook.Ler(Payload);

        var m = Assert.Single(mensagens);
        Assert.Equal("PNID1", m.PhoneNumberId);
        Assert.Equal("5511987654321", m.De);
        Assert.Equal("Marcos", m.Nome);
        Assert.Equal("wamid.ABC", m.Wamid);
        Assert.Equal("Tem ramirezi?", m.Texto);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793300000), m.Em);

        var s = Assert.Single(status);
        Assert.Equal("wamid.XYZ", s.Wamid);
        Assert.Equal("delivered", s.Status);
    }

    [Fact]
    public void Assinatura_valida_e_aceita()
    {
        var corpo = Encoding.UTF8.GetBytes(Payload);
        var assinatura = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("segredo"), corpo)).ToLowerInvariant();

        Assert.True(AssinaturaWebhook.Valida(assinatura, corpo, "segredo"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=nao-e-hex")]
    [InlineData("sha256=00ff")]
    public void Assinatura_ausente_ou_errada_e_recusada(string? cabecalho)
    {
        Assert.False(AssinaturaWebhook.Valida(cabecalho, Encoding.UTF8.GetBytes(Payload), "segredo"));
    }

    [Fact]
    public void Corpo_alterado_invalida_a_assinatura()
    {
        var corpo = Encoding.UTF8.GetBytes(Payload);
        var assinatura = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("segredo"), corpo));

        Assert.False(AssinaturaWebhook.Valida(assinatura, Encoding.UTF8.GetBytes(Payload + " "), "segredo"));
    }
}
