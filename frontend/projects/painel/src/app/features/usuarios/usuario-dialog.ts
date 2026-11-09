import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { firstValueFrom } from 'rxjs';
import { API, Papel, ROTULO_PAPEL, UsuarioEquipe, mensagemDeErro } from '../../core/api';
import { senhaForte } from './senha-dialog';

const DESCRICAO_PAPEL: Record<Papel, string> = {
  Dono: 'Tudo, inclusive usuários e configuração',
  Vendedor: 'Funil, conversas, estoque e catálogo',
  Entregador: 'Só entregas do dia (em breve)',
};

@Component({
  selector: 'app-usuario-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSlideToggleModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ usuario ? 'Editar usuário' : 'Novo usuário' }}</h2>
    <form [formGroup]="form" (ngSubmit)="salvar()">
      <mat-dialog-content class="campos">
        <mat-form-field appearance="outline">
          <mat-label>Nome</mat-label>
          <input matInput formControlName="nome" autocomplete="off" />
          @if (form.controls.nome.invalid) {
            <mat-error>Informe o nome.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>E-mail (usado no login)</mat-label>
          <input matInput type="email" formControlName="email" autocomplete="off" />
          @if (form.controls.email.invalid) {
            <mat-error>Informe um e-mail válido.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Papel</mat-label>
          <mat-select formControlName="papel">
            @for (p of papeis; track p) {
              <mat-option [value]="p">{{ rotulo[p] }} · {{ descricao[p] }}</mat-option>
            }
          </mat-select>
          @if (usuario?.ehVoce) {
            <mat-hint>Você não pode mudar o seu próprio papel.</mat-hint>
          }
        </mat-form-field>
        @if (!usuario) {
          <mat-form-field appearance="outline">
            <mat-label>Senha inicial</mat-label>
            <input matInput type="text" formControlName="senha" autocomplete="new-password" />
            <mat-hint>Pelo menos 8 caracteres, com letra e número. A pessoa pode trocar depois.</mat-hint>
            @if (form.controls.senha.invalid) {
              <mat-error>Pelo menos 8 caracteres, com letra e número.</mat-error>
            }
          </mat-form-field>
        } @else if (!usuario.ehVoce) {
          <mat-slide-toggle formControlName="ativo">Pode entrar no painel</mat-slide-toggle>
        }
        @if (erro()) {
          <p class="erro">{{ erro() }}</p>
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
      gap: 4px;
      padding-top: 8px !important;
    }
    mat-slide-toggle {
      margin: 8px 0;
    }
    .erro {
      margin: 0;
      color: var(--mat-sys-error);
    }
  `,
})
export class UsuarioDialog {
  private readonly http = inject(HttpClient);
  private readonly ref = inject<MatDialogRef<UsuarioDialog, UsuarioEquipe>>(MatDialogRef);
  protected readonly usuario = inject<UsuarioEquipe | null>(MAT_DIALOG_DATA);

  protected readonly papeis: Papel[] = ['Dono', 'Vendedor', 'Entregador'];
  protected readonly rotulo = ROTULO_PAPEL;
  protected readonly descricao = DESCRICAO_PAPEL;
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    nome: [this.usuario?.nome ?? '', [Validators.required, Validators.maxLength(120)]],
    email: [this.usuario?.email ?? '', [Validators.required, Validators.email]],
    papel: [{ value: this.usuario?.papel ?? ('Vendedor' as Papel), disabled: !!this.usuario?.ehVoce }],
    senha: ['', this.usuario ? [] : [Validators.required, senhaForte]],
    ativo: [this.usuario?.ativo ?? true],
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
      const salvo = this.usuario
        ? await firstValueFrom(
            this.http.put<UsuarioEquipe>(`${API}/usuarios/${this.usuario.id}`, {
              nome: v.nome,
              email: v.email,
              papel: v.papel,
              ativo: v.ativo,
            }),
          )
        : await firstValueFrom(
            this.http.post<UsuarioEquipe>(`${API}/usuarios`, { nome: v.nome, email: v.email, papel: v.papel, senha: v.senha }),
          );
      this.ref.close(salvo);
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
      this.salvando.set(false);
    }
  }
}
