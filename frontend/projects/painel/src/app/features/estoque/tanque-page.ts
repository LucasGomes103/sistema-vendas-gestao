import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, LoteNoTanque, TanqueDetalhe, TipoMovimento, mensagemDeErro } from '../../core/api';
import { Auth } from '../../core/auth';
import { EntradaDialog } from './entrada-dialog';
import { AcaoMovimento, MovimentoDialog } from './movimento-dialog';

const ROTULOS: Record<TipoMovimento, { texto: string; icone: string; classe: string }> = {
  Entrada: { texto: 'Entrada', icone: 'add_circle', classe: 'ok' },
  BaixaMorte: { texto: 'Baixa', icone: 'heart_minus', classe: 'alerta' },
  Reserva: { texto: 'Reserva', icone: 'bookmark', classe: 'info' },
  LiberaReserva: { texto: 'Reserva liberada', icone: 'bookmark_remove', classe: 'info' },
  Venda: { texto: 'Venda', icone: 'shopping_bag', classe: 'ok' },
  Transferencia: { texto: 'Transferência', icone: 'swap_horiz', classe: 'info' },
  Ajuste: { texto: 'Ajuste', icone: 'tune', classe: 'info' },
};

/** Detalhe do tanque: lotes, ações de estoque com alvos grandes e histórico de movimentos. */
@Component({
  selector: 'app-tanque-page',
  imports: [DatePipe, RouterLink, MatButtonModule, MatIconModule, MatMenuModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tanque-page.html',
  styleUrl: './tanque-page.scss',
})
export class TanquePage {
  /** Id do tanque, vindo da rota /estoque/tanques/:id. */
  readonly id = input.required<string>();

  protected readonly auth = inject(Auth);
  private readonly dialog = inject(MatDialog);
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);
  private readonly router = inject(Router);

  protected readonly tanque = httpResource<TanqueDetalhe>(() => `${API}/tanques/${this.id()}`);
  protected readonly total = computed(() => (this.tanque.value()?.lotes ?? []).reduce((s, l) => s + l.disponivel, 0));
  protected readonly rotulos = ROTULOS;

  protected entrada(): void {
    this.dialog
      .open(EntradaDialog, { data: { tanqueId: this.id() }, width: '560px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((ok) => ok && this.concluido('Entrada registrada.'));
  }

  protected movimento(acao: AcaoMovimento, lote?: LoteNoTanque): void {
    const t = this.tanque.value();
    if (!t) return;
    this.dialog
      .open(MovimentoDialog, {
        data: { acao, tanqueId: t.id, tanqueNome: t.nome, lotes: t.lotes, loteId: lote?.loteId },
        maxWidth: '95vw',
      })
      .afterClosed()
      .subscribe((ok) => ok && this.concluido('Movimento registrado.'));
  }

  protected async liberar(lote: LoteNoTanque): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${API}/lotes/${lote.loteId}/liberar`, {}));
      this.concluido(`${lote.especie} liberado para venda.`);
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 5000 });
    }
  }

  protected async excluir(): Promise<void> {
    const t = this.tanque.value();
    if (!t || !confirm(`Excluir o tanque "${t.nome}"? O histórico de movimentações continua guardado.`)) return;
    try {
      await firstValueFrom(this.http.delete(`${API}/tanques/${t.id}`));
      this.snack.open('Tanque excluído.', undefined, { duration: 3000 });
      this.router.navigate(['/estoque']);
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    }
  }

  private concluido(mensagem: string): void {
    this.tanque.reload();
    this.snack.open(mensagem, undefined, { duration: 3000 });
  }
}
