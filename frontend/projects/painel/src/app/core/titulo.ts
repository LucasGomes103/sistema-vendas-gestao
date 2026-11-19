import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

export const NOME_PRODUTO = 'LAG CHAT';

/** Título da aba: "Pedidos · LAG CHAT" (ou só "LAG CHAT" quando a rota não tem título). */
@Injectable({ providedIn: 'root' })
export class TituloLagChat extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const pagina = this.buildTitle(snapshot);
    this.title.setTitle(pagina ? `${pagina} · ${NOME_PRODUTO}` : NOME_PRODUTO);
  }
}
