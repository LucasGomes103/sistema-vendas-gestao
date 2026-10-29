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
}

interface Catalogo {
  loja: string;
  itens: ItemCatalogo[];
}

/** Abaixo disso a vitrine avisa "últimas unidades". */
const POUCAS_UNIDADES = 5;

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
      (i) => !termo || normalizar(`${i.nomePopular} ${i.nomeCientifico ?? ''}`).includes(termo),
    );
  });
}

function normalizar(texto: string): string {
  return texto
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .trim();
}
