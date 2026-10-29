import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, ConfigWhatsApp, mensagemDeErro } from '../../core/api';

/**
 * Trava de segurança da conexão do WhatsApp: com "atender todos" desligado, o sistema só lê e
 * responde os números da lista. Pensado para testar com um número de uso pessoal sem expor os contatos.
 */
@Component({
  selector: 'app-config-whatsapp',
  imports: [FormsModule, MatButtonModule, MatChipsModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSlideToggleModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="sv-card bloco">
      <h2><mat-icon>verified_user</mat-icon> WhatsApp: quem o robô pode atender</h2>

      <div class="trava" [class.aberta]="atenderTodos()">
        <mat-slide-toggle [checked]="atenderTodos()" (change)="atenderTodos.set($event.checked); alterado.set(true)">
          Atender todos os contatos
        </mat-slide-toggle>
        @if (atenderTodos()) {
          <p class="aviso">
            <mat-icon>warning</mat-icon>
            O robô vai ler e responder <b>qualquer pessoa</b> que escrever para o número conectado. Use só num número
            dedicado à loja, nunca no seu número pessoal.
          </p>
        } @else {
          <p class="ok">
            <mat-icon>lock</mat-icon>
            Trava ligada: o sistema só lê e responde os números abaixo. Mensagens de qualquer outro contato ficam só
            no celular.
          </p>
        }
      </div>

      <div class="adicionar">
        <mat-form-field appearance="outline" class="numeros">
          <mat-label>Número permitido (com DDD)</mat-label>
          <input
            matInput
            inputmode="tel"
            placeholder="11 91234-5678"
            [ngModel]="novoNumero()"
            (ngModelChange)="novoNumero.set($event)"
            (keydown.enter)="$event.preventDefault(); adicionar()"
          />
          <mat-hint>Os celulares de onde você vai mandar mensagens de teste.</mat-hint>
        </mat-form-field>
        <button mat-stroked-button type="button" [disabled]="!novoNumero().trim()" (click)="adicionar()">
          <mat-icon>add</mat-icon> Adicionar
        </button>
      </div>

      @if (numeros().length) {
        <mat-chip-set aria-label="Números permitidos" class="lista">
          @for (n of numeros(); track n) {
            <mat-chip (removed)="remover(n)">
              <mat-icon matChipAvatar>phone_iphone</mat-icon>
              {{ formatar(n) }}
              <button matChipRemove [attr.aria-label]="'Remover ' + n"><mat-icon>cancel</mat-icon></button>
            </mat-chip>
          }
        </mat-chip-set>
      } @else if (!atenderTodos()) {
        <p class="nenhum">Nenhum número liberado: o robô não responde ninguém.</p>
      }

      <mat-form-field appearance="outline">
        <mat-label>WhatsApp da loja (botão da vitrine)</mat-label>
        <input matInput inputmode="tel" [ngModel]="whatsAppNumero()" (ngModelChange)="whatsAppNumero.set($event); alterado.set(true)" />
        <mat-hint>Número que abre quando o cliente toca em "Enviar pedido pelo WhatsApp".</mat-hint>
      </mat-form-field>

      <div class="acoes">
        <button mat-flat-button [disabled]="(!alterado() && !novoNumero().trim()) || salvando()" (click)="salvar()">
          <mat-icon>save</mat-icon> Salvar WhatsApp
        </button>
      </div>
    </section>
  `,
  styleUrl: './config-page.scss',
  styles: `
    .trava {
      display: grid;
      gap: 8px;
      margin-bottom: 16px;
      padding: 12px 14px;
      border-radius: 12px;
      background: var(--sv-sucesso-fundo);

      &.aberta {
        background: var(--sv-alerta-fundo);
      }

      p {
        display: flex;
        align-items: flex-start;
        gap: 6px;
        margin: 0;
        font: var(--mat-sys-body-small);
      }

      .ok {
        color: var(--sv-sucesso);
      }

      .aviso {
        color: var(--sv-alerta);
      }

      mat-icon {
        flex-shrink: 0;
        width: 18px;
        height: 18px;
        font-size: 18px;
      }
    }

    .adicionar {
      display: flex;
      align-items: flex-start;
      gap: 8px;

      button {
        height: 56px;
      }
    }

    .numeros {
      flex: 1;
    }

    .lista {
      display: block;
      margin: 4px 0 16px;
    }

    .nenhum {
      margin: 4px 0 16px;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }

    .acoes {
      display: flex;
      justify-content: flex-end;
      padding: 8px 0;
    }
  `,
})
export class ConfigWhatsAppComponent {
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);

  private readonly config = httpResource<ConfigWhatsApp>(() => `${API}/config/whatsapp`);

  protected readonly atenderTodos = signal(false);
  protected readonly numeros = signal<string[]>([]);
  protected readonly whatsAppNumero = signal('');
  protected readonly novoNumero = signal('');
  protected readonly alterado = signal(false);
  protected readonly salvando = signal(false);

  constructor() {
    effect(() => {
      const c = this.config.value();
      if (!c) return;
      this.atenderTodos.set(c.atenderTodos);
      this.numeros.set(c.numerosPermitidos);
      this.whatsAppNumero.set(c.whatsAppNumero ?? '');
      this.alterado.set(false);
    });
  }

  /** Adiciona o número digitado; devolve false se estiver incompleto. */
  protected adicionar(): boolean {
    let n = this.novoNumero().replace(/\D/g, '');
    if (!n) return true;
    if (n.length < 10) {
      this.snack.open('Número incompleto: use DDD + número.', 'OK', { duration: 4000 });
      return false;
    }
    if (n.length <= 11) n = '55' + n;
    if (!this.numeros().includes(n)) this.numeros.update((l) => [...l, n]);
    this.novoNumero.set('');
    this.alterado.set(true);
    return true;
  }

  protected remover(n: string): void {
    this.numeros.update((l) => l.filter((x) => x !== n));
    this.alterado.set(true);
  }

  protected formatar(n: string): string {
    const local = n.startsWith('55') ? n.slice(2) : n;
    return local.length === 11 ? `(${local.slice(0, 2)}) ${local.slice(2, 7)}-${local.slice(7)}` : n;
  }

  protected async salvar(): Promise<void> {
    // Número digitado e não adicionado ainda também entra.
    if (!this.adicionar()) return;
    if (this.atenderTodos() && !confirm('Atender TODOS os contatos? Só confirme se o número conectado for exclusivo da loja.')) return;

    this.salvando.set(true);
    try {
      await firstValueFrom(
        this.http.put(`${API}/config/whatsapp`, {
          atenderTodos: this.atenderTodos(),
          numerosPermitidos: this.numeros(),
          whatsAppNumero: this.whatsAppNumero() || null,
          provedor: '',
        }),
      );
      this.alterado.set(false);
      this.snack.open('WhatsApp salvo. A ponte aplica a mudança em até 30 segundos.', undefined, { duration: 4000 });
      this.config.reload();
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.salvando.set(false);
    }
  }
}
