namespace SistemaVendas.Domain.Funil;

public enum EtapaCard
{
    Novo,
    RoboAtendendo,
    FollowUp,
    Quente,
    AguardandoPagamento,
    ConferirPagamento,
    Pago,
    Entregue,
    Perdido
}

public enum MotivoPerda
{
    SemResposta,
    NaoPagou,
    AchouCaro,
    SemEstoque,
    RegiaoSemEntrega,
    Desistiu
}
