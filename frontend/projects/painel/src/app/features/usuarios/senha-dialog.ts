import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, UsuarioEquipe, mensagemDeErro } from '../../core/api';

/** Mesma regra da API: 8 caracteres, com letra e número. */
export function senhaForte(c: AbstractControl): ValidationErrors | null {
  const v = String(c.value ?? '');
  return v.length >= 8 && /\p{L}/u.test(v) && /\d/.test(v) ? null : { fraca: true };
}

function confirmacaoIgual(g: AbstractControl): ValidationErrors | null {
  return g.get('novaSenha')?.value === g.get('confirmacao')?.value ? null : { diferente: true };
}

/**
 * Sem dados: o usuário logado troca a própria senha (confirma a atual).
 * Com um usuário: o dono define uma senha nova para ele.
 */
@Component({
  selector: 'app-senha-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ alvo ? 'Nova senha para ' + alvo.nome : 'Trocar minha senha' }}</h2>
    <form [formGroup]="form" (ngSubmit)="salvar()">
      <mat-dialog-content class="campos">
        @if (!alvo) {
          <mat-form-field appearance="outline">
            <mat-label>Senha atual</mat-label>
            <input matInput [type]="ver() ? 'text' : 'password'" formControlName="senhaAtual" autocomplete="current-password" />
            @if (form.controls.senhaAtual.hasError('required')) {
              <mat-error>Informe a senha atual.</mat-error>
            }
          </mat-form-field>
        } @else {
          <p class="dica">{{ alvo.nome }} vai entrar com essa senha. Combine com a pessoa e peça para ela trocar depois.</p>
        }
        <mat-form-field appearance="outline">
          <mat-label>Nova senha</mat-label>
          <input matInput [type]="ver() ? 'text' : 'password'" formControlName="novaSenha" autocomplete="new-password" />
          <button mat-icon-button matSuffix type="button" (click)="ver.set(!ver())" [attr.aria-label]="ver() ? 'Esconder senha' : 'Mostrar senha'">
            <mat-icon>{{ ver() ? 'visibility_off' : 'visibility' }}</mat-icon>
          </button>
          <mat-hint>Pelo menos 8 caracteres, com letra e número.</mat-hint>
          @if (form.controls.novaSenha.invalid) {
            <mat-error>Pelo menos 8 caracteres, com letra e número.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Repita a nova senha</mat-label>
          <input matInput [type]="ver() ? 'text' : 'password'" formControlName="confirmacao" autocomplete="new-password" />
        </mat-form-field>
        @if (form.hasError('diferente') && form.controls.confirmacao.touched) {
          <p class="erro">As senhas não são iguais.</p>
        }
        @if (erro()) {
          <p class="erro">{{ erro() }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancelar</button>
        <button mat-flat-button type="submit" [disabled]="salvando()">Salvar senha</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .campos {
      display: grid;
      gap: 4px;
      padding-top: 8px !important;
    }
    .dica {
      margin: 0 0 12px;
      color: var(--mat-sys-on-surface-variant);
    }
    .erro {
      margin: 0;
      color: var(--mat-sys-error);
    }
  `,
})
export class SenhaDialog {
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);
  private readonly ref = inject<MatDialogRef<SenhaDialog, boolean>>(MatDialogRef);
  protected readonly alvo = inject<UsuarioEquipe | null>(MAT_DIALOG_DATA);

  protected readonly ver = signal(false);
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      senhaAtual: ['', this.alvo ? [] : [Validators.required]],
      novaSenha: ['', [Validators.required, senhaForte]],
      confirmacao: ['', Validators.required],
    },
    { validators: confirmacaoIgual },
  );

  protected async salvar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.salvando.set(true);
    this.erro.set(null);
    const v = this.form.getRawValue();

    try {
      if (this.alvo) {
        await firstValueFrom(this.http.post(`${API}/usuarios/${this.alvo.id}/senha`, { novaSenha: v.novaSenha }));
        this.snack.open(`Senha de ${this.alvo.nome} alterada.`, undefined, { duration: 3000 });
      } else {
        await firstValueFrom(this.http.post(`${API}/auth/senha`, { senhaAtual: v.senhaAtual, novaSenha: v.novaSenha }));
        this.snack.open('Senha alterada.', undefined, { duration: 3000 });
      }
      this.ref.close(true);
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
      this.salvando.set(false);
    }
  }
}
