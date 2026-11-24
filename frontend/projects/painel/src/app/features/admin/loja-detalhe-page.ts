import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, DetalheLojaAdmin, mensagemDeErro } from '../../core/api';
import { Auth } from '../../core/auth';
import { Confirmar } from '../../core/confirmar';
import { AdminAbas, GraficoDias, parada, rotuloAcao, tempoDesde } from './admin-comum';

const STATUS: Record<string, { rotulo: string; classe: string; icone: string }> = {
  Ativa: { rotulo: 'Ativa', classe: 'ok', icone: 'check_circle' },
  AguardandoConfirmacao: { rotulo: 'Aguardando e-mail', classe: 'info', icone: 'mark_email_unread' },
  Suspensa: { rotulo: 'Suspensa', classe: 'alerta', icone: 'block' },
};

/** Detalhe de uma loja para o administrador: números, configuração, equipe, uso por dia e auditoria. */
@Component({
  selector: 'app-loja-detalhe-page',
  imports: [CurrencyPipe, DatePipe, DecimalPipe, RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, AdminAbas, GraficoDias],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="sv-cabecalho-pagina">
      <div>
        <a class="voltar" routerLink="/admin/lojas"><mat-icon>arrow_back</mat-icon> Lojas</a>
        <h1>{{ detalhe.value()?.loja?.nome ?? 'Loja' }}</h1>
        @if (detalhe.value(); as d) {
          <p>/{{ d.loja.slug }} · {{ d.loja.segmento || 'sem tipo' }} · desde {{ d.loja.criadoEm | date: 'dd/MM/yyyy' }}</p>
        }
      </div>
      @if (detalhe.value(); as d) {
        <span class="acoes">
          @if (d.loja.id !== minhaLoja()) {
            <button mat-flat-button (click)="verComo()" [disabled]="ocupado()"><mat-icon>visibility</mat-icon> Ver como a loja</button>
            @if (d.loja.status === 'Suspensa') {
              <button mat-stroked-button (click)="ativar()" [disabled]="ocupado()"><mat-icon>play_arrow</mat-icon> Reativar</button>
            } @else {
              <button mat-stroked-button class="perigo" (click)="suspender()" [disabled]="ocupado()"><mat-icon>block</mat-icon> Suspender</button>
            }
          }
        </span>
      }
    </header>
    <app-admin-abas />

    @if (detalhe.isLoading() && !detalhe.hasValue()) {
      <mat-progress-bar mode="indeterminate" />
    }
    @if (detalhe.error()) {
      <p class="vazio">Loja não encontrada.</p>
    }

    @if (detalhe.value(); as d) {
      @let s = status[d.loja.status];
      <div class="selos">
        <span class="sv-selo {{ s.classe }}"><mat-icon>{{ s.icone }}</mat-icon>{{ s.rotulo }}</span>
        @if (estaParada()) {
          <span class="sv-selo alerta"><mat-icon>bedtime</mat-icon>Parada: ninguém entra há 7+ dias</span>
        }
        @if (d.loja.lojaOriginal) {
          <span class="sv-selo info"><mat-icon>star</mat-icon>Loja original</span>
        }
        <span class="sv-selo info"><mat-icon>person</mat-icon>{{ d.loja.dono ?? '—' }} · {{ d.loja.emailDono ?? '—' }}</span>
      </div>
      @if (d.loja.motivoSuspensao) {
        <p class="motivo"><mat-icon>info</mat-icon> {{ d.loja.motivoSuspensao }}</p>
      }

      <div class="kpis">
        <div class="sv-card kpi"><span>Último acesso</span><strong>{{ tempo(d.loja.ultimoAcessoEm) }}</strong><small>{{ d.loja.ultimoAcessoEm ? (d.loja.ultimoAcessoEm | date: 'dd/MM HH:mm') : 'ninguém entrou ainda' }}</small></div>
        <div class="sv-card kpi"><span>Conversas (30 dias)</span><strong>{{ d.totais.conversasNoPeriodo | number }}</strong><small>{{ d.conversasUltimas24h }} nas últimas 24 h</small></div>
        <div class="sv-card kpi"><span>Mensagens recebidas</span><strong>{{ d.totais.mensagensRecebidas | number }}</strong><small>{{ d.clientes | number }} cliente(s) cadastrados</small></div>
        <div class="sv-card kpi"><span>Respostas do robô</span><strong>{{ d.totais.respostasRobo | number }}</strong><small>{{ d.totais.mensagensVendedor | number }} de vendedores</small></div>
        <div class="sv-card kpi"><span>Vendido (30 dias)</span><strong>{{ d.totais.vendido | currency: 'BRL' }}</strong><small>{{ d.totais.pedidosPagos }} pago(s) de {{ d.totais.pedidos }}</small></div>
        <div class="sv-card kpi" [class.atencao]="d.totais.falhasIa > 0">
          <span>IA (30 dias)</span><strong>{{ d.totais.tokensEntrada + d.totais.tokensSaida | number }}</strong>
          <small>tokens · {{ d.totais.chamadasIa | number }} chamada(s) · {{ d.totais.falhasIa }} falha(s)</small>
        </div>
      </div>

      <section class="sv-card bloco">
        <h2>Por dia (30 dias)</h2>
        <app-grafico-dias [dias]="d.porDia" [semLojasNovas]="true" />
      </section>

      <div class="duas">
        <section class="sv-card bloco">
          <h2>Configuração</h2>
          <ul class="config">
            <li>
              <span class="sv-selo {{ d.configuracao.whatsAppConectado ? 'ok' : 'info' }}"><mat-icon>chat</mat-icon>WhatsApp</span>
              {{ d.configuracao.whatsAppConectado ? 'conectado' : 'não conectado' }}{{ d.configuracao.whatsAppNumero ? ' · ' + d.configuracao.whatsAppNumero : '' }}
              · {{ d.configuracao.atenderTodos ? 'atende todos' : 'só ' + d.configuracao.numerosPermitidos + ' número(s) permitidos' }}
            </li>
            <li>
              <span class="sv-selo {{ d.loja.mercadoPagoProprio ? 'ok' : 'info' }}"><mat-icon>credit_card</mat-icon>Cartão</span>
              {{ d.loja.mercadoPagoProprio ? 'Mercado Pago próprio' : 'sem Mercado Pago' }}
            </li>
            <li>
              <span class="sv-selo {{ d.configuracao.pixConfigurado ? 'ok' : 'info' }}"><mat-icon>qr_code</mat-icon>Pix</span>
              {{ d.configuracao.pixConfigurado ? 'chave cadastrada' : 'sem chave Pix' }}
            </li>
            <li>
              <span class="sv-selo {{ d.configuracao.freteConfigurado || d.configuracao.zonasDeEntrega ? 'ok' : 'info' }}"><mat-icon>local_shipping</mat-icon>Frete</span>
              {{ d.configuracao.freteConfigurado ? 'por km' : '' }}{{ d.configuracao.freteConfigurado && d.configuracao.zonasDeEntrega ? ' · ' : '' }}{{ d.configuracao.zonasDeEntrega ? d.configuracao.zonasDeEntrega + ' faixa(s) de CEP' : '' }}{{ !d.configuracao.freteConfigurado && !d.configuracao.zonasDeEntrega ? 'não configurado' : '' }}
            </li>
            <li>
              <span class="sv-selo info"><mat-icon>schedule</mat-icon>Horário</span>
              {{ d.configuracao.horarioAtendimento }} · {{ d.loja.produtos }} produto(s) ativo(s)
            </li>
          </ul>
        </section>

        <section class="sv-card bloco">
          <h2>Equipe ({{ d.usuarios.length }})</h2>
          <ul class="equipe">
            @for (u of d.usuarios; track u.id) {
              <li [class.inativo]="!u.ativo">
                <div>
                  <strong>{{ u.nome }}</strong>
                  <small>{{ u.email }} · {{ u.papel }}{{ u.ativo ? '' : ' · desativado' }}</small>
                </div>
                <span class="acesso">{{ tempo(u.ultimoAcessoEm) }}</span>
              </li>
            }
          </ul>
        </section>
      </div>

      <section class="sv-card bloco">
        <h2>O que o suporte fez nesta loja</h2>
        @if (d.auditoria.length) {
          <ul class="auditoria">
            @for (a of d.auditoria; track a.id) {
              @let r = rotulo(a);
              <li>
                <mat-icon>{{ r.icone }}</mat-icon>
                <span><strong>{{ r.rotulo }}</strong>{{ a.detalhe ? ': ' + a.detalhe : '' }}</span>
                <small>{{ a.adminEmail }} · {{ a.criadoEm | date: 'dd/MM/yyyy HH:mm' }}</small>
              </li>
            }
          </ul>
        } @else {
          <p class="vazio">Nada registrado ainda.</p>
        }
      </section>
    }
  `,
  styles: `
    .voltar {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      margin-bottom: 4px;
      color: var(--mat-sys-primary);
      font: var(--mat-sys-label-large);
      text-decoration: none;

      mat-icon {
        font-size: 18px;
        width: 18px;
        height: 18px;
      }
    }
    .acoes,
    .selos {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
    .selos {
      margin-bottom: 12px;
    }
    .motivo {
      display: flex;
      align-items: center;
      gap: 6px;
      margin: 0 0 12px;
      color: var(--sv-alerta);
    }
    .perigo {
      color: var(--mat-sys-error);
    }
    .kpis {
      display: grid;
      grid-template-columns: repeat(6, minmax(0, 1fr));
      gap: 12px;
      margin-bottom: 20px;
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
        font: var(--mat-sys-title-large);
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
    }
    .bloco {
      margin-bottom: 20px;
      padding: 16px 20px;

      h2 {
        margin: 0 0 12px;
        font: var(--mat-sys-title-medium);
        font-weight: 600;
      }
    }
    .duas {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 20px;
    }
    .config,
    .equipe,
    .auditoria {
      display: grid;
      gap: 10px;
      margin: 0;
      padding: 0;
      list-style: none;
      font: var(--mat-sys-body-medium);
    }
    .config li {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 8px;
    }
    .equipe li {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 12px;

      div {
        display: grid;
        min-width: 0;
      }
      small {
        color: var(--mat-sys-on-surface-variant);
        overflow-wrap: anywhere;
      }
      .acesso {
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-label-medium);
        white-space: nowrap;
      }
    }
    .equipe li.inativo {
      opacity: 0.6;
    }
    .auditoria li {
      display: grid;
      grid-template-columns: auto 1fr;
      column-gap: 10px;
      align-items: start;

      mat-icon {
        grid-row: span 2;
        color: var(--mat-sys-on-surface-variant);
      }
      span {
        overflow-wrap: anywhere;
      }
      small {
        color: var(--mat-sys-on-surface-variant);
      }
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
export class LojaDetalhePage {
  /** Id da loja, vindo da rota (withComponentInputBinding). */
  readonly id = input.required<string>();

  private readonly http = inject(HttpClient);
  private readonly auth = inject(Auth);
  private readonly confirmar = inject(Confirmar);
  private readonly snack = inject(MatSnackBar);

  protected readonly status = STATUS;
  protected readonly minhaLoja = computed(() => this.auth.usuario()?.lojaId);
  protected readonly detalhe = httpResource<DetalheLojaAdmin>(() => `${API}/admin/lojas/${this.id()}`);
  protected readonly ocupado = signal(false);

  protected readonly estaParada = computed(() => {
    const l = this.detalhe.value()?.loja;
    return !!l && parada(l.status, l.criadoEm, l.ultimoAcessoEm);
  });

  protected tempo = tempoDesde;
  protected rotulo = rotuloAcao;

  protected async verComo(): Promise<void> {
    const l = this.detalhe.value()?.loja;
    if (!l) return;
    const ok = await this.confirmar.perguntar(
      `Você vai ver o painel da ${l.nome} como o dono vê: conversas, pedidos, estoque e configuração. É só leitura, nada pode ser mudado, e a entrada fica registrada na auditoria.\n\nPara voltar, use o botão "Voltar para a minha loja" no topo.`,
      { titulo: 'Ver como a loja?', botao: 'Ver como a loja' },
    );
    if (!ok) return;
    this.ocupado.set(true);
    try {
      await this.auth.verComoLoja(l.id);
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
      this.ocupado.set(false);
    }
  }

  protected async suspender(): Promise<void> {
    const l = this.detalhe.value()?.loja;
    if (!l) return;
    const motivo = await this.confirmar.pedirTexto(
      `Ninguém da ${l.nome} entra no painel (quem está dentro sai em até 1 minuto), a vitrine sai do ar e o robô para de responder. Dá para reativar depois, sem perder nada.`,
      'Motivo (opcional, aparece só aqui)',
      { titulo: 'Suspender a loja?', botao: 'Suspender', perigo: true },
    );
    if (motivo === null) return;
    await this.executar(`${API}/admin/lojas/${l.id}/suspender`, { motivo }, `${l.nome} suspensa.`);
  }

  protected async ativar(): Promise<void> {
    const l = this.detalhe.value()?.loja;
    if (!l) return;
    const ok = await this.confirmar.perguntar(`A equipe da ${l.nome} volta a entrar, a vitrine volta ao ar e o robô volta a responder.`, {
      titulo: 'Reativar?',
      botao: 'Reativar',
    });
    if (!ok) return;
    await this.executar(`${API}/admin/lojas/${l.id}/ativar`, {}, `${l.nome} ativa.`);
  }

  private async executar(url: string, corpo: object, ok: string): Promise<void> {
    this.ocupado.set(true);
    try {
      await firstValueFrom(this.http.post(url, corpo));
      this.detalhe.reload();
      this.snack.open(ok, undefined, { duration: 3000 });
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.ocupado.set(false);
    }
  }
}
