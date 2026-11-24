using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistemaVendas.Infrastructure.Pagamentos;

namespace SistemaVendas.Workers;

/// <summary>
/// Renova os tokens do Mercado Pago das lojas conectadas pelo botão (valem 180 dias) quando faltam menos de 30 dias.
/// Roda na subida e a cada 6 horas; sem a aplicação da plataforma configurada, não faz nada.
/// </summary>
public class RenovacaoTokensWorker(IServiceScopeFactory scopes, ILogger<RenovacaoTokensWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Espera a API subir (e as migrações rodarem) antes da primeira volta.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var renovados = await scope.ServiceProvider.GetRequiredService<ConexaoMercadoPago>().RenovarVencendoAsync(stoppingToken);
                if (renovados > 0)
                    logger.LogInformation("{N} token(s) do Mercado Pago renovado(s)", renovados);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Falha ao renovar os tokens do Mercado Pago");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
