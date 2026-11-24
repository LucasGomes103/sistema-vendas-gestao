import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { DecimalPipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { API, UsoIA } from '../../core/api';
import { ESTILO_BLOCO } from './estilo-bloco';

/**
 * Uso da IA do robô no mês. A IA vem da plataforma (incluída na mensalidade) com um limite de respostas por mês;
 * acima dele o robô continua respondendo, mas com as respostas prontas por regras.
 */
@Component({
  selector: 'app-config-ia',
  imports: [DecimalPipe, MatIconModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (uso.value(); as u) {
      @if (u.ativa) {
        <section class="sv-card bloco">
          <div class="titulo">
            <h2><mat-icon>smart_toy</mat-icon> Robô com IA</h2>
            @if (u.esgotado) {
              <span class="sv-selo alerta"><mat-icon>warning</mat-icon>Limite do mês atingido</span>
            } @else {
              <span class="sv-selo ok"><mat-icon>check_circle</mat-icon>Ligado</span>
            }
          </div>
          <p class="ajuda">
            @if (u.limite > 0) {
              Neste mês o robô já deu {{ u.respostas | number }} de {{ u.limite | number }} respostas com IA incluídas no seu plano.
              Acima disso ele continua atendendo com as respostas prontas (mais simples) até virar o mês.
            } @else {
              Neste mês o robô já deu {{ u.respostas | number }} respostas com IA (sem limite para esta loja).
            }
            @if (u.porRegras > 0) {
              {{ u.porRegras | number }} {{ u.porRegras === 1 ? 'resposta saiu' : 'respostas saíram' }} pelas respostas prontas.
            }
          </p>
          @if (u.limite > 0) {
            <mat-progress-bar mode="determinate" [value]="porcentagem()" [class.cheia]="u.esgotado" />
          }
        </section>
      }
    }
  `,
  styles: [ESTILO_BLOCO, `mat-progress-bar { margin: 4px 0 8px; border-radius: 4px; } .cheia { --mat-progress-bar-active-indicator-color: var(--sv-alerta); }`],
})
export class ConfigIAComponent {
  protected readonly uso = httpResource<UsoIA>(() => `${API}/config/ia`);
  protected readonly porcentagem = computed(() => {
    const u = this.uso.value();
    return u && u.limite > 0 ? Math.min(100, (u.respostas / u.limite) * 100) : 0;
  });
}
