using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Domain.Vendas;

namespace SistemaVendas.Infrastructure.Dados;

/// <summary>Reservas de 24 h da loja atual: criar (robô), confirmar ou cancelar (vendedor) e expirar (worker).</summary>
public class ServicoReservas(AppDbContext db, ServicoFunil funil)
{
    /// <summary>
    /// Separa o pedido para a conversa. Uma reserva ativa anterior da mesma conversa é cancelada antes,
    /// para o cliente que muda de ideia não segurar peixe em dobro. Não salva: quem chama salva junto.
    /// </summary>
    public async Task<ResultadoReserva> CriarAsync(Conversa conversa, IReadOnlyList<ItemPedido> pedido, DateTimeOffset agora, CancellationToken ct)
    {
        var anteriores = await db.Reservas.Include(r => r.Itens)
            .Where(r => r.ConversaId == conversa.Id && r.Status == StatusReserva.Ativa)
            .ToListAsync(ct);

        var devolvido = new Dictionary<Guid, int>();
        foreach (var anterior in anteriores)
        {
            db.Movimentos.AddRange(RegrasReserva.Encerrar(anterior, StatusReserva.Cancelada, agora, null));
            foreach (var item in anterior.Itens)
                devolvido[item.LoteId] = devolvido.GetValueOrDefault(item.LoteId) + item.Quantidade;
        }

        var ids = pedido.Select(p => p.EspecieId).Distinct().ToList();
        var especies = await db.Especies.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && e.Ativo)
            .ToDictionaryAsync(e => e.Id, e => new EspecieParaReserva(e.Id, e.NomePopular, e.Preco), ct);

        var recebidos = await db.Lotes.AsNoTracking().Where(l => ids.Contains(l.EspecieId)).ToDictionaryAsync(l => l.Id, l => l.RecebidoEm, ct);
        var saldos = (await Saldos.PorLoteAsync(db, ct))
            .Where(s => s.Status == StatusLote.Liberado && ids.Contains(s.EspecieId))
            .Select(s => new SaldoParaReserva(s.LoteId, s.EspecieId, s.TanqueId,
                s.Disponivel + devolvido.GetValueOrDefault(s.LoteId), recebidos.GetValueOrDefault(s.LoteId)))
            .ToList();

        var resultado = RegrasReserva.Criar(conversa.LojaId, conversa.Id, pedido, saldos, especies, agora);
        if (resultado.Reserva is { } reserva)
        {
            db.Reservas.Add(reserva);
            db.Movimentos.AddRange(resultado.Movimentos);
            conversa.ValorEstimado = reserva.Total;
        }
        return resultado;
    }

    /// <summary>Vendedor confirma a venda (o peixe sai do estoque e o card vai para Pago) ou cancela.</summary>
    public async Task<Reserva?> EncerrarAsync(Guid reservaId, StatusReserva status, Guid? usuarioId, CancellationToken ct)
    {
        var reserva = await db.Reservas.Include(r => r.Itens).FirstOrDefaultAsync(r => r.Id == reservaId, ct);
        if (reserva is null)
            return null;

        var agora = DateTimeOffset.UtcNow;
        db.Movimentos.AddRange(RegrasReserva.Encerrar(reserva, status, agora, usuarioId));

        var conversa = await db.Conversas.FirstAsync(c => c.Id == reserva.ConversaId, ct);
        if (status == StatusReserva.Confirmada)
        {
            conversa.ValorEstimado = reserva.Total;
            funil.Registrar(conversa, MotorFunil.Mover(conversa, EtapaCard.Pago, null), OrigemEvento.Usuario, usuarioId);
            conversa.Assumir();
        }
        else if (!await db.Reservas.AnyAsync(r => r.ConversaId == conversa.Id && r.Status == StatusReserva.Ativa && r.Id != reserva.Id, ct))
        {
            conversa.ValorEstimado = null;
        }

        await db.SaveChangesAsync(ct);
        return reserva;
    }

    /// <summary>Reserva vencida: o peixe volta ao estoque. Card que esperava pagamento vira Perdido "não pagou".</summary>
    public async Task ExpirarAsync(Guid reservaId, DateTimeOffset agora, CancellationToken ct)
    {
        var reserva = await db.Reservas.Include(r => r.Itens).FirstOrDefaultAsync(r => r.Id == reservaId, ct);
        if (reserva is null || !RegrasReserva.Vencida(reserva, agora))
            return;

        db.Movimentos.AddRange(RegrasReserva.Encerrar(reserva, StatusReserva.Expirada, agora, null));

        var conversa = await db.Conversas.FirstAsync(c => c.Id == reserva.ConversaId, ct);
        conversa.ValorEstimado = null;
        if (conversa.Etapa is EtapaCard.AguardandoPagamento)
            funil.Registrar(conversa, MotorFunil.Mover(conversa, EtapaCard.Perdido, MotivoPerda.NaoPagou), OrigemEvento.Regra);

        await db.SaveChangesAsync(ct);
    }
}
