import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, mensagemDeErro } from '../../core/api';

interface EstadoConexao {
  disponivel: boolean;
  status: string;
  conectado?: boolean;
  numero?: string | null;
  qr?: string | null;
  codigo?: string | null;
}

/** Conecta o número da loja na ponte do WhatsApp (QR code), sem precisar acessar o servidor. */
@Component({
  selector: 'app-conexao-whatsapp',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (estado.value(); as e) {
      @if (e.disponivel) {
        <section class="sv-card bloco">
          <h2><mat-icon>qr_code_2</mat-icon> Conexão do WhatsApp</h2>

          @if (e.conectado) {
            <p class="ok">
              <mat-icon>check_circle</mat-icon>
              Conectado{{ e.numero ? ' ao número ' + formatar(e.numero) : '' }}. O robô recebe as mensagens dos números
              permitidos abaixo.
            </p>
            <div class="acoes">
              <button mat-stroked-button class="perigo" [disabled]="ocupado()" (click)="desconectar()">
                <mat-icon>link_off</mat-icon> Desconectar
              </button>
            </div>
          } @else if (e.codigo) {
            <p>No celular: <b>WhatsApp › Aparelhos conectados › Conectar um aparelho › Conectar com número de telefone</b> e digite:</p>
            <p class="codigo">{{ e.codigo }}</p>
          } @else if (e.qr) {
            <div class="qr">
              <img [src]="e.qr" alt="QR code para conectar o WhatsApp" width="240" height="240" />
              <p>No celular: <b>WhatsApp › Aparelhos conectados › Conectar um aparelho</b> e aponte para o código.</p>
            </div>
            <details>
              <summary>Prefere digitar um código?</summary>
              <div class="pareamento">
                <mat-form-field appearance="outline">
                  <mat-label>Número que vai conectar</mat-label>
                  <input matInput inputmode="tel" placeholder="11 91234-5678" [ngModel]="numero()" (ngModelChange)="numero.set($event)" />
                </mat-form-field>
                <button mat-stroked-button [disabled]="!numero().trim() || ocupado()" (click)="gerarCodigo()">Gerar código</button>
              </div>
            </details>
          } @else {
            <p class="espera"><mat-icon>hourglass_top</mat-icon> {{ e.status }}…</p>
            <mat-progress-bar mode="indeterminate" />
          }
        </section>
      }
    }
  `,
  styleUrl: './config-page.scss',
  styles: `
    p {
      display: flex;
      align-items: flex-start;
      gap: 6px;
      margin: 0 0 12px;
    }
    .ok {
      color: var(--sv-sucesso);
    }
    .espera {
      color: var(--mat-sys-on-surface-variant);
    }
    .qr {
      display: grid;
      justify-items: center;
      gap: 8px;
      text-align: center;

      img {
        border-radius: 8px;
        background: #fff;
      }
      p {
        display: block;
      }
    }
    .codigo {
      justify-content: center;
      font-size: 32px;
      font-weight: 700;
      letter-spacing: 4px;
    }
    details {
      margin-top: 12px;
    }
    summary {
      cursor: pointer;
      color: var(--mat-sys-primary);
      margin-bottom: 8px;
    }
    .pareamento {
      display: flex;
      align-items: flex-start;
      gap: 8px;
      flex-wrap: wrap;

      mat-form-field {
        flex: 1;
        min-width: 200px;
      }
      button {
        height: 56px;
      }
    }
    .acoes {
      display: flex;
      justify-content: flex-end;
    }
    .perigo {
      color: var(--mat-sys-error);
    }
  `,
})
export class ConexaoWhatsAppComponent {
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);

  protected readonly estado = httpResource<EstadoConexao>(() => `${API}/config/whatsapp/conexao`);
  protected readonly numero = signal('');
  protected readonly ocupado = signal(false);

  constructor() {
    // O QR muda a cada ~20 s e a conexão acontece no celular: acompanha até conectar.
    const timer = setInterval(() => {
      if (!this.estado.value()?.conectado && !this.estado.isLoading()) this.estado.reload();
    }, 5000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected formatar(n: string): string {
    const local = n.startsWith('55') ? n.slice(2) : n;
    return local.length === 11 ? `(${local.slice(0, 2)}) ${local.slice(2, 7)}-${local.slice(7)}` : n;
  }

  protected async gerarCodigo(): Promise<void> {
    let n = this.numero().replace(/\D/g, '');
    if (n.length <= 11) n = '55' + n;
    await this.executar(() => firstValueFrom(this.http.post(`${API}/config/whatsapp/conexao/codigo`, { numero: n })));
  }

  protected async desconectar(): Promise<void> {
    if (!confirm('Desconectar o WhatsApp? O robô para de receber mensagens até conectar de novo.')) return;
    await this.executar(() => firstValueFrom(this.http.post(`${API}/config/whatsapp/conexao/desconectar`, {})));
  }

  private async executar(acao: () => Promise<unknown>): Promise<void> {
    this.ocupado.set(true);
    try {
      await acao();
      this.estado.reload();
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.ocupado.set(false);
    }
  }
}
