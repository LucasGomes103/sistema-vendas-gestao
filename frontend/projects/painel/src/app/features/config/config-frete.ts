import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, FreteConsulta, ZonaEntrega, ZonaEntregaEntrada, mensagemDeErro } from '../../core/api';

const CEP = /^\d{5}-?\d{3}$/;

/** Tabela de frete por faixa de CEP. O robô usa para responder valor e prazo quando o cliente manda o CEP. */
@Component({
  selector: 'app-config-frete',
  imports: [CurrencyPipe, FormsModule, ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="sv-card bloco">
      <h2><mat-icon>local_shipping</mat-icon> Frete por CEP</h2>
      <p class="ajuda">
        Quando o cliente manda o CEP, o robô responde o valor e o prazo da faixa e chama o vendedor para fechar.
        Se as faixas se sobrepõem, vale a mais estreita. Sem nenhuma faixa, o robô usa a resposta pronta de frete.
      </p>

      @if (zonas.value()?.length) {
        <ul class="zonas">
          @for (z of zonas.value(); track z.id) {
            <li [class.editando]="editando() === z.id">
              <div class="dados">
                <strong>{{ z.nome }}</strong>
                <span>{{ z.cepInicio }} a {{ z.cepFim }}</span>
              </div>
              <span class="valor">{{ z.valor | currency: 'BRL' }} · {{ prazo(z.dias) }}</span>
              <button mat-icon-button type="button" matTooltip="Editar" aria-label="Editar faixa" (click)="editar(z)"><mat-icon>edit</mat-icon></button>
              <button mat-icon-button type="button" matTooltip="Excluir" aria-label="Excluir faixa" (click)="excluir(z)"><mat-icon>delete</mat-icon></button>
            </li>
          }
        </ul>
      } @else if (zonas.hasValue()) {
        <p class="vazio">Nenhuma faixa cadastrada ainda.</p>
      }

      <form [formGroup]="form" (ngSubmit)="salvar()" class="grade">
        <mat-form-field appearance="outline" class="inteiro">
          <mat-label>Região</mat-label>
          <input matInput formControlName="nome" placeholder="Ex.: Guarulhos" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>CEP inicial</mat-label>
          <input matInput formControlName="cepInicio" inputmode="numeric" placeholder="07000-000" />
          @if (form.controls.cepInicio.invalid) {
            <mat-error>CEP com 8 dígitos.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>CEP final</mat-label>
          <input matInput formControlName="cepFim" inputmode="numeric" placeholder="07399-999" />
          @if (form.controls.cepFim.invalid) {
            <mat-error>CEP com 8 dígitos.</mat-error>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Valor do frete</mat-label>
          <span matTextPrefix>R$&nbsp;</span>
          <input matInput type="number" min="0" step="0.5" formControlName="valor" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Prazo</mat-label>
          <input matInput type="number" min="0" max="60" formControlName="dias" />
          <span matTextSuffix>dias úteis</span>
          <mat-hint>0 = no mesmo dia</mat-hint>
        </mat-form-field>
        <div class="acoes inteiro">
          @if (editando()) {
            <button mat-button type="button" (click)="limpar()">Cancelar edição</button>
          }
          <button mat-flat-button type="submit" [disabled]="salvando()">
            <mat-icon>{{ editando() ? 'save' : 'add' }}</mat-icon> {{ editando() ? 'Salvar faixa' : 'Adicionar faixa' }}
          </button>
        </div>
      </form>

      <div class="teste">
        <mat-form-field appearance="outline">
          <mat-label>Testar um CEP</mat-label>
          <input matInput inputmode="numeric" placeholder="07010-000" [ngModel]="cepTeste()" (ngModelChange)="cepTeste.set($event)" (keydown.enter)="$event.preventDefault(); testar()" />
        </mat-form-field>
        <button mat-stroked-button type="button" (click)="testar()">Calcular</button>
        @if (resultado(); as r) {
          <span class="resultado" [class.fora]="!r.atende">
            <mat-icon>{{ r.atende ? 'check_circle' : 'block' }}</mat-icon>
            @if (r.atende) {
              {{ r.regiao }}: {{ r.valor | currency: 'BRL' }}, {{ prazo(r.dias ?? 0) }}
            } @else {
              Fora da área de entrega
            }
          </span>
        }
      </div>
    </section>
  `,
  styles: `
    .bloco {
      display: grid;
      gap: 4px;
      margin-top: 16px;
      padding: 16px 20px 8px;

      h2 {
        display: flex;
        align-items: center;
        gap: 8px;
        margin-bottom: 8px;
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
    .zonas {
      margin: 0 0 16px;
      padding: 0;
      list-style: none;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 12px;

      li {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 6px 8px 6px 14px;

        & + li {
          border-top: 1px solid var(--mat-sys-outline-variant);
        }
        &.editando {
          background: var(--mat-sys-secondary-container);
        }
      }
      .dados {
        display: grid;
        flex: 1;
        min-width: 0;

        span {
          color: var(--mat-sys-on-surface-variant);
          font: var(--mat-sys-body-small);
        }
      }
      .valor {
        font: var(--mat-sys-label-large);
        white-space: nowrap;
      }
    }
    .vazio {
      margin: 0 0 12px;
      color: var(--mat-sys-on-surface-variant);
    }
    .grade {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 4px 12px;
    }
    .inteiro {
      grid-column: 1 / -1;
    }
    .acoes {
      display: flex;
      justify-content: flex-end;
      gap: 8px;
      margin-bottom: 12px;
    }
    .teste {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: 8px 12px;
      padding-top: 12px;
      border-top: 1px solid var(--mat-sys-outline-variant);
    }
    .resultado {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      color: var(--sv-sucesso);

      &.fora {
        color: var(--sv-alerta);
      }
    }
    @media (max-width: 599px) {
      .grade {
        grid-template-columns: 1fr;
      }
      .zonas li {
        flex-wrap: wrap;
      }
    }
  `,
})
export class ConfigFreteComponent {
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);

  protected readonly zonas = httpResource<ZonaEntrega[]>(() => `${API}/config/frete`);
  protected readonly editando = signal<string | null>(null);
  protected readonly salvando = signal(false);
  protected readonly cepTeste = signal('');
  protected readonly resultado = signal<FreteConsulta | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    nome: ['', Validators.required],
    cepInicio: ['', [Validators.required, Validators.pattern(CEP)]],
    cepFim: ['', [Validators.required, Validators.pattern(CEP)]],
    valor: [0, [Validators.required, Validators.min(0)]],
    dias: [1, [Validators.required, Validators.min(0), Validators.max(60)]],
  });

  protected prazo(dias: number): string {
    return dias === 0 ? 'no mesmo dia' : dias === 1 ? '1 dia útil' : `${dias} dias úteis`;
  }

  protected editar(z: ZonaEntrega): void {
    this.editando.set(z.id);
    this.form.setValue({ nome: z.nome, cepInicio: z.cepInicio, cepFim: z.cepFim, valor: z.valor, dias: z.dias });
  }

  protected limpar(): void {
    this.editando.set(null);
    this.form.reset();
  }

  protected async salvar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.salvando.set(true);
    const v = this.form.getRawValue();
    const corpo: ZonaEntregaEntrada = { ...v, valor: Number(v.valor), dias: Number(v.dias) };
    try {
      const id = this.editando();
      if (id) await firstValueFrom(this.http.put(`${API}/config/frete/${id}`, corpo));
      else await firstValueFrom(this.http.post(`${API}/config/frete`, corpo));
      this.snack.open(id ? 'Faixa atualizada.' : 'Faixa adicionada.', undefined, { duration: 3000 });
      this.limpar();
      this.zonas.reload();
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.salvando.set(false);
    }
  }

  protected async excluir(z: ZonaEntrega): Promise<void> {
    if (!confirm(`Excluir a faixa "${z.nome}"?`)) return;
    try {
      await firstValueFrom(this.http.delete(`${API}/config/frete/${z.id}`));
      if (this.editando() === z.id) this.limpar();
      this.zonas.reload();
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    }
  }

  protected async testar(): Promise<void> {
    try {
      this.resultado.set(await firstValueFrom(this.http.get<FreteConsulta>(`${API}/frete`, { params: { cep: this.cepTeste() } })));
    } catch (e) {
      this.resultado.set(null);
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 4000 });
    }
  }
}
