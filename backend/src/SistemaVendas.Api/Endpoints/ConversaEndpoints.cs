using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SistemaVendas.Application.Atendimento;
using SistemaVendas.Application.Comum;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Funil;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.WhatsApp;

namespace SistemaVendas.Api.Endpoints;

public record ConversaResumo(
    Guid Id,
    Guid ClienteId,
    string Cliente,
    string Telefone,
    string? UltimaMensagem,
    string? UltimoAutor,
    DateTimeOffset? UltimaMensagemEm,
    int NaoLidas,
    bool DentroDaJanela,
    DateTimeOffset? JanelaFechaEm,
    bool Simulada,
    bool RoboPausado,
    bool ChamouVendedor,
    string? MotivoVendedor,
    DateTimeOffset? ChamouVendedorEm,
    bool SemDono);

/// <summary>Cliente quente esperando um vendedor. Nível 1 = mais de 15 min, 2 = mais de 1 h (aviso ao dono).</summary>
public record AlertaQuente(Guid ConversaId, string Cliente, string? Motivo, DateTimeOffset ChamouVendedorEm, int Nivel, bool Simulada);

public record MensagemSaida(Guid Id, string Direcao, string Autor, string Conteudo, string Status, string? Erro, DateTimeOffset CriadoEm);

public record EnviarEntrada(string Texto);

public record SimularEntrada(string Telefone, string? Nome, string Texto);

public record NumeroWhatsAppEntrada(string? PhoneNumberId);

public record WhatsAppLojaEntrada(string? Numero);

public static class ConversaEndpoints
{
    public static void MapConversaEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/conversas").WithTags("Conversas").RequireAuthorization(AuthEndpoints.PoliticaEquipe);

        grupo.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var agora = DateTimeOffset.UtcNow;
            var lista = await db.Conversas.AsNoTracking()
                .Where(c => c.Aberta)
                .OrderByDescending(c => c.ChamouVendedor)
                .ThenByDescending(c => c.UltimaMensagemEm)
                .Take(100)
                .Join(db.Clientes, c => c.ClienteId, cl => cl.Id, (c, cl) => new
                {
                    Conversa = c,
                    cl.Nome,
                    cl.Telefone,
                    Ultima = db.Mensagens.Where(m => m.ConversaId == c.Id).OrderByDescending(m => m.CriadoEm)
                        .Select(m => new { m.Conteudo, m.Autor }).FirstOrDefault()
                })
                .ToListAsync(ct);

            return lista.Select(x => new ConversaResumo(
                x.Conversa.Id, x.Conversa.ClienteId, x.Nome ?? FormatarTelefone(x.Telefone), x.Telefone,
                x.Ultima?.Conteudo, x.Ultima?.Autor.ToString(), x.Conversa.UltimaMensagemEm, x.Conversa.NaoLidas,
                x.Conversa.DentroDaJanela(agora), x.Conversa.UltimaMsgClienteEm + Conversa.Janela, x.Conversa.Simulada,
                x.Conversa.RoboPausado, x.Conversa.ChamouVendedor, x.Conversa.MotivoVendedor, x.Conversa.ChamouVendedorEm,
                LeadQuente.SemDono(x.Conversa)));
        });

        // Clientes quentes sem dono, para o aviso no topo do painel (consultado a cada poucos segundos).
        grupo.MapGet("/alertas", async (AppDbContext db, CancellationToken ct) =>
        {
            var agora = DateTimeOffset.UtcNow;
            var lista = await db.Conversas.AsNoTracking()
                .Where(c => c.Aberta && c.ChamouVendedor && c.ChamouVendedorEm != null && c.AtendidoEm == null)
                .OrderBy(c => c.ChamouVendedorEm)
                .Take(50)
                .Join(db.Clientes, c => c.ClienteId, cl => cl.Id, (c, cl) => new { Conversa = c, cl.Nome, cl.Telefone })
                .ToListAsync(ct);

            return lista.Select(x => new AlertaQuente(
                x.Conversa.Id, x.Nome ?? FormatarTelefone(x.Telefone), x.Conversa.MotivoVendedor,
                x.Conversa.ChamouVendedorEm!.Value, LeadQuente.Nivel(x.Conversa, agora), x.Conversa.Simulada));
        });

        grupo.MapGet("/{id:guid}/mensagens", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var conversa = await db.Conversas.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (conversa is null)
                return Results.NotFound();

            // Abrir a conversa no painel marca como lida.
            if (conversa.NaoLidas > 0)
            {
                conversa.NaoLidas = 0;
                await db.SaveChangesAsync(ct);
            }

            var mensagens = await db.Mensagens.AsNoTracking()
                .Where(m => m.ConversaId == id)
                .OrderBy(m => m.CriadoEm)
                .Select(m => new MensagemSaida(m.Id, m.Direcao.ToString(), m.Autor.ToString(), m.Conteudo, m.Status.ToString(), m.Erro, m.CriadoEm))
                .ToListAsync(ct);

            return Results.Ok(mensagens);
        });

        // Envio pelo vendedor: vai para o outbox; texto livre só dentro da janela de 24 h.
        grupo.MapPost("/{id:guid}/mensagens", async (Guid id, EnviarEntrada entrada, AppDbContext db, ServicoFunil funil, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(entrada.Texto))
                return Results.Problem("Escreva a mensagem.", statusCode: StatusCodes.Status422UnprocessableEntity);

            var conversa = await db.Conversas.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (conversa is null)
                return Results.NotFound();

            var agora = DateTimeOffset.UtcNow;
            if (!conversa.DentroDaJanela(agora))
                return Results.Problem(
                    "Passaram 24 h desde a última mensagem do cliente. Pela regra da Meta, agora só é possível enviar um modelo aprovado.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            var mensagem = new Mensagem
            {
                ConversaId = conversa.Id,
                Direcao = DirecaoMensagem.Saida,
                Autor = AutorMensagem.Vendedor,
                Conteudo = entrada.Texto.Trim(),
                Status = StatusMensagem.Pendente,
                Categoria = CategoriaMensagem.Servico,
                UsuarioId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var u) ? u : null
            };
            db.Mensagens.Add(mensagem);
            conversa.UltimaMensagemEm = agora;
            conversa.Assumir(agora); // vendedor respondeu: o robô para de falar nesta conversa
            var cfg = await funil.ConfigAsync(conversa.LojaId, ct);
            funil.Registrar(conversa, MotorFunil.AoResponderLoja(conversa, cfg, agora), OrigemEvento.Usuario);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new MensagemSaida(mensagem.Id, mensagem.Direcao.ToString(), mensagem.Autor.ToString(),
                mensagem.Conteudo, mensagem.Status.ToString(), null, mensagem.CriadoEm));
        });

        // Conversa do simulador é apagada de vez; conversa real é só arquivada (o histórico de vendas depende dela).
        // Conversa real: arquiva (some da lista, histórico fica) ou, com definitivo=true, apaga do sistema.
        // Simulada: sempre apaga. Nada é apagado do celular.
        grupo.MapDelete("/{id:guid}", async (Guid id, bool? definitivo, AppDbContext db, CancellationToken ct) =>
        {
            var conversa = await db.Conversas.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (conversa is null)
                return Results.NotFound();

            if (conversa.Simulada || definitivo == true)
            {
                await db.Mensagens.Where(m => m.ConversaId == id).ExecuteDeleteAsync(ct);
                await db.CardEventos.Where(e => e.ConversaId == id).ExecuteDeleteAsync(ct);
                db.Conversas.Remove(conversa);
            }
            else
            {
                conversa.Aberta = false;
                conversa.AguardandoRobo = false;
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);

        grupo.MapPost("/{id:guid}/assumir", (Guid id, AppDbContext db, CancellationToken ct) =>
            AlterarAsync(db, id, c => c.Assumir(), ct));

        grupo.MapPost("/{id:guid}/devolver", async (Guid id, AppDbContext db, ServicoFunil funil, CancellationToken ct) =>
        {
            var conversa = await db.Conversas.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (conversa is null)
                return Results.NotFound();

            conversa.DevolverAoRobo();
            var mudanca = conversa.Etapa == EtapaCard.Quente ? MotorFunil.Mover(conversa, EtapaCard.RoboAtendendo, null) : null;
            var cfg = await funil.ConfigAsync(conversa.LojaId, ct);
            MotorFunil.AoResponderLoja(conversa, cfg, DateTimeOffset.UtcNow);
            funil.Registrar(conversa, mudanca, OrigemEvento.Usuario);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        grupo.MapGet("/status-whatsapp", async (IWhatsAppCliente whatsApp, IAtendenteIA atendente, Microsoft.Extensions.Options.IOptions<WhatsAppOptions> opcoesWa, AppDbContext db, ILojaAtual lojaAtual, IHostEnvironment env, IConfiguration config, CancellationToken ct) =>
        {
            var phoneNumberId = await db.Lojas.Where(l => l.Id == lojaAtual.LojaId).Select(l => l.PhoneNumberId).FirstOrDefaultAsync(ct);
            // Simulador disponível mesmo com o número real ligado: as conversas dele nunca saem pelo WhatsApp.
            var simulador = env.IsDevelopment() || config.GetValue("Simulador:Habilitado", true);
            return Results.Ok(new { simulado = whatsApp.Simulado, simuladorDisponivel = simulador, phoneNumberId, atendente = atendente.Nome, provedor = opcoesWa.Value.UsaPonteLocal ? "Local" : "Meta" });
        });

        // Número do WhatsApp da loja (phone_number_id da Meta). Não é segredo; o token fica fora do banco.
        grupo.MapPut("/numero-whatsapp", async (NumeroWhatsAppEntrada entrada, AppDbContext db, ILojaAtual lojaAtual, CancellationToken ct) =>
        {
            var id = entrada.PhoneNumberId?.Trim();
            if (string.IsNullOrEmpty(id) || !id.All(char.IsDigit))
                return Results.Problem("Informe o Phone Number ID (só números).", statusCode: StatusCodes.Status422UnprocessableEntity);

            var loja = await db.Lojas.FirstAsync(l => l.Id == lojaAtual.LojaId, ct);
            loja.PhoneNumberId = id;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);

        // Número que o cliente vê (botão "pedir pelo WhatsApp" da vitrine).
        grupo.MapPut("/whatsapp-loja", async (WhatsAppLojaEntrada entrada, AppDbContext db, ILojaAtual lojaAtual, CancellationToken ct) =>
        {
            var numero = new string((entrada.Numero ?? "").Where(char.IsDigit).ToArray());
            if (numero.Length is < 12 or > 15)
                return Results.Problem("Informe o número com DDI e DDD, ex.: 5511987654321.", statusCode: StatusCodes.Status422UnprocessableEntity);

            var loja = await db.Lojas.FirstAsync(l => l.Id == lojaAtual.LojaId, ct);
            loja.WhatsAppNumero = numero;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(AuthEndpoints.PoliticaDono);
    }

    /// <summary>
    /// Simulador de cliente (desligável com Simulador:Habilitado=false): monta um webhook igual ao da Meta e coloca na fila,
    /// passando pelo mesmo caminho de uma mensagem real.
    /// </summary>
    public static void MapSimuladorEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/dev/simular-mensagem", async (SimularEntrada entrada, AppDbContext db, ILojaAtual lojaAtual, CancellationToken ct) =>
        {
            var telefone = new string(entrada.Telefone.Where(char.IsDigit).ToArray());
            if (telefone.Length < 10 || string.IsNullOrWhiteSpace(entrada.Texto))
                return Results.Problem("Informe telefone (com DDD) e texto.", statusCode: StatusCodes.Status422UnprocessableEntity);
            if (!telefone.StartsWith("55"))
                telefone = "55" + telefone;

            var loja = await db.Lojas.AsNoTracking().FirstAsync(l => l.Id == lojaAtual.LojaId, ct);
            // Número próprio do simulador: as respostas dessas conversas nunca saem pela Meta.
            var phoneNumberId = WhatsAppOptions.PrefixoSimulador + loja.Slug;

            var payload = JsonSerializer.Serialize(new
            {
                @object = "whatsapp_business_account",
                entry = new[]
                {
                    new
                    {
                        id = "simulador",
                        changes = new[]
                        {
                            new
                            {
                                field = "messages",
                                value = new
                                {
                                    messaging_product = "whatsapp",
                                    metadata = new { display_phone_number = "simulador", phone_number_id = phoneNumberId },
                                    contacts = new[] { new { profile = new { name = entrada.Nome ?? "Cliente" }, wa_id = telefone } },
                                    messages = new[]
                                    {
                                        new
                                        {
                                            from = telefone,
                                            id = $"sim.{Guid.CreateVersion7():N}",
                                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                                            type = "text",
                                            text = new { body = entrada.Texto }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            });

            db.WebhooksRecebidos.Add(new WebhookRecebido { Origem = "simulador", Payload = payload });
            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        })
        .WithTags("Desenvolvimento")
        .RequireAuthorization(AuthEndpoints.PoliticaEquipe);
    }

    private static async Task<IResult> AlterarAsync(AppDbContext db, Guid id, Action<Conversa> alterar, CancellationToken ct)
    {
        var conversa = await db.Conversas.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conversa is null)
            return Results.NotFound();
        alterar(conversa);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static string FormatarTelefone(string telefone)
    {
        if (telefone.StartsWith("lid:"))
            return "Contato do WhatsApp";
        var local = telefone.StartsWith("55") && telefone.Length > 11 ? telefone[2..] : telefone;
        return local.Length == 11 ? $"({local[..2]}) {local[2..7]}-{local[7..]}" : telefone;
    }
}
