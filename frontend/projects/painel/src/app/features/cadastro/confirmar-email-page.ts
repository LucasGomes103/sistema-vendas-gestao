import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { firstValueFrom } from 'rxjs';
import { API, mensagemDeErro } from '../../core/api';
import { Auth } from '../../core/auth';

/** Abre o link do e-mail: confirma o cadastro, ativa a loja e entra no painel. */
@Component({
  selector: 'app-confirmar-email-page',
  imports: [RouterLink, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../login/login-page.scss',
  template: `
    <div class="lado-marca">
      <img class="marca" src="icone.svg" alt="Sistema de Vendas" />
      <h1>Ativando a sua loja.</h1>
    </div>
    <div class="lado-form">
      <div class="cartao">
        @if (erro(); as mensagem) {
          <h2>Não deu para confirmar</h2>
          <div class="erro" role="alert"><mat-icon>error</mat-icon> {{ mensagem }}</div>
          <a mat-flat-button class="entrar" routerLink="/login">Ir para a tela de entrar</a>
        } @else {
          <h2>Confirmando o e-mail…</h2>
          <mat-spinner diameter="32" />
        }
      </div>
    </div>
  `,
})
export class ConfirmarEmailPage implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly rota = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(Auth);

  protected readonly erro = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    const token = this.rota.snapshot.queryParamMap.get('token');
    if (!token) {
      this.erro.set('Link incompleto. Abra o link do e-mail de novo.');
      return;
    }
    try {
      await firstValueFrom(this.http.post(`${API}/cadastro/confirmar`, { token }));
      await this.auth.recarregar();
      await this.router.navigateByUrl('/', { replaceUrl: true });
    } catch (e) {
      this.erro.set(mensagemDeErro(e, 'Link inválido ou vencido. Peça um novo na tela de entrar.'));
    }
  }
}
