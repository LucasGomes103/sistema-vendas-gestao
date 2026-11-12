import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Auth } from '../core/auth';
import { SenhaDialog } from '../features/usuarios/senha-dialog';
import { AlertasQuentes } from '../core/alertas';
import { atualizarACada } from '../core/atualizar';
import { SinoAlertas } from './sino-alertas';

interface ItemMenu {
  rota: string;
  rotulo: string;
  icone: string;
  /** Só aparece para o dono. */
  dono?: boolean;
}

/**
 * Estrutura do painel: trilho de navegação à esquerda no desktop e barra inferior no celular,
 * como nas telas de referência (estoque e conversa se usam em pé, com o polegar).
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule, SinoAlertas],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.tela-cheia]': 'telaCheia()' },
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  protected readonly auth = inject(Auth);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly alertas = inject(AlertasQuentes);

  constructor() {
    void this.alertas.atualizar();
    atualizarACada(8000, () => void this.alertas.atualizar());
  }

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  /** Chat aberto no celular ocupa a tela toda, como no WhatsApp. */
  protected readonly telaCheia = computed(() => /^\/conversas\/[^/]+/.test(this.url()));

  private readonly todos: ItemMenu[] = [
    { rota: '/funil', rotulo: 'Funil', icone: 'view_kanban' },
    { rota: '/conversas', rotulo: 'Conversas', icone: 'chat' },
    { rota: '/pedidos', rotulo: 'Pedidos', icone: 'receipt_long' },
    { rota: '/caixa', rotulo: 'Caixa', icone: 'point_of_sale', dono: true },
    { rota: '/estoque', rotulo: 'Estoque', icone: 'water' },
    { rota: '/catalogo', rotulo: 'Catálogo', icone: 'set_meal' },
  ];

  protected readonly menu = computed(() => this.todos.filter((i) => !i.dono || this.auth.ehDono()));

  /** Itens que ainda vão entrar na fase 1, mostrados desabilitados para dar a visão do todo. */
  protected readonly emBreve: ItemMenu[] = [
    { rota: '', rotulo: 'Métricas', icone: 'monitoring' },
  ];

  protected trocarSenha(): void {
    this.dialog.open(SenhaDialog, { data: null, width: '420px', maxWidth: '95vw' });
  }

  protected readonly iniciais = computed(() =>
    (this.auth.usuario()?.nome ?? '?')
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((p) => p[0].toUpperCase())
      .join(''),
  );

  protected readonly siglaLoja = computed(() =>
    (this.auth.usuario()?.loja ?? 'SV')
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((p) => p[0].toUpperCase())
      .join(''),
  );
}
