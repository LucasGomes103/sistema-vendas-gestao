using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.WhatsApp;

namespace SistemaVendas.Workers;

/// <summary>
/// Motor de regras do funil. A cada 60 s executa as ações vencidas (mover para follow-up, enviar
/// lembretes, marcar perdido) e agenda a próxima. O próprio PostgreSQL faz o papel de fila.
/// </summary>
public class FunilWorker(IServiceScopeFactory scopes, EstadoWorkers estado, ILogger<FunilWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                await ProcessarVencidosAsync(stoppingToken);
                estado.UltimoCicloFunil = DateTimeOffset.UtcNow;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Falha no ciclo do FunilWorker");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessarVencidosAsync(CancellationToken ct)
    {
        var agora = DateTimeOffset.UtcNow;
        List<(Guid Id, Guid Loja)> vencidos;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            vencidos = (await db.Conversas.IgnoreQueryFilters()
                    .Where(c => c.ProximaAcaoEm != null && c.ProximaAcaoEm <= agora && !c.RoboPausado && c.Aberta)
                    .OrderBy(c => c.ProximaAcaoEm)
                    .Take(50)
                    .Select(c => new { c.Id, c.LojaId })
                    .ToListAsync(ct))
                .Select(c => (c.Id, c.LojaId))
                .ToList();
        }

        foreach (var (id, lojaId) in vencidos)
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ILojaAtual>().Definir(lojaId);
            await ExecutarAsync(scope.ServiceProvider, id, ct);
        }
    }

    private static async Task ExecutarAsync(IServiceProvider sp, Guid conversaId, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var funil = sp.GetRequiredService<ServicoFunil>();

        var conversa = await db.Conversas.FirstAsync(c => c.Id == conversaId, ct);
        var loja = await db.Lojas.FirstAsync(l => l.Id == conversa.LojaId, ct);
        var cfg = await funil.ConfigAsync(loja.Id, ct);
        var agora = DateTimeOffset.UtcNow;

        var fuso = TimeZoneInfo.FindSystemTimeZoneById(loja.Fuso);
        var resultado = MotorFunil.Executar(conversa, cfg, agora, fuso, loja.HorarioInicio, loja.HorarioFim);
        funil.Registrar(conversa, resultado.Mudanca, OrigemEvento.Regra);

        if (resultado.Mensagem is { } texto)
        {
            // O primeiro lembrete ainda cai dentro das 24 h (serviço); o segundo é marketing.
            db.Mensagens.Add(new Mensagem
            {
                LojaId = conversa.LojaId,
                ConversaId = conversa.Id,
                Direcao = DirecaoMensagem.Saida,
                Autor = AutorMensagem.Sistema,
                Conteudo = texto,
                Status = StatusMensagem.Pendente,
                Categoria = conversa.DentroDaJanela(agora) ? CategoriaMensagem.Servico : CategoriaMensagem.Marketing
            });
            conversa.UltimaMensagemEm = agora;
        }

        await db.SaveChangesAsync(ct);
    }
}

public class EstadoWorkers
{
    public DateTimeOffset? UltimoCicloFunil { get; set; }
    public DateTimeOffset? UltimoCicloWebhook { get; set; }
    public DateTimeOffset? UltimoCicloMensagens { get; set; }
    public DateTimeOffset? UltimoCicloRobo { get; set; }
}

public static class DependencyInjection
{
    /// <summary>Na fase 1 os workers rodam no mesmo processo da API.</summary>
    public static IServiceCollection AddWorkers(this IServiceCollection services)
    {
        services.AddSingleton<EstadoWorkers>();
        services.AddScoped<ProcessadorWebhook>();
        services.AddHostedService<FunilWorker>();
        services.AddHostedService<WebhookWorker>();
        services.AddHostedService<MensagemWorker>();
        services.AddHostedService<RoboWorker>();
        return services;
    }
}
