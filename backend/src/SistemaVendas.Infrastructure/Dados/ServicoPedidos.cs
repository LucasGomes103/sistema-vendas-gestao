using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Caixa;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Domain.Vendas;
using SistemaVendas.Infrastructure.Entregas;
using SistemaVendas.Infrastructure.Pagamentos;

namespace SistemaVendas.Infrastructure.Dados;

/// <summary>Sugestão para o vendedor montar o pedido: CEP e frete que o sistema já conhece do cliente.</summary>
public record SugestaoPedido(string Cliente, string? Cep, string? Endereco, decimal? Frete, string? FreteResumo, bool PixConfigurado, bool CartaoConfigurado);

/// <summary>Resultado de uma ação que tenta avisar o cliente: Enviada = false quando a janela de 24 h fechou.</summary>
public record ResultadoAviso(Pedido Pedido, bool MensagemEnviada, string? Aviso);

/// <summary>
/// Pedidos da loja atual: nascem da reserva, cobram por Pix copia e cola (confirmação manual) e, com o
/// Mercado Pago ligado, também por link de cartão (confirmação automática pelo webhook). Pagos, viram venda.
/// </summary>
public class ServicoPedidos(AppDbContext db, ServicoFunil funil, ServicoFrete frete, IMercadoPago mercadoPago)
{
    private static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

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
            // Endereço do cadastro só vale para o mesmo CEP (o robô guarda o que o cliente passou na conversa).
            cep is not { } doPedido || RegrasEndereco.ValeParaCep(cliente.Cep, cliente.Endereco, doPedido) ? cliente.Endereco : detalhe?.Endereco,
            detalhe?.Frete is { Atende: true } f ? f.Valor : null,
            detalhe?.Frete.Resumo,
            PixPronto(loja),
            await mercadoPago.ConfiguradoAsync(loja.Id, ct));
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

        var pedido = RegrasPedido.CriarDaReserva(reserva, cliente.Id, numero, entrega, usuarioId, conversa.ResponsavelId);
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
        conversa.Assumir(usuarioId: usuarioId);

        var loja = await db.Lojas.AsNoTracking().FirstAsync(l => l.Id == reserva.LojaId, ct);
        if (PixPronto(loja))
            pedido.PixCopiaECola = RegrasPix.CopiaECola(loja.PixChave!, loja.PixNome!, loja.PixCidade!, pedido.Total, $"PEDIDO{pedido.Numero}");

        var avisoCartao = await GerarLinkCartaoAsync(pedido, loja, ct);

        var resultado = new ResultadoAviso(pedido, false, avisoCartao);
        if (enviarPix)
            resultado = EnviarCobranca(pedido, conversa, loja, usuarioId) is var r && avisoCartao is not null ? r with { Aviso = r.Aviso ?? avisoCartao } : r;

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

        // Link do cartão sempre novo: o do Mercado Pago não aceita outra tentativa depois de uma que falhou.
        var avisoCartao = await GerarLinkCartaoAsync(pedido, loja, ct);

        var conversa = await db.Conversas.FirstAsync(c => c.Id == conversaId, ct);
        var resultado = EnviarCobranca(pedido, conversa, loja, usuarioId);
        await db.SaveChangesAsync(ct);
        return avisoCartao is null ? resultado : resultado with { Aviso = resultado.Aviso ?? avisoCartao };
    }

    public async Task<ResultadoAviso?> ConfirmarPagamentoAsync(Guid pedidoId, FormaPagamento forma, bool avisarCliente, Guid? usuarioId, CancellationToken ct,
        OrigemEvento origem = OrigemEvento.Usuario)
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
        // Pago: entra na agenda de entregas (ou retiradas) para combinar o dia.
        db.Entregas.Add(RegrasEntrega.DoPedido(pedido));

        var resultado = new ResultadoAviso(pedido, false, null);
        if (pedido.ConversaId is { } conversaId)
        {
            var conversa = await db.Conversas.FirstAsync(c => c.Id == conversaId, ct);
            conversa.ValorEstimado = pedido.Total;
            if (conversa.Etapa != EtapaCard.Pago)
                funil.Registrar(conversa, MotorFunil.Mover(conversa, EtapaCard.Pago, null), origem, usuarioId);
            conversa.Assumir(agora, usuarioId);
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
            // Pedido cancelado pelo vendedor: não tem mais cliente quente esperando alguém.
            conversa.EncerrarAlerta();
        }

        await db.SaveChangesAsync(ct);
        return pedido;
    }

    /// <summary>
    /// Mensagem nova do cliente com pedido em aberto. Se parece comprovante ("paguei", foto, PDF), o pedido vai
    /// para "Conferir pagamento" e o vendedor é avisado; a confirmação do pagamento é sempre manual, no painel.
    /// O cliente recebe uma resposta automática: só texto ("paguei") pede o comprovante; foto ou PDF
    /// confirma o recebimento. Não salva: quem chama salva junto.
    /// </summary>
    public async Task AoReceberDoClienteAsync(Conversa conversa, string tipo, string texto, DateTimeOffset agora, CancellationToken ct)
    {
        if (!RegrasPedido.PareceComprovante(tipo, texto))
            return;
        var pedido = await db.Pedidos
            .Where(p => p.ConversaId == conversa.Id && (p.Status == StatusPedido.AguardandoPagamento || p.Status == StatusPedido.ConferirPagamento))
            .OrderByDescending(p => p.CriadoEm).FirstOrDefaultAsync(ct);
        if (pedido is null)
            return;
        var arquivo = RegrasPedido.EhArquivo(tipo);

        if (pedido.Status == StatusPedido.ConferirPagamento)
        {
            // Já avisou que pagou por texto e agora manda o arquivo: agradece uma vez só.
            if (arquivo && pedido.ComprovanteEm is { } desde && !await ArquivoDesdeAsync(conversa.Id, desde, ct))
                Responder(conversa, RegrasPedido.MensagemComprovanteRecebido(pedido), agora);
            return;
        }

        RegrasPedido.ComprovanteRecebido(pedido, agora);
        var motivo = arquivo
            ? $"Cliente mandou o comprovante do pedido #{pedido.Numero}: conferir na conta"
            : $"Cliente diz que pagou o pedido #{pedido.Numero}: conferir na conta";
        conversa.AlertarDeNovo(motivo, agora);
        funil.Registrar(conversa, MotorFunil.Mover(conversa, EtapaCard.ConferirPagamento, null), OrigemEvento.Webhook);
        Responder(conversa, arquivo ? RegrasPedido.MensagemComprovanteRecebido(pedido) : RegrasPedido.MensagemPedirComprovante(pedido), agora);
    }

    private Task<bool> ArquivoDesdeAsync(Guid conversaId, DateTimeOffset desde, CancellationToken ct) =>
        db.Mensagens.AnyAsync(m => m.ConversaId == conversaId && m.Direcao == DirecaoMensagem.Entrada && m.CriadoEm >= desde
                                   && (m.Tipo == "image" || m.Tipo == "document"), ct);

    /// <summary>Resposta automática ao cliente, depois da mensagem dele (um segundo à frente garante a ordem).</summary>
    private void Responder(Conversa conversa, string texto, DateTimeOffset agora) =>
        AvisoCliente.Enfileirar(db, conversa, [texto], agora.AddSeconds(1), null, AutorMensagem.Robo);

    public static bool PixPronto(Domain.Lojas.Loja loja) =>
        !string.IsNullOrWhiteSpace(loja.PixChave) && !string.IsNullOrWhiteSpace(loja.PixNome) && !string.IsNullOrWhiteSpace(loja.PixCidade);

    private ResultadoAviso EnviarCobranca(Pedido pedido, Conversa conversa, Domain.Lojas.Loja loja, Guid? usuarioId)
    {
        if (pedido.PixCopiaECola is null && pedido.LinkCartao is null)
            throw new RegraException("Cadastre a chave Pix da loja em Configuração → Pix (ou ligue o cartão do Mercado Pago) para enviar a cobrança.");
        var agora = DateTimeOffset.UtcNow;
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(loja.Fuso);
        List<string> textos = [RegrasPedido.MensagemCobranca(pedido, RegrasPix.Limpar(loja.PixNome, 25), fuso)];
        if (pedido.PixCopiaECola is { } pix)
            textos.Add(pix);
        var resultado = Avisar(pedido, conversa, textos, agora, usuarioId);
        if (resultado.MensagemEnviada)
            pedido.PixEnviadoEm = agora;
        return resultado;
    }

    /// <summary>
    /// Cria o link do cartão no Mercado Pago, quando ligado. Se o Mercado Pago falhar, o pedido segue só com Pix
    /// e o vendedor recebe o aviso (devolvido aqui).
    /// </summary>
    private async Task<string?> GerarLinkCartaoAsync(Pedido pedido, Domain.Lojas.Loja loja, CancellationToken ct)
    {
        if (!await mercadoPago.ConfiguradoAsync(loja.Id, ct))
            return null;
        try
        {
            var link = await mercadoPago.CriarLinkAsync(loja.Id, pedido, loja.Nome, ct);
            pedido.LinkCartao = link.Url;
            pedido.MpOrderId = link.OrderId;
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or RegraException)
        {
            return e is RegraException
                ? e.Message
                : "O Mercado Pago não respondeu: o pedido saiu sem o link do cartão. Tente \"Enviar cobrança\" de novo daqui a pouco.";
        }
    }

    /// <summary>
    /// Aviso do Mercado Pago (order já buscada na API com o nosso token). Paga ("processed") e com valor certo:
    /// confirma o pedido sozinho (cartão), tira do estoque, lança no caixa e avisa o cliente. Se a reserva
    /// venceu, não mexe no estoque: deixa em "Conferir pagamento" e chama o vendedor. Devolve o que fez (para o log).
    /// </summary>
    public async Task<string> AoPagarNoCartaoAsync(Guid pedidoId, PagamentoMp pagamento, CancellationToken ct)
    {
        var pedido = await db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == pedidoId, ct);
        if (pedido is null)
            return "pedido não encontrado";
        if (!pagamento.Aprovado)
            return $"order {pagamento.Status}/{pagamento.StatusDetalhe}: nada a fazer";
        var idPagamento = pagamento.PagamentoId ?? pagamento.Id;
        if (pedido.MpPagamentoId == idPagamento)
            return "já estava pago";
        if (pedido.Status == StatusPedido.Pago)
        {
            // Pago por outro caminho (Pix, link antigo do cartão): o cliente pagou duas vezes. Só o vendedor estorna.
            var motivoDuplo = $"Pedido #{pedido.Numero} já estava pago e o Mercado Pago aprovou outro pagamento ({idPagamento}, {pagamento.Valor.ToString("C", PtBr)}). Estornar no Mercado Pago";
            if (pedido.ConversaId is { } c2 && await db.Conversas.FirstOrDefaultAsync(c => c.Id == c2, ct) is { } conv)
            {
                conv.AlertarDeNovo(motivoDuplo, DateTimeOffset.UtcNow);
                await db.SaveChangesAsync(ct);
            }
            return motivoDuplo;
        }

        var agora = DateTimeOffset.UtcNow;
        Conversa? conversa = pedido.ConversaId is { } cid ? await db.Conversas.FirstOrDefaultAsync(c => c.Id == cid, ct) : null;
        var reserva = pedido.ReservaId is { } rid ? await db.Reservas.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid, ct) : null;
        var valorCerto = RegrasPedido.ValorDoCartaoConfere(pedido, pagamento.Valor);

        if (pedido.Status == StatusPedido.Cancelado || reserva is not { Status: StatusReserva.Ativa } || !valorCerto)
        {
            // Dinheiro entrou, mas o sistema não pode fechar sozinho: o vendedor decide (separar de novo ou estornar).
            pedido.MpPagamentoId = pagamento.PagamentoId ?? pagamento.Id;
            var motivo = !valorCerto
                ? $"Pedido #{pedido.Numero}: Mercado Pago aprovou {pagamento.Valor.ToString("C", PtBr)} mas o total é {pedido.Total.ToString("C", PtBr)}. Conferir"
                : $"Pedido #{pedido.Numero} pago no cartão (Mercado Pago {pagamento.PagamentoId ?? pagamento.Id}), mas a reserva já tinha encerrado. Separar de novo ou estornar";
            if (pedido.Status == StatusPedido.AguardandoPagamento)
                RegrasPedido.ComprovanteRecebido(pedido, agora);
            if (conversa is not null)
            {
                conversa.AlertarDeNovo(motivo, agora);
                if (pedido.Status == StatusPedido.ConferirPagamento && conversa.Etapa != EtapaCard.ConferirPagamento)
                    funil.Registrar(conversa, MotorFunil.Mover(conversa, EtapaCard.ConferirPagamento, null), OrigemEvento.Webhook);
                Responder(conversa, RegrasPedido.MensagemCartaoForaDoPrazo(pedido), agora);
            }
            await db.SaveChangesAsync(ct);
            return motivo;
        }

        pedido.MpPagamentoId = pagamento.PagamentoId ?? pagamento.Id;
        await ConfirmarPagamentoAsync(pedido.Id, FormaPagamento.Cartao, avisarCliente: true, usuarioId: null, ct, OrigemEvento.Webhook);
        return $"pedido #{pedido.Numero} pago no cartão";
    }

    private ResultadoAviso Avisar(Pedido pedido, Conversa conversa, IEnumerable<string> textos, DateTimeOffset agora, Guid? usuarioId)
    {
        var aviso = AvisoCliente.Enfileirar(db, conversa, textos, agora, usuarioId);
        return new ResultadoAviso(pedido, aviso is null, aviso);
    }
}
