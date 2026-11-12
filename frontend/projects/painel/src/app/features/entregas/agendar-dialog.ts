import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { API, Entrega, EntregaAcao, PessoaEntrega, mensagemDeErro } from '../../core/api';

const JANELAS = ['9h às 12h', '12h às 15h', '14h às 18h', '18h às 20h'];

function isoLocal(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Dia, horário e quem leva. O cliente recebe a confirmação pelo WhatsApp (se a janela de 24 h estiver aberta). */
@Component({
  selector: 'app-agendar-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatCheckboxModule, MatChipsModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ entrega.tipo === 'Entrega' ? 'Agendar entrega' : 'Agendar retirada' }} · pedido #{{ entrega.numeroPedido }}</h2>
    <mat-dialog-content>
      <p class="cliente">{{ entrega.cliente }}{{ entrega.endereco ? ' · ' + entrega.endereco : '' }}</p>
      <mat-form-field appearance="outline">
        <mat-label>Dia</mat-label>
        <input matInput type="date" [min]="hoje" [(ngModel)]="data" />
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>Horário (opcional)</mat-label>
        <input matInput [(ngModel)]="janela" maxlength="40" placeholder="14h às 18h" />
      </mat-form-field>
      <mat-chip-set class="janelas">
        @for (j of janelas; track j) {
          <mat-chip (click)="janela = j">{{ j }}</mat-chip>
        }
      </mat-chip-set>
      @if (entrega.tipo === 'Entrega') {
        <mat-form-field appearance="outline">
          <mat-label>Quem leva</mat-label>
          <mat-select [(ngModel)]="entregadorId">
            <mat-option [value]="null">Ninguém definido</mat-option>
            @for (p of pessoas.value() ?? []; track p.id) {
              <mat-option [value]="p.id">{{ p.nome }}{{ p.papel === 'Entregador' ? '' : ' (' + p.papel.toLowerCase() + ')' }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      }
      @if (entrega.conversaId) {
        <mat-checkbox [(ngModel)]="avisar">Avisar o cliente no WhatsApp</mat-checkbox>
      }
      @if (erro()) {
        <p class="erro">{{ erro() }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button [disabled]="salvando() || !data" (click)="salvar()"><mat-icon>event</mat-icon> Agendar</button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content {
      display: grid;
    }
    .cliente {
      margin: 0 0 16px;
      color: var(--mat-sys-on-surface-variant);
    }
    .janelas {
      margin: -8px 0 16px;
    }
    .erro {
      color: var(--mat-sys-error);
    }
  `,
})
export class AgendarDialog {
  protected readonly entrega = inject<Entrega>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<AgendarDialog, EntregaAcao>);
  private readonly http = inject(HttpClient);

  protected readonly pessoas = httpResource<PessoaEntrega[]>(() => (this.entrega.tipo === 'Entrega' ? `${API}/entregas/pessoas` : undefined));
  protected readonly janelas = JANELAS;
  protected readonly hoje = isoLocal(new Date());
  protected data = this.entrega.data ?? this.hoje;
  protected janela = this.entrega.janela ?? '';
  protected entregadorId: string | null = this.entrega.entregadorId;
  protected avisar = true;
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const r = await firstValueFrom(
        this.http.post<EntregaAcao>(`${API}/entregas/${this.entrega.id}/agendar`, {
          data: this.data,
          janela: this.janela || null,
          entregadorId: this.entregadorId,
          avisarCliente: this.avisar && !!this.entrega.conversaId,
        }),
      );
      this.ref.close(r);
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
    } finally {
      this.salvando.set(false);
    }
  }
}
