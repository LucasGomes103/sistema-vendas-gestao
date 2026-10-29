import { ChangeDetectionStrategy, Component, input } from '@angular/core';
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

@Component({
  selector: 'app-vitrine-page',
  imports: [CurrencyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (catalogo.value(); as c) {
      <header>
        <h1>{{ c.loja }}</h1>
        <p>Disponível agora</p>
      </header>
      <ul class="lista">
        @for (item of c.itens; track item.id) {
          <li>
            @if (item.fotoUrl) {
              <img [src]="item.fotoUrl" [alt]="item.nomePopular" loading="lazy" />
            }
            <strong>{{ item.nomePopular }}</strong>
            @if (item.nomeCientifico) {
              <em>{{ item.nomeCientifico }}</em>
            }
            <span>{{ item.preco | currency: 'BRL' }} · {{ item.disponivel }} disponíveis</span>
          </li>
        } @empty {
          <li>Nenhuma espécie disponível no momento.</li>
        }
      </ul>
    } @else if (catalogo.error()) {
      <p>Loja não encontrada.</p>
    } @else {
      <p>Carregando…</p>
    }
  `,
  styles: `
    :host { display: block; max-width: 640px; margin: 0 auto; padding: 16px; font-family: system-ui, sans-serif; }
    .lista { list-style: none; padding: 0; display: grid; gap: 12px; }
    li { display: grid; gap: 4px; padding: 12px; border: 1px solid #d0d7de; border-radius: 12px; }
    img { width: 100%; border-radius: 8px; }
    em { color: #57606a; }
  `,
})
export class VitrinePage {
  /** Slug da loja, vindo da rota /:loja. */
  readonly loja = input.required<string>();

  protected readonly catalogo = httpResource<Catalogo>(() => `/api/v1/public/${this.loja()}/catalogo`);
}
