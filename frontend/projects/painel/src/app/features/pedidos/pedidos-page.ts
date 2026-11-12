import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { API, Pedido } from '../../core/api';
import { atualizarACada } from '../../core/atualizar';
import { PedidoCardComponent } from './pedido-card';

type Filtro = 'abertos' | 'Pago' | 'Cancelado' | 'todos';

/** Pedidos da loja: os que pedem conferência de pagamento ficam no topo. */
@Component({
  selector: 'app-pedidos-page',
  imports: [CurrencyPipe, MatButtonModule, MatChipsModule, MatIconModule, MatProgressBarModule, PedidoCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="sv-cabecalho-pagina">
      <div>
        <h1>Pedidos</h1>
        <p>Nascem da reserva na conversa. Pix com a chave da loja: confira na conta e confirme o pagamento.</p>
      </div>
    </header>

    <div class="resumo">
      <div class="sv-card numero">
        <span>Conferir pagamento</span>
        <strong [class.alerta]="conferir() > 0">{{ conferir() }}</strong>
      </div>
      <div class="sv-card numero">
        <span>Aguardando pagamento</span>
        <strong>{{ aguardando() }}</strong>
      </div>
      <div class="sv-card numero">
        <span>Em aberto (valor)</span>
        <strong>{{ valorAberto() | currency: 'BRL' }}</strong>
      </div>
    </div>

    <mat-chip-listbox aria-label="Filtrar pedidos" [value]="filtro()" (change)="filtro.set($event.value ?? 'abertos')">
      <mat-chip-option value="abertos">Em aberto</mat-chip-option>
      <mat-chip-option value="Pago">Pagos</mat-chip-option>
      <mat-chip-option value="Cancelado">Cancelados</mat-chip-option>
      <mat-chip-option value="todos">Todos</mat-chip-option>
    </mat-chip-listbox>

    @if (pedidos.isLoading() && !pedidos.hasValue()) {
      <mat-progress-bar mode="indeterminate" />
    } @else if (pedidos.error()) {
      <div class="sv-vazio">
        <mat-icon>cloud_off</mat-icon>
        <p>Não foi possível carregar os pedidos.</p>
        <button mat-stroked-button (click)="pedidos.reload()">Tentar de novo</button>
      </div>
    } @else {
      <div class="lista">
        @for (p of filtrados(); track p.id) {
          <app-pedido-card [pedido]="p" (alterou)="pedidos.reload()" />
        } @empty {
          <div class="sv-vazio">
            <mat-icon>receipt_long</mat-icon>
            <p>Nenhum pedido aqui. Na conversa, toque em "Gerar pedido" na reserva do cliente.</p>
          </div>
        }
      </div>
    }
  `,
  styles: `
    .resumo {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 12px;
      max-width: 820px;
      margin-bottom: 16px;
    }
    .numero {
      display: grid;
      gap: 2px;
      padding: 12px 16px;

      span {
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-label-medium);
      }
      strong {
        font: var(--mat-sys-headline-small);

        &.alerta {
          color: var(--sv-alerta);
        }
      }
    }
    mat-chip-listbox {
      display: block;
      margin-bottom: 12px;
    }
    .lista {
      display: grid;
      gap: 10px;
      max-width: 820px;
    }
    @media (max-width: 599px) {
      .resumo {
        grid-template-columns: 1fr 1fr;

        .numero:last-child {
          grid-column: 1 / -1;
        }
      }
    }
  `,
})
export class PedidosPage {
  protected readonly pedidos = httpResource<Pedido[]>(() => `${API}/pedidos`);
  protected readonly filtro = signal<Filtro>('abertos');

  private readonly lista = computed(() => this.pedidos.value() ?? []);
  protected readonly filtrados = computed(() => {
    const f = this.filtro();
    return this.lista().filter((p) =>
      f === 'todos' ? true : f === 'abertos' ? p.status === 'AguardandoPagamento' || p.status === 'ConferirPagamento' : p.status === f,
    );
  });
  protected readonly conferir = computed(() => this.lista().filter((p) => p.status === 'ConferirPagamento').length);
  protected readonly aguardando = computed(() => this.lista().filter((p) => p.status === 'AguardandoPagamento').length);
  protected readonly valorAberto = computed(() =>
    this.lista()
      .filter((p) => p.status === 'AguardandoPagamento' || p.status === 'ConferirPagamento')
      .reduce((s, p) => s + p.total, 0),
  );

  constructor() {
    atualizarACada(10000, () => this.pedidos.reload());
  }
}
