import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { API } from '../../core/api';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Auth } from '../../core/auth';

@Component({
  selector: 'app-login-page',
  imports: [FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login-page.html',
  styleUrl: './login-page.scss',
})
export class LoginPage {
  protected readonly auth = inject(Auth);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  protected email = '';
  protected senha = '';
  protected readonly mostrarSenha = signal(false);
  protected readonly enviando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly reenviado = signal(false);

  protected async entrar(): Promise<void> {
    if (!this.email || !this.senha || this.enviando()) return;
    this.enviando.set(true);
    this.erro.set(null);

    const erro = await this.auth.entrar(this.email, this.senha);
    this.enviando.set(false);

    if (erro) this.erro.set(erro);
    else await this.router.navigateByUrl('/');
  }

  /** Novo link de confirmação do cadastro (a API responde igual tendo ou não cadastro). */
  protected async reenviar(): Promise<void> {
    this.enviando.set(true);
    try {
      await firstValueFrom(this.http.post(`${API}/cadastro/reenviar`, { email: this.email }));
      this.reenviado.set(true);
    } catch {
      this.erro.set('Não foi possível pedir o link agora. Tente de novo em um minuto.');
    } finally {
      this.enviando.set(false);
    }
  }
}
