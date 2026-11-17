using SistemaVendas.Domain.Comum;
using SistemaVendas.Domain.Estoque;

namespace SistemaVendas.Domain.Vendas;

public enum StatusReserva
{
    Ativa,
    /// <summary>Vendedor confirmou a venda: o peixe sai do estoque de vez.</summary>
    Confirmada,
    Cancelada,
    /// <summary>Passou de 24 h sem confirmar: o peixe volta ao estoque.</summary>
    Expirada
}

/// <summary>
/// Peixe separado para um cliente por 24 h. Enquanto ativa, o robô e a vitrine não oferecem
/// essas unidades para mais ninguém (movimento "reserva" no estoque).
/// </summary>
public class Reserva : EntidadeDaLoja
{
    public Guid ConversaId { get; set; }
    public StatusReserva Status { get; set; } = StatusReserva.Ativa;
    public DateTimeOffset ExpiraEm { get; set; }
    public DateTimeOffset? EncerradaEm { get; set; }
    public Guid? EncerradaPor { get; set; }
    public decimal Total { get; set; }
    public List<ItemReserva> Itens { get; set; } = [];
}

/// <summary>Um lote de onde saiu parte da reserva. Uma espécie pode vir de mais de um lote.</summary>
public class ItemReserva : EntidadeDaLoja
{
    public Guid ReservaId { get; set; }
    public Guid EspecieId { get; set; }
    public required string Especie { get; set; }
    public Guid LoteId { get; set; }
    public Guid TanqueId { get; set; }
    public int Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
}

/// <summary>O que o cliente pediu: espécie e quantidade.</summary>
public record ItemPedido(Guid EspecieId, int Quantidade);

/// <summary>Saldo de um lote liberado, de onde a reserva pode tirar.</summary>
public record SaldoParaReserva(Guid LoteId, Guid EspecieId, Guid TanqueId, int Disponivel, DateTimeOffset RecebidoEm);

public record EspecieParaReserva(Guid Id, string Nome, decimal Preco);

/// <summary>Resultado: a reserva (ou null se nada pôde ser separado), os movimentos e o que faltou.</summary>
public record ResultadoReserva(Reserva? Reserva, List<MovimentoEstoque> Movimentos, List<string> Faltas);

public static class RegrasReserva
{
    public static readonly TimeSpan Duracao = TimeSpan.FromHours(24);

    /// <summary>
    /// Separa o pedido nos lotes mais antigos primeiro. Se não houver o suficiente, separa o que tem
    /// e devolve a falta, para o robô avisar o cliente.
    /// </summary>
    public static ResultadoReserva Criar(
        Guid lojaId, Guid conversaId, IEnumerable<ItemPedido> pedido,
        IReadOnlyCollection<SaldoParaReserva> saldos, IReadOnlyDictionary<Guid, EspecieParaReserva> especies, DateTimeOffset agora)
    {
        var reserva = new Reserva { LojaId = lojaId, ConversaId = conversaId, ExpiraEm = agora + Duracao };
        var movimentos = new List<MovimentoEstoque>();
        var faltas = new List<string>();

        foreach (var item in pedido.Where(i => i.Quantidade > 0).GroupBy(i => i.EspecieId).Select(g => new ItemPedido(g.Key, g.Sum(i => i.Quantidade))))
        {
            if (!especies.TryGetValue(item.EspecieId, out var especie))
            {
                // Id que a IA errou ou espécie desativada: avisa em vez de sumir com o item calado.
                faltas.Add("um dos itens não foi encontrado no catálogo, o atendente confere");
                continue;
            }

            var falta = item.Quantidade;
            // O lote recebido há mais tempo sai primeiro.
            foreach (var saldo in saldos.Where(s => s.EspecieId == item.EspecieId && s.Disponivel > 0).OrderBy(s => s.RecebidoEm))
            {
                if (falta == 0)
                    break;
                var qtd = Math.Min(falta, saldo.Disponivel);
                falta -= qtd;

                reserva.Itens.Add(new ItemReserva
                {
                    LojaId = lojaId, ReservaId = reserva.Id, EspecieId = especie.Id, Especie = especie.Nome,
                    LoteId = saldo.LoteId, TanqueId = saldo.TanqueId, Quantidade = qtd, PrecoUnitario = especie.Preco
                });
                movimentos.Add(Movimento(lojaId, saldo.LoteId, saldo.TanqueId, TipoMovimento.Reserva, qtd, reserva.Id, null));
            }

            if (falta > 0)
            {
                var separado = item.Quantidade - falta;
                faltas.Add(separado == 0
                    ? $"{especie.Nome}: sem estoque no momento"
                    : $"{especie.Nome}: só havia {separado} de {item.Quantidade}");
            }
        }

        reserva.Total = reserva.Itens.Sum(i => i.Quantidade * i.PrecoUnitario);
        return new ResultadoReserva(reserva.Itens.Count > 0 ? reserva : null, reserva.Itens.Count > 0 ? movimentos : [], faltas);
    }

    /// <summary>
    /// Encerra a reserva. Sempre devolve o separado ("libera reserva"); se a venda foi confirmada,
    /// registra a venda na mesma hora, então o saldo final só cai uma vez.
    /// </summary>
    public static List<MovimentoEstoque> Encerrar(Reserva reserva, StatusReserva status, DateTimeOffset agora, Guid? usuarioId)
    {
        if (reserva.Status != StatusReserva.Ativa)
            throw new RegraException("Esta reserva já foi encerrada.");
        if (status == StatusReserva.Ativa)
            throw new RegraException("Escolha como encerrar a reserva.");

        reserva.Status = status;
        reserva.EncerradaEm = agora;
        reserva.EncerradaPor = usuarioId;

        var movimentos = new List<MovimentoEstoque>();
        foreach (var item in reserva.Itens)
        {
            movimentos.Add(Movimento(reserva.LojaId, item.LoteId, item.TanqueId, TipoMovimento.LiberaReserva, item.Quantidade, reserva.Id, usuarioId));
            if (status == StatusReserva.Confirmada)
                movimentos.Add(Movimento(reserva.LojaId, item.LoteId, item.TanqueId, TipoMovimento.Venda, item.Quantidade, reserva.Id, usuarioId));
        }
        return movimentos;
    }

    public static bool Vencida(Reserva reserva, DateTimeOffset agora) => reserva.Status == StatusReserva.Ativa && agora >= reserva.ExpiraEm;

    /// <summary>Linhas "2x Ramirezi" para mensagens e para o card.</summary>
    public static string Resumo(Reserva reserva) =>
        string.Join(", ", reserva.Itens.GroupBy(i => i.Especie).Select(g => $"{g.Sum(i => i.Quantidade)}x {g.Key}"));

    private static MovimentoEstoque Movimento(Guid lojaId, Guid loteId, Guid tanqueId, TipoMovimento tipo, int qtd, Guid reservaId, Guid? usuarioId) => new()
    {
        LojaId = lojaId, LoteId = loteId, TanqueId = tanqueId, Tipo = tipo, Quantidade = qtd, PedidoId = reservaId, UsuarioId = usuarioId,
        Observacao = tipo switch
        {
            TipoMovimento.Reserva => "Reserva de 24 h",
            TipoMovimento.LiberaReserva => "Fim da reserva",
            TipoMovimento.Venda => "Venda confirmada",
            _ => null
        }
    };
}

/// <summary>Lê o pedido que a vitrine manda pelo WhatsApp ("• 2x Ramirezi (casal) (R$ 60,00 cada) = R$ 120,00").</summary>
public static class LeitorPedidoVitrine
{
    private static readonly System.Text.RegularExpressions.Regex Linha =
        new(@"^\s*[•\-*]?\s*(\d{1,3})\s*x\s+(.+?)\s+\(R\$", System.Text.RegularExpressions.RegexOptions.Multiline);

    public static List<(string Nome, int Quantidade)> Ler(string mensagem) =>
        Linha.Matches(mensagem).Select(m => (m.Groups[2].Value.Trim(), int.Parse(m.Groups[1].Value))).ToList();
}
