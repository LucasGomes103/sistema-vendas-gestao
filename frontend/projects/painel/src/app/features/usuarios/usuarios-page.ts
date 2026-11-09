import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, ROTULO_PAPEL, UsuarioEquipe, mensagemDeErro } from '../../core/api';
import { Auth } from '../../core/auth';
import { SenhaDialog } from './senha-dialog';
import { UsuarioDialog } from './usuario-dialog';

/** Equipe da loja: o dono cria, edita, define senha nova e exclui quem acessa o painel. */
@Component({
  selector: 'app-usuarios-page',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './usuarios-page.html',
  styleUrl: './usuarios-page.scss',
})
export class UsuariosPage {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);
  private readonly auth = inject(Auth);

  protected readonly usuarios = httpResource<UsuarioEquipe[]>(() => `${API}/usuarios`);
  protected readonly rotulo = ROTULO_PAPEL;

  protected abrir(usuario?: UsuarioEquipe): void {
    this.dialog
      .open(UsuarioDialog, { data: usuario ?? null, width: '480px', maxWidth: '95vw', autoFocus: 'first-tabbable' })
      .afterClosed()
      .subscribe((salvo) => {
        if (!salvo) return;
        this.usuarios.reload();
        if (salvo.ehVoce) void this.auth.recarregar();
        this.snack.open(usuario ? 'Usuário atualizado.' : `${salvo.nome} já pode entrar no painel.`, undefined, { duration: 3000 });
      });
  }

  protected definirSenha(usuario: UsuarioEquipe): void {
    this.dialog.open(SenhaDialog, { data: usuario, width: '420px', maxWidth: '95vw' });
  }

  protected trocarMinhaSenha(): void {
    this.dialog.open(SenhaDialog, { data: null, width: '420px', maxWidth: '95vw' });
  }

  protected async excluir(usuario: UsuarioEquipe): Promise<void> {
    if (!confirm(`Excluir ${usuario.nome}? A pessoa perde o acesso na hora e isso não pode ser desfeito. Para só bloquear, edite e desligue "Pode entrar no painel".`))
      return;
    try {
      await firstValueFrom(this.http.delete(`${API}/usuarios/${usuario.id}`));
      this.usuarios.reload();
      this.snack.open('Usuário excluído.', undefined, { duration: 3000 });
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    }
  }
}
