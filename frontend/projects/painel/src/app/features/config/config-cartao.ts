import { ChangeDetectionStrategy, Component } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { API, ConfigCartao } from '../../core/api';

/**
 * Cartão pelo Mercado Pago: só mostra a situação. As credenciais ficam nas variáveis do Render
 * (MercadoPago__AccessToken e MercadoPago__WebhookSecret), nunca no banco nem no navegador.
 */
@Component({
  selector: 'app-config-cartao',
  imports: [MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let c = config.value();
    <section class="sv-card bloco">
      <div class="titulo">
        <h2><mat-icon>credit_card</mat-icon> Cartão (Mercado Pago)</h2>
        @if (c?.configurado) {
          @if (c?.modoTeste) {
            <span class="sv-selo info"><mat-icon>science</mat-icon>Ligado em modo de teste</span>
          } @else {
            <span class="sv-selo ok"><mat-icon>check_circle</mat-icon>Ligado</span>
          }
        } @else {
          <span class="sv-selo alerta"><mat-icon>info</mat-icon>Desligado</span>
        }
      </div>
      <p class="ajuda">
        Com o Mercado Pago ligado, a cobrança de cada pedido leva também um link para pagar no cartão de crédito (com parcelas) ou débito.
        Quando o cliente paga pelo link, o pedido é confirmado sozinho: o peixe sai do estoque, entra no caixa como Cartão e o cliente recebe o aviso.
        O Pix continua como está (comprovante e confirmação manual). O Mercado Pago cobra uma taxa por venda no cartão.
      </p>
      @if (c?.configurado) {
        <ul>
          <li>
            <mat-icon>{{ c?.webhookPronto ? 'check' : 'warning' }}</mat-icon>
            {{ c?.webhookPronto ? 'Aviso de pagamento: ' + c?.urlWebhook : 'Sem endereço público para o aviso de pagamento: o pedido não confirma sozinho.' }}
          </li>
          <li>
            <mat-icon>{{ c?.assinaturaConferida ? 'check' : 'warning' }}</mat-icon>
            {{ c?.assinaturaConferida ? 'Assinatura secreta conferida nos avisos.' : 'Falta a assinatura secreta (MercadoPago__WebhookSecret). Funciona, mas é mais seguro preencher.' }}
          </li>
        </ul>
      } @else {
        <p class="ajuda">Para ligar, coloque o Access Token do Mercado Pago na variável MercadoPago__AccessToken do Render.</p>
      }
    </section>
  `,
  styles: `
    .bloco {
      display: grid;
      gap: 4px;
      margin-top: 16px;
      padding: 16px 20px 12px;

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
        margin: -4px 0 8px;
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-body-small);
      }
      ul {
        display: grid;
        gap: 4px;
        margin: 0;
        padding: 0;
        list-style: none;
        font: var(--mat-sys-body-small);
        overflow-wrap: anywhere;
      }
      li {
        display: flex;
        align-items: center;
        gap: 6px;

        mat-icon {
          flex: none;
          font-size: 18px;
          width: 18px;
          height: 18px;
        }
      }
    }
  `,
})
export class ConfigCartaoComponent {
  protected readonly config = httpResource<ConfigCartao>(() => `${API}/config/cartao`);
}
