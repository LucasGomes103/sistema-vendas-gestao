using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Caixa;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Domain.Metricas;
using SistemaVendas.Domain.Vendas;

namespace SistemaVendas.Infrastructure.Dados;

public record ResumoMetricas(int Conversas, int VendasPagas, decimal Conversao, decimal Receita, decimal TicketMedio,
    int ItensVendidos, decimal Frete, int PedidosCancelados, decimal ValorCancelado);

public record PontoVendas(DateOnly Inicio, int Pedidos, decimal Receita);

public record EtapaFunilMetrica(string Etapa, int Conversas);

public record MotivoPerdaMetrica(string Motivo, int Conversas);

public record OrigemMetrica(string Origem, int Vendas, decimal Receita);

public record FormaMetrica(string Forma, int Pedidos, decimal Valor);

/// <summary>Custo e margem só existem quando todos os lotes vendidos têm custo cadastrado (CustoCompleto).</summary>
public record EspecieMetrica(Guid EspecieId, string Especie, int Quantidade, decimal Receita, decimal? Custo, decimal? Margem,
    decimal? MargemPercentual, bool CustoCompleto, int Mortes);

public record TanqueMetrica(Guid TanqueId, string Tanque, string Tipo, int SaldoInicio, int SaldoAtual, int Entradas, int Vendidos,
    int Mortes, decimal? Giro);

public record FornecedorMetrica(string Fornecedor, int Lotes, int Recebidos, int Mortes, decimal Mortalidade, decimal? CustoCompras);

public record CategoriaCaixaMetrica(string Categoria, decimal Valor);

public record CaixaMetrica(decimal Entradas, decimal Saidas, decimal Saldo, List<CategoriaCaixaMetrica> EntradasPorCategoria,
    List<CategoriaCaixaMetrica> Despesas, int DiasFechados, decimal DiferencaGaveta);

public record EntregasMetrica(int Entregues, int Retiradas, decimal? DiasAteEntregar, int EmAberto, int Atrasadas);

public record RelatorioMetricas(
    DateOnly De, DateOnly Ate, DateOnly Hoje, string Agrupamento, bool IncluiSimuladas,
    ResumoMetricas Atual, ResumoMetricas Anterior, DateOnly AnteriorDe, DateOnly AnteriorAte,
    List<PontoVendas> Vendas, List<EtapaFunilMetrica> Funil, List<MotivoPerdaMetrica> MotivosPerda,
    List<OrigemMetrica> Origens, List<FormaMetrica> Formas, List<EspecieMetrica> Especies,
    List<TanqueMetrica> Tanques, List<FornecedorMetrica> Fornecedores, CaixaMetrica Caixa, EntregasMetrica Entregas);

/// <summary>Pedido pago no período, já com a origem e o dia do pagamento no fuso da loja (para relatório e CSV).</summary>
public record PedidoPagoLinha(int Numero, DateOnly Dia, DateTimeOffset PagoEm, string Cliente, string Telefone, string Itens, decimal Subtotal,
    decimal Frete, decimal Total, string Forma, string Tipo, string Origem);

/// <summary>
/// Métricas e relatórios da loja atual. No volume atual tudo é somado em memória, como o estoque;
/// se ficar lento, as somas viram consultas agrupadas no banco.
/// </summary>
public class ServicoMetricas(AppDbContext db)
{
    public async Task<(Periodo Periodo, DateOnly Hoje, TimeZoneInfo Fuso)> PeriodoAsync(DateOnly? de, DateOnly? ate, CancellationToken ct)
    {
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(await db.Lojas.Select(l => l.Fuso).FirstAsync(ct));
        var hoje = RegrasCaixa.Hoje(DateTimeOffset.UtcNow, fuso);
        return (Periodo.Criar(de, ate, hoje), hoje, fuso);
    }

    public async Task<RelatorioMetricas> GerarAsync(DateOnly? de, DateOnly? ate, Agrupamento? agrupar, bool simuladas, CancellationToken ct)
    {
        var (p, hoje, fuso) = await PeriodoAsync(de, ate, ct);
        var anterior = p.Anterior();
        var agrupamento = agrupar ?? RegrasMetricas.AgrupamentoPadrao(p);
        var (inicio, fim) = p.EmUtc(fuso);
        var (inicioAnt, _) = anterior.EmUtc(fuso);

        // Conversas: as simuladas (testes no painel) só entram se pedido.
        var simuladasIds = simuladas ? [] : await db.Conversas.AsNoTracking().Where(c => c.Simulada).Select(c => c.Id).ToListAsync(ct);
        var conversas = await db.Conversas.AsNoTracking()
            .Where(c => c.CriadoEm >= inicioAnt && c.CriadoEm < fim && (simuladas || !c.Simulada))
            .Select(c => new { c.Id, c.CriadoEm })
            .ToListAsync(ct);
        var conversasAtual = conversas.Where(c => c.CriadoEm >= inicio).Select(c => c.Id).ToList();
        var conversasAnterior = conversas.Count - conversasAtual.Count;

        var pagosAtual = await PedidosPagosAsync(p, fuso, simuladasIds, ct);
        var pagosAnterior = await PedidosPagosAsync(anterior, fuso, simuladasIds, ct);
        var canceladosAtual = await CanceladosAsync(inicio, fim, simuladasIds, ct);
        var canceladosAnterior = await CanceladosAsync(inicioAnt, inicio, simuladasIds, ct);

        var vendas = RegrasMetricas.Grupos(p, agrupamento)
            .GroupJoin(pagosAtual.Linhas, g => g, l => RegrasMetricas.InicioDoGrupo(l.Dia, agrupamento),
                (g, ls) => new PontoVendas(g, ls.Count(), ls.Sum(l => l.Total)))
            .ToList();

        return new RelatorioMetricas(
            p.De, p.Ate, hoje, agrupamento.ToString(), simuladas,
            Resumo(conversasAtual.Count, pagosAtual, canceladosAtual),
            Resumo(conversasAnterior, pagosAnterior, canceladosAnterior),
            anterior.De, anterior.Ate,
            vendas,
            await FunilAsync(conversasAtual, ct),
            await MotivosPerdaAsync(inicio, fim, simuladasIds, ct),
            pagosAtual.Linhas.GroupBy(l => l.Origem)
                .Select(g => new OrigemMetrica(g.Key, g.Count(), g.Sum(l => l.Total))).OrderByDescending(o => o.Receita).ToList(),
            pagosAtual.Linhas.GroupBy(l => l.Forma)
                .Select(g => new FormaMetrica(g.Key, g.Count(), g.Sum(l => l.Total))).OrderByDescending(f => f.Valor).ToList(),
            await EspeciesAsync(pagosAtual.Ids, pagosAtual.Origens, inicio, fim, ct),
            await TanquesAsync(inicio, fim, ct),
            await FornecedoresAsync(inicio, fim, ct),
            await CaixaAsync(p, ct),
            await EntregasAsync(inicio, fim, hoje, ct));
    }

    private static ResumoMetricas Resumo(int conversas, PedidosPagos pagos, (int Quantidade, decimal Valor) cancelados)
    {
        var receita = pagos.Linhas.Sum(l => l.Total);
        return new ResumoMetricas(
            conversas, pagos.Linhas.Count, RegrasMetricas.Percentual(pagos.Linhas.Count, conversas), receita,
            RegrasMetricas.Media(receita, pagos.Linhas.Count), pagos.Itens, pagos.Linhas.Sum(l => l.Frete),
            cancelados.Quantidade, cancelados.Valor);
    }

    /// <summary>Ids traz os pedidos e as reservas deles: o movimento de venda guarda o id da reserva.</summary>
    private sealed record PedidosPagos(List<PedidoPagoLinha> Linhas, List<Guid> Ids, List<Guid> Origens, int Itens);

    /// <summary>Pedidos pagos no período (dia do pagamento no fuso da loja), com a origem de cada venda.</summary>
    private async Task<PedidosPagos> PedidosPagosAsync(Periodo p, TimeZoneInfo fuso, List<Guid> simuladasIds, CancellationToken ct)
    {
        var (inicio, fim) = p.EmUtc(fuso);
        var pedidos = await db.Pedidos.AsNoTracking().Include(x => x.Itens)
            .Where(x => x.Status == StatusPedido.Pago && x.PagoEm >= inicio && x.PagoEm < fim)
            .Where(x => x.ConversaId == null || !simuladasIds.Contains(x.ConversaId.Value))
            .OrderBy(x => x.PagoEm)
            .ToListAsync(ct);

        var clienteIds = pedidos.Select(x => x.ClienteId).Distinct().ToList();
        var clientes = await db.Clientes.AsNoTracking().Where(c => clienteIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.Nome, c.Telefone), ct);

        var conversaIds = pedidos.Select(x => x.ConversaId).OfType<Guid>().Distinct().ToList();
        var followups = await db.Conversas.AsNoTracking().Where(c => conversaIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.FollowupsEnviados, ct);
        var daVitrine = (await db.Mensagens.AsNoTracking()
                .Where(m => conversaIds.Contains(m.ConversaId) && m.Direcao == DirecaoMensagem.Entrada
                            && EF.Functions.ILike(m.Conteudo, "%" + PedidoCatalogo.Marcador + "%"))
                .Select(m => m.ConversaId).Distinct().ToListAsync(ct))
            .ToHashSet();

        var linhas = pedidos.Select(x =>
        {
            var (nome, telefone) = clientes.GetValueOrDefault(x.ClienteId);
            var origem = RegrasMetricas.Origem(
                x.ConversaId is not null,
                x.ConversaId is { } c1 && daVitrine.Contains(c1),
                x.ConversaId is { } c2 ? followups.GetValueOrDefault(c2) : 0);
            return new PedidoPagoLinha(
                x.Numero, RegrasCaixa.Hoje(x.PagoEm!.Value, fuso), x.PagoEm.Value, nome ?? "", telefone ?? "", RegrasPedido.Resumo(x),
                x.Subtotal, x.Frete, x.Total, RotuloForma(x.FormaPagamento), x.TipoEntrega == TipoEntrega.Entrega ? "Entrega" : "Retirada", origem);
        }).ToList();

        return new PedidosPagos(linhas, pedidos.Select(x => x.Id).ToList(),
            pedidos.Select(x => x.Id).Concat(pedidos.Select(x => x.ReservaId).OfType<Guid>()).ToList(), pedidos.Sum(x => x.Itens.Sum(i => i.Quantidade)));
    }

    /// <summary>Lista para o CSV de pedidos.</summary>
    public async Task<List<PedidoPagoLinha>> PedidosPagosAsync(Periodo p, TimeZoneInfo fuso, bool simuladas, CancellationToken ct)
    {
        var simuladasIds = simuladas ? [] : await db.Conversas.AsNoTracking().Where(c => c.Simulada).Select(c => c.Id).ToListAsync(ct);
        return (await PedidosPagosAsync(p, fuso, simuladasIds, ct)).Linhas;
    }

    private async Task<(int, decimal)> CanceladosAsync(DateTimeOffset inicio, DateTimeOffset fim, List<Guid> simuladasIds, CancellationToken ct)
    {
        var totais = await db.Pedidos.AsNoTracking()
            .Where(x => x.Status == StatusPedido.Cancelado && x.CanceladoEm >= inicio && x.CanceladoEm < fim)
            .Where(x => x.ConversaId == null || !simuladasIds.Contains(x.ConversaId.Value))
            .Select(x => x.Total).ToListAsync(ct);
        return (totais.Count, totais.Sum());
    }

    private async Task<List<EtapaFunilMetrica>> FunilAsync(List<Guid> conversaIds, CancellationToken ct)
    {
        var eventos = await db.CardEventos.AsNoTracking()
            .Where(e => conversaIds.Contains(e.ConversaId))
            .Select(e => new { e.ConversaId, e.ParaEtapa })
            .ToListAsync(ct);
        var porConversa = conversaIds.ToDictionary(id => id, _ => new List<EtapaCard> { EtapaCard.Novo });
        foreach (var e in eventos)
            porConversa[e.ConversaId].Add(e.ParaEtapa);
        return RegrasMetricas.Funil(porConversa.Values).Select(f => new EtapaFunilMetrica(f.Etapa.ToString(), f.Conversas)).ToList();
    }

    /// <summary>Conversas que foram para Perdido no período, pelo motivo gravado no card.</summary>
    private async Task<List<MotivoPerdaMetrica>> MotivosPerdaAsync(DateTimeOffset inicio, DateTimeOffset fim, List<Guid> simuladasIds, CancellationToken ct)
    {
        var ids = await db.CardEventos.AsNoTracking()
            .Where(e => e.ParaEtapa == EtapaCard.Perdido && e.CriadoEm >= inicio && e.CriadoEm < fim && !simuladasIds.Contains(e.ConversaId))
            .Select(e => e.ConversaId).Distinct().ToListAsync(ct);
        var motivos = await db.Conversas.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => c.MotivoPerda).ToListAsync(ct);
        return motivos.GroupBy(m => m?.ToString() ?? "SemMotivo")
            .Select(g => new MotivoPerdaMetrica(g.Key, g.Count()))
            .OrderByDescending(m => m.Conversas).ToList();
    }

    /// <summary>Mais vendidas, com custo pelos lotes de onde o peixe saiu, e as mortes do período por espécie.</summary>
    private async Task<List<EspecieMetrica>> EspeciesAsync(List<Guid> pedidoIds, List<Guid> origemIds, DateTimeOffset inicio, DateTimeOffset fim, CancellationToken ct)
    {
        var itens = await db.ItensPedido.AsNoTracking().Where(i => pedidoIds.Contains(i.PedidoId)).ToListAsync(ct);
        var saidas = await db.Movimentos.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimento.Venda && m.PedidoId != null && origemIds.Contains(m.PedidoId.Value))
            .Join(db.Lotes, m => m.LoteId, l => l.Id, (m, l) => new { l.EspecieId, m.Quantidade, l.CustoUnitario })
            .ToListAsync(ct);
        var mortes = await db.Movimentos.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimento.BaixaMorte && m.CriadoEm >= inicio && m.CriadoEm < fim)
            .Join(db.Lotes, m => m.LoteId, l => l.Id, (m, l) => new { l.EspecieId, m.Quantidade })
            .ToListAsync(ct);
        var mortesPorEspecie = mortes.GroupBy(m => m.EspecieId).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantidade));

        var especieIds = itens.Select(i => i.EspecieId).Concat(mortesPorEspecie.Keys).Distinct().ToList();
        var nomes = await db.Especies.AsNoTracking().IgnoreQueryFilters()
            .Where(e => especieIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.NomePopular, ct);

        return especieIds.Select(id =>
            {
                var vendidos = itens.Where(i => i.EspecieId == id).ToList();
                var receita = vendidos.Sum(i => i.Quantidade * i.PrecoUnitario);
                var custos = saidas.Where(s => s.EspecieId == id).ToList();
                var completo = vendidos.Count > 0 && custos.Count > 0 && custos.All(s => s.CustoUnitario is not null)
                               && custos.Sum(s => s.Quantidade) >= vendidos.Sum(i => i.Quantidade);
                decimal? custo = completo ? custos.Sum(s => s.Quantidade * s.CustoUnitario!.Value) : null;
                decimal? margem = custo is { } c ? receita - c : null;
                return new EspecieMetrica(id, nomes.GetValueOrDefault(id) ?? vendidos.FirstOrDefault()?.Especie ?? "?",
                    vendidos.Sum(i => i.Quantidade), receita, custo, margem,
                    margem is { } m ? RegrasMetricas.Percentual(m, receita) : null, completo, mortesPorEspecie.GetValueOrDefault(id));
            })
            .OrderByDescending(e => e.Quantidade).ThenByDescending(e => e.Receita).ThenBy(e => e.Especie)
            .ToList();
    }

    /// <summary>Movimento do período por tanque e o giro (vendido ÷ estoque médio).</summary>
    private async Task<List<TanqueMetrica>> TanquesAsync(DateTimeOffset inicio, DateTimeOffset fim, CancellationToken ct)
    {
        var tanques = await db.Tanques.AsNoTracking().OrderBy(t => t.Nome).ToListAsync(ct);
        var movimentos = await db.Movimentos.AsNoTracking()
            .Select(m => new { m.TanqueId, m.Tipo, m.Quantidade, m.CriadoEm }).ToListAsync(ct);

        return tanques.Select(t =>
            {
                var doTanque = movimentos.Where(m => m.TanqueId == t.Id).ToList();
                var saldoInicio = doTanque.Where(m => m.CriadoEm < inicio).Sum(m => MovimentoEstoque.Efeito(m.Tipo, m.Quantidade));
                var saldoFim = doTanque.Where(m => m.CriadoEm < fim).Sum(m => MovimentoEstoque.Efeito(m.Tipo, m.Quantidade));
                var saldoAtual = doTanque.Sum(m => MovimentoEstoque.Efeito(m.Tipo, m.Quantidade));
                var noPeriodo = doTanque.Where(m => m.CriadoEm >= inicio && m.CriadoEm < fim).ToList();
                var entradas = noPeriodo.Where(m => m.Tipo == TipoMovimento.Entrada || (m.Tipo == TipoMovimento.Transferencia && m.Quantidade > 0))
                    .Sum(m => m.Quantidade);
                var vendidos = noPeriodo.Where(m => m.Tipo == TipoMovimento.Venda).Sum(m => m.Quantidade);
                var mortes = noPeriodo.Where(m => m.Tipo == TipoMovimento.BaixaMorte).Sum(m => m.Quantidade);
                return new TanqueMetrica(t.Id, t.Nome, t.Tipo.ToString(), saldoInicio, saldoAtual, entradas, vendidos, mortes,
                    RegrasMetricas.Giro(vendidos, saldoInicio, saldoFim));
            })
            .Where(t => t.SaldoAtual != 0 || t.SaldoInicio != 0 || t.Entradas > 0 || t.Vendidos > 0 || t.Mortes > 0
                        || tanques.First(x => x.Id == t.TanqueId).Ativo)
            .ToList();
    }

    /// <summary>Lotes recebidos no período, por fornecedor: quanto chegou e quanto desse peixe morreu até agora.</summary>
    private async Task<List<FornecedorMetrica>> FornecedoresAsync(DateTimeOffset inicio, DateTimeOffset fim, CancellationToken ct)
    {
        var lotes = await db.Lotes.AsNoTracking().Where(l => l.RecebidoEm >= inicio && l.RecebidoEm < fim).ToListAsync(ct);
        var loteIds = lotes.Select(l => l.Id).ToList();
        var mortes = await db.Movimentos.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimento.BaixaMorte && loteIds.Contains(m.LoteId))
            .GroupBy(m => m.LoteId).Select(g => new { g.Key, Total = g.Sum(m => m.Quantidade) })
            .ToDictionaryAsync(x => x.Key, x => x.Total, ct);
        var fornecedorIds = lotes.Select(l => l.FornecedorId).OfType<Guid>().Distinct().ToList();
        var nomes = await db.Fornecedores.AsNoTracking().Where(f => fornecedorIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Nome, ct);

        return lotes.GroupBy(l => l.FornecedorId)
            .Select(g =>
            {
                var recebidos = g.Sum(l => l.QuantidadeInicial);
                var mortos = g.Sum(l => mortes.GetValueOrDefault(l.Id));
                var semCusto = g.Any(l => l.CustoUnitario is null);
                return new FornecedorMetrica(
                    g.Key is { } f ? nomes.GetValueOrDefault(f) ?? "?" : "Sem fornecedor", g.Count(), recebidos, mortos,
                    RegrasMetricas.Percentual(mortos, recebidos), semCusto ? null : g.Sum(l => l.QuantidadeInicial * l.CustoUnitario!.Value));
            })
            .OrderByDescending(f => f.Mortalidade).ThenByDescending(f => f.Recebidos)
            .ToList();
    }

    private async Task<CaixaMetrica> CaixaAsync(Periodo p, CancellationToken ct)
    {
        var lancamentos = await db.LancamentosCaixa.AsNoTracking().Where(l => l.Data >= p.De && l.Data <= p.Ate).ToListAsync(ct);
        var fechamentos = await db.FechamentosCaixa.AsNoTracking().Where(f => f.Data >= p.De && f.Data <= p.Ate).ToListAsync(ct);
        var entradas = lancamentos.Where(l => l.Tipo == TipoLancamento.Entrada).ToList();
        var saidas = lancamentos.Where(l => l.Tipo == TipoLancamento.Saida).ToList();
        static List<CategoriaCaixaMetrica> PorCategoria(IEnumerable<LancamentoCaixa> ls) =>
            ls.GroupBy(l => l.Categoria).Select(g => new CategoriaCaixaMetrica(g.Key, g.Sum(l => l.Valor))).OrderByDescending(c => c.Valor).ToList();
        return new CaixaMetrica(entradas.Sum(l => l.Valor), saidas.Sum(l => l.Valor), entradas.Sum(l => l.Valor) - saidas.Sum(l => l.Valor),
            PorCategoria(entradas), PorCategoria(saidas), fechamentos.Count, fechamentos.Sum(f => f.Diferenca ?? 0));
    }

    private async Task<EntregasMetrica> EntregasAsync(DateTimeOffset inicio, DateTimeOffset fim, DateOnly hoje, CancellationToken ct)
    {
        var feitas = await db.Entregas.AsNoTracking()
            .Where(e => e.Status == StatusEntrega.Entregue && e.EntregueEm >= inicio && e.EntregueEm < fim)
            .Join(db.Pedidos, e => e.PedidoId, x => x.Id, (e, x) => new { e.Tipo, e.EntregueEm, x.PagoEm })
            .ToListAsync(ct);
        var abertas = await db.Entregas.AsNoTracking().Where(e => e.Status != StatusEntrega.Entregue)
            .Select(e => e.Data).ToListAsync(ct);
        var tempos = feitas.Where(f => f.PagoEm is not null && f.EntregueEm is not null)
            .Select(f => (decimal)(f.EntregueEm!.Value - f.PagoEm!.Value).TotalDays).ToList();
        return new EntregasMetrica(
            feitas.Count(f => f.Tipo == TipoEntrega.Entrega), feitas.Count(f => f.Tipo == TipoEntrega.Retirada),
            tempos.Count == 0 ? null : Math.Round(tempos.Average(), 1),
            abertas.Count, abertas.Count(d => d is { } dia && dia < hoje));
    }

    public static string RotuloForma(FormaPagamento? f) => f switch
    {
        FormaPagamento.Dinheiro => "Dinheiro",
        FormaPagamento.Cartao => "Cartão",
        _ => "Pix"
    };
}
