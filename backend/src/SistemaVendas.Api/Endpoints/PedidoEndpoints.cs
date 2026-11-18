using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Vendas;
using Microsoft.Extensions.Options;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.Pagamentos;

namespace SistemaVendas.Api.Endpoints;

public record ItemPedidoSaida(string Especie, int Quantidade, decimal PrecoUnitario);

public record PedidoSaida(
    Guid Id, int Numero, string Status, Guid ClienteId, string Cliente, string Telefone, Guid? ConversaId, Guid? ReservaId,
    string TipoEntrega, string? Cep, string? Endereco, decimal Subtotal, decimal Frete, decimal Total,
    DateTimeOffset CriadoEm, DateTimeOffset? ExpiraEm, string? PixCopiaECola, DateTimeOffset? PixEnviadoEm, DateTimeOffset? ComprovanteEm,
    string? LinkCartao, bool PagoPeloMercadoPago,
    string? FormaPagamento, DateTimeOffset? PagoEm, DateTimeOffset? CanceladoEm, string? MotivoCancelamento, string? Observacao,
    List<ItemPedidoSaida> Itens,
    Guid? VendedorId = null, string? Vendedor = null, string? CriadoPor = null, string? ConfirmadoPor = null);

/// <summary>Resposta de ação que pode mandar mensagem ao cliente: Aviso explica quando não deu.</summary>
public record PedidoAcaoSaida(PedidoSaida Pedido, bool MensagemEnviada, string? Aviso);

public record CriarPedidoEntrada(Guid ReservaId, TipoEntrega TipoEntrega, decimal Frete, string? Cep, string? Endereco, string? Observacao, bool EnviarPix);

public record ConfirmarPagamentoEntrada(FormaPagamento Forma, bool AvisarCliente = true);

public record CancelarPedidoEntrada(string? Motivo);

public record ConfigPixSaida(string? Chave, string? TipoChave, string? Nome, string? Cidade, bool Configurado);

public record ConfigPixEntrada(string? Chave, string? Nome, string? Cidade);

/// <summary>Cartão pelo Mercado Pago: só leitura (as credenciais ficam no Render, nunca no banco).</summary>
public record ConfigCartaoSaida(bool Configurado, bool ModoTeste, bool WebhookPronto, bool AssinaturaConferida, string? UrlWebhook);

/// <summary>Pedidos e cobrança por Pix com a chave comum da loja (sem provedor, sem custo).</summary>
public static class PedidoEndpoints
{
    public static void MapPedidoEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/pedidos").WithTags("Pedidos").RequireAuthorization(AuthEndpoints.PoliticaEquipe);

        // Em aberto primeiro; filtros por situação ou conversa.
        grupo.MapGet("/", async (StatusPedido? status, Guid? conversaId, AppDbContext db, CancellationToken ct) =>
        {
            var consulta = db.Pedidos.AsNoTracking().Include(p => p.Itens).AsQueryable();
            if (status is { } s)
                consulta = consulta.Where(p => p.Status == s);
            if (conversaId is { } c)
                consulta = consulta.Where(p => p.ConversaId == c);
            var lista = await consulta
                .OrderBy(p => p.Status == StatusPedido.ConferirPagamento ? 0 : p.Status == StatusPedido.AguardandoPagamento ? 1 : 2)
                .ThenByDescending(p => p.Numero)
                .Take(200)
                .ToListAsync(ct);
            return await SaidasAsync(db, lista, ct);
        });

        grupo.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var pedido = await db.Pedidos.AsNoTracking().Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == id, ct);
            return pedido is null ? Results.NotFound() : Results.Ok((await SaidasAsync(db, [pedido], ct))[0]);
        });

        // CEP, endereço e frete que o sistema já sabe do cliente, para preencher o pedido.
        grupo.MapGet("/sugestao", async (Guid reservaId, ServicoPedidos pedidos, CancellationToken ct) =>
            await pedidos.SugerirAsync(reservaId, ct) is { } s ? Results.Ok(s) : Results.NotFound());

        grupo.MapPost("/", (CriarPedidoEntrada e, ServicoPedidos pedidos, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            Executar(db, async () => await pedidos.CriarAsync(e.ReservaId, new DadosEntrega(e.TipoEntrega, e.Frete, e.Cep, e.Endereco, e.Observacao), e.EnviarPix, Usuario(user), ct), ct));

        grupo.MapPost("/{id:guid}/enviar-pix", (Guid id, ServicoPedidos pedidos, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            Executar(db, () => pedidos.EnviarPixAsync(id, Usuario(user), ct), ct));

        grupo.MapPost("/{id:guid}/confirmar-pagamento", (Guid id, ConfirmarPagamentoEntrada e, ServicoPedidos pedidos, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
            Executar(db, () => pedidos.ConfirmarPagamentoAsync(id, e.Forma, e.AvisarCliente, Usuario(user), ct), ct));

        grupo.MapPost("/{id:guid}/cancelar", async (Guid id, CancelarPedidoEntrada e, ServicoPedidos pedidos, AppDbContext db, ClaimsPrincipal user, CancellationToken ct) =>
        {
            try
            {
                var pedido = await pedidos.CancelarAsync(id, e.Motivo, Usuario(user), ct);
                return pedido is null ? Results.NotFound() : Results.Ok((await SaidasAsync(db, [pedido], ct))[0]);
            }
            catch (RegraException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        });

        var config = app.MapGroup("/config/pix").WithTags("Pedidos").RequireAuthorization(AuthEndpoints.PoliticaEquipe);

        config.MapGet("/", async (AppDbContext db, ILojaAtual lojaAtual, CancellationToken ct) =>
        {
            var loja = await db.Lojas.AsNoTracking().FirstAsync(l => l.Id == lojaAtual.LojaId, ct);
            string? tipo = null;
            if (loja.PixChave is { } chave)
                try { tipo = RegrasPix.NormalizarChave(chave).Tipo.ToString(); } catch (RegraException) { }
            return new ConfigPixSaida(loja.PixChave, tipo, loja.PixNome, loja.PixCidade, ServicoPedidos.PixPronto(loja));
        });

        config.MapPut("/", async (ConfigPixEntrada e, AppDbContext db, ILojaAtual lojaAtual, CancellationToken ct) =>
        {
            var loja = await db.Lojas.FirstAsync(l => l.Id == lojaAtual.LojaId, ct);
            try
            {
                var (chave, tipo) = RegrasPix.NormalizarChave(e.Chave);
                var nome = RegrasPix.Limpar(e.Nome, 25);
                var cidade = RegrasPix.Limpar(e.Cidade, 15);
                // Gera um código de teste: se algo não couber no padrão, o erro aparece agora e não na hora de cobrar.
                RegrasPix.CopiaECola(chave, nome, cidade, 1m, "TESTE");
                loja.PixChave = chave;
                loja.PixNome = nome;
                loja.PixCidade = cidade;
                await db.SaveChangesAsync(ct);
                return Results.Ok(new ConfigPixSaida(chave, tipo.ToString(), nome, cidade, true));
            }
            catch (RegraException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);

        app.MapGet("/config/cartao", (IOptions<MercadoPagoOptions> opcoes) =>
        {
            var o = opcoes.Value;
            var token = o.AccessToken ?? "";
            // Credencial de teste: TEST-... (antiga) ou a de produção de um usuário de teste (aparece como teste no painel do MP).
            var teste = token.StartsWith("TEST-", StringComparison.Ordinal) || o.ModoTeste;
            return new ConfigCartaoSaida(o.Configurado, o.Configurado && teste, o.UrlWebhook?.StartsWith("https://") == true,
                !string.IsNullOrWhiteSpace(o.WebhookSecret), o.UrlWebhook);
        })
        .WithTags("Pedidos")
        .RequireAuthorization(AuthEndpoints.PoliticaEquipe);
    }

    private static Guid? Usuario(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : null;

    private static async Task<IResult> Executar(AppDbContext db, Func<Task<ResultadoAviso?>> acao, CancellationToken ct)
    {
        try
        {
            var r = await acao();
            if (r is null)
                return Results.NotFound();
            return Results.Ok(new PedidoAcaoSaida((await SaidasAsync(db, [r.Pedido], ct))[0], r.MensagemEnviada, r.Aviso));
        }
        catch (RegraException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static async Task<List<PedidoSaida>> SaidasAsync(AppDbContext db, List<Pedido> pedidos, CancellationToken ct)
    {
        var ids = pedidos.Select(p => p.ClienteId).Distinct().ToList();
        var clientes = await db.Clientes.AsNoTracking().Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => (c.Nome, c.Telefone), ct);
        var equipe = await ConversaEndpoints.NomesAsync(db, pedidos.SelectMany(p => new[] { p.VendedorId, p.CriadoPor, p.ConfirmadoPor }), ct);
        string? Nome(Guid? id) => id is { } u ? equipe.GetValueOrDefault(u) ?? "Usuário removido" : null;
        return pedidos.Select(p =>
        {
            var (nome, telefone) = clientes.GetValueOrDefault(p.ClienteId);
            return new PedidoSaida(
                p.Id, p.Numero, p.Status.ToString(), p.ClienteId, nome ?? telefone ?? "Cliente", telefone ?? "", p.ConversaId, p.ReservaId,
                p.TipoEntrega.ToString(), p.Cep, p.Endereco, p.Subtotal, p.Frete, p.Total,
                p.CriadoEm, p.ExpiraEm, p.PixCopiaECola, p.PixEnviadoEm, p.ComprovanteEm, p.LinkCartao, p.MpPagamentoId is not null,
                p.FormaPagamento?.ToString(), p.PagoEm, p.CanceladoEm, p.MotivoCancelamento, p.Observacao,
                p.Itens.Select(i => new ItemPedidoSaida(i.Especie, i.Quantidade, i.PrecoUnitario)).ToList(),
                p.VendedorId, Nome(p.VendedorId), Nome(p.CriadoPor), Nome(p.ConfirmadoPor));
        }).ToList();
    }
}
