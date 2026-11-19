import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom } from 'rxjs';
import { API, AtributoProduto, Especie, EspecieEntrada } from '../../core/api';
import { MatIconModule } from '@angular/material/icon';
import { Termos } from '../../core/termos';

/** Espécie salva, para quem abriu já poder mostrá-la e selecioná-la sem recarregar a lista. */
export type EspecieSalva = Especie | undefined;

@Component({
  selector: 'app-especie-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ especie ? 'Editar ' + tx.produtoMin() : tx.novoProduto() }}</h2>
    <form [formGroup]="form" (ngSubmit)="salvar()">
      <mat-dialog-content class="campos">
        <mat-form-field appearance="outline" class="inteiro">
          <mat-label>{{ tx.aquario() ? 'Nome popular' : 'Nome' }}</mat-label>
          <input matInput formControlName="nomePopular" [placeholder]="tx.aquario() ? 'Ex.: Betta halfmoon' : 'Ex.: Camiseta básica preta'" />
          @if (form.controls.nomePopular.hasError('required')) {
            <mat-error>Informe o nome.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline" class="inteiro">
          <mat-label>{{ tx.nomeSecundario() }}</mat-label>
          <input matInput formControlName="nomeCientifico" [placeholder]="tx.aquario() ? 'Ex.: Betta splendens' : 'Opcional'" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Preço</mat-label>
          <span matTextPrefix>R$&nbsp;</span>
          <input matInput type="number" min="0" step="0.01" formControlName="preco" />
          @if (form.controls.preco.invalid) {
            <mat-error>Informe um preço válido.</mat-error>
          }
        </mat-form-field>
        @if (tx.aquario()) {
          <mat-form-field appearance="outline">
            <mat-label>Tamanho</mat-label>
            <input matInput type="number" min="0" step="0.5" formControlName="tamanhoCm" />
            <span matTextSuffix>cm</span>
          </mat-form-field>
        }
        <mat-form-field appearance="outline" class="inteiro">
          <mat-label>Observações</mat-label>
          <textarea
            matInput
            rows="3"
            formControlName="observacoes"
            [placeholder]="tx.aquario() ? 'Compatibilidade, parâmetros de água, cuidados' : 'Detalhes, medidas, cuidados'"
          ></textarea>
        </mat-form-field>
        <div class="inteiro detalhes">
          <span class="titulo-detalhes">Detalhes (opcional)</span>
          <p class="ajuda">Aparecem no catálogo, na vitrine e o robô usa para responder.</p>
          @for (c of campos(); track $index; let i = $index) {
            <div class="linha">
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>Campo</mat-label>
                <input matInput [value]="c.nome" (input)="alterarCampo(i, 'nome', $any($event.target).value)" maxlength="30" />
              </mat-form-field>
              <mat-form-field appearance="outline" subscriptSizing="dynamic">
                <mat-label>Valor</mat-label>
                <input matInput [value]="c.valor" (input)="alterarCampo(i, 'valor', $any($event.target).value)" maxlength="80" />
              </mat-form-field>
              <button mat-icon-button type="button" aria-label="Tirar campo" (click)="tirarCampo(i)"><mat-icon>close</mat-icon></button>
            </div>
          }
          @if (campos().length < maximo) {
            <div class="sugestoes">
              @for (s of sugestoesLivres(); track s) {
                <button mat-stroked-button type="button" (click)="adicionarCampo(s)"><mat-icon>add</mat-icon> {{ s }}</button>
              }
              <button mat-button type="button" (click)="adicionarCampo('')"><mat-icon>add</mat-icon> Outro campo</button>
            </div>
          }
        </div>
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
    .detalhes {
      display: grid;
      gap: 8px;
      margin-bottom: 12px;
    }
    .titulo-detalhes {
      font: var(--mat-sys-title-small);
    }
    .ajuda {
      margin: -6px 0 0;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .linha {
      display: grid;
      grid-template-columns: minmax(0, 2fr) minmax(0, 3fr) auto;
      gap: 8px;
      align-items: center;
    }
    .sugestoes {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }
  `,
})
export class EspecieDialog {
  protected readonly tx = inject(Termos);
  private readonly http = inject(HttpClient);
  private readonly ref = inject<MatDialogRef<EspecieDialog, EspecieSalva>>(MatDialogRef);
  protected readonly especie = inject<Especie | null>(MAT_DIALOG_DATA);

  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  /** Campos opcionais (Tamanho, Cor, Marca...). No máximo 8, como no servidor. */
  protected readonly maximo = 8;
  protected readonly campos = signal<AtributoProduto[]>((this.especie?.atributos ?? []).map((a) => ({ ...a })));
  protected readonly sugestoesLivres = computed(() => {
    const usados = new Set(this.campos().map((c) => c.nome.trim().toLowerCase()));
    return this.tx.camposSugeridos().filter((s) => !usados.has(s.toLowerCase()));
  });

  protected adicionarCampo(nome: string): void {
    this.campos.update((l) => [...l, { nome, valor: '' }]);
  }

  protected alterarCampo(i: number, chave: keyof AtributoProduto, valor: string): void {
    this.campos.update((l) => l.map((c, j) => (j === i ? { ...c, [chave]: valor } : c)));
  }

  protected tirarCampo(i: number): void {
    this.campos.update((l) => l.filter((_, j) => j !== i));
  }

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
      atributos: this.campos()
        .map((c) => ({ nome: c.nome.trim(), valor: c.valor.trim() }))
        .filter((c) => c.nome && c.valor),
    };

    try {
      if (this.especie) {
        await firstValueFrom(this.http.put(`${API}/especies/${this.especie.id}`, corpo));
        this.ref.close({ ...this.especie, ...corpo });
      } else {
        this.ref.close(await firstValueFrom(this.http.post<Especie>(`${API}/especies`, corpo)));
      }
    } catch {
      this.erro.set('Não foi possível salvar. Confira os campos e tente de novo.');
      this.salvando.set(false);
    }
  }
}
