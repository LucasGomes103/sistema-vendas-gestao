using SistemaVendas.Infrastructure.Assinaturas;
using SistemaVendas.Api.Comum;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Entregas;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Endpoints;

public record EntregaSaida(
    Guid Id, Guid PedidoId, int NumeroPedido, string Status, string Tipo, string Cliente, string Telefone, Guid? ConversaId,
    string? Cep, string? Endereco, DateOnly? Data, string? Janela, Guid? EntregadorId, string? Entregador,
    DateTimeOffset? SaiuEm, DateTimeOffset? EntregueEm, string? Observacao, decimal Total, string Itens, DateTimeOffset CriadoEm);

public record EntregaAcaoSaida(EntregaSaida Entrega, bool MensagemEnviada, string? Aviso);

public record AgendarEntrada(DateOnly Data, string? Janela, Guid? EntregadorId, bool AvisarCliente = true);

public record AvisarEntrada(bool AvisarCliente = true);

public record PessoaEntrega(Guid Id, string Nome, string Papel);

/// <summary>Agenda de entregas e retiradas. O entregador vê só as do dia (dele ou sem ninguém) e marca saída e entrega.</summary>
public static class EntregaEndpoints
{
    public static void MapEntregaEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/entregas").WithTags("Entregas").RequireAuthorization(AuthEndpoints.PoliticaEntregas)
            .ExigeRecurso(RecursoDoPlano.Entregas);

        // Em aberto (qualquer data) e as entregues nos últimos 7 dias.
        grupo.MapGet("/", async (AppDbContext db, ServicoEntregas entregas, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var corte = DateTimeOffset.UtcNow.AddDays(-7);
            var consulta = db.Entregas.AsNoTracking().Where(e => e.Status != StatusEntrega.Entregue || e.EntregueEm >= corte);

            if (EhEntregador(user))
            {
                var hoje = await entregas.HojeAsync(ct);
                var eu = Usuario(user);
                consulta = consulta.Where(e => e.Tipo == Domain.Vendas.TipoEntrega.Entrega && e.Data != null && e.Data <= hoje
                    && (e.EntregadorId == null || e.EntregadorId == eu));
            }

            var lista = await consulta.OrderBy(e => e.Data == null ? 0 : 1).ThenBy(e => e.Data).ThenBy(e => e.CriadoEm).Take(300).ToListAsync(ct);
            return await SaidasAsync(db, lista, ct);
        });

        // Quem pode levar: entregadores primeiro, depois o resto da equipe ativa.
        grupo.MapGet("/pessoas", async (AppDbContext db, CancellationToken ct) =>
            (await db.Usuarios.AsNoTracking().Where(u => u.Ativo).ToListAsync(ct))
                .OrderBy(u => u.Papel == Papel.Entregador ? 0 : 1).ThenBy(u => u.Nome)
                .Select(u => new PessoaEntrega(u.Id, u.Nome, u.Papel.ToString())))
            .RequireAuthorization(AuthEndpoints.PoliticaEquipe);

        grupo.MapPost("/{id:guid}/agendar", (Guid id, AgendarEntrada e, ServicoEntregas entregas, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
                Executar(db, () => entregas.AgendarAsync(id, e.Data, e.Janela, e.EntregadorId, e.AvisarCliente, Usuario(user), ct), ct))
            .RequireAuthorization(AuthEndpoints.PoliticaEquipe);

        grupo.MapPost("/{id:guid}/saiu", (Guid id, AvisarEntrada e, ServicoEntregas entregas, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            Executar(db, async () => await PodeMexer(db, id, user, ct) ? await entregas.SaiuAsync(id, e.AvisarCliente, Usuario(user), ct) : null, ct));

        grupo.MapPost("/{id:guid}/entregue", (Guid id, AvisarEntrada e, ServicoEntregas entregas, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            Executar(db, async () => await PodeMexer(db, id, user, ct) ? await entregas.EntregueAsync(id, e.AvisarCliente, Usuario(user), ct) : null, ct));
    }

    private static bool EhEntregador(ClaimsPrincipal user) => user.IsInRole(nameof(Papel.Entregador));

    private static Guid? Usuario(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : null;

    /// <summary>Entregador só mexe na entrega dele ou na que está sem ninguém.</summary>
    private static async Task<bool> PodeMexer(AppDbContext db, Guid id, ClaimsPrincipal user, CancellationToken ct)
    {
        if (!EhEntregador(user))
            return true;
        var eu = Usuario(user);
        return await db.Entregas.AnyAsync(e => e.Id == id && (e.EntregadorId == null || e.EntregadorId == eu), ct);
    }

    private static async Task<IResult> Executar(AppDbContext db, Func<Task<ResultadoEntrega?>> acao, CancellationToken ct)
    {
        try
        {
            var r = await acao();
            if (r is null)
                return Results.NotFound();
            return Results.Ok(new EntregaAcaoSaida((await SaidasAsync(db, [r.Entrega], ct))[0], r.MensagemEnviada, r.Aviso));
        }
        catch (RegraException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static async Task<List<EntregaSaida>> SaidasAsync(AppDbContext db, List<Entrega> entregas, CancellationToken ct)
    {
        var clientesIds = entregas.Select(e => e.ClienteId).Distinct().ToList();
        var pedidosIds = entregas.Select(e => e.PedidoId).ToList();
        var pessoasIds = entregas.Select(e => e.EntregadorId).OfType<Guid>().Distinct().ToList();

        var clientes = await db.Clientes.AsNoTracking().Where(c => clientesIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => (c.Nome, c.Telefone), ct);
        var pedidos = await db.Pedidos.AsNoTracking().Include(p => p.Itens).Where(p => pedidosIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var pessoas = await db.Usuarios.AsNoTracking().Where(u => pessoasIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome, ct);

        return entregas.Select(e =>
        {
            var (nome, telefone) = clientes.GetValueOrDefault(e.ClienteId);
            var pedido = pedidos.GetValueOrDefault(e.PedidoId);
            return new EntregaSaida(
                e.Id, e.PedidoId, e.NumeroPedido, e.Status.ToString(), e.Tipo.ToString(), nome ?? telefone ?? "Cliente", telefone ?? "", e.ConversaId,
                e.Cep, e.Endereco, e.Data, e.Janela, e.EntregadorId, e.EntregadorId is { } p ? pessoas.GetValueOrDefault(p) : null,
                e.SaiuEm, e.EntregueEm, e.Observacao, pedido?.Total ?? 0,
                pedido is null ? "" : Domain.Vendas.RegrasPedido.Resumo(pedido), e.CriadoEm);
        }).ToList();
    }
}
