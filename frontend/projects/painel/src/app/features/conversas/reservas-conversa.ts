import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, Pedido, PedidoAcao, Reserva, mensagemDeErro } from '../../core/api';
import { Confirmar } from '../../core/confirmar';
import { atualizarACada } from '../../core/atualizar';
import { PedidoCardComponent } from '../pedidos/pedido-card';
import { PedidoDialog } from '../pedidos/pedido-dialog';

/**
 * Peixe separado para o cliente por 24 h. O vendedor gera o pedido (com o Pix copia e cola) ou cancela.
 * Pedido em aberto aparece no lugar da reserva, com confirmar pagamento, enviar Pix e cancelar.
 */
@Component({
  selector: 'app-reservas-conversa',
  imports: [CurrencyPipe, DatePipe, MatButtonModule, MatIconModule, PedidoCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (p of pedidosAbertos(); track p.id) {
      <div class="pedido">
        <app-pedido-card [pedido]="p" [mostrarCliente]="false" (alterou)="recarregar()" />
      </div>
    }
    @for (r of semPedido(); track r.id) {
      <div class="reserva">
        <mat-icon>inventory_2</mat-icon>
        <div class="dados">
          <strong>Reservado até {{ r.expiraEm | date: "dd/MM 'às' HH:mm" }}</strong>
          <span>
            @for (i of r.itens; track i.especie) {
              {{ i.quantidade }}x {{ i.especie }}{{ $last ? '' : ', ' }}
            }
            · {{ r.total | currency: 'BRL' }} + frete
          </span>
        </div>
        <span class="acoes">
          <button mat-stroked-button [disabled]="ocupado()" (click)="encerrar(r, 'cancelar')">Cancelar</button>
          <button mat-flat-button [disabled]="ocupado()" (click)="gerarPedido(r)"><mat-icon>receipt_long</mat-icon> Gerar pedido</button>
        </span>
      </div>
    }
  `,
  styles: `
    .pedido {
      margin: 0 16px 8px;
    }
    .reserva {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 8px 12px;
      margin: 0 16px 8px;
      padding: 10px 14px;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 12px;
      background: var(--mat-sys-surface-container);

      > mat-icon {
        color: var(--mat-sys-primary);
      }
    }
    .dados {
      display: grid;
      flex: 1;
      min-width: 180px;

      span {
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-body-small);
      }
    }
    .acoes {
      display: flex;
      gap: 8px;
    }
  `,
})
export class ReservasConversaComponent {
  private readonly confirmar = inject(Confirmar);
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  readonly conversaId = input.required<string>();
  /** Avisa a tela para recarregar o card (a confirmação muda a etapa para Pago). */
  readonly alterou = output<void>();

  protected readonly reservas = httpResource<Reserva[]>(() => `${API}/reservas?conversaId=${this.conversaId()}`);
  protected readonly pedidos = httpResource<Pedido[]>(() => `${API}/pedidos?conversaId=${this.conversaId()}`);
  protected readonly pedidosAbertos = computed(() =>
    (this.pedidos.value() ?? []).filter((p) => p.status === 'AguardandoPagamento' || p.status === 'ConferirPagamento'),
  );
  /** Reservas ativas que ainda não viraram pedido. */
  protected readonly semPedido = computed(() => {
    const comPedido = new Set((this.pedidos.value() ?? []).filter((p) => p.status !== 'Cancelado').map((p) => p.reservaId));
    return (this.reservas.value() ?? []).filter((r) => r.status === 'Ativa' && !comPedido.has(r.id));
  });
  protected readonly ocupado = signal(false);

  constructor() {
    atualizarACada(10000, () => {
      this.reservas.reload();
      this.pedidos.reload();
    });
  }

  protected recarregar(): void {
    this.reservas.reload();
    this.pedidos.reload();
    this.alterou.emit();
  }

  protected async gerarPedido(r: Reserva): Promise<void> {
    const ref = this.dialog.open<PedidoDialog, Reserva, PedidoAcao>(PedidoDialog, { data: r, width: '520px', maxWidth: '95vw' });
    const resultado = await firstValueFrom(ref.afterClosed());
    if (!resultado) return;
    this.snack.open(resultado.aviso ?? `Pedido #${resultado.pedido.numero} criado${resultado.mensagemEnviada ? ' e enviado ao cliente com o Pix' : ''}.`, 'OK', {
      duration: resultado.aviso ? 9000 : 4000,
    });
    this.recarregar();
  }

  protected async encerrar(r: Reserva, acao: 'cancelar'): Promise<void> {
    if (!await this.confirmar.perguntar('O peixe volta para o estoque.', { titulo: 'Cancelar a reserva?', botao: 'Cancelar reserva', perigo: true })) return;
    this.ocupado.set(true);
    try {
      await firstValueFrom(this.http.post(`${API}/reservas/${r.id}/${acao}`, {}));
      this.snack.open('Reserva cancelada.', undefined, { duration: 4000 });
      this.recarregar();
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.ocupado.set(false);
    }
  }
}
