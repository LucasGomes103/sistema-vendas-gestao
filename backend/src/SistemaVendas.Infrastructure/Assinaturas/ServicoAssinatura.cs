using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SistemaVendas.Domain.Lojas;
using SistemaVendas.Infrastructure.Dados;
using SistemaVendas.Infrastructure.Seguranca;

namespace SistemaVendas.Infrastructure.Assinaturas;

/// <summary>Cobrança da mensalidade das lojas (seção "Assinaturas").</summary>
public class AssinaturasOptions
{
    public const string Secao = "Assinaturas";

    /// <summary>Dias depois do vencimento em que a loja segue funcionando, com aviso (Assinaturas__DiasCarencia).</summary>
    public int DiasCarencia { get; set; } = RegrasAssinatura.DiasCarenciaPadrao;
}

/// <summary>Assinatura de uma loja e a situação de hoje.</summary>
public record EstadoAssinatura(
    Assinatura? Assinatura, SituacaoAssinatura Situacao, Plano? Plano, DateTimeOffset? Vencimento, DateTimeOffset? BloqueiaEm, bool LojaOriginal)
{
    public bool Bloqueada => Situacao == SituacaoAssinatura.Bloqueada;

    /// <summary>Loja sem assinatura (as de antes da cobrança) ou a loja original: tudo liberado, sem limite de plano.</summary>
    public static EstadoAssinatura Isenta(Assinatura? a, bool original) => new(a, SituacaoAssinatura.Isenta, null, null, null, original);
}

/// <summary>Lê a assinatura da loja e decide a situação. A loja original (ECS Fish) é sempre isenta.</summary>
public class ServicoAssinatura(AppDbContext db, CredenciaisDaLoja credenciais, IOptions<AssinaturasOptions> opcoes)
{
    private readonly Dictionary<Guid, EstadoAssinatura> _cache = [];

    public int DiasCarencia => Math.Max(0, opcoes.Value.DiasCarencia);

    public async Task<EstadoAssinatura> EstadoAsync(Guid lojaId, CancellationToken ct, DateTimeOffset? agora = null)
    {
        if (agora is null && _cache.TryGetValue(lojaId, out var guardado))
            return guardado;
        var a = await db.Assinaturas.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.LojaId == lojaId, ct);
        var estado = Calcular(a, await credenciais.LojaOriginalAsync(ct) == lojaId, agora ?? DateTimeOffset.UtcNow, DiasCarencia);
        if (agora is null)
            _cache[lojaId] = estado;
        return estado;
    }

    public static EstadoAssinatura Calcular(Assinatura? a, bool lojaOriginal, DateTimeOffset agora, int diasCarencia)
    {
        if (lojaOriginal || a is null || a.Status == StatusAssinatura.Isenta)
            return EstadoAssinatura.Isenta(a, lojaOriginal);
        return new(a, RegrasAssinatura.Situacao(a, agora, diasCarencia), Planos.De(a.Plano), RegrasAssinatura.Vencimento(a),
            RegrasAssinatura.BloqueiaEm(a, diasCarencia), false);
    }

    /// <summary>Plano de cada loja com limites (as isentas ficam de fora). Para a tela de lojas do administrador.</summary>
    public async Task<Dictionary<Guid, Assinatura>> TodasAsync(CancellationToken ct) =>
        await db.Assinaturas.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(a => a.LojaId, ct);
}

/// <summary>O teste grátis conta a partir de quando a loja passa a usar o painel (e-mail confirmado).</summary>
public static class RegrasDoTeste
{
    public static async Task ComecarAsync(AppDbContext db, Guid lojaId, DateTimeOffset agora, CancellationToken ct)
    {
        var a = await db.Assinaturas.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.LojaId == lojaId, ct);
        if (a is { Status: StatusAssinatura.Teste })
        {
            a.TesteAte = agora.AddDays(RegrasAssinatura.DiasTeste);
            a.AtualizadoEm = agora;
        }
    }
}
