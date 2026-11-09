import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, FreteConsulta, FreteKm, mensagemDeErro } from '../../core/api';

const CEP = /^\d{5}-?\d{3}$/;

/**
 * Frete por km a partir da loja. O robô localiza o CEP do cliente, mede a distância de carro e
 * responde o valor. Além do raio máximo, não dá preço: chama o atendente.
 */
@Component({
  selector: 'app-config-frete',
  imports: [CurrencyPipe, DecimalPipe, FormsModule, ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSlideToggleModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="sv-card bloco">
      <div class="titulo">
        <h2><mat-icon>local_shipping</mat-icon> Frete por km</h2>
        <mat-slide-toggle [formControl]="form.controls.ativo">{{ form.controls.ativo.value ? 'Ligado' : 'Desligado' }}</mat-slide-toggle>
      </div>
      <p class="ajuda">
        Quando o cliente manda o CEP, o robô mede a distância de carro desde a loja e responde o valor.
        Acima do raio máximo, ou se o mapa não achar o CEP, ele não dá preço e chama um atendente.
      </p>

      <form [formGroup]="form" (ngSubmit)="salvar()" class="grade">
        <mat-form-field appearance="outline" class="inteiro">
          <mat-label>CEP de onde sai a entrega</mat-label>
          <input matInput formControlName="cepOrigem" inputmode="numeric" placeholder="07010-000" />
          @if (form.controls.cepOrigem.invalid) {
            <mat-error>CEP com 8 dígitos.</mat-error>
          } @else {
            <mat-hint>{{ config.value()?.origemEndereco ?? 'Normalmente o CEP da loja.' }}</mat-hint>
          }
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Taxa base</mat-label>
          <span matTextPrefix>R$&nbsp;</span>
          <input matInput type="number" min="0" step="0.5" formControlName="valorBase" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>A taxa base cobre até</mat-label>
          <input matInput type="number" min="0" step="0.5" formControlName="kmInclusos" />
          <span matTextSuffix>km</span>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Depois disso, por km</mat-label>
          <span matTextPrefix>R$&nbsp;</span>
          <input matInput type="number" min="0" step="0.1" formControlName="valorPorKm" />
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Atendo até</mat-label>
          <input matInput type="number" min="1" max="300" step="1" formControlName="kmMaximo" />
          <span matTextSuffix>km</span>
          <mat-hint>Acima disso, um atendente combina</mat-hint>
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Prazo</mat-label>
          <input matInput type="number" min="0" max="60" formControlName="dias" />
          <span matTextSuffix>dias úteis</span>
          <mat-hint>0 = no mesmo dia</mat-hint>
        </mat-form-field>

        <p class="exemplo inteiro">
          <mat-icon>calculate</mat-icon>
          <span>
            Ex.: a {{ exemplo().km | number: '1.0-1' }} km sai {{ exemplo().valor | currency: 'BRL' }}
            ({{ form.controls.valorBase.value | currency: 'BRL' }} + {{ exemplo().excedente | number: '1.0-1' }} km ×
            {{ form.controls.valorPorKm.value | currency: 'BRL' }}).
          </span>
        </p>

        <div class="acoes inteiro">
          <button mat-flat-button type="submit" [disabled]="salvando()">
            <mat-icon>save</mat-icon> {{ salvando() ? 'Localizando a loja…' : 'Salvar' }}
          </button>
        </div>
      </form>

      <div class="teste">
        <mat-form-field appearance="outline">
          <mat-label>Testar um CEP</mat-label>
          <input matInput inputmode="numeric" placeholder="07010-000" [ngModel]="cepTeste()" (ngModelChange)="cepTeste.set($event)" (keydown.enter)="$event.preventDefault(); testar()" />
        </mat-form-field>
        <button mat-stroked-button type="button" [disabled]="testando()" (click)="testar()">{{ testando() ? 'Calculando…' : 'Calcular' }}</button>
        @if (resultado(); as r) {
          <span class="resultado" [class.fora]="!r.atende">
            <mat-icon>{{ r.atende ? 'check_circle' : 'support_agent' }}</mat-icon>
            @if (r.atende) {
              {{ r.regiao }}: {{ r.valor | currency: 'BRL' }}, {{ prazo(r.dias ?? 0) }}
            } @else {
              {{ r.regiao }}: além do raio, o atendente combina
            }
          </span>
          <p class="detalhe">
            Achei {{ r.endereco ?? 'o mesmo CEP da loja' }}{{ r.fonte ? ' (' + fonte(r.fonte) + ')' : '' }}.
            {{ r.porRota ? 'Distância pela rota de carro.' : r.km > 0 ? 'Rota de carro indisponível: linha reta × 1,3.' : '' }}
          </p>
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
      gap: 4px 12px;
    }
    .inteiro {
      grid-column: 1 / -1;
    }
    .exemplo {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 4px 0 8px;
      padding: 8px 12px;
      border-radius: 10px;
      background: var(--mat-sys-surface-container);
      font: var(--mat-sys-body-small);

      mat-icon {
        flex: none;
        color: var(--mat-sys-primary);
      }
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
    .detalhe {
      flex-basis: 100%;
      margin: -4px 0 8px;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
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
    }
  `,
})
export class ConfigFreteComponent {
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);

  protected readonly config = httpResource<FreteKm>(() => `${API}/config/frete`);
  protected readonly salvando = signal(false);
  protected readonly testando = signal(false);
  protected readonly cepTeste = signal('');
  protected readonly resultado = signal<FreteConsulta | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    ativo: [true],
    cepOrigem: ['', [Validators.required, Validators.pattern(CEP)]],
    valorBase: [10, [Validators.required, Validators.min(0)]],
    kmInclusos: [2, [Validators.required, Validators.min(0)]],
    valorPorKm: [2, [Validators.required, Validators.min(0)]],
    kmMaximo: [15, [Validators.required, Validators.min(1), Validators.max(300)]],
    dias: [1, [Validators.required, Validators.min(0), Validators.max(60)]],
  });

  private readonly valores = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  /** Conta de exemplo na metade do raio, para o dono ver o efeito dos números. */
  protected readonly exemplo = computed(() => {
    this.valores();
    const v = this.form.getRawValue();
    const km = Math.max(Number(v.kmInclusos) + 1, Math.round(Number(v.kmMaximo) / 2));
    const excedente = Math.max(0, km - Number(v.kmInclusos));
    return { km, excedente, valor: Number(v.valorBase) + excedente * Number(v.valorPorKm) };
  });

  constructor() {
    effect(() => {
      const c = this.config.value();
      if (!c) return;
      this.form.setValue({
        ativo: c.configurado ? c.ativo : true,
        cepOrigem: c.cepOrigem ?? '',
        valorBase: c.valorBase,
        kmInclusos: c.kmInclusos,
        valorPorKm: c.valorPorKm,
        kmMaximo: c.configurado ? c.kmMaximo : 15,
        dias: c.dias,
      });
    });
  }

  protected fonte(f: string): string {
    return f === 'osm-rua' ? 'pela rua' : f === 'osm-bairro' ? 'pelo bairro, aproximado' : 'pela BrasilAPI';
  }

  protected prazo(dias: number): string {
    return dias === 0 ? 'no mesmo dia' : dias === 1 ? '1 dia útil' : `${dias} dias úteis`;
  }

  protected async salvar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.salvando.set(true);
    const v = this.form.getRawValue();
    try {
      await firstValueFrom(
        this.http.put(`${API}/config/frete`, {
          ...v,
          valorBase: Number(v.valorBase),
          kmInclusos: Number(v.kmInclusos),
          valorPorKm: Number(v.valorPorKm),
          kmMaximo: Number(v.kmMaximo),
          dias: Number(v.dias),
        }),
      );
      this.snack.open(v.ativo ? 'Frete por km salvo e ligado.' : 'Frete salvo (desligado).', undefined, { duration: 3000 });
      this.config.reload();
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.salvando.set(false);
    }
  }

  protected async testar(): Promise<void> {
    this.testando.set(true);
    try {
      this.resultado.set(await firstValueFrom(this.http.get<FreteConsulta>(`${API}/frete`, { params: { cep: this.cepTeste() } })));
    } catch (e) {
      this.resultado.set(null);
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 5000 });
    } finally {
      this.testando.set(false);
    }
  }
}
