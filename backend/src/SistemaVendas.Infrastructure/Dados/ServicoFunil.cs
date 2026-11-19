using Microsoft.EntityFrameworkCore;
using SistemaVendas.Domain.Atendimento;
using SistemaVendas.Domain.Funil;

namespace SistemaVendas.Infrastructure.Dados;

/// <summary>Acesso à configuração do funil e ao histórico de etapas da loja atual.</summary>
public class ServicoFunil(AppDbContext db)
{
    /// <summary>Configuração da loja; cria com os padrões do documento na primeira vez.</summary>
    public async Task<ConfigFunil> ConfigAsync(Guid lojaId, CancellationToken ct)
    {
        // Olha primeiro o que já foi criado nesta mesma operação e ainda não foi salvo (senão cria duas).
        var cfg = db.ConfigsFunil.Local.FirstOrDefault(c => c.LojaId == lojaId)
            ?? await db.ConfigsFunil.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.LojaId == lojaId, ct);
        if (cfg is not null)
            return cfg;

        cfg = new ConfigFunil { LojaId = lojaId };
        // Loja que não é de aquário: o follow-up padrão não fala de peixe.
        var segmento = await db.Lojas.AsNoTracking().Where(l => l.Id == lojaId).Select(l => l.Segmento).FirstOrDefaultAsync(ct);
        if (!Domain.Lojas.Vocabulario.Para(segmento).Aquario)
            cfg.TextoFollowup1 = ConfigFunil.Followup1Geral;
        db.ConfigsFunil.Add(cfg);
        return cfg;
    }

    public void Registrar(Conversa conversa, MudancaEtapa? mudanca, OrigemEvento origem, Guid? usuarioId = null)
    {
        if (mudanca is null)
            return;

        db.CardEventos.Add(new CardEvento
        {
            LojaId = conversa.LojaId,
            ConversaId = conversa.Id,
            DeEtapa = mudanca.De,
            ParaEtapa = mudanca.Para,
            Origem = origem,
            UsuarioId = usuarioId,
            Detalhe = mudanca.Detalhe
        });
    }
}
