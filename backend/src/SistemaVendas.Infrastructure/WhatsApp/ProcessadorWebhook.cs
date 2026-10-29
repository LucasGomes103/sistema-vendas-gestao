using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Infrastructure.WhatsApp;

/// <summary>Uma mensagem recebida já extraída do payload da Meta.</summary>
public record MensagemRecebida(string PhoneNumberId, string De, string? Nome, string Wamid, DateTimeOffset Em, string Tipo, string Texto);

/// <summary>Uma atualização de status (sent, delivered, read, failed) de mensagem enviada pela loja.</summary>
public record StatusRecebido(string Wamid, string Status, string? Erro);

/// <summary>
/// Lê o webhook da WhatsApp Cloud API e grava cliente, conversa e mensagem.
/// Idempotente: o wamid é único, então um reenvio da Meta não duplica nada.
/// </summary>
public class ProcessadorWebhook(AppDbContext db, ILojaAtual lojaAtual, ILogger<ProcessadorWebhook> logger)
{
    public static (List<MensagemRecebida> Mensagens, List<StatusRecebido> Status) Ler(string payload)
    {
        var mensagens = new List<MensagemRecebida>();
        var status = new List<StatusRecebido>();

        using var json = JsonDocument.Parse(payload);
        if (!json.RootElement.TryGetProperty("entry", out var entradas))
            return (mensagens, status);

        foreach (var entrada in entradas.EnumerateArray())
        foreach (var mudanca in entrada.GetProperty("changes").EnumerateArray())
        {
            if (!mudanca.TryGetProperty("value", out var valor))
                continue;

            var phoneNumberId = valor.GetProperty("metadata").GetProperty("phone_number_id").GetString()!;

            var nomes = new Dictionary<string, string?>();
            if (valor.TryGetProperty("contacts", out var contatos))
                foreach (var c in contatos.EnumerateArray())
                    nomes[c.GetProperty("wa_id").GetString()!] =
                        c.TryGetProperty("profile", out var p) && p.TryGetProperty("name", out var n) ? n.GetString() : null;

            if (valor.TryGetProperty("messages", out var msgs))
                foreach (var m in msgs.EnumerateArray())
                {
                    var de = m.GetProperty("from").GetString()!;
                    var tipo = m.GetProperty("type").GetString()!;
                    var texto = tipo switch
                    {
                        "text" => m.GetProperty("text").GetProperty("body").GetString() ?? "",
                        "button" => m.GetProperty("button").GetProperty("text").GetString() ?? "",
                        "interactive" => "[resposta de botão]",
                        _ => $"[{tipo}]"
                    };
                    var em = DateTimeOffset.FromUnixTimeSeconds(long.Parse(m.GetProperty("timestamp").GetString()!));
                    mensagens.Add(new MensagemRecebida(phoneNumberId, de, nomes.GetValueOrDefault(de), m.GetProperty("id").GetString()!, em, tipo, texto));
                }

            if (valor.TryGetProperty("statuses", out var sts))
                foreach (var s in sts.EnumerateArray())
                {
                    string? erro = null;
                    if (s.TryGetProperty("errors", out var erros) && erros.GetArrayLength() > 0)
                        erro = erros[0].TryGetProperty("title", out var t) ? t.GetString() : "erro";
                    status.Add(new StatusRecebido(s.GetProperty("id").GetString()!, s.GetProperty("status").GetString()!, erro));
                }
        }

        return (mensagens, status);
    }

    public async Task ProcessarAsync(string payload, CancellationToken ct)
    {
        var (mensagens, status) = Ler(payload);

        foreach (var m in mensagens)
            await ReceberAsync(m, ct);

        foreach (var s in status)
            await AtualizarStatusAsync(s, ct);
    }

    private async Task ReceberAsync(MensagemRecebida m, CancellationToken ct)
    {
        // O simulador do painel usa "sim-{slug}" no lugar do número real da loja.
        var simulada = m.PhoneNumberId.StartsWith(WhatsAppOptions.PrefixoSimulador);
        var slug = simulada ? m.PhoneNumberId[WhatsAppOptions.PrefixoSimulador.Length..] : null;
        var loja = await db.Lojas.FirstOrDefaultAsync(l => simulada ? l.Slug == slug : l.PhoneNumberId == m.PhoneNumberId, ct)
            ?? throw new InvalidOperationException($"Nenhuma loja com o número {m.PhoneNumberId}.");
        lojaAtual.Definir(loja.Id);

        if (await db.Mensagens.AnyAsync(x => x.Wamid == m.Wamid, ct))
            return;

        var cliente = await db.Clientes.FirstOrDefaultAsync(c => c.Telefone == m.De, ct);
        if (cliente is null)
        {
            cliente = new Cliente { LojaId = loja.Id, Telefone = m.De, Nome = m.Nome };
            db.Clientes.Add(cliente);
        }
        else if (cliente.Nome is null && m.Nome is not null)
        {
            cliente.Nome = m.Nome;
        }

        if (RegrasAtendimento.PedeParaParar(m.Texto) && cliente.AceitaMarketing)
        {
            cliente.AceitaMarketing = false;
            cliente.AceitaMarketingEm = null;
        }

        var conversa = await db.Conversas.FirstOrDefaultAsync(c => c.ClienteId == cliente.Id && c.Aberta && c.Simulada == simulada, ct);
        if (conversa is null)
        {
            conversa = new Conversa { LojaId = loja.Id, ClienteId = cliente.Id, Simulada = simulada };
            db.Conversas.Add(conversa);
        }

        conversa.UltimaMsgClienteEm = m.Em;
        conversa.UltimaMensagemEm = m.Em;
        conversa.NaoLidas++;

        db.Mensagens.Add(new Mensagem
        {
            LojaId = loja.Id,
            ConversaId = conversa.Id,
            Wamid = m.Wamid,
            Direcao = DirecaoMensagem.Entrada,
            Autor = AutorMensagem.Cliente,
            Tipo = m.Tipo,
            Conteudo = m.Texto,
            Status = StatusMensagem.Recebida,
            CriadoEm = m.Em
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Mensagem recebida de {Telefone}", RegrasAtendimento.MascararTelefone(m.De));
    }

    private async Task AtualizarStatusAsync(StatusRecebido s, CancellationToken ct)
    {
        // Status chega sem loja definida: busca pelo wamid em todas.
        var mensagem = await db.Mensagens.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Wamid == s.Wamid, ct);
        if (mensagem is null)
            return;

        var novo = s.Status switch
        {
            "sent" => StatusMensagem.Enviada,
            "delivered" => StatusMensagem.Entregue,
            "read" => StatusMensagem.Lida,
            "failed" => StatusMensagem.Falhou,
            _ => mensagem.Status
        };

        // Status pode chegar fora de ordem: nunca volta de "lida" para "entregue".
        if (novo == StatusMensagem.Falhou || novo > mensagem.Status)
        {
            mensagem.Status = novo;
            mensagem.Erro = s.Erro ?? mensagem.Erro;
            await db.SaveChangesAsync(ct);
        }
    }
}
