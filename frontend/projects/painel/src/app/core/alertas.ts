import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, AlertaQuente } from './api';
import { Auth } from './auth';

/**
 * Clientes quentes sem dono. O painel consulta a cada poucos segundos e avisa (som + aviso na tela)
 * quando alguém novo chama, quando passa de 15 min e, para o dono, quando passa de 1 h.
 */
@Injectable({ providedIn: 'root' })
export class AlertasQuentes {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly snack = inject(MatSnackBar);
  private readonly auth = inject(Auth);

  readonly lista = signal<AlertaQuente[]>([]);
  readonly urgentes = computed(() => this.lista().filter((a) => a.nivel === 2).length);

  /** Último nível já avisado por conversa: o mesmo aviso não toca duas vezes. */
  private readonly avisados = new Map<string, number>();
  private primeiraLeitura = true;

  async atualizar(): Promise<void> {
    if (!this.auth.usuario()) return;
    let lista: AlertaQuente[];
    try {
      lista = await firstValueFrom(this.http.get<AlertaQuente[]>(`${API}/conversas/alertas`));
    } catch {
      return; // sem rede ou sessão caiu: tenta de novo no próximo ciclo
    }
    this.lista.set(lista);

    const ids = new Set(lista.map((a) => a.conversaId));
    for (const id of [...this.avisados.keys()]) if (!ids.has(id)) this.avisados.delete(id);

    // Ao abrir o painel só registra o que já existe; avisa o que mudar daqui em diante.
    const novos = lista.filter((a) => (this.avisados.get(a.conversaId) ?? -1) < a.nivel);
    for (const a of lista) this.avisados.set(a.conversaId, Math.max(a.nivel, this.avisados.get(a.conversaId) ?? -1));
    if (this.primeiraLeitura) {
      this.primeiraLeitura = false;
      return;
    }

    const importantes = novos.filter((a) => a.nivel < 2 || this.auth.ehDono());
    if (importantes.length === 0) return;
    const a = importantes.sort((x, y) => y.nivel - x.nivel)[0];
    this.avisar(a);
  }

  abrir(a: AlertaQuente): void {
    void this.router.navigate(['/conversas', a.conversaId]);
  }

  /** Minutos esperando um vendedor. */
  minutos(a: AlertaQuente): number {
    return Math.max(0, Math.floor((Date.now() - new Date(a.chamouVendedorEm).getTime()) / 60000));
  }

  private avisar(a: AlertaQuente): void {
    const texto =
      a.nivel === 2
        ? `${a.cliente} espera há mais de 1 h e ninguém assumiu.`
        : a.nivel === 1
          ? `${a.cliente} espera um vendedor há ${this.minutos(a)} min.`
          : `${a.cliente} quer falar com um vendedor.`;
    this.snack
      .open(texto, 'Abrir', { duration: 12000, panelClass: a.nivel === 2 ? 'sv-snack-urgente' : undefined })
      .onAction()
      .subscribe(() => this.abrir(a));
    this.bipe(a.nivel === 2 ? 3 : a.nivel === 1 ? 2 : 1);
    if (document.visibilityState !== 'visible' && 'Notification' in window && Notification.permission === 'granted') {
      new Notification('Cliente quente esperando', { body: texto, tag: `quente-${a.conversaId}` });
    }
  }

  /** Bipe curto pelo próprio navegador (o navegador só toca depois do primeiro clique na página). */
  private bipe(vezes: number): void {
    try {
      const ctx = new AudioContext();
      for (let i = 0; i < vezes; i++) {
        const osc = ctx.createOscillator();
        const vol = ctx.createGain();
        osc.frequency.value = 880;
        vol.gain.value = 0.15;
        osc.connect(vol).connect(ctx.destination);
        const t = ctx.currentTime + i * 0.3;
        osc.start(t);
        osc.stop(t + 0.15);
      }
      setTimeout(() => void ctx.close(), vezes * 300 + 300);
    } catch {
      // navegador sem áudio: o aviso na tela basta
    }
  }

  pedirPermissao(): void {
    if ('Notification' in window && Notification.permission === 'default') void Notification.requestPermission();
  }
}
