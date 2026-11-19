import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, WhatsAppOficial, mensagemDeErro } from '../../core/api';
import { Confirmar } from '../../core/confirmar';
import { ESTILO_BLOCO } from './estilo-bloco';

/**
 * WhatsApp oficial (Cloud API da Meta) com a conta DA LOJA. O sistema confere na Meta se o token enxerga o
 * número antes de salvar. Na fase 2 isto vira o botão "Conectar WhatsApp" (Embedded Signup).
 */
@Component({
  selector: 'app-config-whatsapp-oficial',
  imports: [ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let c = config.value();
    <section class="sv-card bloco">
      <div class="titulo">
        <h2><mat-icon>verified</mat-icon> WhatsApp oficial (Meta)</h2>
        @if (c?.configurado) {
          <span class="sv-selo ok"><mat-icon>check_circle</mat-icon>Conectado</span>
        } @else {
          <span class="sv-selo alerta"><mat-icon>info</mat-icon>Não conectado</span>
        }
      </div>
      <p class="ajuda">
        Número oficial da loja na API da Meta: as mensagens saem pelo número da loja e a Meta cobra as conversas no cartão
        cadastrado na conta do WhatsApp da loja. Sem ele, as conversas de teste ficam no simulador.
      </p>
      @if (c?.configurado) {
        <ul>
          <li>
            <mat-icon>key</mat-icon>
            {{ c?.origem === 'Servidor' ? 'Usando o token do servidor (Render).' : 'Conectado: ' + (c?.resumo ?? '') + '.' }}
          </li>
          @if (c?.phoneNumberId) {
            <li><mat-icon>tag</mat-icon> Phone Number ID {{ c?.phoneNumberId }}</li>
          }
        </ul>
      }
      @if (c && !c.podeSalvar) {
        <p class="ajuda">O servidor ainda não tem a chave mestra (Plataforma__ChaveMestra): por enquanto não dá para salvar o token aqui.</p>
      } @else {
        <form [formGroup]="form" (ngSubmit)="salvar()" class="grade">
          <mat-form-field appearance="outline">
            <mat-label>Phone Number ID</mat-label>
            <input matInput formControlName="phoneNumberId" inputmode="numeric" autocomplete="off" />
            <mat-hint>Meta for Developers → WhatsApp → Configuração da API</mat-hint>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>Token de acesso</mat-label>
            <input matInput formControlName="accessToken" autocomplete="off" placeholder="EAA..." />
            <mat-hint>Token permanente de um usuário do sistema</mat-hint>
          </mat-form-field>
          <div class="acoes inteiro">
            @if (c?.origem === 'Loja') {
              <button mat-stroked-button type="button" (click)="remover()" [disabled]="salvando()"><mat-icon>link_off</mat-icon> Desconectar</button>
            }
            <button mat-flat-button type="submit" [disabled]="salvando()"><mat-icon>save</mat-icon> Conferir e salvar</button>
          </div>
        </form>
      }
    </section>
  `,
  styles: ESTILO_BLOCO,
})
export class ConfigWhatsAppOficialComponent {
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);
  private readonly confirmar = inject(Confirmar);

  protected readonly config = httpResource<WhatsAppOficial>(() => `${API}/config/whatsapp-oficial`);
  protected readonly salvando = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    phoneNumberId: ['', [Validators.required, Validators.pattern(/^\d{5,30}$/)]],
    accessToken: ['', [Validators.required, Validators.minLength(20)]],
  });

  protected async salvar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.salvando.set(true);
    try {
      await firstValueFrom(this.http.put(`${API}/config/whatsapp-oficial`, this.form.getRawValue()));
      this.form.reset();
      this.config.reload();
      this.snack.open('WhatsApp oficial conectado.', undefined, { duration: 4000 });
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.salvando.set(false);
    }
  }

  protected async remover(): Promise<void> {
    const ok = await this.confirmar.perguntar('O robô e a equipe param de enviar mensagens por este número até conectar de novo.', {
      titulo: 'Desconectar o WhatsApp oficial?',
      botao: 'Desconectar',
      perigo: true,
    });
    if (!ok) return;
    this.salvando.set(true);
    try {
      await firstValueFrom(this.http.delete(`${API}/config/whatsapp-oficial`));
      this.config.reload();
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.salvando.set(false);
    }
  }
}
