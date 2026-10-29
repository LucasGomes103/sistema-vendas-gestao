using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Api.Endpoints;

public record CardFunil(
    Guid Id,
    string Cliente,
    string Etapa,
    string? UltimaMensagem,
    DateTimeOffset? UltimaMensagemEm,
    bool Simulada,
    bool RoboPausado,
    bool ChamouVendedor,
    string? MotivoVendedor,
    DateTimeOffset? ChamouVendedorEm,
    int FollowupsEnviados,
    string? ProximaAcao,
    DateTimeOffset? ProximaAcaoEm,
    string? MotivoPerda,
    int NaoLidas);

public record MoverCardEntrada(EtapaCard Etapa, MotivoPerda? MotivoPerda);

public record ConfigFunilDto(
    int HorasAteFollowup,
    int HorasFollowup1,
    int DiasFollowup2,
    int HorasAtePerdido,
    string TextoFollowup1,
    string TextoFollowup2,
    string TextoFrete,
    string TextoChamarVendedor);

public static class FunilEndpoints
{
    public static void MapFunilEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/funil").WithTags("Funil").RequireAuthorization(AuthEndpoints.PoliticaEquipe);

        // Cards do kanban. Perdidos e entregues só dos últimos 7 dias, para o quadro não crescer sem fim.
        grupo.MapGet("/cards", async (AppDbContext db, CancellationToken ct) =>
        {
            var corte = DateTimeOffset.UtcNow.AddDays(-7);
            var lista = await db.Conversas.AsNoTracking()
                .Where(c => c.Aberta && ((c.Etapa != EtapaCard.Perdido && c.Etapa != EtapaCard.Entregue) || c.UltimaMensagemEm >= corte))
                .OrderByDescending(c => c.UltimaMensagemEm)
                .Take(300)
                .Join(db.Clientes, c => c.ClienteId, cl => cl.Id, (c, cl) => new
                {
                    Conversa = c,
                    cl.Nome,
                    cl.Telefone,
                    Ultima = db.Mensagens.Where(m => m.ConversaId == c.Id).OrderByDescending(m => m.CriadoEm).Select(m => m.Conteudo).FirstOrDefault()
                })
                .ToListAsync(ct);

            return lista.Select(x => new CardFunil(
                x.Conversa.Id, x.Nome ?? x.Telefone, x.Conversa.Etapa.ToString(), x.Ultima, x.Conversa.UltimaMensagemEm,
                x.Conversa.Simulada, x.Conversa.RoboPausado, x.Conversa.ChamouVendedor, x.Conversa.MotivoVendedor,
                x.Conversa.ChamouVendedorEm, x.Conversa.FollowupsEnviados, x.Conversa.ProximaAcao?.ToString(),
                x.Conversa.ProximaAcaoEm, x.Conversa.MotivoPerda?.ToString(), x.Conversa.NaoLidas));
        });

        // Mover o card à mão. Etapas de venda pausam o robô; voltar para "Robô atendendo" religa.
        grupo.MapPatch("/cards/{id:guid}/etapa", async (Guid id, MoverCardEntrada entrada, AppDbContext db, ServicoFunil funil, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var conversa = await db.Conversas.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (conversa is null)
                return Results.NotFound();
            if (entrada.Etapa == EtapaCard.Perdido && entrada.MotivoPerda is null)
                return Results.Problem("Escolha o motivo da perda.", statusCode: StatusCodes.Status422UnprocessableEntity);

            var mudanca = MotorFunil.Mover(conversa, entrada.Etapa, entrada.MotivoPerda);

            if (entrada.Etapa == EtapaCard.RoboAtendendo)
            {
                conversa.DevolverAoRobo();
                var cfg = await funil.ConfigAsync(conversa.LojaId, ct);
                MotorFunil.AoResponderLoja(conversa, cfg, DateTimeOffset.UtcNow);
            }
            else if (!MotorFunil.Automatica(entrada.Etapa))
            {
                conversa.Assumir();
            }

            funil.Registrar(conversa, mudanca, OrigemEvento.Usuario, UsuarioId(user));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        grupo.MapGet("/cards/{id:guid}/eventos", async (Guid id, AppDbContext db, CancellationToken ct) =>
            await db.CardEventos.AsNoTracking()
                .Where(e => e.ConversaId == id)
                .OrderBy(e => e.CriadoEm)
                .Select(e => new { De = e.DeEtapa.ToString(), Para = e.ParaEtapa.ToString(), Origem = e.Origem.ToString(), e.Detalhe, e.CriadoEm })
                .ToListAsync(ct));

        var config = app.MapGroup("/config").WithTags("Configuração").RequireAuthorization(AuthEndpoints.PoliticaDono);

        config.MapGet("/funil", async (ServicoFunil funil, AppDbContext db, ILojaAtual loja, CancellationToken ct) =>
        {
            var cfg = await funil.ConfigAsync(loja.LojaId!.Value, ct);
            await db.SaveChangesAsync(ct);
            return Dto(cfg);
        });

        config.MapPut("/funil", async (ConfigFunilDto entrada, ServicoFunil funil, AppDbContext db, ILojaAtual loja, CancellationToken ct) =>
        {
            if (entrada.HorasAteFollowup is < 1 or > 72 || entrada.HorasFollowup1 is < 1 or > 23 ||
                entrada.DiasFollowup2 is < 1 or > 30 || entrada.HorasAtePerdido is < 1 or > 168)
                return Results.Problem(
                    "Prazos fora do permitido. O follow-up 1 precisa ficar dentro das 24 h da janela do WhatsApp (até 23 h).",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            if (new[] { entrada.TextoFollowup1, entrada.TextoFollowup2, entrada.TextoFrete, entrada.TextoChamarVendedor }.Any(string.IsNullOrWhiteSpace))
                return Results.Problem("Preencha todos os textos.", statusCode: StatusCodes.Status422UnprocessableEntity);

            var cfg = await funil.ConfigAsync(loja.LojaId!.Value, ct);
            cfg.HorasAteFollowup = entrada.HorasAteFollowup;
            cfg.HorasFollowup1 = entrada.HorasFollowup1;
            cfg.DiasFollowup2 = entrada.DiasFollowup2;
            cfg.HorasAtePerdido = entrada.HorasAtePerdido;
            cfg.TextoFollowup1 = entrada.TextoFollowup1.Trim();
            cfg.TextoFollowup2 = entrada.TextoFollowup2.Trim();
            cfg.TextoFrete = entrada.TextoFrete.Trim();
            cfg.TextoChamarVendedor = entrada.TextoChamarVendedor.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(Dto(cfg));
        });
    }

    private static ConfigFunilDto Dto(ConfigFunil c) => new(
        c.HorasAteFollowup, c.HorasFollowup1, c.DiasFollowup2, c.HorasAtePerdido,
        c.TextoFollowup1, c.TextoFollowup2, c.TextoFrete, c.TextoChamarVendedor);

    private static Guid? UsuarioId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
