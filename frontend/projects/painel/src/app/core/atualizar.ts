import { DestroyRef, inject } from '@angular/core';

/**
 * Recarrega um recurso a cada intervalo enquanto a tela estiver aberta.
 * Ponte até o SignalR, que vai empurrar mensagens novas em tempo real.
 */
export function atualizarACada(ms: number, recarregar: () => void): void {
  const id = setInterval(() => {
    if (document.visibilityState === 'visible') recarregar();
  }, ms);
  inject(DestroyRef).onDestroy(() => clearInterval(id));
}
