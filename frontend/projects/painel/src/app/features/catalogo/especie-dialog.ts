import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom } from 'rxjs';
import { API, Especie, EspecieEntrada } from '../../core/api';

/** Id da espécie salva (a nova, quando é cadastro), para quem abriu já poder selecioná-la. */
export type EspecieSalva = string | undefined;

@Component({
  selector: 'app-especie-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ especie ? 'Editar espécie' : 'Nova espécie' }}</h2>
    <form [formGroup]="form" (ngSubmit)="salvar()">
      <mat-dialog-content class="campos">
        <mat-form-field appearance="outline" class="inteiro">
          <mat-label>Nome popular</mat-label>
          <input matInput formControlName="nomePopular" placeholder="Ex.: Betta halfmoon" />
          @if (form.controls.nomePopular.hasError('required')) {
            <mat-error>Informe o nome popular.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline" class="inteiro">
          <mat-label>Nome científico</mat-label>
          <input matInput formControlName="nomeCientifico" placeholder="Ex.: Betta splendens" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Preço</mat-label>
          <span matTextPrefix>R$&nbsp;</span>
          <input matInput type="number" min="0" step="0.01" formControlName="preco" />
          @if (form.controls.preco.invalid) {
            <mat-error>Informe um preço válido.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Tamanho</mat-label>
          <input matInput type="number" min="0" step="0.5" formControlName="tamanhoCm" />
          <span matTextSuffix>cm</span>
        </mat-form-field>
        <mat-form-field appearance="outline" class="inteiro">
          <mat-label>Observações</mat-label>
          <textarea
            matInput
            rows="3"
            formControlName="observacoes"
            placeholder="Compatibilidade, parâmetros de água, cuidados"
          ></textarea>
        </mat-form-field>
        @if (erro()) {
          <p class="erro inteiro">{{ erro() }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancelar</button>
        <button mat-flat-button type="submit" [disabled]="salvando()">Salvar</button>
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
    .erro {
      margin: 0;
      color: var(--mat-sys-error);
    }
  `,
})
export class EspecieDialog {
  private readonly http = inject(HttpClient);
  private readonly ref = inject<MatDialogRef<EspecieDialog, EspecieSalva>>(MatDialogRef);
  protected readonly especie = inject<Especie | null>(MAT_DIALOG_DATA);

  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    nomePopular: [this.especie?.nomePopular ?? '', [Validators.required, Validators.maxLength(120)]],
    nomeCientifico: [this.especie?.nomeCientifico ?? ''],
    preco: [this.especie?.preco ?? 0, [Validators.required, Validators.min(0)]],
    tamanhoCm: [this.especie?.tamanhoCm ?? (null as number | null)],
    observacoes: [this.especie?.observacoes ?? ''],
  });

  protected async salvar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.salvando.set(true);
    this.erro.set(null);

    const v = this.form.getRawValue();
    const corpo: EspecieEntrada = {
      nomePopular: v.nomePopular,
      nomeCientifico: v.nomeCientifico || null,
      preco: Number(v.preco),
      tamanhoCm: v.tamanhoCm ? Number(v.tamanhoCm) : null,
      observacoes: v.observacoes || null,
    };

    try {
      if (this.especie) {
        await firstValueFrom(this.http.put(`${API}/especies/${this.especie.id}`, corpo));
        this.ref.close(this.especie.id);
      } else {
        const criada = await firstValueFrom(this.http.post<{ id: string }>(`${API}/especies`, corpo));
        this.ref.close(criada.id);
      }
    } catch {
      this.erro.set('Não foi possível salvar. Confira os campos e tente de novo.');
      this.salvando.set(false);
    }
  }
}
