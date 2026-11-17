import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Pedido, ROTULO_FORMA, ROTULO_STATUS_PEDIDO } from '../../core/api';
import { AcoesPedido } from './acoes-pedido';

/** Um pedido com as ações que cabem na situação dele. */
@Component({
  selector: 'app-pedido-card',
  imports: [CurrencyPipe, DatePipe, RouterLink, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let p = pedido();
    <article class="pedido" [class.conferir]="p.status === 'ConferirPagamento'" [class.encerrado]="p.status === 'Cancelado'">
      <header>
        <strong class="numero">#{{ p.numero }}</strong>
        <span class="sv-selo" [class.ok]="p.status === 'Pago'" [class.alerta]="p.status === 'ConferirPagamento'" [class.info]="p.status === 'AguardandoPagamento'">
          <mat-icon>{{ icone() }}</mat-icon>{{ rotulo[p.status] }}
        </span>
        @if (mostrarCliente()) {
          @if (p.conversaId) {
            <a class="cliente" [routerLink]="['/conversas', p.conversaId]" matTooltip="Abrir conversa">{{ p.cliente }}</a>
          } @else {
            <span class="cliente">{{ p.cliente }}</span>
          }
        }
        <span class="total">{{ p.total | currency: 'BRL' }}</span>
      </header>

      <p class="itens">
        @for (i of p.itens; track i.especie) {
          {{ i.quantidade }}x {{ i.especie }}{{ $last ? '' : ', ' }}
        }
        @if (p.tipoEntrega === 'Entrega') {
          · frete {{ p.frete | currency: 'BRL' }}
        }
      </p>
      <p class="detalhe">
        <mat-icon>{{ p.tipoEntrega === 'Entrega' ? 'local_shipping' : 'storefront' }}</mat-icon>
        {{ p.tipoEntrega === 'Entrega' ? (p.endereco ?? 'Entrega') : 'Retirada na loja' }}
      </p>
      <p class="detalhe">
        <mat-icon>schedule</mat-icon>
        @switch (p.status) {
          @case ('Pago') {
            Pago em {{ p.pagoEm | date: "dd/MM 'às' HH:mm" }}{{ p.formaPagamento ? ' · ' + forma[p.formaPagamento] : '' }}{{ p.pagoPeloMercadoPago ? ' (Mercado Pago, confirmado sozinho)' : '' }}
          }
          @case ('Cancelado') {
            Cancelado em {{ p.canceladoEm | date: "dd/MM 'às' HH:mm" }}{{ p.motivoCancelamento ? ' · ' + p.motivoCancelamento : '' }}
          }
          @default {
            Criado {{ p.criadoEm | date: "dd/MM 'às' HH:mm" }}
            @if (p.expiraEm) {
              · reservado até {{ p.expiraEm | date: "dd/MM 'às' HH:mm" }}
            }
            @if (p.pixEnviadoEm) {
              · cobrança enviada {{ p.pixEnviadoEm | date: 'HH:mm' }}
            }
            @if (p.comprovanteEm) {
              · cliente disse que pagou {{ p.comprovanteEm | date: 'HH:mm' }}
            }
          }
        }
      </p>
      @if (p.observacao) {
        <p class="detalhe"><mat-icon>notes</mat-icon> {{ p.observacao }}</p>
      }

      @if (aberto()) {
        <footer>
          <button mat-flat-button [disabled]="ocupado()" (click)="executar(acoes.confirmarPagamento(p))"><mat-icon>check</mat-icon> Confirmar pagamento</button>
          @if (p.pixCopiaECola || p.linkCartao) {
            @if (p.conversaId) {
              <button mat-stroked-button [disabled]="ocupado()" (click)="executar(acoes.enviarPix(p))">
                <mat-icon>send</mat-icon> {{ p.pixEnviadoEm ? 'Reenviar cobrança' : 'Enviar cobrança' }}
              </button>
            }
            @if (p.pixCopiaECola) {
              <button mat-icon-button matTooltip="Copiar Pix copia e cola" aria-label="Copiar Pix" (click)="acoes.copiarPix(p)"><mat-icon>content_copy</mat-icon></button>
            }
            @if (p.linkCartao) {
              <button mat-icon-button matTooltip="Copiar link do cartão (Mercado Pago)" aria-label="Copiar link do cartão" (click)="acoes.copiarLinkCartao(p)"><mat-icon>credit_card</mat-icon></button>
            }
          }
          <button mat-icon-button [matMenuTriggerFor]="mais" aria-label="Mais ações"><mat-icon>more_vert</mat-icon></button>
          <mat-menu #mais="matMenu">
            <button mat-menu-item (click)="executar(acoes.cancelar(p))"><mat-icon>cancel</mat-icon> Cancelar pedido</button>
          </mat-menu>
        </footer>
      }
    </article>
  `,
  styles: `
    .pedido {
      display: grid;
      gap: 4px;
      padding: 12px 16px;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 14px;
      background: var(--mat-sys-surface-container-low);

      &.conferir {
        border-color: var(--sv-alerta-borda);
        background: var(--sv-alerta-fundo);
      }
      &.encerrado {
        opacity: 0.7;
      }
    }
    header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px;

      .numero {
        font: var(--mat-sys-title-medium);
      }
      .cliente {
        overflow: hidden;
        color: inherit;
        font: var(--mat-sys-title-small);
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .total {
        margin-left: auto;
        font: var(--mat-sys-title-medium);
      }
    }
    p {
      margin: 0;
    }
    .itens {
      font: var(--mat-sys-body-medium);
    }
    .detalhe {
      display: flex;
      align-items: center;
      gap: 6px;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);

      mat-icon {
        flex: none;
        width: 16px;
        height: 16px;
        font-size: 16px;
      }
    }
    footer {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px;
      margin-top: 8px;
    }
  `,
})
export class PedidoCardComponent {
  protected readonly acoes = inject(AcoesPedido);

  readonly pedido = input.required<Pedido>();
  readonly mostrarCliente = input(true);
  readonly alterou = output<Pedido>();

  protected readonly rotulo = ROTULO_STATUS_PEDIDO;
  protected readonly forma = ROTULO_FORMA;
  protected readonly ocupado = signal(false);
  protected readonly aberto = computed(() => ['AguardandoPagamento', 'ConferirPagamento'].includes(this.pedido().status));
  protected readonly icone = computed(
    () => ({ AguardandoPagamento: 'hourglass_top', ConferirPagamento: 'fact_check', Pago: 'check_circle', Cancelado: 'cancel' })[this.pedido().status],
  );

  protected async executar(acao: Promise<Pedido | null>): Promise<void> {
    this.ocupado.set(true);
    try {
      const r = await acao;
      if (r) this.alterou.emit(r);
    } finally {
      this.ocupado.set(false);
    }
  }
}
