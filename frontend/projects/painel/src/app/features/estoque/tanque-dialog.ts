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
import { API, mensagemDeErro } from '../../core/api';

/** Id do tanque criado, para quem abriu o diálogo já poder selecioná-lo. */
export type TanqueCriado = string | undefined;

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
    <h2 mat-dialog-title>Novo tanque</h2>
    <form [formGroup]="form" (ngSubmit)="salvar()">
      <mat-dialog-content class="campos">
        <mat-form-field appearance="outline">
          <mat-label>Nome</mat-label>
          <input matInput formControlName="nome" placeholder="Ex.: T01, Aquário da vitrine" cdkFocusInitial />
          <mat-error>Informe o nome do tanque.</mat-error>
        </mat-form-field>

        <mat-button-toggle-group formControlName="tipo" aria-label="Tipo do tanque" class="tipo">
          <mat-button-toggle value="Venda"><mat-icon>storefront</mat-icon> Venda</mat-button-toggle>
          <mat-button-toggle value="Quarentena"><mat-icon>shield</mat-icon> Quarentena</mat-button-toggle>
        </mat-button-toggle-group>

        <mat-form-field appearance="outline">
          <mat-label>Litros</mat-label>
          <input matInput type="number" min="1" inputmode="numeric" formControlName="litros" />
          <mat-hint>Opcional</mat-hint>
        </mat-form-field>

        @if (erro()) {
          <p class="erro"><mat-icon>error</mat-icon>{{ erro() }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancelar</button>
        <button mat-flat-button type="submit" [disabled]="salvando()">Cadastrar tanque</button>
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
      const criado = await firstValueFrom(
        this.http.post<{ id: string }>(`${API}/tanques`, {
          nome: v.nome.trim(),
          tipo: v.tipo,
          litros: v.litros ? Number(v.litros) : null,
        }),
      );
      this.ref.close(criado.id);
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
      this.salvando.set(false);
    }
  }
}
