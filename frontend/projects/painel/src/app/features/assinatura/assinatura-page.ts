import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { API, AssinaturaLoja, UsoDoPlano } from '../../core/api';
import { Auth } from '../../core/auth';

/** Texto e cor da situação da assinatura. */
export function rotuloSituacao(a: AssinaturaLoja): { texto: string; classe: 'ok' | 'info' | 'alerta' | 'erro'; icone: string } {
  switch (a.situacao) {
    case 'Isenta':
      return { texto: 'Isenta', classe: 'ok', icone: 'verified' };
    case 'Cortesia':
      return { texto: 'Cortesia', classe: 'ok', icone: 'redeem' };
    case 'Teste':
      return { texto: 'Teste grátis', classe: 'info', icone: 'hourglass_top' };
    case 'EmDia':
      return { texto: a.status === 'Cancelada' ? 'Cancelada (em uso até o fim do período)' : 'Em dia', classe: a.status === 'Cancelada' ? 'alerta' : 'ok', icone: 'check_circle' };
    case 'Carencia':
      return { texto: 'Pagamento pendente', classe: 'alerta', icone: 'warning' };
    case 'Bloqueada':
      return { texto: 'Bloqueada', classe: 'erro', icone: 'block' };
  }
}

/** Assinatura da loja: plano atual, teste grátis, vencimento e os planos disponíveis. */
@Component({
  selector: 'app-assinatura-page',
  imports: [CurrencyPipe, DatePipe, DecimalPipe, MatButtonModule, MatIconModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="sv-cabecalho-pagina">
      <div>
        <h1>Assinatura</h1>
        <p>O seu plano, o teste grátis e a mensalidade.</p>
      </div>
    </header>

    @if (assinatura.isLoading() && !assinatura.hasValue()) {
      <mat-progress-bar mode="indeterminate" />
    }

    @if (assinatura.value(); as a) {
      @let s = situacao();
      <section class="sv-card bloco">
        <div class="titulo">
          <h2><mat-icon>workspace_premium</mat-icon> {{ a.plano ? 'Plano ' + a.plano.nome : 'Sem plano (isenta)' }}</h2>
          @if (s) {
            <span class="sv-selo {{ s.classe }}"><mat-icon>{{ s.icone }}</mat-icon>{{ s.texto }}</span>
          }
        </div>
        <p class="ajuda">
          @switch (a.situacao) {
            @case ('Isenta') {
              Esta loja não paga mensalidade e não tem limites de plano.
            }
            @case ('Cortesia') {
              Cortesia da plataforma{{ a.cortesiaAte ? ' até ' + (a.cortesiaAte | date: 'dd/MM/yyyy') : '' }}. Tudo do plano {{ a.plano?.nome }} liberado.
            }
            @case ('Teste') {
              Teste grátis até {{ a.testeAte | date: "dd/MM/yyyy 'às' HH:mm" }}. Assine antes disso para não parar o atendimento.
            }
            @case ('EmDia') {
              @if (a.status === 'Cancelada') {
                A cobrança foi cancelada. A loja funciona até {{ a.vencimento | date: 'dd/MM/yyyy' }}.
              } @else {
                Mensalidade em dia. Próximo vencimento: {{ a.vencimento | date: 'dd/MM/yyyy' }}.
              }
            }
            @case ('Carencia') {
              Venceu em {{ a.vencimento | date: 'dd/MM/yyyy' }}. Tudo continua funcionando até {{ a.bloqueiaEm | date: "dd/MM/yyyy 'às' HH:mm" }};
              depois o robô para e o painel fica só para consulta até o pagamento.
            }
            @case ('Bloqueada') {
              A mensalidade venceu em {{ a.vencimento | date: 'dd/MM/yyyy' }}. O robô está parado e o painel está só para consulta.
              Assim que o pagamento for confirmado, tudo volta na hora.
            }
          }
        </p>
      </section>

      @if (uso.value(); as u) {
        <section class="sv-card bloco">
          <h2><mat-icon>data_usage</mat-icon> Uso do plano</h2>
          <p class="ajuda">Conversas e respostas com IA contam no mês (zeram no dia 1º); usuários e produtos são os ativos agora.</p>
          <div class="usos">
            @for (l of linhasDeUso(); track l.rotulo) {
              <div class="uso">
                <div class="rotulo">
                  <span>{{ l.rotulo }}</span>
                  <strong [class.cheio]="l.limite !== null && l.valor >= l.limite">
                    {{ l.valor | number }}{{ l.limite === null ? '' : ' de ' + (l.limite | number) }}
                  </strong>
                </div>
                @if (l.limite !== null) {
                  <mat-progress-bar mode="determinate" [value]="l.porcentagem" [class.cheia]="l.porcentagem >= 100" [class.quase]="l.porcentagem >= 80 && l.porcentagem < 100" />
                } @else {
                  <span class="sem-limite">sem limite</span>
                }
              </div>
            }
          </div>
          <p class="ajuda">
            Quando as conversas do mês acabam, cliente novo vai direto para a equipe (o robô não responde) até virar o mês.
            Quando as respostas com IA acabam, o robô continua com as respostas prontas, mais simples.
          </p>
        </section>
      }

      <h2 class="secao">Planos</h2>
      <div class="planos">
        @for (p of a.planos; track p.id) {
          <article class="sv-card plano" [class.atual]="p.id === a.plano?.id">
            <header>
              <h3>{{ p.nome }}</h3>
              @if (p.id === a.plano?.id) {
                <span class="sv-selo info">Seu plano</span>
              }
            </header>
            <p class="preco">{{ p.preco | currency: 'BRL' }}<small>/mês</small></p>
            <ul>
              <li><mat-icon>forum</mat-icon>{{ p.conversas | number }} conversas por mês</li>
              <li><mat-icon>smart_toy</mat-icon>{{ p.respostasIa | number }} respostas com IA por mês</li>
              <li><mat-icon>group</mat-icon>{{ p.usuarios }} usuários no painel</li>
              <li><mat-icon>inventory_2</mat-icon>{{ p.produtos === null ? 'Produtos sem limite' : 'Até ' + (p.produtos | number) + ' produtos' }}</li>
              <li [class.nao]="!p.cartao"><mat-icon>{{ p.cartao ? 'credit_card' : 'remove' }}</mat-icon>Link de cartão (Mercado Pago)</li>
              <li [class.nao]="!p.entregas"><mat-icon>{{ p.entregas ? 'local_shipping' : 'remove' }}</mat-icon>Entregas e entregador</li>
              <li [class.nao]="!p.dominioProprio"><mat-icon>{{ p.dominioProprio ? 'language' : 'remove' }}</mat-icon>Vitrine no domínio próprio</li>
            </ul>
          </article>
        }
      </div>
      <p class="ajuda rodape">"Conversa" é cada cliente diferente que escreveu para a loja no mês. As respostas do robô dentro dela não têm limite.</p>
    }
  `,
  styles: `
    :host { display: block; }
    .bloco { display: grid; gap: 4px; margin-top: 16px; padding: 16px 20px 12px; }
    .titulo { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    h2 { display: flex; align-items: center; gap: 8px; margin: 0; font: var(--mat-sys-title-medium); font-weight: 600; }
    h2 mat-icon { color: var(--mat-sys-primary); }
    .ajuda { margin: 4px 0 8px; color: var(--mat-sys-on-surface-variant); }
    .secao { margin: 24px 0 8px; }
    .planos { display: grid; gap: 12px; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); }
    .plano { padding: 16px 20px; display: flex; flex-direction: column; gap: 4px; }
    .plano.atual { outline: 2px solid var(--mat-sys-primary); }
    .plano header { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
    .plano h3 { margin: 0; font: var(--mat-sys-title-large); }
    .preco { margin: 0; font: var(--mat-sys-headline-small); font-weight: 600; }
    .preco small { font: var(--mat-sys-body-medium); color: var(--mat-sys-on-surface-variant); }
    ul { list-style: none; margin: 8px 0 0; padding: 0; display: grid; gap: 6px; }
    li { display: flex; align-items: center; gap: 8px; }
    li mat-icon { font-size: 20px; width: 20px; height: 20px; color: var(--mat-sys-primary); }
    li.nao { color: var(--mat-sys-on-surface-variant); text-decoration: line-through; }
    li.nao mat-icon { color: var(--mat-sys-outline); }
    .rodape { margin-top: 12px; }
    .usos { display: grid; gap: 12px; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); margin: 4px 0 8px; }
    .uso .rotulo { display: flex; justify-content: space-between; gap: 8px; margin-bottom: 4px; }
    .uso strong.cheio { color: var(--sv-alerta); }
    .sem-limite { color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-body-small); }
    mat-progress-bar { border-radius: 4px; }
    .quase { --mat-progress-bar-active-indicator-color: var(--sv-alerta); }
    .cheia { --mat-progress-bar-active-indicator-color: var(--mat-sys-error); }
  `,
})
export class AssinaturaPage {
  protected readonly auth = inject(Auth);
  protected readonly assinatura = httpResource<AssinaturaLoja>(() => `${API}/assinatura`);
  protected readonly uso = httpResource<UsoDoPlano>(() => `${API}/assinatura/uso`);
  protected readonly linhasDeUso = computed(() => {
    const u = this.uso.value();
    if (!u) return [];
    const linha = (rotulo: string, valor: number, limite: number | null) => ({
      rotulo,
      valor,
      limite,
      porcentagem: limite ? Math.min(100, (valor / limite) * 100) : 0,
    });
    return [
      linha('Conversas no mês', u.conversas, u.limiteConversas),
      linha('Respostas com IA no mês', u.respostasIa, u.limiteRespostasIa > 0 ? u.limiteRespostasIa : null),
      linha('Usuários ativos', u.usuarios, u.limiteUsuarios),
      linha('Produtos', u.produtos, u.limiteProdutos),
    ];
  });
  protected readonly situacao = computed(() => {
    const a = this.assinatura.value();
    return a ? rotuloSituacao(a) : null;
  });

}
