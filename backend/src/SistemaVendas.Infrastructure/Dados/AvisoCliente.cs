using SistemaVendas.Domain.Atendimento;

namespace SistemaVendas.Infrastructure.Dados;

/// <summary>Mensagens da loja ao cliente (pedido, pagamento, entrega), pelo mesmo outbox das conversas.</summary>
public static class AvisoCliente
{
    public const string JanelaFechada =
        "A janela de 24 h do WhatsApp fechou, então a mensagem não foi enviada. Copie o texto e mande quando o cliente escrever de novo.";

    /// <summary>Coloca as mensagens na fila. Devolve null se enviou, ou o motivo de não ter enviado. Não salva.</summary>
    public static string? Enfileirar(AppDbContext db, Conversa conversa, IEnumerable<string> textos, DateTimeOffset agora, Guid? usuarioId)
    {
        // Fora da janela, a Meta só aceita modelo aprovado.
        if (!conversa.DentroDaJanela(agora))
            return JanelaFechada;

        // Um milissegundo entre elas garante a ordem na conversa e no envio.
        var i = 0;
        foreach (var texto in textos)
            db.Mensagens.Add(new Mensagem
            {
                LojaId = conversa.LojaId,
                ConversaId = conversa.Id,
                Direcao = DirecaoMensagem.Saida,
                Autor = AutorMensagem.Vendedor,
                Conteudo = texto,
                Status = StatusMensagem.Pendente,
                Categoria = CategoriaMensagem.Servico,
                UsuarioId = usuarioId,
                CriadoEm = agora.AddMilliseconds(i++)
            });
        conversa.UltimaMensagemEm = agora;
        return null;
    }
}
