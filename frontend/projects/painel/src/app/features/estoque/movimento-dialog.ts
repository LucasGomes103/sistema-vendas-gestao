import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { API, LoteNoTanque, Tanque, mensagemDeErro } from '../../core/api';
import { Termos } from '../../core/termos';

export type AcaoMovimento = 'baixa' | 'transferencia' | 'ajuste';

export interface DadosMovimento {
  acao: AcaoMovimento;
  tanqueId: string;
  tanqueNome: string;
  lotes: LoteNoTanque[];
  loteId?: string;
}

function titulos(aquario: boolean, local: string): Record<AcaoMovimento, string> {
  return {
    baixa: aquario ? 'Baixa por morte' : 'Baixa por perda',
    transferencia: `Transferir para outro ${local}`,
    ajuste: 'Ajuste de contagem',
  };
}

@Component({
  selector: 'app-movimento-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ titulo }}</h2>
    <form [formGroup]="form" (ngSubmit)="salvar()">
      <mat-dialog-content class="campos">
        <p class="origem"><mat-icon>{{ tx.iconeLocal() }}</mat-icon>{{ dados.tanqueNome }}</p>

        <mat-form-field appearance="outline">
          <mat-label>{{ tx.aquario() ? 'Peixe' : 'Produto' }} (lote)</mat-label>
          <mat-select formControlName="loteId">
            @for (l of dados.lotes; track l.loteId) {
              <mat-option [value]="l.loteId">
                {{ l.especie }} · {{ l.disponivel }} no {{ tx.localMin() }}{{ l.status === 'Quarentena' ? ' · ' + tx.isolamentoCurto() : '' }}
              </mat-option>
            }
          </mat-select>
          <mat-error>Escolha o lote.</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>{{ dados.acao === 'ajuste' ? 'Diferença (+ ou −)' : 'Quantidade' }}</mat-label>
          <input matInput type="number" inputmode="numeric" formControlName="quantidade" />
          @if (lote() && dados.acao !== 'ajuste') {
            <mat-hint>Máximo {{ lote()!.disponivel }}</mat-hint>
          }
          <mat-error>Informe a quantidade.</mat-error>
        </mat-form-field>

        @if (dados.acao === 'transferencia') {
          <mat-form-field appearance="outline">
            <mat-label>{{ tx.local() }} de destino</mat-label>
            <mat-select formControlName="tanqueDestinoId">
              @for (t of destinos(); track t.id) {
                <mat-option [value]="t.id">{{ t.nome }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        }

        <mat-form-field appearance="outline">
          <mat-label>{{ dados.acao === 'ajuste' ? 'Motivo (obrigatório)' : 'Observação' }}</mat-label>
          <input matInput formControlName="observacao" [placeholder]="dados.acao === 'baixa' ? (tx.aquario() ? 'Ex.: chegou fraco do fornecedor' : 'Ex.: chegou com defeito') : ''" />
        </mat-form-field>

        @if (erro()) {
          <p class="erro"><mat-icon>error</mat-icon>{{ erro() }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancelar</button>
        <button mat-flat-button type="submit" [class.perigo]="dados.acao === 'baixa'" [disabled]="salvando()">
          Confirmar
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .campos {
      display: grid;
      gap: 0;
      padding-top: 8px !important;
      min-width: min(420px, 80vw);
    }
    .origem {
      display: flex;
      align-items: center;
      gap: 6px;
      margin: 0 0 12px;
      color: var(--mat-sys-on-surface-variant);
    }
    .erro {
      display: flex;
      align-items: center;
      gap: 6px;
      margin: 0;
      color: var(--mat-sys-error);
    }
    .perigo {
      --mat-button-filled-container-color: var(--mat-sys-error);
      --mat-button-filled-label-text-color: var(--mat-sys-on-error);
    }
  `,
})
export class MovimentoDialog {
  protected readonly tx = inject(Termos);
  private readonly http = inject(HttpClient);
  private readonly ref = inject<MatDialogRef<MovimentoDialog, boolean>>(MatDialogRef);
  protected readonly dados = inject<DadosMovimento>(MAT_DIALOG_DATA);
  protected readonly titulo = titulos(this.tx.aquario(), this.tx.localMin())[this.dados.acao];

  private readonly tanques = httpResource<Tanque[]>(() =>
    this.dados.acao === 'transferencia' ? `${API}/tanques` : undefined,
  );
  protected readonly destinos = computed(() => (this.tanques.value() ?? []).filter((t) => t.id !== this.dados.tanqueId));

  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).group({
    loteId: [this.dados.loteId ?? (this.dados.lotes.length === 1 ? this.dados.lotes[0].loteId : ''), Validators.required],
    quantidade: [null as number | null, Validators.required],
    tanqueDestinoId: [null as string | null],
    observacao: [''],
  });

  private readonly loteId = toSignal(this.form.controls.loteId.valueChanges, { initialValue: this.form.controls.loteId.value });
  protected readonly lote = computed(() => this.dados.lotes.find((l) => l.loteId === this.loteId()));

  protected async salvar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.salvando.set(true);
    this.erro.set(null);

    const v = this.form.getRawValue();
    try {
      await firstValueFrom(
        this.http.post(`${API}/tanques/${this.dados.tanqueId}/${this.dados.acao}`, {
          loteId: v.loteId,
          quantidade: Number(v.quantidade),
          tanqueDestinoId: v.tanqueDestinoId,
          observacao: v.observacao || null,
        }),
      );
      this.ref.close(true);
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
      this.salvando.set(false);
    }
  }
}
