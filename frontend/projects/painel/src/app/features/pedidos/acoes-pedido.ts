import { ChangeDetectionStrategy, Component, Injectable, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar } from '@angular/material/snack-bar';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { API, FormaPagamento, Pedido, PedidoAcao, mensagemDeErro } from '../../core/api';
import { Confirmar } from '../../core/confirmar';

interface Confirmacao {
  forma: FormaPagamento;
  avisarCliente: boolean;
}

/** Forma de pagamento e aviso ao cliente. Com Pix comum, o vendedor confere na conta antes de confirmar. */
@Component({
  selector: 'app-confirmar-pagamento-dialog',
  imports: [CurrencyPipe, FormsModule, MatDialogModule, MatButtonModule, MatButtonToggleModule, MatCheckboxModule, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Confirmar pagamento do pedido #{{ pedido.numero }}</h2>
    <mat-dialog-content>
      <p class="valor">{{ pedido.total | currency: 'BRL' }} · {{ pedido.cliente }}</p>
      <p class="ajuda">Confira se o valor caiu na conta da loja. Ao confirmar, os itens saem do estoque e o card vai para Pago.
        Pagamento pelo link do cartão (Mercado Pago) confirma sozinho: não precisa vir aqui.</p>
      <mat-button-toggle-group [ngModel]="forma()" (ngModelChange)="forma.set($event)" aria-label="Forma de pagamento">
        <mat-button-toggle value="Pix"><mat-icon>pix</mat-icon> Pix</mat-button-toggle>
        <mat-button-toggle value="Dinheiro"><mat-icon>payments</mat-icon> Dinheiro</mat-button-toggle>
        <mat-button-toggle value="Cartao"><mat-icon>credit_card</mat-icon> Cartão</mat-button-toggle>
      </mat-button-toggle-group>
      @if (pedido.conversaId) {
        <mat-checkbox [ngModel]="avisar()" (ngModelChange)="avisar.set($event)">Avisar o cliente no WhatsApp</mat-checkbox>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Voltar</button>
      <button mat-flat-button [mat-dialog-close]="resultado()"><mat-icon>check</mat-icon> Confirmar</button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-dialog-content {
      display: grid;
      gap: 12px;
      justify-items: start;
    }
    .valor {
      margin: 0;
      font: var(--mat-sys-title-large);
    }
    .ajuda {
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
  `,
})
export class ConfirmarPagamentoDialog {
  protected readonly pedido = inject<Pedido>(MAT_DIALOG_DATA);
  protected readonly forma = signal<FormaPagamento>('Pix');
  protected readonly avisar = signal(true);
  protected resultado(): Confirmacao {
    return { forma: this.forma(), avisarCliente: this.avisar() };
  }
}

/** Ações de um pedido, usadas na tela de pedidos e na conversa. Devolvem o pedido atualizado (ou null se nada mudou). */
@Injectable({ providedIn: 'root' })
export class AcoesPedido {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly confirmar = inject(Confirmar);

  async confirmarPagamento(p: Pedido): Promise<Pedido | null> {
    const escolha = await firstValueFrom(
      this.dialog.open<ConfirmarPagamentoDialog, Pedido, Confirmacao>(ConfirmarPagamentoDialog, { data: p, width: '460px', maxWidth: '95vw' }).afterClosed(),
    );
    if (!escolha) return null;
    return this.acao(`${API}/pedidos/${p.id}/confirmar-pagamento`, escolha, `Pedido #${p.numero} pago: os itens saíram do estoque.`);
  }

  async enviarPix(p: Pedido): Promise<Pedido | null> {
    return this.acao(`${API}/pedidos/${p.id}/enviar-pix`, {}, `Cobrança do pedido #${p.numero} enviada ao cliente.`);
  }

  async cancelar(p: Pedido): Promise<Pedido | null> {
    const motivo = await this.confirmar.pedirTexto('Os itens voltam para o estoque e o card vai para Perdido.', 'Motivo (opcional)', {
      titulo: `Cancelar o pedido #${p.numero}?`,
      botao: 'Cancelar pedido',
      perigo: true,
    });
    if (motivo === null) return null;
    try {
      const r = await firstValueFrom(this.http.post<Pedido>(`${API}/pedidos/${p.id}/cancelar`, { motivo }));
      this.snack.open(`Pedido #${p.numero} cancelado.`, undefined, { duration: 4000 });
      return r;
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
      return null;
    }
  }

  async copiarPix(p: Pedido): Promise<void> {
    if (!p.pixCopiaECola) return;
    try {
      await navigator.clipboard.writeText(p.pixCopiaECola);
      this.snack.open('Código Pix copiado.', undefined, { duration: 2500 });
    } catch {
      prompt('Copie o código Pix:', p.pixCopiaECola);
    }
  }

  async copiarLinkCartao(p: Pedido): Promise<void> {
    if (!p.linkCartao) return;
    try {
      await navigator.clipboard.writeText(p.linkCartao);
      this.snack.open('Link do cartão copiado.', undefined, { duration: 2500 });
    } catch {
      prompt('Copie o link do cartão:', p.linkCartao);
    }
  }

  private async acao(url: string, corpo: unknown, sucesso: string): Promise<Pedido | null> {
    try {
      const r = await firstValueFrom(this.http.post<PedidoAcao>(url, corpo));
      this.snack.open(r.aviso ?? sucesso, 'OK', { duration: r.aviso ? 9000 : 4000 });
      return r.pedido;
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
      return null;
    }
  }
}
