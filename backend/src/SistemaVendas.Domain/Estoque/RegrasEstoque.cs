namespace SistemaVendas.Domain.Estoque;

/// <summary>Erro de regra de negócio, com mensagem para mostrar ao usuário.</summary>
public class RegraException(string mensagem) : Exception(mensagem);

/// <summary>
/// Regras dos movimentos de estoque. Toda alteração vira movimento, para a soma fechar
/// e a mortalidade por lote e fornecedor sair de uma consulta.
/// </summary>
public static class RegrasEstoque
{
    /// <summary>Lote novo: entra em quarentena quando o tanque é de quarentena ou há dias de quarentena.</summary>
    public static (Lote Lote, MovimentoEstoque Entrada) Receber(
        Guid lojaId, Guid especieId, Tanque tanque, Guid? fornecedorId, int quantidade,
        decimal? custoUnitario, int quarentenaDias, DateTimeOffset agora)
    {
        if (quantidade <= 0)
            throw new RegraException("A quantidade precisa ser maior que zero.");
        if (quarentenaDias < 0)
            throw new RegraException("Dias de quarentena não podem ser negativos.");

        var emQuarentena = tanque.Tipo == TipoTanque.Quarentena || quarentenaDias > 0;
        var lote = new Lote
        {
            LojaId = lojaId,
            EspecieId = especieId,
            TanqueId = tanque.Id,
            FornecedorId = fornecedorId,
            QuantidadeInicial = quantidade,
            CustoUnitario = custoUnitario,
            RecebidoEm = agora,
            Status = emQuarentena ? StatusLote.Quarentena : StatusLote.Liberado,
            QuarentenaAte = emQuarentena ? agora.AddDays(quarentenaDias > 0 ? quarentenaDias : 7) : null
        };

        var entrada = new MovimentoEstoque
        {
            LojaId = lojaId,
            LoteId = lote.Id,
            TanqueId = tanque.Id,
            Tipo = TipoMovimento.Entrada,
            Quantidade = quantidade
        };

        return (lote, entrada);
    }

    /// <summary>Baixa por morte: não pode tirar mais do que existe no tanque.</summary>
    public static MovimentoEstoque Baixa(Lote lote, Guid tanqueId, int saldoNoTanque, int quantidade, string? observacao, Guid? usuarioId)
    {
        ValidarSaida(saldoNoTanque, quantidade);
        return new MovimentoEstoque
        {
            LojaId = lote.LojaId,
            LoteId = lote.Id,
            TanqueId = tanqueId,
            Tipo = TipoMovimento.BaixaMorte,
            Quantidade = quantidade,
            Observacao = observacao,
            UsuarioId = usuarioId
        };
    }

    /// <summary>Transferência gera dois movimentos: negativo na origem e positivo no destino.</summary>
    public static (MovimentoEstoque Saida, MovimentoEstoque Chegada) Transferir(
        Lote lote, Guid origemId, Guid destinoId, int saldoNaOrigem, int quantidade, Guid? usuarioId)
    {
        if (origemId == destinoId)
            throw new RegraException("Escolha um tanque de destino diferente do atual.");
        ValidarSaida(saldoNaOrigem, quantidade);

        MovimentoEstoque Mov(Guid tanque, int qtd) => new()
        {
            LojaId = lote.LojaId,
            LoteId = lote.Id,
            TanqueId = tanque,
            Tipo = TipoMovimento.Transferencia,
            Quantidade = qtd,
            UsuarioId = usuarioId
        };

        return (Mov(origemId, -quantidade), Mov(destinoId, quantidade));
    }

    /// <summary>Ajuste aceita quantidade positiva ou negativa e sempre exige observação.</summary>
    public static MovimentoEstoque Ajuste(Lote lote, Guid tanqueId, int saldoNoTanque, int quantidade, string? observacao, Guid? usuarioId)
    {
        if (quantidade == 0)
            throw new RegraException("O ajuste precisa ser diferente de zero.");
        if (string.IsNullOrWhiteSpace(observacao))
            throw new RegraException("Explique o motivo do ajuste.");
        if (saldoNoTanque + quantidade < 0)
            throw new RegraException($"O ajuste deixaria o saldo negativo (há {saldoNoTanque} no tanque).");

        return new MovimentoEstoque
        {
            LojaId = lote.LojaId,
            LoteId = lote.Id,
            TanqueId = tanqueId,
            Tipo = TipoMovimento.Ajuste,
            Quantidade = quantidade,
            Observacao = observacao.Trim(),
            UsuarioId = usuarioId
        };
    }

    /// <summary>Libera o lote da quarentena: a partir daí ele entra no disponível para venda.</summary>
    public static void Liberar(Lote lote)
    {
        if (lote.Status != StatusLote.Quarentena)
            throw new RegraException("Este lote não está em quarentena.");
        lote.Status = StatusLote.Liberado;
        lote.QuarentenaAte = null;
    }

    private static void ValidarSaida(int saldo, int quantidade)
    {
        if (quantidade <= 0)
            throw new RegraException("A quantidade precisa ser maior que zero.");
        if (quantidade > saldo)
            throw new RegraException($"Só há {saldo} neste tanque.");
    }
}
