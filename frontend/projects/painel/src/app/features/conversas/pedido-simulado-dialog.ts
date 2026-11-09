import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { API, Especie } from '../../core/api';

const BRL = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

/**
 * Monta um pedido como se o cliente tivesse usado a vitrine e devolve exatamente o texto que o
 * botão "Pedir pelo WhatsApp" manda. O simulador envia esse texto como mensagem do cliente.
 */
@Component({
  selector: 'app-pedido-simulado-dialog',
  imports: [MatButtonModule, MatDialogModule, MatIconModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Pedido pelo catálogo</h2>
    <mat-dialog-content>
      <p class="ajuda">Escolha como o cliente faria na vitrine. A mensagem enviada é a mesma do botão "Pedir pelo WhatsApp".</p>
      @if (especies.isLoading() && !especies.hasValue()) {
        <mat-progress-bar mode="indeterminate" />
      }
      @for (e of disponiveis(); track e.id) {
        <div class="item">
          @if (e.fotoUrl) {
            <img [src]="e.fotoUrl" alt="" />
          } @else {
            <span class="sem-foto"><mat-icon>set_meal</mat-icon></span>
          }
          <span class="nome">
            <strong>{{ e.nomePopular }}</strong>
            <small>{{ brl(e.preco) }} · {{ e.disponivel }} em estoque</small>
          </span>
          <button mat-icon-button aria-label="Menos" [disabled]="!qtd(e.id)" (click)="alterar(e, -1)"><mat-icon>remove</mat-icon></button>
          <span class="qtd">{{ qtd(e.id) }}</span>
          <button mat-icon-button aria-label="Mais" [disabled]="qtd(e.id) >= e.disponivel" (click)="alterar(e, 1)"><mat-icon>add</mat-icon></button>
        </div>
      } @empty {
        @if (especies.hasValue()) {
          <p class="ajuda">Nenhuma espécie com estoque. Dê entrada de um lote no Estoque primeiro.</p>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions>
      <span class="total">{{ totalItens() ? brl(total()) : '' }}</span>
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button [disabled]="!totalItens()" [mat-dialog-close]="texto()">
        <mat-icon>send</mat-icon> Enviar como cliente
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .ajuda {
      margin: 0 0 12px;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .item {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 6px 0;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    img,
    .sem-foto {
      width: 40px;
      height: 40px;
      border-radius: 8px;
      object-fit: cover;
      flex: none;
    }
    .sem-foto {
      display: grid;
      place-items: center;
      background: var(--mat-sys-surface-container-high);
      color: var(--mat-sys-on-surface-variant);
    }
    .nome {
      flex: 1;
      display: flex;
      flex-direction: column;
      min-width: 0;
      small {
        color: var(--mat-sys-on-surface-variant);
      }
    }
    .qtd {
      min-width: 20px;
      text-align: center;
      font-weight: 600;
    }
    .total {
      margin-right: auto;
      font-weight: 600;
    }
  `,
})
export class PedidoSimuladoDialog {
  protected readonly especies = httpResource<Especie[]>(() => `${API}/especies`);
  protected readonly disponiveis = computed(() => (this.especies.value() ?? []).filter((e) => e.ativo && e.disponivel > 0));
  private readonly carrinho = signal<Record<string, number>>({});

  private readonly escolhidos = computed(() => {
    const c = this.carrinho();
    return this.disponiveis()
      .filter((e) => (c[e.id] ?? 0) > 0)
      .map((e) => ({ e, qtd: c[e.id] }));
  });
  protected readonly totalItens = computed(() => this.escolhidos().reduce((s, x) => s + x.qtd, 0));
  protected readonly total = computed(() => this.escolhidos().reduce((s, x) => s + x.qtd * x.e.preco, 0));

  /** Mesmo formato da vitrine (vitrine-page.ts), que o robô reconhece e transforma em reserva. */
  protected readonly texto = computed(() => {
    const linhas = this.escolhidos().map((x) => `• ${x.qtd}x ${x.e.nomePopular} (${BRL.format(x.e.preco)} cada) = ${BRL.format(x.qtd * x.e.preco)}`);
    return ['🛒 *Pedido pelo catálogo*', '', ...linhas, '', `*Total: ${BRL.format(this.total())}* + frete`].join('\n');
  });

  protected qtd(id: string): number {
    return this.carrinho()[id] ?? 0;
  }

  protected alterar(e: Especie, delta: number): void {
    this.carrinho.update((c) => ({ ...c, [e.id]: Math.min(e.disponivel, Math.max(0, (c[e.id] ?? 0) + delta)) }));
  }

  protected brl(v: number): string {
    return BRL.format(v);
  }
}
