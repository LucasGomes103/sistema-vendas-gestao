import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom } from 'rxjs';
import { API, LojaAdmin, mensagemDeErro } from '../../core/api';

/** Administrador da plataforma edita nome, endereço e tipo da loja e nome/e-mail do dono. */
@Component({
  selector: 'app-editar-loja-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Editar loja</h2>
    <form [formGroup]="form" (ngSubmit)="salvar()">
      <mat-dialog-content class="campos">
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Nome da loja</mat-label>
          <input matInput formControlName="nome" autocomplete="off" />
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Endereço da vitrine</mat-label>
          <span matTextPrefix>/</span>
          <input matInput formControlName="slug" autocomplete="off" />
          @if (form.controls.slug.value !== loja.slug) {
            <mat-hint class="aviso">O link antigo da vitrine (/{{ loja.slug }}) para de funcionar.</mat-hint>
          } @else {
            <mat-hint>Letras minúsculas sem acento, números e hífen.</mat-hint>
          }
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Tipo de loja</mat-label>
          <input matInput formControlName="segmento" autocomplete="off" />
        </mat-form-field>
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Limite de respostas da IA por mês</mat-label>
          <input matInput type="number" min="0" formControlName="limiteRespostasIAMes" placeholder="Padrão da plataforma" />
          <mat-hint>Vazio: padrão da plataforma. 0: sem limite. Acima do limite o robô responde por regras.</mat-hint>
        </mat-form-field>
        @if (loja.emailDono) {
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Nome do dono</mat-label>
            <input matInput formControlName="nomeDono" autocomplete="off" />
          </mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>E-mail do dono (login)</mat-label>
            <input matInput type="email" formControlName="emailDono" autocomplete="off" />
            @if (form.controls.emailDono.value?.trim()?.toLowerCase() !== loja.emailDono) {
              <mat-hint class="aviso">O dono passa a entrar com este e-mail e sai da sessão atual.</mat-hint>
            }
          </mat-form-field>
        }
        @if (erro()) {
          <p class="erro">{{ erro() }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancelar</button>
        <button mat-flat-button type="submit" [disabled]="salvando() || form.invalid">Salvar</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .campos {
      display: grid;
      gap: 16px;
      padding-top: 8px !important;
    }
    .aviso {
      color: var(--sv-alerta);
    }
    .erro {
      margin: 0;
      color: var(--mat-sys-error);
    }
  `,
})
export class EditarLojaDialog {
  protected readonly loja = inject<LojaAdmin>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<EditarLojaDialog, boolean>>(MatDialogRef);
  private readonly http = inject(HttpClient);

  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    nome: [this.loja.nome, [Validators.required, Validators.minLength(2), Validators.maxLength(120)]],
    slug: [this.loja.slug, [Validators.required, Validators.minLength(3), Validators.maxLength(40)]],
    segmento: [this.loja.segmento ?? '', Validators.maxLength(60)],
    nomeDono: [this.loja.dono ?? '', this.loja.emailDono ? [Validators.required, Validators.minLength(2)] : []],
    emailDono: [this.loja.emailDono ?? '', this.loja.emailDono ? [Validators.required, Validators.email] : []],
    limiteRespostasIAMes: new FormControl<number | null>(this.loja.limiteIaProprio, [Validators.min(0), Validators.max(1_000_000)]),
  });

  protected async salvar(): Promise<void> {
    if (this.form.invalid) return;
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const v = this.form.getRawValue();
      const limite = v.limiteRespostasIAMes;
      await firstValueFrom(
        this.http.put(`${API}/admin/lojas/${this.loja.id}`, { ...v, limiteRespostasIAMes: limite === null || `${limite}` === '' ? null : Number(limite) }),
      );
      this.ref.close(true);
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
    } finally {
      this.salvando.set(false);
    }
  }
}
