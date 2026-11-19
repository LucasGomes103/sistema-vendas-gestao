using SistemaVendas.Domain.Lojas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Domain.Vendas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.Entregas;

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
        var loja = await db.Lojas.Where(l => l.Id == conversa.LojaId).Select(l => new { l.Nome, l.Segmento }).FirstAsync(ct);
        var nomeLoja = loja.Nome;

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

        var primeiraResposta = !await db.Mensagens.AnyAsync(m => m.ConversaId == conversaId && m.Direcao == DirecaoMensagem.Saida, ct);
        var resposta = await atendente.ResponderAsync(nomeLoja, falas, ferramentas, ct);
        if (primeiraResposta && !resposta.ChamarVendedor)
            resposta = resposta with { Texto = CatalogoNaPrimeiraResposta.Aplicar(resposta.Texto, await ferramentas.LinkCatalogoAsync(ct), Vocabulario.Para(loja.Segmento).Emoji) };
        var agora = DateTimeOffset.UtcNow;

        // Vendedor pode ter assumido enquanto a IA pensava: nesse caso o robô não fala.
        await db.Entry(conversa).ReloadAsync(ct);
        if (conversa.RoboPausado)
            return;

        // A IA não guarda os ids das espécies entre uma mensagem e outra: quando o cliente só manda o CEP
        // ou o endereço, ela às vezes "reserva de novo" com itens faltando e troca a reserva boa por uma menor.
        // Com reserva ativa, a mensagem de entrega não mexe na reserva.
        var ultimaDoCliente = falas.LastOrDefault(f => f.DoCliente)?.Texto;
        if (resposta.Reservar.Count > 0 && (resposta.Endereco is not null || RegrasFrete.AcharCep(ultimaDoCliente) is not null)
            && await db.Reservas.AnyAsync(r => r.ConversaId == conversa.Id && r.Status == StatusReserva.Ativa, ct))
        {
            logger.LogInformation("Conversa {Id}: reserva repetida na mensagem de entrega ignorada", conversa.Id);
            resposta = resposta with { Reservar = [] };
        }

        // Cliente pediu para separar: cria a reserva de 24 h conferindo o estoque de agora.
        if (resposta.Reservar.Count > 0)
        {
            var reservas = sp.GetRequiredService<ServicoReservas>();
            var r = await reservas.CriarAsync(conversa, resposta.Reservar, agora, ct);
            resposta = resposta with
            {
                Texto = TextoReserva.Acrescentar(resposta.Texto, r),
                ChamarVendedor = true,
                MotivoVendedor = r.Reserva is { } nova
                    ? $"{resposta.MotivoVendedor ?? "Reserva"} · Reservado: {RegrasReserva.Resumo(nova)} ({nova.Total.ToString("C", TextoReserva.PtBr)})"
                    : resposta.MotivoVendedor ?? "Cliente quer reservar (sem estoque)"
            };
        }

        // Cliente passou o endereço de entrega: fica no cadastro e preenche o pedido; o vendedor já pode fechar.
        if (resposta.Endereco is { } endereco)
        {
            var cliente = await db.Clientes.FirstAsync(c => c.Id == conversa.ClienteId, ct);
            cliente.Cep = endereco.Cep.ToString("00000000");
            cliente.Endereco = endereco.Texto;
            resposta = resposta with
            {
                ChamarVendedor = true,
                MotivoVendedor = $"{resposta.MotivoVendedor ?? conversa.MotivoVendedor ?? "Entrega"} · Endereço: {endereco.Texto}"
            };
        }

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
            // Robô pediu o CEP (e a loja calcula frete por km) ou o endereço de entrega: ele segue na
            // conversa até a resposta chegar, e só então pausa para o vendedor.
            var aguardar = AguardaEntrega.PediuEndereco(resposta.Texto, enderecoCompleto: resposta.Endereco is not null)
                || AguardaEntrega.PediuCep(resposta.Texto, falas) && await sp.GetRequiredService<ServicoFrete>().ConfigAtivaAsync(ct) is not null;
            conversa.ChamarVendedor(motivo, agora, pausarRobo: !aguardar);
            funil.Registrar(conversa, MotorFunil.AoChamarVendedor(conversa, motivo), OrigemEvento.Ia);
            logger.LogInformation("Conversa {Id} marcada como quente: {Motivo}", conversa.Id, conversa.MotivoVendedor);
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Confirmação da reserva que vai no fim da resposta do robô.</summary>
public static class TextoReserva
{
    public static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    public static string Acrescentar(string texto, ResultadoReserva r)
    {
        var linhas = new List<string> { texto.TrimEnd() };
        if (r.Reserva is { } reserva)
        {
            var itens = reserva.Itens.GroupBy(i => (i.Especie, i.PrecoUnitario))
                .Select(g => $"• {g.Sum(i => i.Quantidade)}x {g.Key.Especie} = {(g.Sum(i => i.Quantidade) * g.Key.PrecoUnitario).ToString("C", PtBr)}");
            linhas.Add("");
            linhas.Add($"✅ Separei para você por 24 h:\n{string.Join('\n', itens)}\nTotal: {reserva.Total.ToString("C", PtBr)} + frete");
        }
        if (r.Faltas.Count > 0)
        {
            linhas.Add("");
            linhas.Add($"⚠️ {string.Join("; ", r.Faltas)}.");
        }
        return string.Join('\n', linhas);
    }
}
