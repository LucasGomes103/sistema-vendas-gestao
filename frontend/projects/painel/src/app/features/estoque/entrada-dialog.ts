import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { API, Especie, Fornecedor, Tanque, mensagemDeErro } from '../../core/api';
import { Auth } from '../../core/auth';
import { TanqueDialog, TanqueCriado } from './tanque-dialog';

export interface DadosEntrada {
  /** Tanque já escolhido (quando aberto a partir do detalhe do tanque). */
  tanqueId?: string;
}

/** Entrada de lote: chegada de peixe do fornecedor, feita em pé pelo celular. */
@Component({
  selector: 'app-entrada-dialog',
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
    <h2 mat-dialog-title>Entrada de lote</h2>
    <form [formGroup]="form" (ngSubmit)="salvar()">
      <mat-dialog-content class="campos">
        <mat-form-field appearance="outline" class="inteiro">
          <mat-label>Espécie</mat-label>
          <mat-select formControlName="especieId">
            @for (e of especies.value() ?? []; track e.id) {
              <mat-option [value]="e.id">{{ e.nomePopular }}</mat-option>
            }
          </mat-select>
          <mat-error>Escolha a espécie.</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline" class="inteiro">
          <mat-label>Tanque</mat-label>
          <mat-select formControlName="tanqueId">
            @for (t of tanques.value() ?? []; track t.id) {
              <mat-option [value]="t.id">{{ t.nome }}{{ t.tipo === 'Quarentena' ? ' (quarentena)' : '' }}</mat-option>
            }
          </mat-select>
          <mat-error>Escolha o tanque.</mat-error>
          @if (tanques.hasValue() && !tanques.value().length) {
            <mat-hint>Nenhum tanque cadastrado ainda.</mat-hint>
          }
        </mat-form-field>
        @if (auth.ehDono()) {
          <button mat-button type="button" class="inteiro novo" (click)="novoTanque()">
            <mat-icon>add</mat-icon> Cadastrar novo tanque
          </button>
        }

        <mat-form-field appearance="outline">
          <mat-label>Quantidade</mat-label>
          <input matInput type="number" min="1" inputmode="numeric" formControlName="quantidade" />
          <mat-error>Informe a quantidade.</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Custo por peixe</mat-label>
          <span matTextPrefix>R$&nbsp;</span>
          <input matInput type="number" min="0" step="0.01" inputmode="decimal" formControlName="custoUnitario" />
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Fornecedor</mat-label>
          <mat-select formControlName="fornecedorId">
            <mat-option [value]="null">Sem fornecedor</mat-option>
            @for (f of fornecedores.value() ?? []; track f.id) {
              <mat-option [value]="f.id">{{ f.nome }}</mat-option>
            }
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Dias de quarentena</mat-label>
          <input matInput type="number" min="0" inputmode="numeric" formControlName="quarentenaDias" />
          <mat-hint>0 = já disponível para venda</mat-hint>
        </mat-form-field>

        @if (erro()) {
          <p class="erro inteiro"><mat-icon>error</mat-icon>{{ erro() }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancelar</button>
        <button mat-flat-button type="submit" [disabled]="salvando()">Registrar entrada</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .campos {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 0 12px;
      padding-top: 8px !important;
    }
    .inteiro {
      grid-column: 1 / -1;
    }
    .novo {
      justify-self: start;
      margin: -4px 0 8px;
    }
    .erro {
      display: flex;
      align-items: center;
      gap: 6px;
      margin: 0;
      color: var(--mat-sys-error);
    }
  `,
})
export class EntradaDialog {
  private readonly http = inject(HttpClient);
  private readonly ref = inject<MatDialogRef<EntradaDialog, boolean>>(MatDialogRef);
  private readonly dados = inject<DadosEntrada | null>(MAT_DIALOG_DATA);
  private readonly dialog = inject(MatDialog);
  protected readonly auth = inject(Auth);

  protected readonly especies = httpResource<Especie[]>(() => `${API}/especies`);
  protected readonly tanques = httpResource<Tanque[]>(() => `${API}/tanques`);
  protected readonly fornecedores = httpResource<Fornecedor[]>(() => `${API}/fornecedores`);

  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).group({
    especieId: ['', Validators.required],
    tanqueId: [this.dados?.tanqueId ?? '', Validators.required],
    quantidade: [null as number | null, [Validators.required, Validators.min(1)]],
    custoUnitario: [null as number | null, Validators.min(0)],
    fornecedorId: [null as string | null],
    quarentenaDias: [0, Validators.min(0)],
  });

  protected novoTanque(): void {
    this.dialog
      .open<TanqueDialog, unknown, TanqueCriado>(TanqueDialog, { width: '440px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((id) => {
        if (!id) return;
        this.tanques.reload();
        this.form.controls.tanqueId.setValue(id);
      });
  }

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
        this.http.post(`${API}/lotes`, {
          especieId: v.especieId,
          tanqueId: v.tanqueId,
          fornecedorId: v.fornecedorId,
          quantidade: Number(v.quantidade),
          custoUnitario: v.custoUnitario === null ? null : Number(v.custoUnitario),
          quarentenaDias: Number(v.quarentenaDias ?? 0),
        }),
      );
      this.ref.close(true);
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
      this.salvando.set(false);
    }
  }
}
