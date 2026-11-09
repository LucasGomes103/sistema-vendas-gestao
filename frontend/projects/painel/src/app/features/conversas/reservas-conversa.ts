import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, Reserva, mensagemDeErro } from '../../core/api';
import { atualizarACada } from '../../core/atualizar';

/** Peixe separado para o cliente por 24 h: o vendedor confirma a venda (sai do estoque) ou cancela. */
@Component({
  selector: 'app-reservas-conversa',
  imports: [CurrencyPipe, DatePipe, MatButtonModule, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (r of ativas(); track r.id) {
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
          <button mat-flat-button [disabled]="ocupado()" (click)="encerrar(r, 'confirmar')"><mat-icon>check</mat-icon> Confirmar venda</button>
        </span>
      </div>
    }
  `,
  styles: `
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
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);

  readonly conversaId = input.required<string>();
  /** Avisa a tela para recarregar o card (a confirmação muda a etapa para Pago). */
  readonly alterou = output<void>();

  protected readonly reservas = httpResource<Reserva[]>(() => `${API}/reservas?conversaId=${this.conversaId()}`);
  protected readonly ativas = computed(() => (this.reservas.value() ?? []).filter((r) => r.status === 'Ativa'));
  protected readonly ocupado = signal(false);

  constructor() {
    atualizarACada(10000, () => this.reservas.reload());
  }

  protected async encerrar(r: Reserva, acao: 'confirmar' | 'cancelar'): Promise<void> {
    if (acao === 'cancelar' && !confirm('Cancelar a reserva? O peixe volta para o estoque.')) return;
    this.ocupado.set(true);
    try {
      await firstValueFrom(this.http.post(`${API}/reservas/${r.id}/${acao}`, {}));
      this.snack.open(acao === 'confirmar' ? 'Venda confirmada: o peixe saiu do estoque e o card foi para Pago.' : 'Reserva cancelada.', undefined, { duration: 4000 });
      this.reservas.reload();
      this.alterou.emit();
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.ocupado.set(false);
    }
  }
}
