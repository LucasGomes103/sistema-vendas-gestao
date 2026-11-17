import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { API, CaixaDia, LancamentoCaixa, ROTULO_FORMA, TipoLancamento, mensagemDeErro } from '../../core/api';
import { Confirmar } from '../../core/confirmar';
import { DadosLancamento, LancamentoDialog } from './lancamento-dialog';

/** Conferência do dinheiro na gaveta antes de fechar o dia. */
@Component({
  selector: 'app-fechar-caixa-dialog',
  imports: [CurrencyPipe, FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Fechar o caixa</h2>
    <mat-dialog-content>
      <p>Saldo do dia: <strong>{{ dados.resumo.saldo | currency: 'BRL' }}</strong></p>
      <p>Dinheiro que deveria estar na gaveta: <strong>{{ dados.resumo.dinheiroEsperado | currency: 'BRL' }}</strong></p>
      <mat-form-field appearance="outline">
        <mat-label>Dinheiro contado (opcional)</mat-label>
        <span matTextPrefix>R$&nbsp;</span>
        <input matInput type="number" min="0" step="0.01" [(ngModel)]="contado" />
      </mat-form-field>
      <mat-form-field appearance="outline">
        <mat-label>Observação (opcional)</mat-label>
        <input matInput [(ngModel)]="observacao" />
      </mat-form-field>
      <p class="ajuda">Depois de fechado, o dia não aceita lançamento manual (vendas pagas continuam entrando). Dá para reabrir.</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button [mat-dialog-close]="{ dinheiroContado: contado === null || contado === '' ? null : +contado, observacao: observacao || null }">Fechar o dia</button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content {
      display: grid;
    }
    p {
      margin: 0 0 12px;
    }
    .ajuda {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
  `,
})
export class FecharCaixaDialog {
  protected readonly dados = inject<CaixaDia>(MAT_DIALOG_DATA);
  protected contado: number | string | null = null;
  protected observacao = '';
}

/** Caixa do dia: vendas pagas entram sozinhas; despesas e vendas no balcão o dono lança; no fim do dia, fecha. */
@Component({
  selector: 'app-caixa-page',
  imports: [CurrencyPipe, DatePipe, RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="sv-cabecalho-pagina">
      <div>
        <h1>Caixa</h1>
        <p>Vendas pagas entram sozinhas. Lance aqui as despesas e o que foi vendido fora dos pedidos.</p>
      </div>
      @if (caixa.value(); as c) {
        @if (!c.fechamento) {
          <span class="botoes">
            <button mat-stroked-button (click)="lancar('Saida')"><mat-icon>remove</mat-icon> Despesa</button>
            <button mat-stroked-button (click)="lancar('Entrada')"><mat-icon>add</mat-icon> Entrada</button>
          </span>
        }
      }
    </header>

    <nav class="dia">
      <button mat-icon-button aria-label="Dia anterior" (click)="mudarDia(-1)"><mat-icon>chevron_left</mat-icon></button>
      <strong class="data">{{ dataDoDia() | date: "EEEE, dd 'de' MMMM" }}</strong>
      <button mat-icon-button aria-label="Dia seguinte" [disabled]="ehHoje()" (click)="mudarDia(1)"><mat-icon>chevron_right</mat-icon></button>
      @if (!ehHoje()) {
        <button mat-button (click)="data.set(null)">Hoje</button>
      }
    </nav>

    @if (caixa.isLoading() && !caixa.hasValue()) {
      <mat-progress-bar mode="indeterminate" />
    } @else if (caixa.error()) {
      <div class="sv-vazio">
        <mat-icon>cloud_off</mat-icon>
        <p>Não foi possível carregar o caixa.</p>
        <button mat-stroked-button (click)="caixa.reload()">Tentar de novo</button>
      </div>
    } @else if (caixa.value(); as c) {
      <div class="resumo">
        <div class="sv-card numero">
          <span>Entradas</span>
          <strong class="entrada">{{ c.resumo.entradas | currency: 'BRL' }}</strong>
        </div>
        <div class="sv-card numero">
          <span>Saídas</span>
          <strong class="saida">{{ c.resumo.saidas | currency: 'BRL' }}</strong>
        </div>
        <div class="sv-card numero">
          <span>Saldo do dia</span>
          <strong>{{ c.resumo.saldo | currency: 'BRL' }}</strong>
        </div>
        <div class="sv-card numero">
          <span>Dinheiro na gaveta</span>
          <strong>{{ c.resumo.dinheiroEsperado | currency: 'BRL' }}</strong>
        </div>
      </div>

      <p class="formas">
        @for (f of c.resumo.porForma; track f.forma) {
          <span class="sv-selo info">{{ forma[f.forma] }}: {{ f.entradas - f.saidas | currency: 'BRL' }}</span>
        }
      </p>

      @if (c.fechamento; as f) {
        <section class="sv-card fechado">
          <mat-icon>lock</mat-icon>
          <div>
            <strong>Caixa fechado {{ f.fechadoEm | date: "dd/MM 'às' HH:mm" }}{{ f.fechadoPor ? ' por ' + f.fechadoPor : '' }}</strong>
            <span>
              Saldo no fechamento {{ f.saldo | currency: 'BRL' }}
              @if (f.dinheiroContado !== null) {
                · gaveta {{ f.dinheiroContado | currency: 'BRL' }}
                @if (f.diferenca) {
                  <em [class.falta]="f.diferenca < 0">({{ f.diferenca > 0 ? 'sobra' : 'falta' }} de {{ abs(f.diferenca) | currency: 'BRL' }})</em>
                } @else {
                  (confere)
                }
              }
              @if (f.saldo !== c.resumo.saldo) {
                · entrou venda depois do fechamento
              }
            </span>
            @if (f.observacao) {
              <span>{{ f.observacao }}</span>
            }
          </div>
          <button mat-stroked-button (click)="reabrir()">Reabrir</button>
        </section>
      }

      <ul class="sv-card lista">
        @for (l of c.lancamentos; track l.id) {
          <li>
            <mat-icon [class.entrada]="l.tipo === 'Entrada'" [class.saida]="l.tipo === 'Saida'">{{ l.tipo === 'Entrada' ? 'south_west' : 'north_east' }}</mat-icon>
            <div class="dados">
              <strong>{{ l.categoria }}</strong>
              <span>
                {{ l.criadoEm | date: 'HH:mm' }} · {{ forma[l.forma] }}{{ l.descricao ? ' · ' + l.descricao : '' }}{{ l.usuario ? ' · ' + l.usuario : '' }}
              </span>
            </div>
            <span class="valor" [class.saida]="l.tipo === 'Saida'">{{ l.tipo === 'Saida' ? '−' : '' }}{{ l.valor | currency: 'BRL' }}</span>
            @if (l.pedidoId) {
              <a mat-icon-button routerLink="/pedidos" matTooltip="Ver pedidos" aria-label="Ver pedidos"><mat-icon>receipt_long</mat-icon></a>
            } @else if (!c.fechamento) {
              <button mat-icon-button matTooltip="Apagar" aria-label="Apagar lançamento" (click)="apagar(l)"><mat-icon>delete</mat-icon></button>
            }
          </li>
        } @empty {
          <li class="vazio">Nada lançado neste dia.</li>
        }
      </ul>

      @if (!c.fechamento) {
        <div class="fechar">
          <button mat-flat-button (click)="fechar(c)"><mat-icon>lock</mat-icon> Fechar o caixa do dia</button>
        </div>
      }
    }
  `,
  styles: `
    .botoes {
      display: flex;
      gap: 8px;
    }
    .dia {
      display: flex;
      align-items: center;
      gap: 4px;
      margin-bottom: 12px;

      strong::first-letter {
        text-transform: uppercase;
      }
    }
    .resumo {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 12px;
      max-width: 820px;
      margin-bottom: 12px;
    }
    .numero {
      display: grid;
      gap: 2px;
      padding: 12px 16px;

      span {
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-label-medium);
      }
      strong {
        font: var(--mat-sys-title-large);
      }
    }
    .entrada {
      color: var(--sv-sucesso);
    }
    .saida {
      color: var(--mat-sys-error);
    }
    .formas {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      margin: 0 0 16px;
    }
    .fechado {
      display: flex;
      align-items: center;
      gap: 12px;
      max-width: 820px;
      margin-bottom: 12px;
      padding: 12px 16px;

      > mat-icon {
        color: var(--mat-sys-primary);
      }
      div {
        display: grid;
        flex: 1;
        gap: 2px;

        span {
          color: var(--mat-sys-on-surface-variant);
          font: var(--mat-sys-body-small);
        }
        em.falta {
          color: var(--mat-sys-error);
        }
      }
    }
    .lista {
      max-width: 820px;
      margin: 0;
      padding: 0;
      list-style: none;

      li {
        display: flex;
        align-items: center;
        gap: 12px;
        padding: 10px 16px;

        & + li {
          border-top: 1px solid var(--mat-sys-outline-variant);
        }
        &.vazio {
          justify-content: center;
          padding: 24px;
          color: var(--mat-sys-on-surface-variant);
        }
      }
      .dados {
        display: grid;
        flex: 1;
        min-width: 0;

        span {
          overflow: hidden;
          color: var(--mat-sys-on-surface-variant);
          font: var(--mat-sys-body-small);
          text-overflow: ellipsis;
          white-space: nowrap;
        }
      }
      .valor {
        font: var(--mat-sys-title-small);
        white-space: nowrap;
      }
    }
    .fechar {
      display: flex;
      justify-content: flex-end;
      max-width: 820px;
      margin-top: 16px;
    }
    @media (max-width: 599px) {
      .resumo {
        grid-template-columns: 1fr 1fr;
      }
    }
  `,
})
export class CaixaPage {
  private readonly confirmar = inject(Confirmar);
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);

  protected readonly forma = ROTULO_FORMA;
  /** null = hoje (no fuso da loja, quem decide é a API). */
  protected readonly data = signal<string | null>(null);
  protected readonly caixa = httpResource<CaixaDia>(() => `${API}/caixa${this.data() ? `?data=${this.data()}` : ''}`);

  protected readonly dataDoDia = computed(() => {
    const d = this.caixa.value()?.data ?? this.data();
    return d ? new Date(`${d}T12:00:00`) : null;
  });
  protected readonly ehHoje = computed(() => {
    const c = this.caixa.value();
    return !c || c.data === c.hoje;
  });

  protected abs = Math.abs;

  protected mudarDia(delta: number): void {
    const c = this.caixa.value();
    if (!c) return;
    const d = new Date(`${c.data}T12:00:00`);
    d.setDate(d.getDate() + delta);
    const iso = d.toISOString().slice(0, 10);
    this.data.set(iso >= c.hoje ? null : iso);
  }

  protected async lancar(tipo: TipoLancamento): Promise<void> {
    const c = this.caixa.value();
    if (!c) return;
    const ref = this.dialog.open<LancamentoDialog, DadosLancamento, boolean>(LancamentoDialog, {
      data: { tipo, data: c.data, categorias: tipo === 'Entrada' ? c.categoriasEntrada.filter((x) => x !== 'Venda') : c.categoriasSaida },
      width: '440px',
      maxWidth: '95vw',
    });
    if (await firstValueFrom(ref.afterClosed())) this.caixa.reload();
  }

  protected async apagar(l: LancamentoCaixa): Promise<void> {
    if (!await this.confirmar.perguntar(`Apagar "${l.categoria}" de ${l.valor.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}?`, { botao: 'Apagar', perigo: true })) return;
    await this.executar(this.http.delete(`${API}/caixa/lancamentos/${l.id}`), 'Lançamento apagado.');
  }

  protected async fechar(c: CaixaDia): Promise<void> {
    const r = await firstValueFrom(
      this.dialog.open<FecharCaixaDialog, CaixaDia, { dinheiroContado: number | null; observacao: string | null }>(FecharCaixaDialog, {
        data: c,
        width: '440px',
        maxWidth: '95vw',
      }).afterClosed(),
    );
    if (!r) return;
    await this.executar(this.http.post(`${API}/caixa/fechar`, { data: c.data, ...r }), 'Caixa do dia fechado.');
  }

  protected async reabrir(): Promise<void> {
    const c = this.caixa.value();
    if (!c || !await this.confirmar.perguntar('Reabrir o caixa deste dia?', { botao: 'Reabrir' })) return;
    await this.executar(this.http.delete(`${API}/caixa/fechamento?data=${c.data}`), 'Caixa reaberto.');
  }

  private async executar(req: ReturnType<HttpClient['delete']>, sucesso: string): Promise<void> {
    try {
      await firstValueFrom(req);
      this.snack.open(sucesso, undefined, { duration: 3000 });
      this.caixa.reload();
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    }
  }
}
