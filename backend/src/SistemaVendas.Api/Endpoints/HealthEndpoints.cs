using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Workers;

namespace SistemaVendas.Api.Endpoints;

public static class HealthEndpoints
{
    private static readonly TimeSpan LimiteWorkerParado = TimeSpan.FromMinutes(5);

    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // API, banco e último ciclo do worker. Card parado é venda perdida.
        app.MapGet("/health", async (AppDbContext db, EstadoWorkers workers, CancellationToken ct) =>
        {
            var bancoOk = await db.Database.CanConnectAsync(ct);
            var ultimoCiclo = workers.UltimoCicloFunil;
            var workerOk = ultimoCiclo is not null && DateTimeOffset.UtcNow - ultimoCiclo < LimiteWorkerParado;

            var corpo = new { api = "ok", banco = bancoOk ? "ok" : "falha", funilWorker = workerOk ? "ok" : "parado", ultimoCiclo };
            return bancoOk && workerOk ? Results.Ok(corpo) : Results.Json(corpo, statusCode: 503);
        });
    }
}
