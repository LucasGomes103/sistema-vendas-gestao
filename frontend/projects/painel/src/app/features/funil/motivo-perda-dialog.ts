import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule } from '@angular/material/dialog';
import { MatRadioModule } from '@angular/material/radio';
import { MotivoPerda } from '../../core/api';

/** Nenhum card vai para "Perdido" sem motivo: é o que mostra à loja onde está perdendo venda. */
@Component({
  selector: 'app-motivo-perda-dialog',
  imports: [MatDialogModule, MatButtonModule, MatRadioModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Por que a venda foi perdida?</h2>
    <mat-dialog-content>
      <mat-radio-group class="opcoes" [value]="motivo()" (change)="motivo.set($event.value)">
        @for (m of opcoes; track m.valor) {
          <mat-radio-button [value]="m.valor">{{ m.texto }}</mat-radio-button>
        }
      </mat-radio-group>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button [disabled]="!motivo()" [mat-dialog-close]="motivo()">Marcar como perdido</button>
    </mat-dialog-actions>
  `,
  styles: `
    .opcoes {
      display: grid;
      gap: 4px;
    }
  `,
})
export class MotivoPerdaDialog {
  protected readonly motivo = signal<MotivoPerda | null>(null);
  protected readonly opcoes: { valor: MotivoPerda; texto: string }[] = [
    { valor: 'SemResposta', texto: 'Sem resposta' },
    { valor: 'NaoPagou', texto: 'Não pagou a reserva' },
    { valor: 'AchouCaro', texto: 'Achou caro' },
    { valor: 'SemEstoque', texto: 'Sem estoque do que queria' },
    { valor: 'RegiaoSemEntrega', texto: 'Região sem entrega' },
    { valor: 'Desistiu', texto: 'Desistiu ou comprou em outro lugar' },
  ];
}
