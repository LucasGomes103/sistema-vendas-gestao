import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom } from 'rxjs';
import { API, Tanque, mensagemDeErro } from '../../core/api';
import { Termos } from '../../core/termos';

/** Tanque criado, para quem abriu o diálogo já poder mostrá-lo e selecioná-lo. */
export type TanqueCriado = Tanque | undefined;

/** Cadastro de tanque (só o dono). */
@Component({
  selector: 'app-tanque-dialog',
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ tx.novoLocal() }}</h2>
    <form [formGroup]="form" (ngSubmit)="salvar()">
      <mat-dialog-content class="campos">
        <mat-form-field appearance="outline">
          <mat-label>Nome</mat-label>
          <input matInput formControlName="nome" [placeholder]="tx.aquario() ? 'Ex.: T01, Aquário da vitrine' : 'Ex.: Prateleira A, Depósito, Vitrine'" cdkFocusInitial />
          <mat-error>Informe o nome {{ 'do ' + tx.localMin() }}.</mat-error>
        </mat-form-field>

        <mat-button-toggle-group formControlName="tipo" [attr.aria-label]="'Tipo do ' + tx.localMin()" class="tipo">
          <mat-button-toggle value="Venda"><mat-icon>storefront</mat-icon> Venda</mat-button-toggle>
          <mat-button-toggle value="Quarentena"><mat-icon>shield</mat-icon> {{ tx.aquario() ? 'Quarentena' : 'Não vende' }}</mat-button-toggle>
        </mat-button-toggle-group>

        @if (tx.aquario()) {
          <mat-form-field appearance="outline">
            <mat-label>Litros</mat-label>
            <input matInput type="number" min="1" inputmode="numeric" formControlName="litros" />
            <mat-hint>Opcional</mat-hint>
          </mat-form-field>
        }

        @if (erro()) {
          <p class="erro"><mat-icon>error</mat-icon>{{ erro() }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancelar</button>
        <button mat-flat-button type="submit" [disabled]="salvando()">Cadastrar {{ tx.localMin() }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .campos {
      display: grid;
      gap: 12px;
      padding-top: 8px !important;
    }
    .tipo {
      width: 100%;

      mat-button-toggle {
        flex: 1;
      }
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
export class TanqueDialog {
  protected readonly tx = inject(Termos);
  private readonly http = inject(HttpClient);
  private readonly ref = inject<MatDialogRef<TanqueDialog, TanqueCriado>>(MatDialogRef);

  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    nome: ['', [Validators.required, Validators.pattern(/\S/)]],
    tipo: ['Venda' as 'Venda' | 'Quarentena'],
    litros: [null as number | null, Validators.min(1)],
  });

  protected async salvar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.salvando.set(true);
    this.erro.set(null);

    const v = this.form.getRawValue();
    try {
      const corpo = { nome: v.nome.trim(), tipo: v.tipo, litros: v.litros ? Number(v.litros) : null };
      const criado = await firstValueFrom(this.http.post<{ id: string }>(`${API}/tanques`, corpo));
      this.ref.close({ ...corpo, id: criado.id, total: 0, baixasUltimos7Dias: 0, itens: [] });
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
      this.salvando.set(false);
    }
  }
}
