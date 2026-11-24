using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistemaVendas.Infrastructure.Assinaturas;

namespace SistemaVendas.Workers;

/// <summary>
/// De hora em hora confere as lojas que pagam e manda os e-mails de cobrança (teste acabando, mensalidade em aberto,
/// bloqueio) e de uso perto do limite do plano. Cada aviso sai uma vez só.
/// </summary>
public class AvisosAssinaturaWorker(IServiceScopeFactory scopes, ILogger<AvisosAssinaturaWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Espera a API subir (e as migrações rodarem) antes da primeira volta.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var n = await scope.ServiceProvider.GetRequiredService<AvisosAssinatura>().VerificarTodasAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (n > 0)
                    logger.LogInformation("{N} avisos de assinatura mandados", n);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Falha no ciclo do AvisosAssinaturaWorker");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
