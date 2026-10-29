using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SistemaVendas.Workers;

/// <summary>
/// Motor de regras do funil. Roda a cada 60 s e vai processar os cards com ação vencida
/// (SELECT ... FOR UPDATE SKIP LOCKED). Por enquanto só registra o ciclo, que alimenta
/// o /health; as regras entram junto com o módulo de funil.
/// </summary>
public class FunilWorker(EstadoWorkers estado, ILogger<FunilWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            estado.UltimoCicloFunil = DateTimeOffset.UtcNow;
            logger.LogDebug("Ciclo do funil executado");
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public class EstadoWorkers
{
    public DateTimeOffset? UltimoCicloFunil { get; set; }
}

public static class DependencyInjection
{
    /// <summary>Na fase 1 os workers rodam no mesmo processo da API.</summary>
    public static IServiceCollection AddWorkers(this IServiceCollection services)
    {
        services.AddSingleton<EstadoWorkers>();
        services.AddHostedService<FunilWorker>();
        return services;
    }
}
