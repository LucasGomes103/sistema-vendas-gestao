import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { API, Tanque } from '../../core/api';
import { Auth } from '../../core/auth';
import { EntradaDialog } from './entrada-dialog';
import { TanqueDialog, TanqueCriado } from './tanque-dialog';

type Filtro = 'Venda' | 'Quarentena' | 'Alertas';

interface StatusTanque {
  classe: 'ok' | 'alerta' | 'info';
  icone: string;
  texto: string;
}

/** Abaixo disso, o tanque de venda é sinalizado como estoque baixo. */
const ESTOQUE_BAIXO = 5;

@Component({
  selector: 'app-estoque-page',
  imports: [RouterLink, MatButtonModule, MatChipsModule, MatIconModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './estoque-page.html',
  styleUrl: './estoque-page.scss',
})
export class EstoquePage {
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  protected readonly auth = inject(Auth);

  protected readonly tanques = httpResource<Tanque[]>(() => `${API}/tanques`);
  protected readonly filtro = signal<Filtro>('Venda');

  protected entrada(): void {
    this.dialog
      .open(EntradaDialog, { data: null, width: '560px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((ok) => {
        if (!ok) return;
        this.tanques.reload();
        this.snack.open('Entrada registrada.', undefined, { duration: 3000 });
      });
  }

  protected novoTanque(): void {
    this.dialog
      .open<TanqueDialog, unknown, TanqueCriado>(TanqueDialog, { width: '440px', maxWidth: '95vw' })
      .afterClosed()
      .subscribe((id) => {
        if (!id) return;
        this.tanques.reload();
        this.snack.open('Tanque cadastrado.', undefined, { duration: 3000 });
      });
  }

  private readonly lista = computed(() => this.tanques.value() ?? []);
  private readonly maior = computed(() => Math.max(1, ...this.lista().map((t) => t.total)));

  protected readonly resumo = computed(() => {
    const lista = this.lista();
    return {
      tanques: lista.length,
      disponiveis: lista.filter((t) => t.tipo === 'Venda').reduce((s, t) => s + t.total, 0),
      quarentena: lista.filter((t) => t.tipo === 'Quarentena').reduce((s, t) => s + t.total, 0),
      baixas: lista.reduce((s, t) => s + t.baixasUltimos7Dias, 0),
    };
  });

  protected readonly contagem = computed(() => ({
    Venda: this.lista().filter((t) => t.tipo === 'Venda').length,
    Quarentena: this.lista().filter((t) => t.tipo === 'Quarentena').length,
    Alertas: this.lista().filter((t) => this.temAlerta(t)).length,
  }));

  /** Tanques com alerta aparecem primeiro: o que pede ação vem antes. */
  protected readonly visiveis = computed(() => {
    const f = this.filtro();
    return this.lista()
      .filter((t) => (f === 'Alertas' ? this.temAlerta(t) : t.tipo === f))
      .sort((a, b) => Number(this.temAlerta(b)) - Number(this.temAlerta(a)));
  });

  protected status(t: Tanque): StatusTanque {
    if (t.baixasUltimos7Dias > 0)
      return { classe: 'alerta', icone: 'warning', texto: `${t.baixasUltimos7Dias} baixas em 7 dias` };
    if (t.tipo === 'Quarentena') return { classe: 'info', icone: 'shield', texto: 'Em quarentena' };
    if (t.total === 0) return { classe: 'alerta', icone: 'inventory_2', texto: 'Vazio' };
    if (t.total <= ESTOQUE_BAIXO) return { classe: 'alerta', icone: 'trending_down', texto: 'Estoque baixo' };
    return { classe: 'ok', icone: 'check_circle', texto: 'Tudo certo' };
  }

  protected ocupacao(t: Tanque): number {
    return Math.round((t.total / this.maior()) * 100);
  }

  private temAlerta(t: Tanque): boolean {
    return this.status(t).classe === 'alerta';
  }
}
