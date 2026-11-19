import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API, UsuarioLogado } from './api';

/** Sessão por cookie HttpOnly: o navegador guarda o cookie, aqui fica só quem está logado. */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly _usuario = signal<UsuarioLogado | null>(null);
  private verificado = false;

  readonly usuario = this._usuario.asReadonly();
  readonly logado = computed(() => this._usuario() !== null);
  readonly ehDono = computed(() => this._usuario()?.papel === 'Dono');
  /** Entregador só usa a agenda de entregas. */
  readonly ehEntregador = computed(() => this._usuario()?.papel === 'Entregador');

  /** Consulta a sessão atual uma vez (ao abrir o painel). */
  async carregar(): Promise<boolean> {
    if (!this.verificado) {
      this.verificado = true;
      try {
        this._usuario.set(await firstValueFrom(this.http.get<UsuarioLogado>(`${API}/auth/eu`)));
      } catch {
        this._usuario.set(null);
      }
    }
    return this.logado();
  }

  /** Código do último erro de login (ex.: 'email_nao_confirmado'), para a tela oferecer o próximo passo. */
  readonly codigoErro = signal<string | null>(null);

  async entrar(email: string, senha: string): Promise<string | null> {
    this.codigoErro.set(null);
    try {
      this._usuario.set(await firstValueFrom(this.http.post<UsuarioLogado>(`${API}/auth/login`, { email, senha })));
      this.verificado = true;
      return null;
    } catch (e) {
      const erro = e as HttpErrorResponse;
      const status = erro.status;
      if (status === 401) return 'E-mail ou senha inválidos.';
      if (status === 403) {
        this.codigoErro.set(erro.error?.codigo ?? null);
        return erro.error?.detail ?? 'Acesso bloqueado.';
      }
      if (status === 429) return 'Muitas tentativas. Aguarde um minuto e tente de novo.';
      return 'Não foi possível conectar. A API está rodando?';
    }
  }

  /** Atualiza nome e e-mail mostrados no menu depois que o próprio usuário se editou (ou acabou de entrar). */
  async recarregar(): Promise<void> {
    this.verificado = true;
    try {
      this._usuario.set(await firstValueFrom(this.http.get<UsuarioLogado>(`${API}/auth/eu`)));
    } catch {
      /* a sessão é conferida de novo na próxima chamada */
    }
  }

  async sair(): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${API}/auth/logout`, {}));
    } finally {
      this.sessaoExpirada();
    }
  }

  /** Chamado também pelo interceptor quando a API responde 401. */
  sessaoExpirada(): void {
    this._usuario.set(null);
    void this.router.navigate(['/login']);
  }
}
