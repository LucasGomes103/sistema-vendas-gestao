import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom } from 'rxjs';
import { API, PedidoAcao, Reserva, SugestaoPedido, TipoEntrega, mensagemDeErro } from '../../core/api';

/** Transforma a reserva de 24 h em pedido: entrega ou retirada, frete e, se quiser, já manda o Pix ao cliente. */
@Component({
  selector: 'app-pedido-dialog',
  imports: [CurrencyPipe, ReactiveFormsModule, MatDialogModule, MatButtonModule, MatButtonToggleModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Gerar pedido</h2>
    <mat-dialog-content>
      @if (sugestao.isLoading()) {
        <mat-progress-bar mode="indeterminate" />
      }
      <ul class="itens">
        @for (i of reserva.itens; track i.especie) {
          <li>
            <span>{{ i.quantidade }}x {{ i.especie }}</span>
            <span>{{ i.quantidade * i.precoUnitario | currency: 'BRL' }}</span>
          </li>
        }
      </ul>

      <form [formGroup]="form" class="campos">
        <mat-button-toggle-group formControlName="tipoEntrega" aria-label="Entrega ou retirada" class="tipo">
          <mat-button-toggle value="Entrega"><mat-icon>local_shipping</mat-icon> Entrega</mat-button-toggle>
          <mat-button-toggle value="Retirada"><mat-icon>storefront</mat-icon> Retirada na loja</mat-button-toggle>
        </mat-button-toggle-group>

        @if (entrega()) {
          <div class="linha">
            <mat-form-field appearance="outline" class="cep">
              <mat-label>CEP</mat-label>
              <input matInput formControlName="cep" inputmode="numeric" placeholder="07010-000" />
            </mat-form-field>
            <mat-form-field appearance="outline" class="frete">
              <mat-label>Frete</mat-label>
              <span matTextPrefix>R$&nbsp;</span>
              <input matInput type="number" min="0" step="0.5" formControlName="frete" />
            </mat-form-field>
          </div>
          @if (sugestao.value()?.freteResumo; as resumo) {
            <p class="dica"><mat-icon>calculate</mat-icon> {{ resumo }}</p>
          }
          <mat-form-field appearance="outline">
            <mat-label>Endereço de entrega</mat-label>
            <textarea matInput rows="2" formControlName="endereco" placeholder="Rua, número, complemento, bairro"></textarea>
            @if (form.controls.endereco.invalid && form.controls.endereco.touched) {
              <mat-error>Informe o endereço.</mat-error>
            }
          </mat-form-field>
        }

        <mat-form-field appearance="outline">
          <mat-label>Observação (opcional)</mat-label>
          <input matInput formControlName="observacao" />
        </mat-form-field>

        @let s = sugestao.value();
        @if (s && !s.pixConfigurado && !s.cartaoConfigurado) {
          <p class="dica alerta"><mat-icon>info</mat-icon> Sem chave Pix configurada: o pedido é criado, mas sem código para o cliente. Configure em Configuração → Pix da loja.</p>
        } @else {
          <mat-checkbox formControlName="enviarPix">
            Enviar o pedido para o cliente agora com
            {{ s?.pixConfigurado === false ? 'o link do cartão' : s?.cartaoConfigurado ? 'o Pix copia e cola e o link do cartão' : 'o Pix copia e cola' }}
          </mat-checkbox>
        }
      </form>

      @if (erro()) {
        <p class="erro">{{ erro() }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions>
      <span class="total">Total {{ total() | currency: 'BRL' }}</span>
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button [disabled]="salvando()" (click)="criar()"><mat-icon>receipt_long</mat-icon> Criar pedido</button>
    </mat-dialog-actions>
  `,
  styles: `
    .itens {
      display: grid;
      gap: 4px;
      margin: 0 0 16px;
      padding: 10px 14px;
      border-radius: 12px;
      background: var(--mat-sys-surface-container);
      list-style: none;

      li {
        display: flex;
        justify-content: space-between;
        gap: 12px;
      }
    }
    .campos {
      display: grid;
      gap: 4px;
    }
    .tipo {
      margin-bottom: 16px;
      justify-self: start;
    }
    .linha {
      display: flex;
      gap: 12px;

      .cep {
        flex: 1;
      }
      .frete {
        width: 140px;
      }
    }
    .dica {
      display: flex;
      align-items: center;
      gap: 6px;
      margin: -8px 0 12px;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);

      mat-icon {
        flex: none;
        width: 18px;
        height: 18px;
        font-size: 18px;
      }
      &.alerta {
        margin: 4px 0 0;
        padding: 8px 12px;
        border-radius: 10px;
        color: var(--sv-alerta);
        background: var(--sv-alerta-fundo);
      }
    }
    .erro {
      color: var(--mat-sys-error);
    }
    mat-dialog-actions .total {
      margin-right: auto;
      padding-left: 8px;
      font: var(--mat-sys-title-small);
    }
  `,
})
export class PedidoDialog {
  protected readonly reserva = inject<Reserva>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<PedidoDialog, PedidoAcao>);
  private readonly http = inject(HttpClient);

  protected readonly sugestao = httpResource<SugestaoPedido>(() => `${API}/pedidos/sugestao?reservaId=${this.reserva.id}`);
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    tipoEntrega: ['Entrega' as TipoEntrega],
    cep: [''],
    frete: [0, [Validators.min(0)]],
    endereco: [''],
    observacao: [''],
    enviarPix: [true],
  });

  private readonly valores = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });
  protected readonly entrega = computed(() => this.valores().tipoEntrega === 'Entrega');
  protected readonly total = computed(() => this.reserva.total + (this.entrega() ? Number(this.valores().frete) || 0 : 0));

  constructor() {
    effect(() => {
      const s = this.sugestao.value();
      if (!s) return;
      // Só preenche o que o vendedor ainda não mexeu.
      const c = this.form.controls;
      if (!c.cep.dirty && s.cep) c.cep.setValue(s.cep.replace(/^(\d{5})(\d{3})$/, '$1-$2'));
      if (!c.endereco.dirty && s.endereco) c.endereco.setValue(s.endereco);
      if (!c.frete.dirty && s.frete != null) c.frete.setValue(s.frete);
      if (!s.pixConfigurado && !s.cartaoConfigurado) c.enviarPix.setValue(false);
    });
  }

  protected async criar(): Promise<void> {
    const v = this.form.getRawValue();
    if (v.tipoEntrega === 'Entrega' && !v.endereco.trim()) {
      this.form.controls.endereco.markAsTouched();
      this.form.controls.endereco.setErrors({ required: true });
      return;
    }
    this.salvando.set(true);
    this.erro.set(null);
    try {
      const r = await firstValueFrom(
        this.http.post<PedidoAcao>(`${API}/pedidos`, {
          reservaId: this.reserva.id,
          tipoEntrega: v.tipoEntrega,
          frete: Number(v.frete) || 0,
          cep: v.cep || null,
          endereco: v.endereco || null,
          observacao: v.observacao || null,
          enviarPix: v.enviarPix,
        }),
      );
      this.ref.close(r);
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
    } finally {
      this.salvando.set(false);
    }
  }
}
