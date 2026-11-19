import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';

interface ItemCatalogo {
  id: string;
  nomePopular: string;
  nomeCientifico: string | null;
  tamanhoCm: number | null;
  preco: number;
  disponivel: number;
  fotoUrl: string | null;
  /** Campos opcionais do produto (Tamanho: M, Cor: preto...). */
  atributos?: { nome: string; valor: string }[];
}

interface Termos {
  aquario: boolean;
  produto: string;
  produtos: string;
  produtoFeminino: boolean;
  itens: string;
}

interface Catalogo {
  loja: string;
  /** Tipo de loja escrito no cadastro (ex.: "Roupas"). */
  segmento?: string | null;
  /** Palavras do tipo de loja; sem elas (API antiga), aquarismo. */
  termos?: Termos;
  /** WhatsApp da loja (só dígitos, com DDI). Sem ele, a vitrine não mostra o carrinho. */
  whatsApp: string | null;
  itens: ItemCatalogo[];
}

/** Abaixo disso a vitrine avisa "últimas unidades". */
const POUCAS_UNIDADES = 5;

/** Mesmo texto que o robô reconhece como pedido montado na vitrine (PedidoCatalogo.Marcador no backend). */
const MARCADOR_PEDIDO = 'Pedido pelo catálogo';

const BRL = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

@Component({
  selector: 'app-vitrine-page',
  imports: [CurrencyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './vitrine-page.html',
  styleUrl: './vitrine-page.scss',
})
export class VitrinePage {
  /** Slug da loja, vindo da rota /:loja. */
  readonly loja = input.required<string>();

  protected readonly catalogo = httpResource<Catalogo>(() => `/api/v1/public/${this.loja()}/catalogo`);
  protected readonly busca = signal('');
  protected readonly poucas = POUCAS_UNIDADES;

  /** Peixe na ECS Fish; produto nas lojas de outros tipos. */
  protected readonly aquario = computed(() => this.catalogo.value()?.termos?.aquario ?? true);
  protected readonly subtitulo = computed(() =>
    this.aquario() ? 'Peixes ornamentais' : (this.catalogo.value()?.segmento?.trim() || 'Catálogo'),
  );
  protected readonly palavraItem = computed(() => (this.aquario() ? 'peixe' : 'produto'));
  protected readonly itensPlural = computed(() => (this.aquario() ? 'peixes' : 'produtos'));
  /** "espécie disponível" / "produto disponível". */
  protected contagemTexto(n: number): string {
    const t = this.catalogo.value()?.termos;
    const nome = (n === 1 ? (t?.produto ?? 'Espécie') : (t?.produtos ?? 'Espécies')).toLowerCase();
    return `${n} ${nome} ${n === 1 ? 'disponível' : 'disponíveis'}`;
  }

  /** Carrinho: id da espécie -> quantidade. */
  private readonly carrinho = signal<Record<string, number>>({});

  protected readonly sigla = computed(() =>
    (this.catalogo.value()?.loja ?? '')
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((p) => p[0].toUpperCase())
      .join(''),
  );

  protected readonly itens = computed(() => {
    const termo = normalizar(this.busca());
    return (this.catalogo.value()?.itens ?? []).filter(
      (i) => !termo || normalizar(`${i.nomePopular} ${i.nomeCientifico ?? ''} ${(i.atributos ?? []).map((a) => a.valor).join(' ')}`).includes(termo),
    );
  });

  private readonly escolhidos = computed(() => {
    const qtds = this.carrinho();
    return (this.catalogo.value()?.itens ?? [])
      .filter((i) => (qtds[i.id] ?? 0) > 0)
      .map((i) => ({ item: i, qtd: qtds[i.id] }));
  });

  protected readonly totalItens = computed(() => this.escolhidos().reduce((s, e) => s + e.qtd, 0));
  protected readonly total = computed(() => this.escolhidos().reduce((s, e) => s + e.qtd * e.item.preco, 0));

  protected quantidade(id: string): number {
    return this.carrinho()[id] ?? 0;
  }

  protected alterar(item: ItemCatalogo, delta: number): void {
    this.carrinho.update((c) => {
      const nova = Math.min(item.disponivel, Math.max(0, (c[item.id] ?? 0) + delta));
      return { ...c, [item.id]: nova };
    });
  }

  /** Abre o WhatsApp da loja com o pedido já escrito; o robô reconhece e chama o vendedor. */
  protected linkPedido(numero: string): string {
    const linhas = this.escolhidos().map(
      (e) => `• ${e.qtd}x ${e.item.nomePopular} (${BRL.format(e.item.preco)} cada) = ${BRL.format(e.qtd * e.item.preco)}`,
    );
    const texto = [`🛒 *${MARCADOR_PEDIDO}*`, '', ...linhas, '', `*Total: ${BRL.format(this.total())}* + frete`].join('\n');
    return `https://wa.me/${numero}?text=${encodeURIComponent(texto)}`;
  }
}

function normalizar(texto: string): string {
  return texto
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .trim();
}
