using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.IA;
using SistemaVendas.Infrastructure.WhatsApp;
using SistemaVendas.Domain.Lojas;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Infrastructure.Seguranca;

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
    int NaoLidas,
    bool SemDono,
    Guid? ResponsavelId = null,
    string? Responsavel = null);

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

/// <summary>WhatsApp oficial (Cloud API) da loja: o token nunca volta, só o resumo.</summary>
public record WhatsAppOficialSaida(bool Configurado, string? Origem, string? PhoneNumberId, string? Resumo, bool PodeSalvar);

public record WhatsAppOficialEntrada(string? PhoneNumberId, string? AccessToken);

/// <summary>Uso da IA no mês: Limite 0 = sem limite.</summary>
public record UsoIASaida(bool Ativa, string Atendente, int Respostas, int PorRegras, int Limite, bool Esgotado);

public record ConfigWhatsAppDto(bool AtenderTodos, List<string> NumerosPermitidos, string? WhatsAppNumero, string Provedor);

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
            var nomes = await ConversaEndpoints.NomesAsync(db, lista.Select(x => x.Conversa.ResponsavelId), ct);

            return lista.Select(x => new CardFunil(
                x.Conversa.Id, x.Nome ?? x.Telefone, x.Conversa.Etapa.ToString(), x.Ultima, x.Conversa.UltimaMensagemEm,
                x.Conversa.Simulada, x.Conversa.RoboPausado, x.Conversa.ChamouVendedor, x.Conversa.MotivoVendedor,
                x.Conversa.ChamouVendedorEm, x.Conversa.FollowupsEnviados, x.Conversa.ProximaAcao?.ToString(),
                x.Conversa.ProximaAcaoEm, x.Conversa.MotivoPerda?.ToString(), x.Conversa.NaoLidas, LeadQuente.SemDono(x.Conversa),
                x.Conversa.ResponsavelId, x.Conversa.ResponsavelId is { } r ? nomes.GetValueOrDefault(r) ?? "Usuário removido" : null));
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
                conversa.Assumir(usuarioId: UsuarioId(user));
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

        // Uso da IA da plataforma neste mês (o dono vê quanto falta para o robô passar a responder por regras).
        config.MapGet("/ia", async (ILojaAtual loja, ServicoUsoIA uso, IAtendenteIA atendente, CancellationToken ct) =>
        {
            var r = await uso.ResumoAsync(loja.LojaId!.Value, DateTimeOffset.UtcNow, ct);
            return new UsoIASaida(atendente is AtendenteComLimite, atendente.Nome, r.Respostas, r.PorRegras, r.Limite, r.Esgotado);
        });

        config.MapGet("/whatsapp", async (AppDbContext db, ILojaAtual loja, IOptions<WhatsAppOptions> wa, CancellationToken ct) =>
        {
            var l = await db.Lojas.AsNoTracking().FirstAsync(x => x.Id == loja.LojaId, ct);
            return new ConfigWhatsAppDto(l.AtenderTodos, l.NumerosPermitidos, l.WhatsAppNumero, wa.Value.UsaPonteLocal ? "Local" : "Meta");
        });

        config.MapPut("/whatsapp", async (ConfigWhatsAppDto entrada, AppDbContext db, ILojaAtual loja, IOptions<WhatsAppOptions> wa, CancellationToken ct) =>
        {
            var l = await db.Lojas.FirstAsync(x => x.Id == loja.LojaId, ct);
            l.AtenderTodos = entrada.AtenderTodos;
            l.NumerosPermitidos = Loja.NormalizarNumeros(entrada.NumerosPermitidos ?? []);

            var numero = new string((entrada.WhatsAppNumero ?? "").Where(char.IsDigit).ToArray());
            l.WhatsAppNumero = numero.Length == 0 ? null : Loja.NormalizarNumeros([numero]).FirstOrDefault();

            await db.SaveChangesAsync(ct);
            return Results.Ok(new ConfigWhatsAppDto(l.AtenderTodos, l.NumerosPermitidos, l.WhatsAppNumero, wa.Value.UsaPonteLocal ? "Local" : "Meta"));
        });

        config.MapGet("/whatsapp-oficial", async (AppDbContext db, ILojaAtual loja, CredenciaisDaLoja credenciais, ServicoCredenciais cofre, CancellationToken ct) =>
        {
            var lojaId = loja.LojaId!.Value;
            var phone = await db.Lojas.Where(l => l.Id == lojaId).Select(l => l.PhoneNumberId).FirstOrDefaultAsync(ct);
            var emUso = await credenciais.WhatsAppAsync(lojaId, ct);
            var resumo = emUso?.Origem == OrigemCredencial.Loja ? (await cofre.ResumosAsync(lojaId, ct)).GetValueOrDefault(TipoCredencial.WhatsApp).Resumo : null;
            return new WhatsAppOficialSaida(emUso is not null, emUso?.Origem.ToString(), phone, resumo, cofre.Disponivel);
        });

        // O dono informa o Phone Number ID e o token da conta do WhatsApp DA LOJA (na fase 2, Embedded Signup).
        // A Meta confere se o token enxerga o número antes de salvar, e um número só pode estar numa loja.
        config.MapPut("/whatsapp-oficial", async (WhatsAppOficialEntrada e, AppDbContext db, ILojaAtual loja, ServicoCredenciais cofre,
            VerificadorWhatsApp verificador, CancellationToken ct) =>
        {
            var lojaId = loja.LojaId!.Value;
            var phone = e.PhoneNumberId?.Trim() ?? "";
            var token = e.AccessToken?.Trim() ?? "";
            if (phone.Length is < 5 or > 30 || !phone.All(char.IsAsciiDigit))
                return Results.Problem("Informe o Phone Number ID (só números).", statusCode: StatusCodes.Status422UnprocessableEntity);
            if (token.Length is < 20 or > 600 || token.Any(char.IsWhiteSpace))
                return Results.Problem("Cole o token de acesso da Meta.", statusCode: StatusCodes.Status422UnprocessableEntity);
            if (!cofre.Disponivel)
                return Results.Problem("Falta a chave mestra (Plataforma__ChaveMestra) no servidor.", statusCode: StatusCodes.Status422UnprocessableEntity);
            if (await db.Lojas.AnyAsync(l => l.PhoneNumberId == phone && l.Id != lojaId, ct))
                return Results.Problem("Este número já está ligado a outra loja.", statusCode: StatusCodes.Status409Conflict);

            var (numero, erro) = await verificador.ConferirAsync(phone, token, ct);
            if (erro is not null)
                return Results.Problem(erro, statusCode: StatusCodes.Status422UnprocessableEntity);

            var l = await db.Lojas.FirstAsync(x => x.Id == lojaId, ct);
            l.PhoneNumberId = phone;
            await db.SaveChangesAsync(ct);
            await cofre.SalvarAsync(lojaId, new CredencialWhatsApp(token), $"token {ServicoCredenciais.Final(token)}, número {numero}", ct);
            return Results.NoContent();
        });

        config.MapDelete("/whatsapp-oficial", async (ILojaAtual loja, ServicoCredenciais cofre, CancellationToken ct) =>
        {
            await cofre.RemoverAsync<CredencialWhatsApp>(loja.LojaId!.Value, ct);
            return Results.NoContent();
        });

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
