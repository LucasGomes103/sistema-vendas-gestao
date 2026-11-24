/** Prefixo da API. Em desenvolvimento o proxy (proxy.conf.json) encaminha para o backend. */
export const API = '/api/v1';

export type Papel = 'Dono' | 'Vendedor' | 'Entregador';

export interface UsuarioLogado {
  id: string;
  nome: string;
  email: string;
  papel: Papel;
  lojaId: string;
  loja: string;
  lojaSlug: string;
  /** Administrador da plataforma (e-mail em Plataforma__Admins). */
  adminPlataforma?: boolean;
  /** Palavras do tipo de loja (espécie/tanque ou produto/local). Use o serviço Termos. */
  termos?: Vocabulario;
  /** Administrador vendo esta loja como suporte: só leitura. */
  modoSuporte?: boolean;
}

/** Palavras do tipo de loja, vindas da API (Vocabulario). */
export interface Vocabulario {
  chave: 'aquarismo' | 'geral';
  descricaoLoja: string;
  produto: string;
  produtos: string;
  produtoFeminino: boolean;
  itens: string;
  local: string;
  locais: string;
  nomeSecundario: string;
  aquario: boolean;
  isolamento: string;
  emoji: string;
  /** Campos opcionais sugeridos para o produto neste tipo de loja. */
  camposSugeridos?: string[];
}


/** Loja vista pelo administrador da plataforma: só números agregados. */
export interface LojaAdmin {
  id: string;
  nome: string;
  slug: string;
  segmento: string | null;
  status: 'Ativa' | 'AguardandoConfirmacao' | 'Suspensa';
  motivoSuspensao: string | null;
  criadoEm: string;
  dono: string | null;
  emailDono: string | null;
  emailConfirmado: boolean;
  usuarios: number;
  produtos: number;
  conversas30Dias: number;
  pedidos30Dias: number;
  vendido30Dias: number;
  ultimaMensagemEm: string | null;
  whatsAppProprio: boolean;
  mercadoPagoProprio: boolean;
  lojaOriginal: boolean;
  /** Último acesso ao painel de qualquer usuário da loja. */
  ultimoAcessoEm: string | null;
  respostasRobo30Dias: number;
  tokensIa30Dias: number;
  falhasIa30Dias: number;
  /** Respostas com IA neste mês e o limite que vale (0 = sem limite). */
  respostasIaMes: number;
  limiteIaMes: number;
  /** Limite próprio da loja; null = padrão da plataforma. */
  limiteIaProprio: number | null;
}

/** Um dia da série do painel da plataforma (horário de Brasília). */
export interface DiaPlataforma {
  dia: string;
  lojasNovas: number;
  mensagensRecebidas: number;
  respostasRobo: number;
  pedidos: number;
  vendido: number;
  tokensIa: number;
}

export interface TotaisPlataforma {
  conversasNoPeriodo: number;
  mensagensRecebidas: number;
  respostasRobo: number;
  mensagensVendedor: number;
  pedidos: number;
  pedidosPagos: number;
  vendido: number;
  chamadasIa: number;
  tokensEntrada: number;
  tokensSaida: number;
  falhasIa: number;
  mensagensComFalha: number;
}

export interface VisaoGeralPlataforma {
  dias: number;
  lojas: {
    total: number;
    ativas: number;
    aguardandoConfirmacao: number;
    suspensas: number;
    novasNoPeriodo: number;
    comMovimentoNoPeriodo: number;
    paradas: number;
  };
  totais: TotaisPlataforma;
  porDia: DiaPlataforma[];
  saude: {
    iaConfigurada: boolean;
    modeloIa: string;
    emailConfigurado: boolean;
    chaveMestraConfigurada: boolean;
    lojasComWhatsAppProprio: number;
    lojasComMercadoPagoProprio: number;
  };
}

export interface AuditoriaAdmin {
  id: string;
  criadoEm: string;
  adminEmail: string;
  acao: 'editar' | 'suspender' | 'ativar' | 'excluir' | 'ver_como_loja' | 'sair_da_loja' | string;
  lojaId: string | null;
  lojaSlug: string | null;
  detalhe: string | null;
}

export interface DetalheLojaAdmin {
  loja: LojaAdmin;
  configuracao: {
    whatsAppConectado: boolean;
    whatsAppNumero: string | null;
    atenderTodos: boolean;
    numerosPermitidos: number;
    pixConfigurado: boolean;
    freteConfigurado: boolean;
    zonasDeEntrega: number;
    horarioAtendimento: string;
  };
  totais: TotaisPlataforma;
  clientes: number;
  conversasUltimas24h: number;
  usuarios: {
    id: string;
    nome: string;
    email: string;
    papel: Papel;
    ativo: boolean;
    emailConfirmado: boolean;
    criadoEm: string;
    ultimoAcessoEm: string | null;
  }[];
  porDia: DiaPlataforma[];
  auditoria: AuditoriaAdmin[];
}

export interface UsuarioEquipe {
  id: string;
  nome: string;
  email: string;
  papel: Papel;
  ativo: boolean;
  criadoEm: string;
  ehVoce: boolean;
}

export const ROTULO_PAPEL: Record<Papel, string> = {
  Dono: 'Dono',
  Vendedor: 'Vendedor',
  Entregador: 'Entregador',
};

export interface Especie {
  id: string;
  nomePopular: string;
  nomeCientifico: string | null;
  tamanhoCm: number | null;
  preco: number;
  observacoes: string | null;
  ativo: boolean;
  disponivel: number;
  fotoUrl: string | null;
  /** Campos opcionais do produto (Tamanho: M, Cor: preto...). */
  atributos?: AtributoProduto[];
}

export interface AtributoProduto {
  nome: string;
  valor: string;
}

export interface EspecieEntrada {
  nomePopular: string;
  nomeCientifico: string | null;
  tamanhoCm: number | null;
  preco: number;
  observacoes: string | null;
  atributos: AtributoProduto[];
}

export interface ItemTanque {
  especieId: string;
  especie: string;
  disponivel: number;
  emQuarentena: boolean;
}

export interface Tanque {
  id: string;
  nome: string;
  tipo: 'Venda' | 'Quarentena';
  litros: number | null;
  total: number;
  baixasUltimos7Dias: number;
  itens: ItemTanque[];
}

export interface LoteNoTanque {
  loteId: string;
  especieId: string;
  especie: string;
  fornecedor: string | null;
  status: 'Quarentena' | 'Liberado' | 'Encerrado';
  recebidoEm: string;
  quarentenaAte: string | null;
  disponivel: number;
}

export type TipoMovimento = 'Entrada' | 'BaixaMorte' | 'Reserva' | 'LiberaReserva' | 'Venda' | 'Transferencia' | 'Ajuste';

export interface MovimentoResumo {
  id: string;
  tipo: TipoMovimento;
  quantidade: number;
  especie: string;
  observacao: string | null;
  usuario: string | null;
  criadoEm: string;
}

export interface TanqueDetalhe {
  id: string;
  nome: string;
  tipo: 'Venda' | 'Quarentena';
  litros: number | null;
  lotes: LoteNoTanque[];
  movimentos: MovimentoResumo[];
}

export interface Fornecedor {
  id: string;
  nome: string;
  telefone: string | null;
}

export interface ConversaResumo {
  id: string;
  clienteId: string;
  cliente: string;
  telefone: string;
  ultimaMensagem: string | null;
  ultimoAutor: 'Cliente' | 'Robo' | 'Vendedor' | 'Sistema' | null;
  ultimaMensagemEm: string | null;
  naoLidas: number;
  dentroDaJanela: boolean;
  janelaFechaEm: string | null;
  simulada: boolean;
  roboPausado: boolean;
  chamouVendedor: boolean;
  motivoVendedor: string | null;
  chamouVendedorEm: string | null;
  semDono: boolean;
  /** Vendedor que cuida da conversa (quem assumiu ou respondeu primeiro, ou quem o dono alocou). */
  responsavelId: string | null;
  responsavel: string | null;
  responsavelDesde: string | null;
}

/** Quem pode ficar com uma conversa: dono e vendedores ativos. */
export interface Atendente {
  id: string;
  nome: string;
  papel: 'Dono' | 'Vendedor';
}

/** Cliente quente esperando um vendedor. Nível 1 = mais de 15 min, 2 = mais de 1 h. */
export interface AlertaQuente {
  conversaId: string;
  cliente: string;
  motivo: string | null;
  chamouVendedorEm: string;
  nivel: 0 | 1 | 2;
  simulada: boolean;
  responsavelId: string | null;
  responsavel: string | null;
}

export interface Mensagem {
  id: string;
  direcao: 'Entrada' | 'Saida';
  autor: 'Cliente' | 'Robo' | 'Vendedor' | 'Sistema';
  conteudo: string;
  status: 'Pendente' | 'Enviada' | 'Entregue' | 'Lida' | 'Falhou' | 'Recebida';
  erro: string | null;
  criadoEm: string;
  /** Quem da equipe enviou (só nas mensagens do vendedor). */
  usuarioId: string | null;
  usuario: string | null;
}

export type EtapaCard =
  | 'Novo'
  | 'RoboAtendendo'
  | 'FollowUp'
  | 'Quente'
  | 'AguardandoPagamento'
  | 'ConferirPagamento'
  | 'Pago'
  | 'Entregue'
  | 'Perdido';

export type MotivoPerda = 'SemResposta' | 'NaoPagou' | 'AchouCaro' | 'SemEstoque' | 'RegiaoSemEntrega' | 'Desistiu';

export interface CardFunil {
  id: string;
  cliente: string;
  etapa: EtapaCard;
  ultimaMensagem: string | null;
  ultimaMensagemEm: string | null;
  simulada: boolean;
  roboPausado: boolean;
  chamouVendedor: boolean;
  motivoVendedor: string | null;
  chamouVendedorEm: string | null;
  followupsEnviados: number;
  proximaAcao: 'MoverParaFollowup' | 'EnviarFollowup1' | 'EnviarFollowup2' | 'MarcarPerdido' | null;
  proximaAcaoEm: string | null;
  motivoPerda: MotivoPerda | null;
  naoLidas: number;
  semDono: boolean;
  responsavelId: string | null;
  responsavel: string | null;
}

export interface ConfigFunil {
  horasAteFollowup: number;
  horasFollowup1: number;
  diasFollowup2: number;
  horasAtePerdido: number;
  textoFollowup1: string;
  textoFollowup2: string;
  textoFrete: string;
  textoChamarVendedor: string;
}

/** Frete por km a partir da loja: taxa base até alguns km, depois valor por km, até o raio máximo. */
export interface FreteKm {
  ativo: boolean;
  cepOrigem: string | null;
  kmInclusos: number;
  valorBase: number;
  valorPorKm: number;
  kmMaximo: number;
  dias: number;
  configurado: boolean;
  origemEndereco: string | null;
}

export interface FreteConsulta {
  cep: string;
  atende: boolean;
  regiao: string | null;
  valor: number | null;
  dias: number | null;
  resumo: string;
  km: number;
  porRota: boolean;
  endereco: string | null;
  fonte: string | null;
  origem: string | null;
}

export interface ReservaItem {
  especie: string;
  quantidade: number;
  precoUnitario: number;
}

export interface Reserva {
  id: string;
  conversaId: string;
  status: 'Ativa' | 'Confirmada' | 'Cancelada' | 'Expirada';
  criadoEm: string;
  expiraEm: string;
  encerradaEm: string | null;
  total: number;
  itens: ReservaItem[];
}

export type StatusPedido = 'AguardandoPagamento' | 'ConferirPagamento' | 'Pago' | 'Cancelado';
export type FormaPagamento = 'Pix' | 'Dinheiro' | 'Cartao';
export type TipoEntrega = 'Entrega' | 'Retirada';

export const ROTULO_STATUS_PEDIDO: Record<StatusPedido, string> = {
  AguardandoPagamento: 'Aguardando pagamento',
  ConferirPagamento: 'Conferir pagamento',
  Pago: 'Pago',
  Cancelado: 'Cancelado',
};

export const ROTULO_FORMA: Record<FormaPagamento, string> = { Pix: 'Pix', Dinheiro: 'Dinheiro', Cartao: 'Cartão' };

export interface Pedido {
  id: string;
  numero: number;
  status: StatusPedido;
  clienteId: string;
  cliente: string;
  telefone: string;
  conversaId: string | null;
  reservaId: string | null;
  tipoEntrega: TipoEntrega;
  cep: string | null;
  endereco: string | null;
  subtotal: number;
  frete: number;
  total: number;
  criadoEm: string;
  expiraEm: string | null;
  pixCopiaECola: string | null;
  pixEnviadoEm: string | null;
  comprovanteEm: string | null;
  /** Link do Mercado Pago para pagar no cartão (quando o cartão está ligado). */
  linkCartao: string | null;
  /** Pago pelo link do cartão: confirmado sozinho pelo aviso do Mercado Pago. */
  pagoPeloMercadoPago: boolean;
  formaPagamento: FormaPagamento | null;
  pagoEm: string | null;
  canceladoEm: string | null;
  motivoCancelamento: string | null;
  observacao: string | null;
  itens: ReservaItem[];
  /** Vendedor que fica com a venda (o responsável pela conversa). */
  vendedorId: string | null;
  vendedor: string | null;
  criadoPor: string | null;
  confirmadoPor: string | null;
}

/** Ação que pode avisar o cliente: aviso explica quando a mensagem não saiu (janela de 24 h fechada). */
export interface PedidoAcao {
  pedido: Pedido;
  mensagemEnviada: boolean;
  aviso: string | null;
}

export interface SugestaoPedido {
  cliente: string;
  cep: string | null;
  endereco: string | null;
  frete: number | null;
  freteResumo: string | null;
  pixConfigurado: boolean;
  cartaoConfigurado: boolean;
}

export interface ConfigPix {
  chave: string | null;
  tipoChave: string | null;
  nome: string | null;
  cidade: string | null;
  configurado: boolean;
}

/**
 * Cartão pelo Mercado Pago da loja. O token nunca volta do servidor: só o resumo.
 * origem: 'Loja' (salvo no painel, cifrado) ou 'Servidor' (variáveis do Render, só a loja original).
 */
export interface ConfigCartao {
  configurado: boolean;
  modoTeste: boolean;
  webhookPronto: boolean;
  assinaturaConferida: boolean;
  urlWebhook: string | null;
  origem: 'Loja' | 'Servidor' | null;
  resumo: string | null;
  podeSalvar: boolean;
}

/** WhatsApp oficial (Cloud API da Meta) da loja: o token nunca volta, só o resumo. */
/** Uso da IA da plataforma no mês (limite 0 = sem limite). */
export interface UsoIA {
  ativa: boolean;
  atendente: string;
  respostas: number;
  porRegras: number;
  limite: number;
  esgotado: boolean;
}

export interface WhatsAppOficial {
  configurado: boolean;
  origem: 'Loja' | 'Servidor' | null;
  phoneNumberId: string | null;
  resumo: string | null;
  podeSalvar: boolean;
}

export type TipoLancamento = 'Entrada' | 'Saida';

export interface LancamentoCaixa {
  id: string;
  tipo: TipoLancamento;
  categoria: string;
  valor: number;
  descricao: string | null;
  forma: FormaPagamento;
  data: string;
  pedidoId: string | null;
  criadoEm: string;
  usuario: string | null;
}

export interface CaixaDia {
  data: string;
  hoje: string;
  lancamentos: LancamentoCaixa[];
  resumo: {
    entradas: number;
    saidas: number;
    saldo: number;
    dinheiroEsperado: number;
    porForma: { forma: FormaPagamento; entradas: number; saidas: number }[];
  };
  fechamento: {
    entradas: number;
    saidas: number;
    saldo: number;
    dinheiroEsperado: number;
    dinheiroContado: number | null;
    diferenca: number | null;
    observacao: string | null;
    fechadoEm: string;
    fechadoPor: string | null;
  } | null;
  categoriasEntrada: string[];
  categoriasSaida: string[];
}

export type StatusEntrega = 'AAgendar' | 'Agendada' | 'SaiuParaEntrega' | 'Entregue';

export interface Entrega {
  id: string;
  pedidoId: string;
  numeroPedido: number;
  status: StatusEntrega;
  tipo: TipoEntrega;
  cliente: string;
  telefone: string;
  conversaId: string | null;
  cep: string | null;
  endereco: string | null;
  data: string | null;
  janela: string | null;
  entregadorId: string | null;
  entregador: string | null;
  saiuEm: string | null;
  entregueEm: string | null;
  observacao: string | null;
  total: number;
  itens: string;
  criadoEm: string;
}

export interface EntregaAcao {
  entrega: Entrega;
  mensagemEnviada: boolean;
  aviso: string | null;
}

export interface PessoaEntrega {
  id: string;
  nome: string;
  papel: Papel;
}

export interface ConfigWhatsApp {
  atenderTodos: boolean;
  numerosPermitidos: string[];
  whatsAppNumero: string | null;
  provedor: 'Meta' | 'Local';
}

/** Mensagem do ProblemDetails da API (regras de negócio voltam como 422). */
export function mensagemDeErro(erro: unknown, padrao = 'Não foi possível concluir. Tente de novo.'): string {
  const corpo = (erro as { error?: { detail?: string; errors?: Record<string, string[]> } } | null)?.error;
  // Erros de validação (400) vêm por campo; mostra o primeiro.
  const primeiroCampo = corpo?.errors ? Object.values(corpo.errors).flat()[0] : undefined;
  return corpo?.detail ?? primeiroCampo ?? padrao;
}
