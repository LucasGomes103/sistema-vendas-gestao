import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Auth } from '../core/auth';

interface ItemMenu {
  rota: string;
  rotulo: string;
  icone: string;
}

/**
 * Estrutura do painel: trilho de navegação à esquerda no desktop e barra inferior no celular,
 * como nas telas de referência (estoque e conversa se usam em pé, com o polegar).
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  protected readonly auth = inject(Auth);

  protected readonly menu: ItemMenu[] = [
    { rota: '/estoque', rotulo: 'Estoque', icone: 'water' },
    { rota: '/catalogo', rotulo: 'Catálogo', icone: 'set_meal' },
  ];

  /** Itens que ainda vão entrar na fase 1, mostrados desabilitados para dar a visão do todo. */
  protected readonly emBreve: ItemMenu[] = [
    { rota: '', rotulo: 'Funil', icone: 'view_kanban' },
    { rota: '', rotulo: 'Conversas', icone: 'chat' },
    { rota: '', rotulo: 'Pedidos', icone: 'receipt_long' },
    { rota: '', rotulo: 'Métricas', icone: 'monitoring' },
  ];

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
