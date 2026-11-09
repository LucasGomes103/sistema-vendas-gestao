using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Vendas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Endpoints;

public record ItemReservaSaida(string Especie, int Quantidade, decimal PrecoUnitario);

public record ReservaSaida(Guid Id, Guid ConversaId, string Status, DateTimeOffset CriadoEm, DateTimeOffset ExpiraEm, DateTimeOffset? EncerradaEm, decimal Total, List<ItemReservaSaida> Itens);

/// <summary>Reservas de 24 h: a equipe vê, confirma a venda ou cancela.</summary>
public static class ReservaEndpoints
{
    public static void MapReservaEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/reservas").WithTags("Reservas").RequireAuthorization(AuthEndpoints.PoliticaEquipe);

        // Da conversa: as ativas primeiro, depois as últimas encerradas.
        grupo.MapGet("/", async (Guid? conversaId, AppDbContext db, CancellationToken ct) =>
        {
            var consulta = db.Reservas.AsNoTracking().Include(r => r.Itens).AsQueryable();
            consulta = conversaId is { } id
                ? consulta.Where(r => r.ConversaId == id)
                : consulta.Where(r => r.Status == StatusReserva.Ativa);
            var lista = await consulta.OrderBy(r => r.Status).ThenByDescending(r => r.CriadoEm).Take(20).ToListAsync(ct);
            return lista.Select(Saida);
        });

        grupo.MapPost("/{id:guid}/confirmar", (Guid id, ServicoReservas reservas, ClaimsPrincipal user, CancellationToken ct) =>
            Encerrar(id, StatusReserva.Confirmada, reservas, user, ct));

        grupo.MapPost("/{id:guid}/cancelar", (Guid id, ServicoReservas reservas, ClaimsPrincipal user, CancellationToken ct) =>
            Encerrar(id, StatusReserva.Cancelada, reservas, user, ct));
    }

    private static async Task<IResult> Encerrar(Guid id, StatusReserva status, ServicoReservas reservas, ClaimsPrincipal user, CancellationToken ct)
    {
        try
        {
            var usuario = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : (Guid?)null;
            var reserva = await reservas.EncerrarAsync(id, status, usuario, ct);
            return reserva is null ? Results.NotFound() : Results.Ok(Saida(reserva));
        }
        catch (RegraException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static ReservaSaida Saida(Reserva r) => new(
        r.Id, r.ConversaId, r.Status.ToString(), r.CriadoEm, r.ExpiraEm, r.EncerradaEm, r.Total,
        r.Itens.GroupBy(i => (i.Especie, i.PrecoUnitario))
            .Select(g => new ItemReservaSaida(g.Key.Especie, g.Sum(i => i.Quantidade), g.Key.PrecoUnitario))
            .ToList());
}
