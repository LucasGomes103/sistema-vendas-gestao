using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.WhatsApp;

namespace SistemaVendas.Workers;

/// <summary>Processa a fila webhook_recebido: o endpoint só grava e responde 200 na hora.</summary>
public class WebhookWorker(IServiceScopeFactory scopes, EstadoWorkers estado, ILogger<WebhookWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                await ProcessarFilaAsync(stoppingToken);
                estado.UltimoCicloWebhook = DateTimeOffset.UtcNow;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Falha no ciclo do WebhookWorker");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessarFilaAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pendentes = await db.WebhooksRecebidos
            .Where(w => w.ProcessadoEm == null && w.Erro == null)
            .OrderBy(w => w.CriadoEm)
            .Take(20)
            .ToListAsync(ct);

        foreach (var webhook in pendentes)
        {
            // Escopo por webhook: cada um define a própria loja.
            using var itemScope = scopes.CreateScope();
            var processador = itemScope.ServiceProvider.GetRequiredService<ProcessadorWebhook>();
            try
            {
                await processador.ProcessarAsync(webhook.Payload, ct);
                webhook.ProcessadoEm = DateTimeOffset.UtcNow;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Webhook {Id} com erro", webhook.Id);
                webhook.Erro = e.Message;
            }
            await db.SaveChangesAsync(ct);
        }
    }
}

/// <summary>
/// Outbox: nenhum caso de uso chama o WhatsApp direto. A mensagem é gravada como pendente
/// e este worker envia, atualiza o status e tenta de novo com espera crescente se falhar.
/// </summary>
public class MensagemWorker(
    IServiceScopeFactory scopes,
    WhatsAppSimulado simulado,
    IOptions<WhatsAppOptions> opcoes,
    EstadoWorkers estado,
    ILogger<MensagemWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                await EnviarPendentesAsync(stoppingToken);
                estado.UltimoCicloMensagens = DateTimeOffset.UtcNow;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Falha no ciclo do MensagemWorker");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EnviarPendentesAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Do escopo: o cliente lê a credencial de cada loja no banco.
        var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppCliente>();
        var agora = DateTimeOffset.UtcNow;

        var pendentes = await db.Mensagens.IgnoreQueryFilters()
            .Where(m => m.Status == StatusMensagem.Pendente && (m.ProximaTentativaEm == null || m.ProximaTentativaEm <= agora))
            .OrderBy(m => m.CriadoEm)
            .Take(20)
            .Join(db.Conversas.IgnoreQueryFilters(), m => m.ConversaId, c => c.Id, (m, c) => new { m, c.ClienteId, c.Simulada })
            .Join(db.Clientes.IgnoreQueryFilters(), x => x.ClienteId, c => c.Id, (x, c) => new { x.m, x.Simulada, c.Telefone })
            .Join(db.Lojas, x => x.m.LojaId, l => l.Id, (x, l) => new { Mensagem = x.m, x.Simulada, x.Telefone, l.PhoneNumberId, Loja = l })
            .ToListAsync(ct);

        foreach (var p in pendentes)
        {
            try
            {
                // Conversa do simulador nunca sai pela Meta, mesmo com o número real configurado.
                if (p.Simulada)
                {
                    p.Mensagem.Wamid = await simulado.EnviarTextoAsync(p.Mensagem.LojaId, "simulador", p.Telefone, p.Mensagem.Conteudo, ct);
                }
                else if (opcoes.Value.PonteDaLoja(p.Loja.Slug) && !p.Loja.PodeAtender(p.Telefone))
                {
                    // Trava de segurança da conexão local: nunca escreve para quem está fora da lista.
                    p.Mensagem.Status = StatusMensagem.Falhou;
                    p.Mensagem.Erro = "Número fora da lista de permitidos (Configuração > WhatsApp).";
                    await db.SaveChangesAsync(ct);
                    continue;
                }
                else
                {
                    p.Mensagem.Wamid = await whatsApp.EnviarTextoAsync(p.Mensagem.LojaId, p.PhoneNumberId ?? "", p.Telefone, p.Mensagem.Conteudo, ct);
                }
                p.Mensagem.Status = StatusMensagem.Enviada;
                p.Mensagem.Erro = null;
                p.Mensagem.ProximaTentativaEm = null;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning("Envio falhou para {Telefone}: {Erro}", RegrasAtendimento.MascararTelefone(p.Telefone), e.Message);
                RegrasAtendimento.RegistrarFalha(p.Mensagem, e.Message, DateTimeOffset.UtcNow);
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
