import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { toSignal } from '@angular/core/rxjs-interop';
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
import { EspecieDialog, EspecieSalva } from '../catalogo/especie-dialog';
import { Termos } from '../../core/termos';

export interface DadosEntrada {
  /** Tanque já escolhido (quando aberto a partir do detalhe do tanque). */
  tanqueId?: string;
}

/** Entrada de lote: chegada de peixe do fornecedor, feita em pé pelo celular. */
@Component({
  selector: 'app-entrada-dialog',
  imports: [
    CurrencyPipe,
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
          <mat-label>{{ tx.produto() }}</mat-label>
          <mat-select formControlName="especieId">
            @for (e of especies.value() ?? []; track e.id) {
              <mat-option [value]="e.id">{{ e.nomePopular }}</mat-option>
            }
          </mat-select>
          <mat-error>Escolha {{ tx.oProduto() }}.</mat-error>
          @if (especieEscolhida(); as esp) {
            <mat-hint>Preço de venda: {{ esp.preco | currency: 'BRL' }} (vem do cadastro {{ tx.doProduto() }})</mat-hint>
          } @else if (especies.hasValue() && !especies.value().length) {
            <mat-hint>{{ tx.nenhumProduto() }} {{ tx.v().produtoFeminino ? 'cadastrada' : 'cadastrado' }} ainda.</mat-hint>
          }
        </mat-form-field>
        @if (auth.ehDono()) {
          <button mat-button type="button" class="inteiro novo" (click)="novaEspecie()">
            <mat-icon>add</mat-icon> Cadastrar {{ tx.novoProduto().toLowerCase() }}
          </button>
        }

        <mat-form-field appearance="outline" class="inteiro">
          <mat-label>{{ tx.local() }}</mat-label>
          <mat-select formControlName="tanqueId">
            @for (t of tanques.value() ?? []; track t.id) {
              <mat-option [value]="t.id">{{ t.nome }}{{ t.tipo === 'Quarentena' ? ' (' + tx.isolamentoCurto() + ')' : '' }}</mat-option>
            }
          </mat-select>
          <mat-error>Escolha o {{ tx.localMin() }}.</mat-error>
          @if (tanques.hasValue() && !tanques.value().length) {
            <mat-hint>Nenhum {{ tx.localMin() }} cadastrado ainda.</mat-hint>
          }
        </mat-form-field>
        @if (auth.ehDono()) {
          <button mat-button type="button" class="inteiro novo" (click)="novoTanque()">
            <mat-icon>add</mat-icon> Cadastrar {{ tx.novoLocal().toLowerCase() }}
          </button>
        }

        <mat-form-field appearance="outline">
          <mat-label>Quantidade</mat-label>
          <input matInput type="number" min="1" inputmode="numeric" formControlName="quantidade" />
          <mat-error>Informe a quantidade.</mat-error>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Custo por {{ tx.item() }} (compra)</mat-label>
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

        @if (tx.aquario()) {
          <mat-form-field appearance="outline">
            <mat-label>Dias de quarentena</mat-label>
            <input matInput type="number" min="0" inputmode="numeric" formControlName="quarentenaDias" />
            <mat-hint>0 = já disponível para venda</mat-hint>
          </mat-form-field>
        }

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
  protected readonly tx = inject(Termos);
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

  private readonly especieId = toSignal(this.form.controls.especieId.valueChanges, { initialValue: this.form.controls.especieId.value });
  protected readonly especieEscolhida = computed(() => (this.especies.value() ?? []).find((e) => e.id === this.especieId()));

  protected novaEspecie(): void {
    this.dialog
      .open<EspecieDialog, null, EspecieSalva>(EspecieDialog, { data: null, width: '520px', maxWidth: '95vw', autoFocus: 'first-tabbable' })
      .afterClosed()
      .subscribe((especie) => {
        if (!especie) return;
        // Entra direto na lista (sem esperar recarregar) para o combo já mostrar a espécie selecionada.
        this.especies.update((lista) =>
          [...(lista ?? []), especie].sort((a, b) => a.nomePopular.localeCompare(b.nomePopular, 'pt-BR')),
        );
        this.form.controls.especieId.setValue(especie.id);
      });
  }

  protected novoTanque(): void {
    this.dialog
      .open<TanqueDialog, unknown, TanqueCriado>(TanqueDialog, { width: '440px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((tanque) => {
        if (!tanque) return;
        this.tanques.update((lista) => [...(lista ?? []), tanque].sort((a, b) => a.nome.localeCompare(b.nome, 'pt-BR')));
        this.form.controls.tanqueId.setValue(tanque.id);
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
