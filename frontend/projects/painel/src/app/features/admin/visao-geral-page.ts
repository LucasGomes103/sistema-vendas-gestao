import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { CurrencyPipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { API, LojaAdmin, VisaoGeralPlataforma } from '../../core/api';
import { AdminAbas, GraficoDias, parada, tempoDesde } from './admin-comum';

type Ranking = 'vendido30Dias' | 'conversas30Dias' | 'respostasRobo30Dias' | 'tokensIa30Dias';

/** Visão geral da plataforma: lojas, uso, vendas, IA e saúde das integrações. */
@Component({
  selector: 'app-visao-geral-page',
  imports: [CurrencyPipe, DecimalPipe, RouterLink, MatIconModule, MatProgressBarModule, AdminAbas, GraficoDias],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="sv-cabecalho-pagina">
      <div>
        <h1>Painel da plataforma</h1>
        <p>Como o LAG CHAT está sendo usado por todas as lojas.</p>
      </div>
      <div class="periodo" role="group" aria-label="Período">
        @for (d of [7, 30, 90]; track d) {
          <button type="button" [class.ativa]="dias() === d" (click)="dias.set(d)">{{ d }} dias</button>
        }
      </div>
    </header>
    <app-admin-abas />

    @if (visao.isLoading() && !visao.hasValue()) {
      <mat-progress-bar mode="indeterminate" />
    }
    @if (visao.error()) {
      <p class="vazio">Não foi possível carregar o painel. Tente de novo em instantes.</p>
    }

    @if (visao.value(); as v) {
      <section class="bloco">
        <h2>Lojas</h2>
        <div class="kpis">
          <div class="sv-card kpi"><span>Ativas</span><strong>{{ v.lojas.ativas }}</strong><small>de {{ v.lojas.total }} no total</small></div>
          <div class="sv-card kpi"><span>Novas</span><strong>{{ v.lojas.novasNoPeriodo }}</strong><small>nos últimos {{ v.dias }} dias</small></div>
          <div class="sv-card kpi"><span>Em uso</span><strong>{{ v.lojas.comMovimentoNoPeriodo }}</strong><small>com conversa ou acesso no período</small></div>
          <div class="sv-card kpi" [class.atencao]="v.lojas.paradas > 0">
            <span>Paradas</span><strong>{{ v.lojas.paradas }}</strong><small>ativas sem acesso há 7+ dias</small>
          </div>
          <div class="sv-card kpi"><span>Aguardando e-mail</span><strong>{{ v.lojas.aguardandoConfirmacao }}</strong><small>cadastro não confirmado</small></div>
          <div class="sv-card kpi"><span>Suspensas</span><strong>{{ v.lojas.suspensas }}</strong><small>sem acesso, vitrine fora</small></div>
        </div>
      </section>

      <section class="bloco">
        <h2>Uso nos últimos {{ v.dias }} dias</h2>
        <div class="kpis">
          <div class="sv-card kpi"><span>Conversas</span><strong>{{ v.totais.conversasNoPeriodo | number }}</strong><small>com mensagem no período</small></div>
          <div class="sv-card kpi"><span>Mensagens recebidas</span><strong>{{ v.totais.mensagensRecebidas | number }}</strong><small>de clientes</small></div>
          <div class="sv-card kpi">
            <span>Respostas do robô</span><strong>{{ v.totais.respostasRobo | number }}</strong><small>{{ v.totais.mensagensVendedor | number }} de vendedores</small>
          </div>
          <div class="sv-card kpi">
            <span>Vendido</span><strong>{{ v.totais.vendido | currency: 'BRL' }}</strong><small>{{ v.totais.pedidosPagos }} pago(s) de {{ v.totais.pedidos }} pedido(s)</small>
          </div>
          <div class="sv-card kpi">
            <span>Tokens de IA</span><strong>{{ v.totais.tokensEntrada + v.totais.tokensSaida | number }}</strong>
            <small>{{ v.totais.chamadasIa | number }} chamada(s) ao {{ v.saude.modeloIa }}</small>
          </div>
          <div class="sv-card kpi" [class.atencao]="v.totais.falhasIa > 0 || v.totais.mensagensComFalha > 0">
            <span>Falhas</span><strong>{{ v.totais.falhasIa + v.totais.mensagensComFalha | number }}</strong>
            <small>{{ v.totais.falhasIa }} da IA · {{ v.totais.mensagensComFalha }} de envio</small>
          </div>
        </div>
      </section>

      <section class="sv-card bloco painel">
        <h2>Por dia</h2>
        <app-grafico-dias [dias]="v.porDia" />
      </section>

      <div class="duas">
        <section class="sv-card bloco painel">
          <h2>Lojas que mais usam (30 dias)</h2>
          <div class="periodo pequeno" role="group" aria-label="Ordenar por">
            @for (r of rankings; track r.id) {
              <button type="button" [class.ativa]="ranking() === r.id" (click)="ranking.set(r.id)">{{ r.rotulo }}</button>
            }
          </div>
          @if (topo().length) {
            <ol class="ranking">
              @for (l of topo(); track l.id) {
                <li>
                  <a [routerLink]="['/admin/lojas', l.id]">{{ l.nome }}</a>
                  <span class="trilho"><span class="barra" [style.width.%]="largura(l)"></span></span>
                  <strong>{{ ranking() === 'vendido30Dias' ? (l.vendido30Dias | currency: 'BRL') : (l[ranking()] | number) }}</strong>
                </li>
              }
            </ol>
          } @else {
            <p class="vazio">Nenhuma loja com movimento.</p>
          }
        </section>

        <section class="sv-card bloco painel">
          <h2>Saúde</h2>
          <ul class="saude">
            <li>
              <span class="sv-selo {{ v.saude.iaConfigurada ? 'ok' : 'alerta' }}"><mat-icon>{{ v.saude.iaConfigurada ? 'check_circle' : 'error' }}</mat-icon>IA</span>
              {{ v.saude.iaConfigurada ? 'Gemini configurado (' + v.saude.modeloIa + ')' : 'Sem chave do Gemini: o robô responde só por regras' }}
            </li>
            <li>
              <span class="sv-selo {{ v.saude.emailConfigurado ? 'ok' : 'info' }}"><mat-icon>{{ v.saude.emailConfigurado ? 'check_circle' : 'info' }}</mat-icon>E-mail</span>
              {{ v.saude.emailConfigurado ? 'SMTP configurado: o cadastro manda o link de confirmação' : 'Sem SMTP: o link de confirmação só vai para o log (ative a loja à mão)' }}
            </li>
            <li>
              <span class="sv-selo {{ v.saude.chaveMestraConfigurada ? 'ok' : 'alerta' }}"><mat-icon>{{ v.saude.chaveMestraConfigurada ? 'check_circle' : 'error' }}</mat-icon>Cofre</span>
              {{ v.saude.chaveMestraConfigurada ? 'Chave mestra configurada: chaves das lojas cifradas' : 'Sem Plataforma__ChaveMestra: lojas não conseguem salvar chaves' }}
            </li>
            <li>
              <span class="sv-selo info"><mat-icon>chat</mat-icon>WhatsApp</span>
              {{ v.saude.lojasComWhatsAppProprio }} loja(s) com número próprio conectado
            </li>
            <li>
              <span class="sv-selo info"><mat-icon>credit_card</mat-icon>Cartão</span>
              {{ v.saude.lojasComMercadoPagoProprio }} loja(s) com Mercado Pago próprio
            </li>
          </ul>
          @if (paradas().length) {
            <h3>Lojas paradas</h3>
            <ul class="paradas">
              @for (l of paradas(); track l.id) {
                <li><a [routerLink]="['/admin/lojas', l.id]">{{ l.nome }}</a> · último acesso {{ tempo(l.ultimoAcessoEm) }}</li>
              }
            </ul>
          }
        </section>
      </div>
    }
  `,
  styles: `
    .periodo {
      display: flex;
      gap: 6px;
      flex-wrap: wrap;

      button {
        padding: 6px 14px;
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
    .periodo.pequeno {
      margin-bottom: 12px;
      button {
        padding: 4px 12px;
      }
    }
    .bloco {
      margin-bottom: 20px;
      h2 {
        margin: 0 0 12px;
        font: var(--mat-sys-title-medium);
        font-weight: 600;
      }
      h3 {
        margin: 16px 0 8px;
        font: var(--mat-sys-title-small);
      }
    }
    .painel {
      padding: 16px 20px;
    }
    .kpis {
      display: grid;
      grid-template-columns: repeat(6, minmax(0, 1fr));
      gap: 12px;
    }
    .kpi {
      display: grid;
      gap: 2px;
      padding: 14px 16px;

      span {
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-label-medium);
      }
      strong {
        font: var(--mat-sys-headline-small);
        font-weight: 600;
        overflow-wrap: anywhere;
      }
      small {
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-body-small);
      }
    }
    .kpi.atencao {
      background: var(--sv-alerta-fundo);
      strong {
        color: var(--sv-alerta);
      }
    }
    .duas {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 20px;
    }
    .ranking {
      display: grid;
      gap: 8px;
      margin: 0;
      padding: 0;
      list-style: none;

      li {
        display: grid;
        grid-template-columns: minmax(0, 1.2fr) minmax(0, 1fr) auto;
        align-items: center;
        gap: 10px;
        font: var(--mat-sys-body-medium);
      }
      a {
        color: inherit;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .trilho {
        height: 8px;
        border-radius: 4px;
        background: var(--mat-sys-surface-container-high);
      }
      .barra {
        display: block;
        height: 100%;
        border-radius: 4px;
        background: var(--mat-sys-primary);
      }
    }
    .saude,
    .paradas {
      display: grid;
      gap: 10px;
      margin: 0;
      padding: 0;
      list-style: none;
      font: var(--mat-sys-body-medium);

      li {
        display: flex;
        align-items: center;
        flex-wrap: wrap;
        gap: 8px;
      }
    }
    .paradas a,
    .linha a {
      color: var(--mat-sys-primary);
    }
    .vazio {
      color: var(--mat-sys-on-surface-variant);
    }
    @media (max-width: 1099px) {
      .kpis {
        grid-template-columns: repeat(3, minmax(0, 1fr));
      }
      .duas {
        grid-template-columns: 1fr;
      }
    }
    @media (max-width: 479px) {
      .kpis {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }
  `,
})
export class VisaoGeralPage {
  protected readonly dias = signal(30);
  protected readonly visao = httpResource<VisaoGeralPlataforma>(() => `${API}/admin/visao-geral?dias=${this.dias()}`);
  private readonly lojas = httpResource<LojaAdmin[]>(() => `${API}/admin/lojas`);

  protected readonly rankings: { id: Ranking; rotulo: string }[] = [
    { id: 'vendido30Dias', rotulo: 'Vendido' },
    { id: 'conversas30Dias', rotulo: 'Conversas' },
    { id: 'respostasRobo30Dias', rotulo: 'Respostas do robô' },
    { id: 'tokensIa30Dias', rotulo: 'Tokens de IA' },
  ];
  protected readonly ranking = signal<Ranking>('vendido30Dias');

  protected readonly topo = computed(() => {
    const campo = this.ranking();
    return [...(this.lojas.value() ?? [])]
      .filter((l) => l[campo] > 0)
      .sort((a, b) => b[campo] - a[campo])
      .slice(0, 8);
  });

  protected readonly paradas = computed(() =>
    (this.lojas.value() ?? []).filter((l) => parada(l.status, l.criadoEm, l.ultimoAcessoEm)).slice(0, 10),
  );

  protected largura(l: LojaAdmin): number {
    const maior = this.topo()[0]?.[this.ranking()] ?? 0;
    return maior > 0 ? (l[this.ranking()] / maior) * 100 : 0;
  }

  protected tempo(iso: string | null): string {
    return tempoDesde(iso);
  }
}
