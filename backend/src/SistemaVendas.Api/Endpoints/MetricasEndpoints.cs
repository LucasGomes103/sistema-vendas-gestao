using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Caixa;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Metricas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Endpoints;

/// <summary>Métricas do período (vendas, funil, estoque, caixa, entregas) e os relatórios em CSV. Só o dono.</summary>
public static class MetricasEndpoints
{
    public static readonly string[] Relatorios = ["vendas", "pedidos", "especies", "estoque", "fornecedores", "caixa", "vendedores"];

    public static void MapMetricasEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/metricas").WithTags("Métricas").RequireAuthorization(AuthEndpoints.PoliticaDono);

        grupo.MapGet("/", async (DateOnly? de, DateOnly? ate, Agrupamento? agrupar, bool? simuladas, Guid? vendedorId, ServicoMetricas metricas, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await metricas.GerarAsync(de, ate, agrupar, simuladas ?? false, ct, vendedorId));
            }
            catch (RegraException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });

        grupo.MapGet("/exportar/{relatorio}", async (string relatorio, DateOnly? de, DateOnly? ate, Agrupamento? agrupar, bool? simuladas,
            Guid? vendedorId, ServicoMetricas metricas, AppDbContext db, CancellationToken ct) =>
        {
            relatorio = relatorio.ToLowerInvariant().Replace(".csv", "");
            if (!Relatorios.Contains(relatorio))
                return Results.NotFound();
            try
            {
                var (p, _, fuso) = await metricas.PeriodoAsync(de, ate, ct);
                var incluiSimuladas = simuladas ?? false;
                byte[] csv;
                if (relatorio == "pedidos")
                {
                    var pedidos = await metricas.PedidosPagosAsync(p, fuso, incluiSimuladas, ct, vendedorId);
                    csv = Csv.Gerar(["Pedido", "Pago em", "Cliente", "Telefone", "Itens", "Subtotal", "Frete", "Total", "Forma", "Entrega", "Origem", "Vendedor"],
                        pedidos.Select(x => new object?[]
                        {
                            x.Numero, TimeZoneInfo.ConvertTime(x.PagoEm, fuso), x.Cliente, x.Telefone, x.Itens, x.Subtotal, x.Frete, x.Total, x.Forma, x.Tipo, x.Origem,
                            x.Vendedor ?? "Sem vendedor"
                        }));
                }
                else if (relatorio == "caixa")
                {
                    var lancamentos = await db.LancamentosCaixa.AsNoTracking().Where(l => l.Data >= p.De && l.Data <= p.Ate)
                        .OrderBy(l => l.Data).ThenBy(l => l.CriadoEm).ToListAsync(ct);
                    csv = Csv.Gerar(["Data", "Tipo", "Categoria", "Forma", "Valor", "Descrição"],
                        lancamentos.Select(l => new object?[]
                        {
                            l.Data, l.Tipo == TipoLancamento.Entrada ? "Entrada" : "Saída", l.Categoria, ServicoMetricas.RotuloForma(l.Forma),
                            l.Tipo == TipoLancamento.Entrada ? l.Valor : -l.Valor, l.Descricao
                        }));
                }
                else
                {
                    var r = await metricas.GerarAsync(p.De, p.Ate, agrupar, incluiSimuladas, ct, vendedorId);
                    var v = Domain.Lojas.Vocabulario.Para(await db.Lojas.AsNoTracking().Where(l => l.Id == db.LojaAtualId).Select(l => l.Segmento).FirstOrDefaultAsync(ct));
                    var mortes = v.Aquario ? "Mortes" : "Perdas";
                    csv = relatorio switch
                    {
                        "vendas" => Csv.Gerar(["Início", "Pedidos pagos", "Receita"],
                            r.Vendas.Select(v => new object?[] { v.Inicio, v.Pedidos, v.Receita })),
                        "especies" => Csv.Gerar([v.Produto, "Vendidos", "Receita", "Custo", "Margem", "Margem %", $"{mortes} no período"],
                            r.Especies.Select(e => new object?[] { e.Especie, e.Quantidade, e.Receita, e.Custo, e.Margem, e.MargemPercentual, e.Mortes })),
                        "estoque" => Csv.Gerar([v.Local, "Tipo", "Saldo no início", "Entradas", "Vendidos", mortes, "Giro", "Saldo atual"],
                            r.Tanques.Select(t => new object?[] { t.Tanque, t.Tipo, t.SaldoInicio, t.Entradas, t.Vendidos, t.Mortes, t.Giro, t.SaldoAtual })),
                        "vendedores" => Csv.Gerar(["Vendedor", "Conversas atendidas", "Mensagens", "Vendas pagas", "Receita", "Ticket médio", "Cancelados", "Em atendimento agora"],
                            r.Vendedores.Select(v => new object?[] { v.Nome, v.ConversasAtendidas, v.Mensagens, v.Vendas, v.Receita, v.TicketMedio, v.Cancelados, v.EmAtendimento })),
                        _ => Csv.Gerar(["Fornecedor", "Lotes", "Recebidos", mortes, v.Aquario ? "Mortalidade %" : "Perdas %", "Custo das compras"],
                            r.Fornecedores.Select(f => new object?[] { f.Fornecedor, f.Lotes, f.Recebidos, f.Mortes, f.Mortalidade, f.CustoCompras }))
                    };
                }
                return Results.File(csv, "text/csv; charset=utf-8", $"{relatorio}_{p.De:yyyy-MM-dd}_a_{p.Ate:yyyy-MM-dd}.csv");
            }
            catch (RegraException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });
    }
}
