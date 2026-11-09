using SistemaVendas.Domain.Funil;

namespace SistemaVendas.Domain.Atendimento;

/// <summary>
/// Cliente quente que ninguém assumiu: aos 15 min o painel volta a avisar; com 1 h o aviso vai para o dono
/// e o robô diz ao cliente que um atendente já foi chamado (ele não fica falando sozinho).
/// </summary>
public static class LeadQuente
{
    public static readonly TimeSpan Lembrete = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Escalada = TimeSpan.FromMinutes(60);

    public static bool SemDono(Conversa c) => c.Aberta && c.ChamouVendedor && c.ChamouVendedorEm is not null && c.AtendidoEm is null;

    /// <summary>0 = recém-chamado, 1 = passou de 15 min, 2 = passou de 1 h.</summary>
    public static int Nivel(Conversa c, DateTimeOffset agora)
    {
        if (!SemDono(c))
            return 0;
        var espera = agora - c.ChamouVendedorEm!.Value;
        return espera >= Escalada ? 2 : espera >= Lembrete ? 1 : 0;
    }

    /// <summary>Sobe o nível de aviso. Devolve o texto para o cliente quando chega a 1 h (uma vez só).</summary>
    public static string? Avancar(Conversa c, DateTimeOffset agora, TimeZoneInfo fuso, TimeOnly abre, TimeOnly fecha)
    {
        var nivel = Nivel(c, agora);
        if (nivel <= c.AvisosQuente)
            return null;

        c.AvisosQuente = nivel;
        if (nivel < 2)
            return null;

        var aberta = HorarioComercial.ProximoHorarioPermitido(agora, fuso, abre, fecha) == agora;
        return aberta
            ? "Já chamei o responsável da loja e um atendente vai falar com você em instantes. Obrigado pela paciência! 🙏"
            : $"Já deixei seu pedido com o responsável da loja. Assim que abrirmos, às {abre:HH\\:mm}, um atendente fala com você. 🙏";
    }
}
