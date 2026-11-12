import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { API, FormaPagamento, TipoLancamento, mensagemDeErro } from '../../core/api';

export interface DadosLancamento {
  tipo: TipoLancamento;
  data: string;
  categorias: string[];
}

/** Despesa (ração, energia, fornecedor...) ou entrada que não veio de pedido, como venda no balcão. */
@Component({
  selector: 'app-lancamento-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ dados.tipo === 'Entrada' ? 'Nova entrada' : 'Nova despesa' }}</h2>
    <mat-dialog-content>
      <form [formGroup]="form" class="campos">
        <mat-form-field appearance="outline">
          <mat-label>Categoria</mat-label>
          <mat-select formControlName="categoria">
            @for (c of dados.categorias; track c) {
              <mat-option [value]="c">{{ c }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Valor</mat-label>
          <span matTextPrefix>R$&nbsp;</span>
          <input matInput type="number" min="0.01" step="0.01" formControlName="valor" />
        </mat-form-field>
        <mat-button-toggle-group formControlName="forma" aria-label="Forma">
          <mat-button-toggle value="Pix">Pix</mat-button-toggle>
          <mat-button-toggle value="Dinheiro">Dinheiro</mat-button-toggle>
          <mat-button-toggle value="Cartao">Cartão</mat-button-toggle>
        </mat-button-toggle-group>
        <mat-form-field appearance="outline">
          <mat-label>Descrição (opcional)</mat-label>
          <input matInput formControlName="descricao" />
        </mat-form-field>
      </form>
      @if (erro()) {
        <p class="erro">{{ erro() }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button [disabled]="salvando()" (click)="salvar()"><mat-icon>save</mat-icon> Lançar</button>
    </mat-dialog-actions>
  `,
  styles: `
    .campos {
      display: grid;
      gap: 4px;
    }
    mat-button-toggle-group {
      justify-self: start;
      margin-bottom: 16px;
    }
    .erro {
      color: var(--mat-sys-error);
    }
  `,
})
export class LancamentoDialog {
  protected readonly dados = inject<DadosLancamento>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<LancamentoDialog, boolean>);
  private readonly http = inject(HttpClient);
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    categoria: [this.dados.categorias[0] ?? '', Validators.required],
    valor: [null as number | null, [Validators.required, Validators.min(0.01)]],
    forma: [(this.dados.tipo === 'Entrada' ? 'Dinheiro' : 'Pix') as FormaPagamento],
    descricao: [''],
  });

  protected async salvar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const v = this.form.getRawValue();
      await firstValueFrom(
        this.http.post(`${API}/caixa/lancamentos`, { ...v, valor: Number(v.valor), descricao: v.descricao || null, tipo: this.dados.tipo, data: this.dados.data }),
      );
      this.ref.close(true);
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
    } finally {
      this.salvando.set(false);
    }
  }
}
