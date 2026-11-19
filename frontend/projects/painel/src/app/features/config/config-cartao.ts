import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, ConfigCartao, mensagemDeErro } from '../../core/api';
import { Auth } from '../../core/auth';
import { Confirmar } from '../../core/confirmar';
import { ESTILO_BLOCO } from './estilo-bloco';

/**
 * Cartão pelo Mercado Pago da conta DA LOJA. O dono cola o Access Token e a assinatura secreta; o servidor guarda
 * cifrado e nunca devolve o token (só o final dele). A loja original pode seguir com as variáveis do Render.
 */
@Component({
  selector: 'app-config-cartao',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule],
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
        Quando o cliente paga pelo link, o pedido é confirmado sozinho: o produto sai do estoque, entra no caixa como Cartão e o cliente recebe o aviso.
        O Pix continua como está (comprovante e confirmação manual). O dinheiro cai na conta do Mercado Pago da loja, que cobra uma taxa por venda.
      </p>
      @if (c?.configurado) {
        <ul>
          <li>
            <mat-icon>key</mat-icon>
            {{ c?.origem === 'Servidor' ? 'Usando o token do servidor (Render).' : 'Token salvo: ' + (c?.resumo ?? '') + '.' }}
          </li>
          <li>
            <mat-icon>{{ c?.webhookPronto ? 'check' : 'warning' }}</mat-icon>
            {{ c?.webhookPronto ? 'No Mercado Pago (Suas integrações → Webhooks, evento Order), a URL deve ser: ' + c?.urlWebhook : 'Sem endereço público para o aviso de pagamento: o pedido não confirma sozinho.' }}
          </li>
          <li>
            <mat-icon>{{ c?.assinaturaConferida ? 'check' : 'warning' }}</mat-icon>
            {{ c?.assinaturaConferida ? 'Assinatura secreta conferida nos avisos.' : 'Falta a assinatura secreta do webhook. Funciona, mas é mais seguro preencher.' }}
          </li>
        </ul>
      }

      @if (auth.ehDono()) {
        @if (c && !c.podeSalvar) {
          <p class="ajuda">O servidor ainda não tem a chave mestra (Plataforma__ChaveMestra): por enquanto não dá para salvar o token aqui.</p>
        } @else {
          <form [formGroup]="form" (ngSubmit)="salvar()" class="grade">
            <mat-form-field appearance="outline" class="inteiro">
              <mat-label>{{ c?.origem === 'Loja' ? 'Trocar o Access Token' : 'Access Token do Mercado Pago' }}</mat-label>
              <input matInput formControlName="accessToken" autocomplete="off" placeholder="APP_USR-..." />
              <mat-hint>Mercado Pago → Suas integrações → sua aplicação → Credenciais de produção</mat-hint>
            </mat-form-field>
            <mat-form-field appearance="outline" class="inteiro">
              <mat-label>Assinatura secreta do webhook (opcional)</mat-label>
              <input matInput formControlName="webhookSecret" autocomplete="off" />
              <mat-hint>Em branco ao trocar o token: mantém a assinatura já salva</mat-hint>
            </mat-form-field>
            <mat-checkbox formControlName="modoTeste" class="inteiro">Credenciais de teste (conta de teste do Mercado Pago)</mat-checkbox>
            <div class="acoes inteiro">
              @if (c?.origem === 'Loja') {
                <button mat-stroked-button type="button" (click)="remover()" [disabled]="salvando()"><mat-icon>link_off</mat-icon> Desligar cartão</button>
              }
              <button mat-flat-button type="submit" [disabled]="salvando()"><mat-icon>save</mat-icon> Salvar</button>
            </div>
          </form>
        }
      }
    </section>
  `,
  styles: ESTILO_BLOCO,
})
export class ConfigCartaoComponent {
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);
  private readonly confirmar = inject(Confirmar);
  protected readonly auth = inject(Auth);

  protected readonly config = httpResource<ConfigCartao>(() => `${API}/config/cartao`);
  protected readonly salvando = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    accessToken: ['', [Validators.required, Validators.minLength(20)]],
    webhookSecret: [''],
    modoTeste: [false],
  });

  protected async salvar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.salvando.set(true);
    try {
      await firstValueFrom(this.http.put(`${API}/config/cartao`, this.form.getRawValue()));
      this.form.reset({ accessToken: '', webhookSecret: '', modoTeste: this.form.getRawValue().modoTeste });
      this.config.reload();
      this.snack.open('Mercado Pago salvo. As próximas cobranças já saem com o link do cartão.', undefined, { duration: 4000 });
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.salvando.set(false);
    }
  }

  protected async remover(): Promise<void> {
    const ok = await this.confirmar.perguntar('As próximas cobranças saem só com Pix. Os links já enviados continuam valendo no Mercado Pago.', {
      titulo: 'Desligar o cartão?',
      botao: 'Desligar',
      perigo: true,
    });
    if (!ok) return;
    this.salvando.set(true);
    try {
      await firstValueFrom(this.http.delete(`${API}/config/cartao`));
      this.config.reload();
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.salvando.set(false);
    }
  }
}
