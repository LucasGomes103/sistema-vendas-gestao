using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Caixa;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Funil;

namespace SistemaVendas.Infrastructure.Dados;

/// <summary>Resultado de uma ação na entrega: Aviso explica quando a mensagem ao cliente não saiu.</summary>
public record ResultadoEntrega(Entrega Entrega, bool MensagemEnviada, string? Aviso);

/// <summary>Entregas e retiradas da loja atual: agendar, saiu para entrega e entregue (card vai para Entregue).</summary>
public class ServicoEntregas(AppDbContext db, ServicoFunil funil)
{
    public async Task<ResultadoEntrega?> AgendarAsync(Guid id, DateOnly data, string? janela, Guid? entregadorId, bool avisar, Guid? usuarioId, CancellationToken ct)
    {
        var e = await db.Entregas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null)
            return null;
        if (entregadorId is { } ent && !await db.Usuarios.AnyAsync(u => u.Id == ent && u.Ativo, ct))
            throw new RegraException("Entregador não encontrado.");

        RegrasEntrega.Agendar(e, data, janela, entregadorId, await HojeAsync(ct));
        return await ConcluirAsync(e, avisar ? RegrasEntrega.MensagemAgendada(e) : null, usuarioId, ct);
    }

    public async Task<ResultadoEntrega?> SaiuAsync(Guid id, bool avisar, Guid? usuarioId, CancellationToken ct)
    {
        var e = await db.Entregas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null)
            return null;
        RegrasEntrega.SaiuParaEntrega(e, DateTimeOffset.UtcNow, usuarioId);
        return await ConcluirAsync(e, avisar ? RegrasEntrega.MensagemSaiu(e, await TermosAsync(ct)) : null, usuarioId, ct);
    }

    public async Task<ResultadoEntrega?> EntregueAsync(Guid id, bool avisar, Guid? usuarioId, CancellationToken ct)
    {
        var e = await db.Entregas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null)
            return null;
        RegrasEntrega.MarcarEntregue(e, DateTimeOffset.UtcNow, usuarioId);

        if (e.ConversaId is { } cid && await db.Conversas.FirstOrDefaultAsync(c => c.Id == cid, ct) is { } conversa && conversa.Etapa != EtapaCard.Entregue)
            funil.Registrar(conversa, MotorFunil.Mover(conversa, EtapaCard.Entregue, null), OrigemEvento.Usuario, usuarioId);

        return await ConcluirAsync(e, avisar ? RegrasEntrega.MensagemEntregue(e, await TermosAsync(ct)) : null, usuarioId, ct);
    }

    public async Task<DateOnly> HojeAsync(CancellationToken ct)
    {
        var loja = db.LojaAtualId;
        var fuso = await db.Lojas.AsNoTracking().Where(l => l.Id == loja).Select(l => l.Fuso).FirstAsync(ct);
        return RegrasCaixa.Hoje(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(fuso));
    }

    private async Task<Domain.Lojas.Vocabulario> TermosAsync(CancellationToken ct) =>
        Domain.Lojas.Vocabulario.Para(await db.Lojas.AsNoTracking().Where(l => l.Id == db.LojaAtualId).Select(l => l.Segmento).FirstOrDefaultAsync(ct));

    private async Task<ResultadoEntrega> ConcluirAsync(Entrega e, string? mensagem, Guid? usuarioId, CancellationToken ct)
    {
        string? aviso = null;
        if (mensagem is not null)
        {
            var conversa = e.ConversaId is { } cid ? await db.Conversas.FirstOrDefaultAsync(c => c.Id == cid, ct) : null;
            aviso = conversa is null
                ? "Pedido sem conversa no WhatsApp: avise o cliente por outro meio."
                : AvisoCliente.Enfileirar(db, conversa, [mensagem], DateTimeOffset.UtcNow, usuarioId);
        }
        await db.SaveChangesAsync(ct);
        return new ResultadoEntrega(e, mensagem is not null && aviso is null, aviso);
    }
}
