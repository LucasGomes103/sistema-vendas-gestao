using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;

namespace SistemaVendas.Infrastructure.Assinaturas;

public enum RecursoDoPlano
{
    Cartao,
    Entregas,
    DominioProprio
}

/// <summary>Quanto a loja usou no mês (conversas e IA) e agora (usuários ativos e produtos ativos).</summary>
public record UsoDoPlano(int Conversas, int Usuarios, int Produtos, int RespostasIa);

/// <summary>
/// Limites do plano da loja. "Conversa" = cliente diferente que escreveu no mês (horário de Brasília), sem contar o
/// simulador. Loja isenta (sem assinatura ou a loja original) não tem limite nenhum.
/// </summary>
public class ServicoLimites(AppDbContext db, ServicoAssinatura assinaturas)
{
    /// <summary>Início do mês corrente no horário de Brasília.</summary>
    public static DateTimeOffset InicioDoMes(DateTimeOffset agora)
    {
        var dia = RegrasLimiteIa.InicioDoMes(agora);
        return new DateTimeOffset(dia.Year, dia.Month, 1, 0, 0, 0, TimeSpan.FromHours(-3)).ToUniversalTime();
    }

    /// <summary>Primeira mensagem do mês de cada cliente da loja (conversas reais).</summary>
    private IQueryable<PrimeiraDoMes> PrimeirasDoMes(Guid lojaId, DateTimeOffset inicio) =>
        from m in db.Mensagens.IgnoreQueryFilters()
        join c in db.Conversas.IgnoreQueryFilters() on m.ConversaId equals c.Id
        where m.LojaId == lojaId && c.LojaId == lojaId && m.Direcao == DirecaoMensagem.Entrada && m.CriadoEm >= inicio && !c.Simulada
        group m by c.ClienteId into g
        select new PrimeiraDoMes { ClienteId = g.Key, Em = g.Min(x => x.CriadoEm) };

    private sealed class PrimeiraDoMes
    {
        public Guid ClienteId { get; init; }
        public DateTimeOffset Em { get; init; }
    }

    public Task<int> ConversasDoMesAsync(Guid lojaId, DateTimeOffset agora, CancellationToken ct) =>
        PrimeirasDoMes(lojaId, InicioDoMes(agora)).CountAsync(ct);

    /// <summary>
    /// O robô pode atender este cliente? Sim se a loja não tem limite ou se o cliente está entre os primeiros N que
    /// escreveram no mês (quem já estava sendo atendido não é cortado quando chega gente nova).
    /// </summary>
    public async Task<bool> ClienteDentroDoLimiteAsync(Guid lojaId, Guid clienteId, DateTimeOffset agora, CancellationToken ct)
    {
        if ((await assinaturas.EstadoAsync(lojaId, ct)).Plano is not { } plano)
            return true;
        var primeiras = PrimeirasDoMes(lojaId, InicioDoMes(agora));
        var minha = await primeiras.Where(p => p.ClienteId == clienteId).Select(p => (DateTimeOffset?)p.Em).FirstOrDefaultAsync(ct);
        if (minha is null)
            return true;
        return await primeiras.CountAsync(p => p.Em < minha, ct) < plano.Conversas;
    }

    public async Task<UsoDoPlano> UsoAsync(Guid lojaId, int respostasIa, DateTimeOffset agora, CancellationToken ct) => new(
        await ConversasDoMesAsync(lojaId, agora, ct),
        await db.Usuarios.IgnoreQueryFilters().CountAsync(u => u.LojaId == lojaId && u.Ativo, ct),
        await db.Especies.IgnoreQueryFilters().CountAsync(e => e.LojaId == lojaId && e.Ativo, ct),
        respostasIa);

    /// <summary>Erro para mostrar quando a loja tenta ter mais um usuário ativo além do plano. Null = pode.</summary>
    public async Task<string?> ErroMaisUmUsuarioAsync(Guid lojaId, CancellationToken ct)
    {
        if ((await assinaturas.EstadoAsync(lojaId, ct)).Plano is not { } plano)
            return null;
        var ativos = await db.Usuarios.IgnoreQueryFilters().CountAsync(u => u.LojaId == lojaId && u.Ativo, ct);
        return ativos < plano.Usuarios ? null
            : $"O plano {plano.Nome} permite {plano.Usuarios} usuários ativos. Desative alguém ou troque de plano em Assinatura.";
    }

    /// <summary>Erro para mostrar quando a loja tenta cadastrar mais um produto além do plano. Null = pode.</summary>
    public async Task<string?> ErroMaisUmProdutoAsync(Guid lojaId, CancellationToken ct)
    {
        if ((await assinaturas.EstadoAsync(lojaId, ct)).Plano is not { Produtos: { } limite } plano)
            return null;
        var ativos = await db.Especies.IgnoreQueryFilters().CountAsync(e => e.LojaId == lojaId && e.Ativo, ct);
        return ativos < limite ? null
            : $"O plano {plano.Nome} permite até {limite:N0} produtos. Remova algum ou troque de plano em Assinatura.";
    }

    /// <summary>O plano da loja inclui o recurso? Isenta: sim.</summary>
    public async Task<bool> TemRecursoAsync(Guid lojaId, RecursoDoPlano recurso, CancellationToken ct) =>
        (await assinaturas.EstadoAsync(lojaId, ct)).Plano is not { } plano || recurso switch
        {
            RecursoDoPlano.Cartao => plano.Cartao,
            RecursoDoPlano.Entregas => plano.Entregas,
            RecursoDoPlano.DominioProprio => plano.DominioProprio,
            _ => false
        };

    public static string NomeDoRecurso(RecursoDoPlano r) => r switch
    {
        RecursoDoPlano.Cartao => "o link de cartão",
        RecursoDoPlano.Entregas => "as entregas",
        _ => "o domínio próprio"
    };
}
