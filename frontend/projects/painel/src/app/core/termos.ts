import { Injectable, computed, inject } from '@angular/core';
import { Vocabulario } from './api';
import { Auth } from './auth';

/** Até a sessão carregar (ou numa API antiga), o painel fala como sempre falou: aquarismo. */
export const AQUARISMO: Vocabulario = {
  chave: 'aquarismo',
  descricaoLoja: 'uma loja de peixes ornamentais',
  produto: 'Espécie',
  produtos: 'Espécies',
  produtoFeminino: true,
  itens: 'peixes',
  local: 'Tanque',
  locais: 'Tanques',
  nomeSecundario: 'Nome científico',
  aquario: true,
  isolamento: 'Quarentena',
  emoji: '🐠',
};

/**
 * Textos do painel que mudam com o tipo de loja: espécie/tanque/peixe no aquarismo, produto/local/produto nas outras.
 * Por dentro (rotas, API, banco) continua "especie" e "tanque".
 */
@Injectable({ providedIn: 'root' })
export class Termos {
  private readonly auth = inject(Auth);

  readonly v = computed(() => this.auth.usuario()?.termos ?? AQUARISMO);
  readonly aquario = computed(() => this.v().aquario);

  /** "Espécie" / "Produto". */
  readonly produto = computed(() => this.v().produto);
  /** "espécie" / "produto". */
  readonly produtoMin = computed(() => this.v().produto.toLowerCase());
  readonly produtos = computed(() => this.v().produtos);
  readonly produtosMin = computed(() => this.v().produtos.toLowerCase());
  /** "Nova espécie" / "Novo produto". */
  readonly novoProduto = computed(() => `${this.v().produtoFeminino ? 'Nova' : 'Novo'} ${this.produtoMin()}`);
  /** "Nenhuma espécie" / "Nenhum produto". */
  readonly nenhumProduto = computed(() => `${this.v().produtoFeminino ? 'Nenhuma' : 'Nenhum'} ${this.produtoMin()}`);
  /** "da espécie" / "do produto". */
  readonly doProduto = computed(() => `${this.v().produtoFeminino ? 'da' : 'do'} ${this.produtoMin()}`);
  /** "a espécie" / "o produto". */
  readonly oProduto = computed(() => `${this.v().produtoFeminino ? 'a' : 'o'} ${this.produtoMin()}`);

  /** O que o cliente leva: "peixe" / "produto" (singular, minúsculo). */
  readonly item = computed(() => (this.v().aquario ? 'peixe' : 'produto'));
  /** "peixes" / "produtos". */
  readonly itens = computed(() => this.v().itens);
  /** "Peixes" / "Produtos". */
  readonly itensMai = computed(() => this.v().itens.charAt(0).toUpperCase() + this.v().itens.slice(1));

  /** "Tanque" / "Local". */
  readonly local = computed(() => this.v().local);
  readonly localMin = computed(() => this.v().local.toLowerCase());
  readonly locais = computed(() => this.v().locais);
  readonly locaisMin = computed(() => this.v().locais.toLowerCase());

  readonly nomeSecundario = computed(() => this.v().nomeSecundario);
  /** Campos opcionais sugeridos no cadastro do produto. */
  readonly camposSugeridos = computed(() => this.v().camposSugeridos ?? (this.v().aquario ? ['Temperatura', 'pH', 'Alimentação'] : ['Marca', 'Tamanho', 'Cor']));

  /** Ícones: peixe e água no aquarismo; caixa e prateleira nas outras lojas. */
  readonly iconeProduto = computed(() => (this.v().aquario ? 'set_meal' : 'inventory_2'));
  readonly iconeLocal = computed(() => (this.v().aquario ? 'water' : 'shelves'));
  /** "neste tanque" / "neste local". */
  readonly nesteLocal = computed(() => `neste ${this.localMin()}`);
  /** "Novo tanque" / "Novo local". */
  readonly novoLocal = computed(() => `Novo ${this.localMin()}`);

  /** "Quarentena" / "Separado (não vende)". */
  readonly isolamento = computed(() => this.v().isolamento);
  /** Selo curto: "quarentena" / "separado". */
  readonly isolamentoCurto = computed(() => (this.v().aquario ? 'quarentena' : 'separado'));
  /** "em quarentena" / "separados (não vendem)". */
  readonly emIsolamento = computed(() => (this.v().aquario ? 'em quarentena' : 'separados (não vendem)'));
}
