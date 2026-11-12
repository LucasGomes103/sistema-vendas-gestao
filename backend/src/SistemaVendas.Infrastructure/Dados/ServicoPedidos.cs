using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Caixa;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Domain.Vendas;
using SistemaVendas.Infrastructure.Entregas;

namespace SistemaVendas.Infrastructure.Dados;

/// <summary>Sugestão para o vendedor montar o pedido: CEP e frete que o sistema já conhece do cliente.</summary>
public record SugestaoPedido(string Cliente, string? Cep, string? Endereco, decimal? Frete, string? FreteResumo, bool PixConfigurado);

/// <summary>Resultado de uma ação que tenta avisar o cliente: Enviada = false quando a janela de 24 h fechou.</summary>
public record ResultadoAviso(Pedido Pedido, bool MensagemEnviada, string? Aviso);

/// <summary>Pedidos da loja atual: nascem da reserva, cobram por Pix copia e cola e viram venda quando pagos.</summary>
public class ServicoPedidos(AppDbContext db, ServicoFunil funil, ServicoFrete frete)
{
    public async Task<SugestaoPedido?> SugerirAsync(Guid reservaId, CancellationToken ct)
    {
        var reserva = await db.Reservas.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reservaId, ct);
        if (reserva is null)
            return null;
        var conversa = await db.Conversas.AsNoTracking().FirstAsync(c => c.Id == reserva.ConversaId, ct);
        var cliente = await db.Clientes.AsNoTracking().FirstAsync(c => c.Id == conversa.ClienteId, ct);
        var loja = await db.Lojas.AsNoTracking().FirstAsync(l => l.Id == reserva.LojaId, ct);

        // CEP: o que o cliente mandou por último na conversa; senão o do cadastro.
        var textos = await db.Mensagens.AsNoTracking()
            .Where(m => m.ConversaId == conversa.Id && m.Direcao == DirecaoMensagem.Entrada)
            .OrderByDescending(m => m.CriadoEm).Take(30).Select(m => m.Conteudo).ToListAsync(ct);
        var cep = textos.Select(RegrasFrete.AcharCep).FirstOrDefault(c => c is not null) ?? RegrasFrete.LerCep(cliente.Cep);

        DetalheFrete? detalhe = null;
        if (cep is { } numero)
        {
            try { detalhe = await frete.DetalharAsync(numero, ct); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { /* mapas fora do ar: vendedor digita */ }
        }

        return new SugestaoPedido(
            cliente.Nome ?? cliente.Telefone,
            cep is { } c ? c.ToString("00000000") : null,
            cliente.Endereco ?? detalhe?.Endereco,
            detalhe?.Frete is { Atende: true } f ? f.Valor : null,
            detalhe?.Frete.Resumo,
            PixPronto(loja));
    }

    public async Task<ResultadoAviso> CriarAsync(Guid reservaId, DadosEntrega entrega, bool enviarPix, Guid? usuarioId, CancellationToken ct)
    {
        var reserva = await db.Reservas.Include(r => r.Itens).FirstOrDefaultAsync(r => r.Id == reservaId, ct)
            ?? throw new RegraException("Reserva não encontrada.");
        if (await db.Pedidos.AnyAsync(p => p.ReservaId == reservaId && p.Status != StatusPedido.Cancelado, ct))
            throw new RegraException("Esta reserva já tem um pedido.");

        var conversa = await db.Conversas.FirstAsync(c => c.Id == reserva.ConversaId, ct);
        var cliente = await db.Clientes.FirstAsync(c => c.Id == conversa.ClienteId, ct);
        var numero = (await db.Pedidos.MaxAsync(p => (int?)p.Numero, ct) ?? 0) + 1;

        var pedido = RegrasPedido.CriarDaReserva(reserva, cliente.Id, numero, entrega, usuarioId);
        db.Pedidos.Add(pedido);

        // Dado mínimo: endereço só fica guardado quando há entrega.
        if (pedido.TipoEntrega == TipoEntrega.Entrega)
        {
            cliente.Cep = pedido.Cep ?? cliente.Cep;
            cliente.Endereco = pedido.Endereco;
        }

        conversa.ValorEstimado = pedido.Total;
        if (conversa.Etapa is not (EtapaCard.AguardandoPagamento or EtapaCard.Pago or EtapaCard.Entregue))
            funil.Registrar(conversa, MotorFunil.Mover(conversa, EtapaCard.AguardandoPagamento, null), OrigemEvento.Usuario, usuarioId);
        conversa.Assumir();

        var loja = await db.Lojas.AsNoTracking().FirstAsync(l => l.Id == reserva.LojaId, ct);
        if (PixPronto(loja))
            pedido.PixCopiaECola = RegrasPix.CopiaECola(loja.PixChave!, loja.PixNome!, loja.PixCidade!, pedido.Total, $"PEDIDO{pedido.Numero}");

        var resultado = new ResultadoAviso(pedido, false, null);
        if (enviarPix)
            resultado = EnviarCobranca(pedido, conversa, loja, usuarioId);

        await db.SaveChangesAsync(ct);
        return resultado;
    }

    public async Task<ResultadoAviso?> EnviarPixAsync(Guid pedidoId, Guid? usuarioId, CancellationToken ct)
    {
        var pedido = await db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
        if (pedido is null)
            return null;
        if (!pedido.Aberto)
            throw new RegraException("Só dá para cobrar um pedido em aberto.");
        if (pedido.ConversaId is not { } conversaId)
            throw new RegraException("Pedido sem conversa no WhatsApp: copie o Pix e mande ao cliente.");

        var loja = await db.Lojas.AsNoTracking().FirstAsync(l => l.Id == pedido.LojaId, ct);
        if (PixPronto(loja))
            pedido.PixCopiaECola = RegrasPix.CopiaECola(loja.PixChave!, loja.PixNome!, loja.PixCidade!, pedido.Total, $"PEDIDO{pedido.Numero}");

        var conversa = await db.Conversas.FirstAsync(c => c.Id == conversaId, ct);
        var resultado = EnviarCobranca(pedido, conversa, loja, usuarioId);
        await db.SaveChangesAsync(ct);
        return resultado;
    }

    public async Task<ResultadoAviso?> ConfirmarPagamentoAsync(Guid pedidoId, FormaPagamento forma, bool avisarCliente, Guid? usuarioId, CancellationToken ct)
    {
        var pedido = await db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
        if (pedido is null)
            return null;

        var agora = DateTimeOffset.UtcNow;
        // Falha antes de salvar: nada do que muda aqui chega ao banco.
        RegrasPedido.ConfirmarPagamento(pedido, forma, agora, usuarioId);
        var reserva = pedido.ReservaId is { } rid ? await db.Reservas.Include(r => r.Itens).FirstOrDefaultAsync(r => r.Id == rid, ct) : null;
        if (reserva is not { Status: StatusReserva.Ativa })
            throw new RegraException("A reserva deste pedido já foi encerrada, então o peixe pode ter voltado ao estoque. Separe de novo e monte outro pedido.");
        // A reserva vira venda: o peixe sai do estoque.
        db.Movimentos.AddRange(RegrasReserva.Encerrar(reserva, StatusReserva.Confirmada, agora, usuarioId));

        // Entra no caixa do dia (fuso da loja) na forma em que foi pago.
        var loja = await db.Lojas.AsNoTracking().FirstAsync(l => l.Id == pedido.LojaId, ct);
        db.LancamentosCaixa.Add(RegrasCaixa.DaVenda(pedido, RegrasCaixa.Hoje(agora, TimeZoneInfo.FindSystemTimeZoneById(loja.Fuso))));

        var resultado = new ResultadoAviso(pedido, false, null);
        if (pedido.ConversaId is { } conversaId)
        {
            var conversa = await db.Conversas.FirstAsync(c => c.Id == conversaId, ct);
            conversa.ValorEstimado = pedido.Total;
            if (conversa.Etapa != EtapaCard.Pago)
                funil.Registrar(conversa, MotorFunil.Mover(conversa, EtapaCard.Pago, null), OrigemEvento.Usuario, usuarioId);
            conversa.Assumir(agora);
            conversa.EncerrarAlerta(); // venda fechada: não é mais cliente quente esperando alguém
            if (avisarCliente)
                resultado = Avisar(pedido, conversa, [RegrasPedido.MensagemPago(pedido)], agora, usuarioId);
        }

        await db.SaveChangesAsync(ct);
        return resultado;
    }

    public async Task<Pedido?> CancelarAsync(Guid pedidoId, string? motivo, Guid? usuarioId, CancellationToken ct)
    {
        var pedido = await db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
        if (pedido is null)
            return null;

        var agora = DateTimeOffset.UtcNow;
        RegrasPedido.Cancelar(pedido, string.IsNullOrWhiteSpace(motivo) ? "Cancelado pelo vendedor" : motivo.Trim(), agora);

        // O peixe volta ao estoque.
        if (pedido.ReservaId is { } rid && await db.Reservas.Include(r => r.Itens).FirstOrDefaultAsync(r => r.Id == rid, ct) is { Status: StatusReserva.Ativa } reserva)
            db.Movimentos.AddRange(RegrasReserva.Encerrar(reserva, StatusReserva.Cancelada, agora, usuarioId));

        if (pedido.ConversaId is { } conversaId)
        {
            var conversa = await db.Conversas.FirstAsync(c => c.Id == conversaId, ct);
            conversa.ValorEstimado = null;
            if (conversa.Etapa is EtapaCard.AguardandoPagamento or EtapaCard.ConferirPagamento)
                funil.Registrar(conversa, MotorFunil.Mover(conversa, EtapaCard.Perdido, MotivoPerda.Desistiu), OrigemEvento.Usuario, usuarioId);
        }

        await db.SaveChangesAsync(ct);
        return pedido;
    }

    /// <summary>
    /// Mensagem nova do cliente com pedido em aberto: se parece comprovante ("paguei", foto, PDF), o pedido
    /// vai para "Conferir pagamento" e o vendedor é avisado. Não salva: quem chama salva junto.
    /// </summary>
    public async Task AoReceberDoClienteAsync(Conversa conversa, string tipo, string texto, DateTimeOffset agora, CancellationToken ct)
    {
        if (!RegrasPedido.PareceComprovante(tipo, texto))
            return;
        var pedido = await db.Pedidos
            .Where(p => p.ConversaId == conversa.Id && p.Status == StatusPedido.AguardandoPagamento)
            .OrderByDescending(p => p.CriadoEm).FirstOrDefaultAsync(ct);
        if (pedido is null || !RegrasPedido.ComprovanteRecebido(pedido, agora))
            return;

        var motivo = $"Cliente diz que pagou o pedido #{pedido.Numero}: conferir na conta";
        conversa.AlertarDeNovo(motivo, agora);
        funil.Registrar(conversa, MotorFunil.Mover(conversa, EtapaCard.ConferirPagamento, null), OrigemEvento.Webhook);
    }

    public static bool PixPronto(Domain.Lojas.Loja loja) =>
        !string.IsNullOrWhiteSpace(loja.PixChave) && !string.IsNullOrWhiteSpace(loja.PixNome) && !string.IsNullOrWhiteSpace(loja.PixCidade);

    private ResultadoAviso EnviarCobranca(Pedido pedido, Conversa conversa, Domain.Lojas.Loja loja, Guid? usuarioId)
    {
        if (pedido.PixCopiaECola is null)
            throw new RegraException("Cadastre a chave Pix da loja em Configuração → Pix para enviar a cobrança.");
        var agora = DateTimeOffset.UtcNow;
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(loja.Fuso);
        var resultado = Avisar(pedido, conversa, [RegrasPedido.MensagemCobranca(pedido, RegrasPix.Limpar(loja.PixNome, 25), fuso), pedido.PixCopiaECola], agora, usuarioId);
        if (resultado.MensagemEnviada)
            pedido.PixEnviadoEm = agora;
        return resultado;
    }

    private ResultadoAviso Avisar(Pedido pedido, Conversa conversa, IEnumerable<string> textos, DateTimeOffset agora, Guid? usuarioId)
    {
        if (!conversa.DentroDaJanela(agora))
            return new ResultadoAviso(pedido, false,
                "A janela de 24 h do WhatsApp fechou, então a mensagem não foi enviada. Copie o texto e mande quando o cliente escrever de novo.");

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
        return new ResultadoAviso(pedido, true, null);
    }
}
