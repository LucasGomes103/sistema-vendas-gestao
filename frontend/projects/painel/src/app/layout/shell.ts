import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
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
import { Termos } from '../core/termos';

interface ItemMenu {
  rota: string;
  rotulo: string;
  icone: string;
  /** Só aparece para o dono. */
  dono?: boolean;
  /** Aparece também para o entregador (que só vê estes). */
  entregador?: boolean;
  /** No celular fica no menu da conta, não na barra inferior (que só cabe 5). */
  foraDaBarra?: boolean;
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
  protected readonly tx = inject(Termos);
  protected readonly auth = inject(Auth);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly alertas = inject(AlertasQuentes);

  constructor() {
    // Entregador não atende conversas: sem alertas de cliente quente, e o painel dele é só a agenda de entregas.
    if (!this.auth.ehEntregador()) {
      void this.alertas.atualizar();
      atualizarACada(8000, () => void this.alertas.atualizar());
    }
    effect(() => {
      if (this.auth.ehEntregador() && !this.url().startsWith('/entregas')) void this.router.navigateByUrl('/entregas');
    });
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
    { rota: '/entregas', rotulo: 'Entregas', icone: 'local_shipping', entregador: true },
    { rota: '/caixa', rotulo: 'Caixa', icone: 'point_of_sale', dono: true, foraDaBarra: true },
    { rota: '/metricas', rotulo: 'Métricas', icone: 'monitoring', dono: true, foraDaBarra: true },
    { rota: '/estoque', rotulo: 'Estoque', icone: 'water' },
    { rota: '/catalogo', rotulo: 'Catálogo', icone: 'set_meal', foraDaBarra: true },
  ];

  protected readonly menu = computed(() => {
    // Ícones do estoque e do catálogo seguem o tipo de loja (água e peixe só no aquarismo).
    const icones: Record<string, string> = { '/estoque': this.tx.iconeLocal(), '/catalogo': this.tx.iconeProduto() };
    const itens = this.todos.map((i) => (icones[i.rota] ? { ...i, icone: icones[i.rota] } : i));
    return this.auth.ehEntregador() ? itens.filter((i) => i.entregador) : itens.filter((i) => !i.dono || this.auth.ehDono());
  });

  protected readonly barra = computed(() => this.menu().filter((i) => !i.foraDaBarra));
  protected readonly noMenuDaConta = computed(() => this.menu().filter((i) => i.foraDaBarra));

  /** Itens que ainda vão entrar, mostrados desabilitados para dar a visão do todo. */
  protected readonly emBreve: ItemMenu[] = [];

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
