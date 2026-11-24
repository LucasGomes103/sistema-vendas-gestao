using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Domain.Vendas;
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
                await ExpirarReservasAsync(stoppingToken);
                await AvisarQuentesSemDonoAsync(stoppingToken);
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

    /// <summary>Reservas de 24 h vencidas: o peixe volta para o estoque.</summary>
    private async Task ExpirarReservasAsync(CancellationToken ct)
    {
        var agora = DateTimeOffset.UtcNow;
        List<(Guid Id, Guid Loja)> vencidas;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            vencidas = (await db.Reservas.IgnoreQueryFilters()
                    .Where(r => r.Status == StatusReserva.Ativa && r.ExpiraEm <= agora)
                    .OrderBy(r => r.ExpiraEm)
                    .Take(50)
                    .Select(r => new { r.Id, r.LojaId })
                    .ToListAsync(ct))
                .Select(r => (r.Id, r.LojaId))
                .ToList();
        }

        foreach (var (id, lojaId) in vencidas)
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ILojaAtual>().Definir(lojaId);
            await scope.ServiceProvider.GetRequiredService<ServicoReservas>().ExpirarAsync(id, agora, ct);
            logger.LogInformation("Reserva {Id} expirou: peixe de volta ao estoque", id);
        }
    }

    /// <summary>
    /// Cliente quente sem dono: aos 15 min só sobe o aviso no painel; com 1 h o painel avisa o dono e
    /// o robô diz ao cliente que um atendente já foi chamado.
    /// </summary>
    private async Task AvisarQuentesSemDonoAsync(CancellationToken ct)
    {
        var agora = DateTimeOffset.UtcNow;
        var limite = agora - LeadQuente.Lembrete;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var conversas = await db.Conversas.IgnoreQueryFilters()
            .Where(c => c.Aberta && c.ChamouVendedor && c.AtendidoEm == null && c.AvisosQuente < 2 && c.ChamouVendedorEm <= limite)
            .Take(50)
            .ToListAsync(ct);
        if (conversas.Count == 0)
            return;

        var lojaIds = conversas.Select(c => c.LojaId).Distinct().ToList();
        var lojas = await db.Lojas.Where(l => lojaIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);

        foreach (var conversa in conversas)
        {
            var loja = lojas[conversa.LojaId];
            var fuso = TimeZoneInfo.FindSystemTimeZoneById(loja.Fuso);
            var antes = conversa.AvisosQuente;
            var texto = LeadQuente.Avancar(conversa, agora, fuso, loja.HorarioInicio, loja.HorarioFim);
            if (conversa.AvisosQuente != antes)
                logger.LogInformation("Cliente quente sem dono na conversa {Id}: aviso nível {Nivel}", conversa.Id, conversa.AvisosQuente);

            if (texto is not null && conversa.DentroDaJanela(agora))
            {
                db.Mensagens.Add(new Mensagem
                {
                    LojaId = conversa.LojaId,
                    ConversaId = conversa.Id,
                    Direcao = DirecaoMensagem.Saida,
                    Autor = AutorMensagem.Robo,
                    Conteudo = texto,
                    Status = StatusMensagem.Pendente,
                    Categoria = CategoriaMensagem.Servico
                });
                conversa.UltimaMensagemEm = agora;
            }
        }

        await db.SaveChangesAsync(ct);
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
        services.AddHostedService<RenovacaoTokensWorker>();
        return services;
    }
}
