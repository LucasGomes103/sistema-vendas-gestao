using SistemaVendas.Domain.Comum;
using SistemaVendas.Domain.Estoque;
using SistemaVendas.Domain.Vendas;

namespace SistemaVendas.Domain.Caixa;

public enum TipoLancamento
{
    Entrada,
    Saida
}

/// <summary>Entrada ou saída de dinheiro do dia. Venda paga entra sozinha; despesas o dono lança.</summary>
public class LancamentoCaixa : EntidadeDaLoja
{
    public TipoLancamento Tipo { get; set; }
    public required string Categoria { get; set; }
    public decimal Valor { get; set; }
    public string? Descricao { get; set; }
    public FormaPagamento Forma { get; set; }

    /// <summary>Dia do caixa no fuso da loja.</summary>
    public DateOnly Data { get; set; }

    /// <summary>Preenchido quando o lançamento veio do pagamento de um pedido.</summary>
    public Guid? PedidoId { get; set; }
    public Guid? UsuarioId { get; set; }
}

/// <summary>Fechamento do dia: guarda os totais e quanto dinheiro havia na gaveta. Dia fechado não aceita lançamento manual.</summary>
public class FechamentoCaixa : EntidadeDaLoja
{
    public DateOnly Data { get; set; }
    public decimal Entradas { get; set; }
    public decimal Saidas { get; set; }
    public decimal Saldo { get; set; }
    public decimal DinheiroEsperado { get; set; }
    public decimal? DinheiroContado { get; set; }
    public decimal? Diferenca { get; set; }
    public string? Observacao { get; set; }
    public Guid? FechadoPor { get; set; }
}

public record TotaisForma(FormaPagamento Forma, decimal Entradas, decimal Saidas);

public record ResumoCaixa(decimal Entradas, decimal Saidas, decimal Saldo, decimal DinheiroEsperado, List<TotaisForma> PorForma);

public static class RegrasCaixa
{
    public const string CategoriaVenda = "Venda";

    public static readonly string[] CategoriasEntrada = [CategoriaVenda, "Venda no balcão", "Outras entradas"];

    public static readonly string[] CategoriasSaida =
        ["Compra de peixes", "Ração e insumos", "Energia e água", "Aluguel", "Embalagem e entrega", "Manutenção", "Outras despesas"];

    /// <summary>Categorias de saída de uma loja que não é de aquário.</summary>
    public static readonly string[] CategoriasSaidaGeral =
        ["Compra de mercadoria", "Insumos", "Energia e água", "Aluguel", "Embalagem e entrega", "Manutenção", "Outras despesas"];

    public static string[] CategoriasSaidaPara(Lojas.Vocabulario v) => v.Aquario ? CategoriasSaida : CategoriasSaidaGeral;

    public static LancamentoCaixa Lancar(Guid lojaId, TipoLancamento tipo, string? categoria, decimal valor, string? descricao,
        FormaPagamento forma, DateOnly data, bool diaFechado, Guid? usuarioId)
    {
        if (diaFechado)
            throw new RegraException("O caixa deste dia já foi fechado. Reabra o dia para lançar.");
        if (valor <= 0)
            throw new RegraException("O valor precisa ser maior que zero.");
        if (valor > 1_000_000)
            throw new RegraException("Valor alto demais; confira.");
        var cat = categoria?.Trim();
        if (string.IsNullOrEmpty(cat))
            throw new RegraException("Escolha a categoria.");
        if (cat.Length > 60)
            throw new RegraException("Categoria com no máximo 60 letras.");

        return new LancamentoCaixa
        {
            LojaId = lojaId, Tipo = tipo, Categoria = cat, Valor = Math.Round(valor, 2), Forma = forma, Data = data, UsuarioId = usuarioId,
            Descricao = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim()
        };
    }

    /// <summary>Venda paga entra no caixa do dia do pagamento, na forma em que foi paga.</summary>
    public static LancamentoCaixa DaVenda(Pedido pedido, DateOnly data) => new()
    {
        LojaId = pedido.LojaId, Tipo = TipoLancamento.Entrada, Categoria = CategoriaVenda, Valor = pedido.Total,
        Forma = pedido.FormaPagamento ?? FormaPagamento.Pix, Data = data, PedidoId = pedido.Id, UsuarioId = pedido.ConfirmadoPor,
        Descricao = $"Pedido #{pedido.Numero}: {RegrasPedido.Resumo(pedido)}"
    };

    public static ResumoCaixa Resumir(IReadOnlyCollection<LancamentoCaixa> lancamentos)
    {
        decimal Soma(TipoLancamento t, FormaPagamento? f = null) =>
            lancamentos.Where(l => l.Tipo == t && (f is null || l.Forma == f)).Sum(l => l.Valor);

        var entradas = Soma(TipoLancamento.Entrada);
        var saidas = Soma(TipoLancamento.Saida);
        var porForma = Enum.GetValues<FormaPagamento>()
            .Select(f => new TotaisForma(f, Soma(TipoLancamento.Entrada, f), Soma(TipoLancamento.Saida, f)))
            .ToList();
        var dinheiro = Soma(TipoLancamento.Entrada, FormaPagamento.Dinheiro) - Soma(TipoLancamento.Saida, FormaPagamento.Dinheiro);
        return new ResumoCaixa(entradas, saidas, entradas - saidas, dinheiro, porForma);
    }

    public static FechamentoCaixa Fechar(Guid lojaId, DateOnly data, IReadOnlyCollection<LancamentoCaixa> lancamentos,
        decimal? dinheiroContado, string? observacao, bool jaFechado, Guid? usuarioId)
    {
        if (jaFechado)
            throw new RegraException("O caixa deste dia já está fechado.");
        if (dinheiroContado is < 0)
            throw new RegraException("O dinheiro contado não pode ser negativo.");

        var r = Resumir(lancamentos);
        return new FechamentoCaixa
        {
            LojaId = lojaId, Data = data, Entradas = r.Entradas, Saidas = r.Saidas, Saldo = r.Saldo,
            DinheiroEsperado = r.DinheiroEsperado, DinheiroContado = dinheiroContado,
            Diferenca = dinheiroContado is { } c ? c - r.DinheiroEsperado : null,
            Observacao = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim(), FechadoPor = usuarioId
        };
    }

    public static DateOnly Hoje(DateTimeOffset agora, TimeZoneInfo fuso) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(agora, fuso).DateTime);
}
