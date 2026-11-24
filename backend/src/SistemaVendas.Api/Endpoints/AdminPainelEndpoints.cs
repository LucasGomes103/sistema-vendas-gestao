using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SistemaVendas.Api.Comum;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Domain.Vendas;
using SistemaVendas.Infrastructure.Assinaturas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.Email;
using SistemaVendas.Infrastructure.IA;
using SistemaVendas.Infrastructure.Seguranca;

namespace SistemaVendas.Api.Endpoints;

/// <summary>Um dia da série do painel (horário de Brasília).</summary>
public record DiaPlataforma(DateOnly Dia, int LojasNovas, int MensagensRecebidas, int RespostasRobo, int Pedidos, decimal Vendido, long TokensIa);

public record LojasPlataforma(int Total, int Ativas, int AguardandoConfirmacao, int Suspensas, int NovasNoPeriodo, int ComMovimentoNoPeriodo, int Paradas);

public record TotaisPlataforma(
    int ConversasNoPeriodo, int MensagensRecebidas, int RespostasRobo, int MensagensVendedor, int Pedidos, int PedidosPagos, decimal Vendido,
    int ChamadasIa, long TokensEntrada, long TokensSaida, int FalhasIa, int MensagensComFalha);

public record SaudePlataforma(bool IaConfigurada, string ModeloIa, bool EmailConfigurado, bool ChaveMestraConfigurada,
    int LojasComWhatsAppProprio, int LojasComMercadoPagoProprio);

public record VisaoGeralPlataforma(int Dias, LojasPlataforma Lojas, TotaisPlataforma Totais, IReadOnlyList<DiaPlataforma> PorDia, SaudePlataforma Saude);

public record UsuarioDaLojaAdmin(Guid Id, string Nome, string Email, string Papel, bool Ativo, bool EmailConfirmado, DateTimeOffset CriadoEm, DateTimeOffset? UltimoAcessoEm);

public record AuditoriaSaida(Guid Id, DateTimeOffset CriadoEm, string AdminEmail, string Acao, Guid? LojaId, string? LojaSlug, string? Detalhe);

public record ConfiguracaoLojaAdmin(bool WhatsAppConectado, string? WhatsAppNumero, bool AtenderTodos, int NumerosPermitidos, bool PixConfigurado,
    bool FreteConfigurado, int ZonasDeEntrega, string HorarioAtendimento);

public record DetalheLojaAdmin(
    LojaAdminSaida Loja, ConfiguracaoLojaAdmin Configuracao, TotaisPlataforma Totais, int Clientes, int ConversasUltimas24h,
    IReadOnlyList<UsuarioDaLojaAdmin> Usuarios, IReadOnlyList<DiaPlataforma> PorDia, IReadOnlyList<AuditoriaSaida> Auditoria);

/// <summary>
/// Painel do super admin: visão geral da plataforma, detalhe de uma loja, auditoria e o modo suporte (entrar para
/// ver o painel de uma loja, só leitura). Números agregados; conversas e clientes só pelo modo suporte, que fica auditado.
/// </summary>
public static class AdminPainelEndpoints
{
    /// <summary>Loja ativa sem ninguém entrar no painel há mais que isso aparece como parada.</summary>
    public const int DiasParaParada = 7;

    public static void MapAdminPainelEndpoints(this RouteGroupBuilder grupo)
    {
        grupo.MapGet("/visao-geral", async (int? dias, AppDbContext db, IOptions<GeminiOptions> gemini, IOptions<EmailOptions> email,
            IOptionsMonitor<PlataformaOptions> plataforma, CancellationToken ct) =>
        {
            var periodo = Math.Clamp(dias ?? 30, 1, 365);
            var agora = DateTimeOffset.UtcNow;
            var desde = InicioDoPeriodo(agora, periodo);
            var parada = agora.AddDays(-DiasParaParada);

            var lojas = await db.Lojas.AsNoTracking().Select(l => new { l.Id, l.Status, l.CriadoEm }).ToListAsync(ct);
            var acessos = await db.Usuarios.IgnoreQueryFilters().Where(u => u.UltimoAcessoEm != null).GroupBy(u => u.LojaId)
                .Select(g => new { g.Key, Ultimo = g.Max(u => u.UltimoAcessoEm) }).ToDictionaryAsync(x => x.Key, x => x.Ultimo, ct);
            var comMovimento = (await db.Conversas.IgnoreQueryFilters().Where(c => !c.Simulada && c.UltimaMensagemEm >= desde)
                .Select(c => c.LojaId).Distinct().ToListAsync(ct)).ToHashSet();
            foreach (var (loja, ultimo) in acessos)
                if (ultimo >= desde)
                    comMovimento.Add(loja);

            var resumoLojas = new LojasPlataforma(
                lojas.Count,
                lojas.Count(l => l.Status == StatusLoja.Ativa),
                lojas.Count(l => l.Status == StatusLoja.AguardandoConfirmacao),
                lojas.Count(l => l.Status == StatusLoja.Suspensa),
                lojas.Count(l => l.CriadoEm >= desde),
                lojas.Count(l => comMovimento.Contains(l.Id)),
                lojas.Count(l => l.Status == StatusLoja.Ativa && l.CriadoEm < parada && !(acessos.GetValueOrDefault(l.Id) >= parada)));

            var credenciais = await db.CredenciaisLoja.IgnoreQueryFilters().Select(c => new { c.LojaId, c.Tipo }).ToListAsync(ct);
            var g = gemini.Value;
            var saude = new SaudePlataforma(
                !string.IsNullOrWhiteSpace(g.ApiKey), g.Modelo, email.Value.Configurado,
                !string.IsNullOrWhiteSpace(plataforma.CurrentValue.ChaveMestra),
                credenciais.Where(c => c.Tipo == TipoCredencial.WhatsApp).Select(c => c.LojaId).Distinct().Count(),
                credenciais.Where(c => c.Tipo == TipoCredencial.MercadoPago).Select(c => c.LojaId).Distinct().Count());

            return Results.Ok(new VisaoGeralPlataforma(periodo, resumoLojas,
                await TotaisAsync(db, null, desde, ct), await PorDiaAsync(db, null, desde, periodo, agora, ct), saude));
        });

        grupo.MapGet("/lojas/{id:guid}", async (Guid id, int? dias, AppDbContext db, CredenciaisDaLoja credenciais, ServicoAssinatura assinaturas,
            ServicoLimiteIa limiteIa, CancellationToken ct) =>
        {
            var loja = await db.Lojas.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
            if (loja is null)
                return Results.NotFound();
            var periodo = Math.Clamp(dias ?? 30, 1, 365);
            var agora = DateTimeOffset.UtcNow;
            var desde = InicioDoPeriodo(agora, periodo);

            var usuarios = await db.Usuarios.IgnoreQueryFilters().AsNoTracking().Where(u => u.LojaId == id)
                .OrderBy(u => u.Papel).ThenBy(u => u.Nome)
                .Select(u => new UsuarioDaLojaAdmin(u.Id, u.Nome, u.Email, u.Papel.ToString(), u.Ativo, u.EmailConfirmadoEm != null, u.CriadoEm, u.UltimoAcessoEm))
                .ToListAsync(ct);
            var dono = usuarios.Where(u => u.Papel == nameof(Papel.Dono)).OrderBy(u => u.CriadoEm).FirstOrDefault();
            var tipos = await db.CredenciaisLoja.IgnoreQueryFilters().Where(c => c.LojaId == id).Select(c => c.Tipo).ToListAsync(ct);
            var totais = await TotaisAsync(db, id, desde, ct);
            var produtos = await db.Especies.IgnoreQueryFilters().CountAsync(e => e.LojaId == id && e.Ativo, ct);
            var ultimaMensagem = await db.Conversas.IgnoreQueryFilters().Where(c => c.LojaId == id && !c.Simulada)
                .MaxAsync(c => (DateTimeOffset?)c.UltimaMensagemEm, ct);

            var resumo = new LojaAdminSaida(loja.Id, loja.Nome, loja.Slug, loja.Segmento, loja.Status.ToString(), loja.MotivoSuspensao, loja.CriadoEm,
                dono?.Nome, dono?.Email, dono?.EmailConfirmado ?? false, usuarios.Count, produtos,
                totais.ConversasNoPeriodo, totais.Pedidos, totais.Vendido, ultimaMensagem,
                tipos.Contains(TipoCredencial.WhatsApp), tipos.Contains(TipoCredencial.MercadoPago), await credenciais.LojaOriginalAsync(ct) == id)
            {
                UltimoAcessoEm = usuarios.Max(u => u.UltimoAcessoEm),
                RespostasRobo30Dias = totais.RespostasRobo,
                TokensIa30Dias = totais.TokensEntrada + totais.TokensSaida,
                FalhasIa30Dias = totais.FalhasIa,
            }.ComAssinatura(await assinaturas.EstadoAsync(id, ct));
            var ia = await limiteIa.ResumoAsync(id, agora, ct);
            resumo = resumo with { RespostasIaMes = ia.Respostas, LimiteIaMes = ia.Limite, LimiteIaProprio = loja.LimiteRespostasIAMes };

            var config = new ConfiguracaoLojaAdmin(
                loja.PhoneNumberId is not null || tipos.Contains(TipoCredencial.WhatsApp), loja.WhatsAppNumero, loja.AtenderTodos, loja.NumerosPermitidos.Count,
                !string.IsNullOrWhiteSpace(loja.PixChave),
                await db.FretesPorKm.IgnoreQueryFilters().AnyAsync(f => f.LojaId == id, ct),
                await db.ZonasEntrega.IgnoreQueryFilters().CountAsync(z => z.LojaId == id, ct),
                $"{loja.HorarioInicio:HH\\:mm}–{loja.HorarioFim:HH\\:mm}");

            var auditoria = await db.AuditoriaAdmin.AsNoTracking().Where(a => a.LojaAlvoId == id).OrderByDescending(a => a.CriadoEm).Take(30)
                .Select(a => new AuditoriaSaida(a.Id, a.CriadoEm, a.AdminEmail, a.Acao, a.LojaAlvoId, a.LojaAlvoSlug, a.Detalhe)).ToListAsync(ct);

            return Results.Ok(new DetalheLojaAdmin(resumo, config, totais,
                await db.Clientes.IgnoreQueryFilters().CountAsync(c => c.LojaId == id, ct),
                await db.Conversas.IgnoreQueryFilters().CountAsync(c => c.LojaId == id && !c.Simulada && c.UltimaMensagemEm >= agora.AddDays(-1), ct),
                usuarios, await PorDiaAsync(db, id, desde, periodo, agora, ct), auditoria));
        });

        grupo.MapGet("/auditoria", async (Guid? lojaId, int? limite, AppDbContext db, CancellationToken ct) =>
        {
            var q = db.AuditoriaAdmin.AsNoTracking();
            if (lojaId is { } l)
                q = q.Where(a => a.LojaAlvoId == l);
            return await q.OrderByDescending(a => a.CriadoEm).Take(Math.Clamp(limite ?? 200, 1, 1000))
                .Select(a => new AuditoriaSaida(a.Id, a.CriadoEm, a.AdminEmail, a.Acao, a.LojaAlvoId, a.LojaAlvoSlug, a.Detalhe))
                .ToListAsync(ct);
        });

        // Modo suporte: o administrador passa a ver o painel da loja como o dono vê, sem poder mudar nada.
        grupo.MapPost("/lojas/{id:guid}/ver-como", async (Guid id, AppDbContext db, HttpContext http, ILoggerFactory logs, CancellationToken ct) =>
        {
            var loja = await db.Lojas.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
            if (loja is null)
                return Results.NotFound();
            var adminId = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var admin = await db.Usuarios.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == adminId, ct);
            if (admin.LojaId == id)
                return Results.Problem("Esta já é a sua loja.", statusCode: StatusCodes.Status422UnprocessableEntity);

            AdminEndpoints.Auditar(db, http.User, AcaoAdmin.VerComoLoja, loja.Id, loja.Slug, $"entrou para ver {loja.Nome} (só leitura)");
            await db.SaveChangesAsync(ct);
            await Sessao.EntrarComoSuporteAsync(http, admin, loja);
            logs.CreateLogger("Admin").LogWarning("{Admin} entrou no modo suporte da loja {Slug}", admin.Email, loja.Slug);
            return Results.NoContent();
        });

        grupo.MapPost("/suporte/sair", async (AppDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if (!Sessao.EmModoSuporte(http.User))
                return Results.NoContent();
            var adminId = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var admin = await db.Usuarios.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == adminId, ct);
            var propria = await db.Lojas.AsNoTracking().FirstAsync(l => l.Id == admin.LojaId, ct);
            var vista = Guid.TryParse(http.User.FindFirstValue(LojaAtualDaRequisicao.ClaimLoja), out var v) ? v : (Guid?)null;

            AdminEndpoints.Auditar(db, http.User, AcaoAdmin.SairDaLoja, vista, http.User.FindFirstValue("loja_slug"), null);
            await db.SaveChangesAsync(ct);
            await Sessao.EntrarAsync(http, admin, propria);
            return Results.NoContent();
        });
    }

    /// <summary>Começo do período: meia-noite (Brasília) de <paramref name="dias"/> - 1 dias atrás, para o dia de hoje contar.</summary>
    private static DateTimeOffset InicioDoPeriodo(DateTimeOffset agora, int dias)
    {
        var hojeBrasil = agora.ToOffset(Brasilia).Date;
        return new DateTimeOffset(hojeBrasil.AddDays(-(dias - 1)), Brasilia).ToUniversalTime();
    }

    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    private static async Task<TotaisPlataforma> TotaisAsync(AppDbContext db, Guid? loja, DateTimeOffset desde, CancellationToken ct)
    {
        var diaDesde = DateOnly.FromDateTime(desde.ToOffset(Brasilia).Date);
        var conversas = db.Conversas.IgnoreQueryFilters().Where(c => !c.Simulada && (loja == null || c.LojaId == loja));
        var mensagens = db.Mensagens.IgnoreQueryFilters().Where(m => m.CriadoEm >= desde && (loja == null || m.LojaId == loja))
            .Where(m => conversas.Any(c => c.Id == m.ConversaId));
        var porAutor = await mensagens.GroupBy(m => new { m.Direcao, m.Autor })
            .Select(g => new { g.Key.Direcao, g.Key.Autor, N = g.Count() }).ToListAsync(ct);
        var falhas = await mensagens.CountAsync(m => m.Status == Domain.Atendimento.StatusMensagem.Falhou, ct);
        var pedidos = await db.Pedidos.IgnoreQueryFilters().Where(p => p.CriadoEm >= desde && (loja == null || p.LojaId == loja))
            .GroupBy(_ => 1)
            .Select(g => new { N = g.Count(), Pagos = g.Count(p => p.Status == StatusPedido.Pago), Vendido = g.Where(p => p.Status == StatusPedido.Pago).Sum(p => p.Total) })
            .FirstOrDefaultAsync(ct);
        var ia = await db.UsosIa.IgnoreQueryFilters().Where(u => u.Dia >= diaDesde && (loja == null || u.LojaId == loja))
            .GroupBy(_ => 1)
            .Select(g => new { Chamadas = g.Sum(u => u.Chamadas), Entrada = g.Sum(u => u.TokensEntrada), Saida = g.Sum(u => u.TokensSaida), Falhas = g.Sum(u => u.Falhas) })
            .FirstOrDefaultAsync(ct);

        int Contar(Domain.Atendimento.DirecaoMensagem d, Domain.Atendimento.AutorMensagem? a = null) =>
            porAutor.Where(x => x.Direcao == d && (a == null || x.Autor == a)).Sum(x => x.N);

        return new TotaisPlataforma(
            await conversas.CountAsync(c => c.UltimaMensagemEm >= desde, ct),
            Contar(Domain.Atendimento.DirecaoMensagem.Entrada),
            Contar(Domain.Atendimento.DirecaoMensagem.Saida, Domain.Atendimento.AutorMensagem.Robo),
            Contar(Domain.Atendimento.DirecaoMensagem.Saida, Domain.Atendimento.AutorMensagem.Vendedor),
            pedidos?.N ?? 0, pedidos?.Pagos ?? 0, pedidos?.Vendido ?? 0,
            ia?.Chamadas ?? 0, ia?.Entrada ?? 0, ia?.Saida ?? 0, ia?.Falhas ?? 0, falhas);
    }

    private sealed record LinhaDia(DateOnly Dia, int N, decimal Valor);

    /// <summary>Série diária (Brasília) do período, com zero nos dias sem movimento.</summary>
    private static async Task<List<DiaPlataforma>> PorDiaAsync(AppDbContext db, Guid? loja, DateTimeOffset desde, int dias, DateTimeOffset agora, CancellationToken ct)
    {
        // Agrupar por dia no fuso de Brasília só dá no SQL; as consultas são só contagens.
        const string Dia = "(criado_em at time zone 'America/Sao_Paulo')::date";
        var filtroLoja = loja is null ? "" : " and loja_id = {1}";
        object[] p = loja is null ? [desde] : [desde, loja.Value];

        async Task<Dictionary<DateOnly, LinhaDia>> Ler(string sql) =>
            (await db.Database.SqlQueryRaw<LinhaDia>(sql, p).ToListAsync(ct)).ToDictionary(x => x.Dia);

        var recebidas = await Ler($$"""
            select {{Dia}} as "Dia", count(*)::int as "N", 0::numeric as "Valor" from mensagem m
            where criado_em >= {0}{{filtroLoja}} and direcao = 'Entrada'
              and not exists (select 1 from conversa c where c.id = m.conversa_id and c.simulada)
            group by 1
            """);
        var robo = await Ler($$"""
            select {{Dia}} as "Dia", count(*)::int as "N", 0::numeric as "Valor" from mensagem m
            where criado_em >= {0}{{filtroLoja}} and direcao = 'Saida' and autor = 'Robo'
              and not exists (select 1 from conversa c where c.id = m.conversa_id and c.simulada)
            group by 1
            """);
        var pedidos = await Ler($$"""
            select {{Dia}} as "Dia", count(*)::int as "N", coalesce(sum(case when status = 'Pago' then total end), 0) as "Valor"
            from pedido where criado_em >= {0}{{filtroLoja}} group by 1
            """);
        var lojasNovas = loja is null
            ? await Ler($$"""select {{Dia}} as "Dia", count(*)::int as "N", 0::numeric as "Valor" from loja where criado_em >= {0} group by 1""")
            : new Dictionary<DateOnly, LinhaDia>();
        var diaDesde = DateOnly.FromDateTime(desde.ToOffset(Brasilia).Date);
        var tokens = await db.UsosIa.IgnoreQueryFilters().Where(u => u.Dia >= diaDesde && (loja == null || u.LojaId == loja))
            .GroupBy(u => u.Dia).Select(g => new { g.Key, T = g.Sum(u => u.TokensEntrada + u.TokensSaida) })
            .ToDictionaryAsync(x => x.Key, x => x.T, ct);

        var serie = new List<DiaPlataforma>(dias);
        for (var i = 0; i < dias; i++)
        {
            var d = diaDesde.AddDays(i);
            if (d > DateOnly.FromDateTime(agora.ToOffset(Brasilia).Date))
                break;
            serie.Add(new DiaPlataforma(d,
                lojasNovas.GetValueOrDefault(d)?.N ?? 0,
                recebidas.GetValueOrDefault(d)?.N ?? 0,
                robo.GetValueOrDefault(d)?.N ?? 0,
                pedidos.GetValueOrDefault(d)?.N ?? 0,
                pedidos.GetValueOrDefault(d)?.Valor ?? 0,
                tokens.GetValueOrDefault(d)));
        }
        return serie;
    }
}
