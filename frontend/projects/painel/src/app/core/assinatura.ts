import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API, AssinaturaLoja } from './api';

/** Assinatura da loja logada (plano, teste, carência, bloqueio). Carregada pelo Shell e usada no menu e na faixa. */
@Injectable({ providedIn: 'root' })
export class AssinaturaAtual {
  private readonly http = inject(HttpClient);
  private readonly _valor = signal<AssinaturaLoja | null>(null);

  readonly valor = this._valor.asReadonly();
  /** Plano com limites; null = isenta ou ainda não carregou (tudo liberado). */
  readonly plano = computed(() => this._valor()?.plano ?? null);
  readonly temEntregas = computed(() => this.plano()?.entregas !== false);
  readonly temCartao = computed(() => this.plano()?.cartao !== false);
  readonly bloqueada = computed(() => this._valor()?.situacao === 'Bloqueada');

  async carregar(): Promise<void> {
    try {
      this._valor.set(await firstValueFrom(this.http.get<AssinaturaLoja>(`${API}/assinatura`)));
    } catch {
      /* sem a assinatura o painel segue como antes */
    }
  }
}
