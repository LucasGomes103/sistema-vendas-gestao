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
