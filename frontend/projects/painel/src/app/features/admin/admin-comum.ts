import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuditoriaAdmin, DiaPlataforma } from '../../core/api';

/** Loja ativa sem ninguém entrar no painel há mais que isso aparece como parada (igual ao servidor). */
export const DIAS_PARADA = 7;

/** "hoje", "ontem", "há 5 dias", "há 2 meses". */
export function tempoDesde(iso: string | null | undefined): string {
  if (!iso) return 'nunca';
  const dias = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000);
  if (dias <= 0) return 'hoje';
  if (dias === 1) return 'ontem';
  if (dias < 60) return `há ${dias} dias`;
  return `há ${Math.floor(dias / 30)} meses`;
}

export function parada(status: string, criadoEm: string, ultimoAcessoEm: string | null): boolean {
  const limite = Date.now() - DIAS_PARADA * 86_400_000;
  return status === 'Ativa' && new Date(criadoEm).getTime() < limite && (!ultimoAcessoEm || new Date(ultimoAcessoEm).getTime() < limite);
}

export const ACOES_AUDITORIA: Record<string, { rotulo: string; icone: string }> = {
  editar: { rotulo: 'Editou a loja', icone: 'edit' },
  suspender: { rotulo: 'Suspendeu', icone: 'block' },
  ativar: { rotulo: 'Ativou', icone: 'play_arrow' },
  excluir: { rotulo: 'Excluiu a loja', icone: 'delete_forever' },
  ver_como_loja: { rotulo: 'Entrou como suporte', icone: 'visibility' },
  sair_da_loja: { rotulo: 'Saiu do modo suporte', icone: 'visibility_off' },
  assinatura: { rotulo: 'Mudou a assinatura', icone: 'workspace_premium' },
};

/** Texto da situação da assinatura de uma loja, para o painel da plataforma. */
export const SITUACAO_ASSINATURA: Record<string, { rotulo: string; classe: string }> = {
  Isenta: { rotulo: 'Isenta', classe: 'info' },
  Cortesia: { rotulo: 'Cortesia', classe: 'ok' },
  Teste: { rotulo: 'Em teste', classe: 'info' },
  EmDia: { rotulo: 'Em dia', classe: 'ok' },
  Carencia: { rotulo: 'Pagamento pendente', classe: 'alerta' },
  Bloqueada: { rotulo: 'Bloqueada por atraso', classe: 'erro' },
};

export function rotuloAcao(a: AuditoriaAdmin): { rotulo: string; icone: string } {
  return ACOES_AUDITORIA[a.acao] ?? { rotulo: a.acao, icone: 'history' };
}

/** Abas do painel da plataforma. */
@Component({
  selector: 'app-admin-abas',
  imports: [RouterLink, RouterLinkActive, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="abas" aria-label="Painel da plataforma">
      <a routerLink="/admin" routerLinkActive="ativa" [routerLinkActiveOptions]="{ exact: true }"><mat-icon>dashboard</mat-icon>Visão geral</a>
      <a routerLink="/admin/lojas" routerLinkActive="ativa"><mat-icon>storefront</mat-icon>Lojas</a>
      <a routerLink="/admin/auditoria" routerLinkActive="ativa"><mat-icon>history</mat-icon>Auditoria</a>
    </nav>
  `,
  styles: `
    .abas {
      display: flex;
      gap: 4px;
      margin-bottom: 20px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
      overflow-x: auto;
    }
    a {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 10px 14px;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-title-small);
      text-decoration: none;
      white-space: nowrap;
      border-bottom: 3px solid transparent;

      mat-icon {
        font-size: 20px;
        width: 20px;
        height: 20px;
      }
    }
    a.ativa {
      color: var(--mat-sys-primary);
      border-bottom-color: var(--mat-sys-primary);
    }
  `,
})
export class AdminAbas {}

type Metrica = 'mensagensRecebidas' | 'respostasRobo' | 'pedidos' | 'vendido' | 'tokensIa' | 'lojasNovas';

const METRICAS: { id: Metrica; rotulo: string }[] = [
  { id: 'mensagensRecebidas', rotulo: 'Mensagens recebidas' },
  { id: 'respostasRobo', rotulo: 'Respostas do robô' },
  { id: 'pedidos', rotulo: 'Pedidos' },
  { id: 'vendido', rotulo: 'Vendido' },
  { id: 'tokensIa', rotulo: 'Tokens de IA' },
  { id: 'lojasNovas', rotulo: 'Lojas novas' },
];

/** Gráfico de barras por dia, uma métrica por vez (escolhida nos botões acima). */
@Component({
  selector: 'app-grafico-dias',
  imports: [CurrencyPipe, DatePipe, DecimalPipe, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="escolha" role="group" aria-label="O que mostrar no gráfico">
      @for (m of metricas(); track m.id) {
        <button type="button" [class.ativa]="m.id === metrica()" (click)="metrica.set(m.id)">{{ m.rotulo }}</button>
      }
    </div>
    @if (maior() > 0) {
      <div class="grafico" role="img" [attr.aria-label]="rotulo() + ' por dia'">
        <span class="escala">{{ ehDinheiro() ? (maior() | currency: 'BRL' : 'symbol' : '1.0-0') : (maior() | number: '1.0-0') }}</span>
        <div class="barras">
          @for (d of dias(); track d.dia; let i = $index) {
            <div class="coluna" [matTooltip]="(d.dia + 'T12:00:00' | date: 'EEE, dd/MM') + ': ' + (ehDinheiro() ? (valor(d) | currency: 'BRL') : (valor(d) | number: '1.0-0'))">
              <span class="barra" [style.height.%]="(valor(d) / maior()) * 100"></span>
              <span class="rotulo">{{ mostrarRotulo(i) ? (d.dia + 'T12:00:00' | date: 'dd/MM') : '' }}</span>
            </div>
          }
        </div>
      </div>
    } @else {
      <p class="vazio">Sem {{ rotulo().toLowerCase() }} no período.</p>
    }
  `,
  styles: `
    .escolha {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      margin-bottom: 12px;

      button {
        padding: 4px 12px;
        border: 1px solid var(--mat-sys-outline-variant);
        border-radius: 16px;
        color: var(--mat-sys-on-surface-variant);
        background: transparent;
        font: var(--mat-sys-label-large);
        cursor: pointer;
      }
      button.ativa {
        color: var(--mat-sys-on-secondary-container);
        background: var(--mat-sys-secondary-container);
        border-color: transparent;
      }
    }
    .grafico {
      display: grid;
      gap: 4px;
      padding-bottom: 20px;
    }
    .escala {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-label-small);
    }
    .barras {
      display: flex;
      align-items: stretch;
      gap: 2px;
      height: 180px;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
      background: repeating-linear-gradient(
        to bottom,
        transparent 0,
        transparent calc(25% - 1px),
        color-mix(in srgb, var(--mat-sys-outline-variant) 50%, transparent) calc(25% - 1px),
        color-mix(in srgb, var(--mat-sys-outline-variant) 50%, transparent) 25%
      );
    }
    .coluna {
      position: relative;
      display: flex;
      flex: 1;
      flex-direction: column;
      justify-content: flex-end;
      min-width: 0;
      cursor: default;

      .barra {
        display: block;
        width: 100%;
        max-width: 32px;
        margin: 0 auto;
        border-radius: 4px 4px 0 0;
        background: var(--mat-sys-primary);
      }
      &:hover .barra {
        background: color-mix(in srgb, var(--mat-sys-primary) 75%, black);
      }
      .rotulo {
        position: absolute;
        top: calc(100% + 4px);
        left: 50%;
        transform: translateX(-50%);
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-label-small);
        white-space: nowrap;
      }
    }
    .vazio {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class GraficoDias {
  readonly dias = input.required<DiaPlataforma[]>();
  /** Esconde "Lojas novas" (no detalhe de uma loja não faz sentido). */
  readonly semLojasNovas = input(false);

  protected readonly metrica = signal<Metrica>('mensagensRecebidas');
  protected readonly metricas = computed(() => METRICAS.filter((m) => !this.semLojasNovas() || m.id !== 'lojasNovas'));
  protected readonly rotulo = computed(() => METRICAS.find((m) => m.id === this.metrica())!.rotulo);
  protected readonly ehDinheiro = computed(() => this.metrica() === 'vendido');
  protected readonly maior = computed(() => Math.max(0, ...this.dias().map((d) => this.valor(d))));

  protected valor(d: DiaPlataforma): number {
    return d[this.metrica()];
  }

  /** Rótulos espaçados para não encavalar (no máximo uns 8 no eixo). */
  protected mostrarRotulo(i: number): boolean {
    const n = this.dias().length;
    const passo = Math.max(1, Math.ceil(n / 8));
    return (n - 1 - i) % passo === 0;
  }
}
