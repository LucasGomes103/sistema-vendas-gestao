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
}

export interface Especie {
  id: string;
  nomePopular: string;
  nomeCientifico: string | null;
  tamanhoCm: number | null;
  preco: number;
  observacoes: string | null;
  ativo: boolean;
  disponivel: number;
}

export interface EspecieEntrada {
  nomePopular: string;
  nomeCientifico: string | null;
  tamanhoCm: number | null;
  preco: number;
  observacoes: string | null;
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
}

export interface Mensagem {
  id: string;
  direcao: 'Entrada' | 'Saida';
  autor: 'Cliente' | 'Robo' | 'Vendedor' | 'Sistema';
  conteudo: string;
  status: 'Pendente' | 'Enviada' | 'Entregue' | 'Lida' | 'Falhou' | 'Recebida';
  erro: string | null;
  criadoEm: string;
}

/** Mensagem do ProblemDetails da API (regras de negócio voltam como 422). */
export function mensagemDeErro(erro: unknown, padrao = 'Não foi possível concluir. Tente de novo.'): string {
  const corpo = (erro as { error?: { detail?: string } } | null)?.error;
  return corpo?.detail ?? padrao;
}
