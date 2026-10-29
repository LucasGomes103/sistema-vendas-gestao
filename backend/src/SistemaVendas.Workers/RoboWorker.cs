using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Workers;

/// <summary>
/// Robô de atendimento: responde as conversas com mensagem nova do cliente, a menos que um
/// vendedor tenha assumido. A resposta vai para o outbox como qualquer outra mensagem.
/// </summary>
public class RoboWorker(IServiceScopeFactory scopes, EstadoWorkers estado, ILogger<RoboWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(2);
    private const int FalasNoContexto = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo);
        do
        {
            try
            {
                await ResponderPendentesAsync(stoppingToken);
                estado.UltimoCicloRobo = DateTimeOffset.UtcNow;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Falha no ciclo do RoboWorker");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ResponderPendentesAsync(CancellationToken ct)
    {
        List<(Guid Conversa, Guid Loja)> pendentes;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            pendentes = (await db.Conversas.IgnoreQueryFilters()
                    .Where(c => c.AguardandoRobo && !c.RoboPausado)
                    .OrderBy(c => c.UltimaMsgClienteEm)
                    .Take(10)
                    .Select(c => new { c.Id, c.LojaId })
                    .ToListAsync(ct))
                .Select(c => (c.Id, c.LojaId))
                .ToList();
        }

        foreach (var (conversaId, lojaId) in pendentes)
        {
            // Um escopo por conversa, com a loja definida para o filtro global.
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<ILojaAtual>().Definir(lojaId);
            await ResponderAsync(scope.ServiceProvider, conversaId, ct);
        }
    }

    private async Task ResponderAsync(IServiceProvider sp, Guid conversaId, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var atendente = sp.GetRequiredService<IAtendenteIA>();
        var ferramentas = sp.GetRequiredService<IFerramentasAtendimento>();

        var conversa = await db.Conversas.FirstAsync(c => c.Id == conversaId, ct);
        var nomeLoja = await db.Lojas.Where(l => l.Id == conversa.LojaId).Select(l => l.Nome).FirstAsync(ct);

        var falas = (await db.Mensagens.AsNoTracking()
                .Where(m => m.ConversaId == conversaId)
                .OrderByDescending(m => m.CriadoEm)
                .Take(FalasNoContexto)
                .Select(m => new { m.Direcao, m.Conteudo })
                .ToListAsync(ct))
            .AsEnumerable()
            .Reverse()
            .Select(m => new FalaConversa(m.Direcao == DirecaoMensagem.Entrada, m.Conteudo))
            .SkipWhile(f => !f.DoCliente) // o modelo espera que a conversa comece pelo cliente
            .ToList();

        conversa.AguardandoRobo = false;
        if (falas.Count == 0)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var resposta = await atendente.ResponderAsync(nomeLoja, falas, ferramentas, ct);
        var agora = DateTimeOffset.UtcNow;

        // Vendedor pode ter assumido enquanto a IA pensava: nesse caso o robô não fala.
        await db.Entry(conversa).ReloadAsync(ct);
        if (conversa.RoboPausado)
            return;

        db.Mensagens.Add(new Mensagem
        {
            LojaId = conversa.LojaId,
            ConversaId = conversa.Id,
            Direcao = DirecaoMensagem.Saida,
            Autor = AutorMensagem.Robo,
            Conteudo = resposta.Texto,
            Status = StatusMensagem.Pendente,
            Categoria = CategoriaMensagem.Servico
        });
        conversa.UltimaMensagemEm = agora;
        conversa.AguardandoRobo = false;

        var funil = sp.GetRequiredService<ServicoFunil>();
        var cfg = await funil.ConfigAsync(conversa.LojaId, ct);
        funil.Registrar(conversa, MotorFunil.AoResponderLoja(conversa, cfg, agora), OrigemEvento.Ia);

        if (resposta.ChamarVendedor)
        {
            var motivo = resposta.MotivoVendedor ?? "Cliente quer falar com uma pessoa";
            conversa.ChamarVendedor(motivo, agora);
            funil.Registrar(conversa, MotorFunil.AoChamarVendedor(conversa, motivo), OrigemEvento.Ia);
            logger.LogInformation("Conversa {Id} marcada como quente: {Motivo}", conversa.Id, conversa.MotivoVendedor);
        }

        await db.SaveChangesAsync(ct);
    }
}
