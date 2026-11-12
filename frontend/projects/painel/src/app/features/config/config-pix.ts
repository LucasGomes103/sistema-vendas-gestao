import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, ConfigPix, mensagemDeErro } from '../../core/api';

const TIPO: Record<string, string> = {
  Cpf: 'CPF',
  Cnpj: 'CNPJ',
  Telefone: 'celular',
  Email: 'e-mail',
  Aleatoria: 'chave aleatória',
};

/**
 * Pix com a chave comum da loja: sem provedor de pagamento e sem taxa. O sistema gera o "copia e cola"
 * de cada pedido com o valor certo; o vendedor confere o pagamento na conta e confirma no pedido.
 */
@Component({
  selector: 'app-config-pix',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="sv-card bloco">
      <div class="titulo">
        <h2><mat-icon>pix</mat-icon> Pix da loja</h2>
        @if (config.value()?.configurado) {
          <span class="sv-selo ok"><mat-icon>check_circle</mat-icon>Pronto para cobrar</span>
        } @else {
          <span class="sv-selo alerta"><mat-icon>info</mat-icon>Falta configurar</span>
        }
      </div>
      <p class="ajuda">
        Com a chave Pix comum (sem custo), cada pedido ganha um código "Pix copia e cola" com o valor e o número do pedido.
        O aviso de pagamento não é automático: confira na conta e toque em "Confirmar pagamento" no pedido.
      </p>

      <form [formGroup]="form" (ngSubmit)="salvar()" class="grade">
        <mat-form-field appearance="outline" class="inteiro">
          <mat-label>Chave Pix</mat-label>
          <input matInput formControlName="chave" placeholder="CPF, CNPJ, celular, e-mail ou chave aleatória" />
          <mat-hint>
            @if (config.value()?.tipoChave; as t) {
              Lida como {{ tipo[t] ?? t }}.
            }
            Celular: escreva com +55 para não ser confundido com CPF.
          </mat-hint>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Nome de quem recebe</mat-label>
          <input matInput formControlName="nome" maxlength="25" />
          <mat-hint>Como aparece no banco do cliente (até 25 letras)</mat-hint>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Cidade</mat-label>
          <input matInput formControlName="cidade" maxlength="15" />
        </mat-form-field>
        <div class="acoes inteiro">
          <button mat-flat-button type="submit" [disabled]="salvando()"><mat-icon>save</mat-icon> Salvar</button>
        </div>
      </form>
    </section>
  `,
  styles: `
    .bloco {
      display: grid;
      gap: 4px;
      margin-top: 16px;
      padding: 16px 20px 8px;

      .titulo {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 12px;
        margin-bottom: 8px;
      }
      h2 {
        display: flex;
        align-items: center;
        gap: 8px;
        margin: 0;
        font: var(--mat-sys-title-medium);
        font-weight: 600;

        mat-icon {
          color: var(--mat-sys-primary);
        }
      }
      .ajuda {
        margin: -4px 0 12px;
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-body-small);
      }
    }
    .grade {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 12px;
    }
    .inteiro {
      grid-column: 1 / -1;
    }
    .acoes {
      display: flex;
      justify-content: flex-end;
      margin-bottom: 8px;
    }
    @media (max-width: 599px) {
      .grade {
        grid-template-columns: 1fr;
      }
    }
  `,
})
export class ConfigPixComponent {
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);

  protected readonly tipo = TIPO;
  protected readonly config = httpResource<ConfigPix>(() => `${API}/config/pix`);
  protected readonly salvando = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    chave: ['', Validators.required],
    nome: ['', [Validators.required, Validators.maxLength(25)]],
    cidade: ['', [Validators.required, Validators.maxLength(15)]],
  });

  constructor() {
    effect(() => {
      const c = this.config.value();
      if (c) this.form.reset({ chave: c.chave ?? '', nome: c.nome ?? '', cidade: c.cidade ?? '' });
    });
  }

  protected async salvar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.salvando.set(true);
    try {
      const salvo = await firstValueFrom(this.http.put<ConfigPix>(`${API}/config/pix`, this.form.getRawValue()));
      this.config.set(salvo);
      this.snack.open('Pix salvo. Os próximos pedidos já saem com o código copia e cola.', undefined, { duration: 4000 });
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.salvando.set(false);
    }
  }
}
