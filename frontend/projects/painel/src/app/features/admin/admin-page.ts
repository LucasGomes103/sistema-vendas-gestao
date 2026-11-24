import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, LojaAdmin, mensagemDeErro } from '../../core/api';
import { Confirmar } from '../../core/confirmar';
import { Auth } from '../../core/auth';
import { EditarLojaDialog } from './editar-loja-dialog';
import { RouterLink } from '@angular/router';
import { DecimalPipe } from '@angular/common';
import { AdminAbas, parada, tempoDesde } from './admin-comum';

const STATUS: Record<LojaAdmin['status'], { rotulo: string; classe: string; icone: string }> = {
  Ativa: { rotulo: 'Ativa', classe: 'ok', icone: 'check_circle' },
  AguardandoConfirmacao: { rotulo: 'Aguardando e-mail', classe: 'info', icone: 'mark_email_unread' },
  Suspensa: { rotulo: 'Suspensa', classe: 'alerta', icone: 'block' },
};

/** Lojas da plataforma: todas as lojas, uso dos últimos 30 dias, detalhe, ver como a loja, suspender e ativar. */
@Component({
  selector: 'app-admin-page',
  imports: [CurrencyPipe, DatePipe, DecimalPipe, RouterLink, AdminAbas, FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="sv-cabecalho-pagina">
      <div>
        <h1>Lojas da plataforma</h1>
        <p>{{ resumo() }}</p>
      </div>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="busca">
        <mat-icon matPrefix>search</mat-icon>
        <mat-label>Buscar loja, endereço ou e-mail</mat-label>
        <input matInput [ngModel]="busca()" (ngModelChange)="busca.set($event)" />
      </mat-form-field>
    </header>
    <app-admin-abas />

    @if (lojas.isLoading() && !lojas.hasValue()) {
      <mat-progress-bar mode="indeterminate" />
    }

    <div class="lista">
      @for (l of filtradas(); track l.id) {
        @let s = status[l.status];
        <article class="sv-card loja">
          <div class="topo">
            <div>
              <h2><a [routerLink]="['/admin/lojas', l.id]">{{ l.nome }}</a></h2>
              <p class="sub">/{{ l.slug }} · {{ l.segmento || 'sem tipo' }} · desde {{ l.criadoEm | date: 'dd/MM/yyyy' }}</p>
            </div>
            <span class="selos">
              @if (estaParada(l)) {
                <span class="sv-selo alerta"><mat-icon>bedtime</mat-icon>Parada</span>
              }
              <span class="sv-selo {{ s.classe }}"><mat-icon>{{ s.icone }}</mat-icon>{{ s.rotulo }}</span>
            </span>
          </div>
          <p class="dono">
            <mat-icon>person</mat-icon> {{ l.dono ?? '—' }} · {{ l.emailDono ?? '—' }}
            @if (l.lojaOriginal) {
              <span class="sv-selo info"><mat-icon>star</mat-icon>Loja original</span>
            }
          </p>
          @if (l.motivoSuspensao) {
            <p class="motivo"><mat-icon>info</mat-icon> {{ l.motivoSuspensao }}</p>
          }
          <dl class="numeros">
            <div><dt>Conversas (30 dias)</dt><dd>{{ l.conversas30Dias }}</dd></div>
            <div><dt>Pedidos (30 dias)</dt><dd>{{ l.pedidos30Dias }}</dd></div>
            <div><dt>Vendido (30 dias)</dt><dd>{{ l.vendido30Dias | currency: 'BRL' }}</dd></div>
            <div><dt>Produtos</dt><dd>{{ l.produtos }}</dd></div>
            <div><dt>Usuários</dt><dd>{{ l.usuarios }}</dd></div>
            <div><dt>Última mensagem</dt><dd>{{ l.ultimaMensagemEm ? (l.ultimaMensagemEm | date: 'dd/MM HH:mm') : '—' }}</dd></div>
            <div><dt>Último acesso</dt><dd>{{ tempo(l.ultimoAcessoEm) }}</dd></div>
            <div><dt>Respostas do robô (30 dias)</dt><dd>{{ l.respostasRobo30Dias | number }}</dd></div>
          </dl>
          <div class="rodape">
            <span class="integracoes">
              <span class="sv-selo {{ l.whatsAppProprio ? 'ok' : 'info' }}"><mat-icon>chat</mat-icon>WhatsApp {{ l.whatsAppProprio ? 'próprio' : 'não conectado' }}</span>
              <span class="sv-selo {{ l.mercadoPagoProprio ? 'ok' : 'info' }}"><mat-icon>credit_card</mat-icon>Mercado Pago {{ l.mercadoPagoProprio ? 'próprio' : 'não conectado' }}</span>
            </span>
            <span class="acoes">
              <a mat-stroked-button [routerLink]="['/admin/lojas', l.id]"><mat-icon>insights</mat-icon> Detalhes</a>
              @if (l.id !== minhaLoja()) {
                <button mat-stroked-button (click)="verComo(l)" [disabled]="ocupado()"><mat-icon>visibility</mat-icon> Ver como a loja</button>
              }
              <button mat-stroked-button (click)="editar(l)" [disabled]="ocupado()"><mat-icon>edit</mat-icon> Editar</button>
              @if (l.status === 'Suspensa') {
                <button mat-flat-button (click)="ativar(l, 'Reativar')" [disabled]="ocupado()"><mat-icon>play_arrow</mat-icon> Reativar</button>
              } @else {
                @if (l.status === 'AguardandoConfirmacao') {
                  <button mat-stroked-button (click)="ativar(l, 'Ativar sem e-mail')" [disabled]="ocupado()"><mat-icon>verified</mat-icon> Ativar sem e-mail</button>
                }
                @if (l.id !== minhaLoja()) {
                  <button mat-stroked-button class="perigo" (click)="suspender(l)" [disabled]="ocupado()"><mat-icon>block</mat-icon> Suspender</button>
                }
              }
              @if (l.id !== minhaLoja() && !l.lojaOriginal) {
                <button mat-stroked-button class="perigo" (click)="excluir(l)" [disabled]="ocupado()"><mat-icon>delete_forever</mat-icon> Excluir</button>
              }
            </span>
          </div>
        </article>
      } @empty {
        @if (lojas.hasValue()) {
          <p class="vazio">Nenhuma loja encontrada.</p>
        }
      }
    </div>
  `,
  styles: `
    .busca {
      width: min(360px, 100%);
    }
    .lista {
      display: grid;
      gap: 12px;
    }
    .loja {
      display: grid;
      gap: 8px;
      padding: 16px 20px;
    }
    .topo {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 12px;
      flex-wrap: wrap;

      h2 {
        margin: 0;
        font: var(--mat-sys-title-medium);
        font-weight: 600;

        a {
          color: inherit;
          text-decoration: none;
        }
        a:hover {
          text-decoration: underline;
        }
      }
    }
    .selos {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }
    .sub,
    .dono,
    .motivo {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 6px;
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
      overflow-wrap: anywhere;

      mat-icon {
        font-size: 18px;
        width: 18px;
        height: 18px;
      }
    }
    .motivo {
      color: var(--sv-alerta);
    }
    .numeros {
      display: grid;
      grid-template-columns: repeat(8, minmax(0, 1fr));
      gap: 8px;
      margin: 4px 0;

      dt {
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-label-small);
      }
      dd {
        margin: 0;
        font: var(--mat-sys-title-small);
        font-weight: 600;
      }
    }
    .rodape {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 8px;
      flex-wrap: wrap;
    }
    .integracoes,
    .acoes {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }
    .perigo {
      color: var(--mat-sys-error);
    }
    .vazio {
      color: var(--mat-sys-on-surface-variant);
    }
    @media (max-width: 1099px) {
      .numeros {
        grid-template-columns: repeat(4, minmax(0, 1fr));
      }
    }
    @media (max-width: 839px) {
      .numeros {
        grid-template-columns: repeat(3, minmax(0, 1fr));
      }
    }
    @media (max-width: 479px) {
      .numeros {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }
  `,
})
export class AdminPage {
  private readonly http = inject(HttpClient);
  private readonly confirmar = inject(Confirmar);
  private readonly snack = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  protected readonly status = STATUS;
  private readonly auth = inject(Auth);
  protected readonly minhaLoja = computed(() => this.auth.usuario()?.lojaId);
  protected readonly lojas = httpResource<LojaAdmin[]>(() => `${API}/admin/lojas`);
  protected readonly busca = signal('');
  protected readonly ocupado = signal(false);

  protected readonly filtradas = computed(() => {
    const termo = this.busca().trim().toLowerCase();
    const todas = this.lojas.value() ?? [];
    if (!termo) return todas;
    return todas.filter((l) => [l.nome, l.slug, l.emailDono ?? '', l.segmento ?? ''].some((t) => t.toLowerCase().includes(termo)));
  });

  protected readonly resumo = computed(() => {
    const todas = this.lojas.value() ?? [];
    const ativas = todas.filter((l) => l.status === 'Ativa').length;
    const pendentes = todas.filter((l) => l.status === 'AguardandoConfirmacao').length;
    const suspensas = todas.filter((l) => l.status === 'Suspensa').length;
    return `${todas.length} loja(s): ${ativas} ativa(s), ${pendentes} aguardando e-mail, ${suspensas} suspensa(s)`;
  });

  protected tempo = tempoDesde;

  protected estaParada(l: LojaAdmin): boolean {
    return parada(l.status, l.criadoEm, l.ultimoAcessoEm);
  }

  protected async verComo(l: LojaAdmin): Promise<void> {
    const ok = await this.confirmar.perguntar(
      `Você vai ver o painel da ${l.nome} como o dono vê: conversas, pedidos, estoque e configuração. É só leitura, nada pode ser mudado, e a entrada fica registrada na auditoria.\n\nPara voltar, use o botão "Voltar para a minha loja" no topo.`,
      { titulo: 'Ver como a loja?', botao: 'Ver como a loja' },
    );
    if (!ok) return;
    this.ocupado.set(true);
    try {
      await this.auth.verComoLoja(l.id);
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
      this.ocupado.set(false);
    }
  }

  protected async suspender(l: LojaAdmin): Promise<void> {
    const motivo = await this.confirmar.pedirTexto(
      `Ninguém da ${l.nome} entra no painel (quem está dentro sai em até 1 minuto), a vitrine sai do ar e o robô para de responder. Dá para reativar depois, sem perder nada.`,
      'Motivo (opcional, aparece só aqui)',
      { titulo: 'Suspender a loja?', botao: 'Suspender', perigo: true },
    );
    if (motivo === null) return;
    await this.executar(`${API}/admin/lojas/${l.id}/suspender`, { motivo }, `${l.nome} suspensa.`);
  }

  protected async ativar(l: LojaAdmin, acao: string): Promise<void> {
    const texto =
      l.status === 'Suspensa'
        ? `A equipe da ${l.nome} volta a entrar, a vitrine volta ao ar e o robô volta a responder.`
        : `Ativa a ${l.nome} sem a confirmação do e-mail. Faça isso só se você conferiu que o e-mail é do dono.`;
    if (!(await this.confirmar.perguntar(texto, { titulo: `${acao}?`, botao: acao }))) return;
    await this.executar(`${API}/admin/lojas/${l.id}/ativar`, {}, `${l.nome} ativa.`);
  }

  protected async editar(l: LojaAdmin): Promise<void> {
    const ref = this.dialog.open(EditarLojaDialog, { data: l, width: '480px', maxWidth: '95vw' });
    if (await firstValueFrom(ref.afterClosed())) {
      this.lojas.reload();
      this.snack.open(`${l.nome} atualizada.`, undefined, { duration: 3000 });
    }
  }

  protected async excluir(l: LojaAdmin): Promise<void> {
    const digitado = await this.confirmar.pedirTexto(
      `Apaga a ${l.nome} e tudo dela: produtos, estoque, conversas, clientes, pedidos, caixa, usuários e as chaves de WhatsApp e Mercado Pago. Não tem como desfazer.\n\nSe só quer bloquear o acesso, use Suspender.\n\nPara confirmar, digite o endereço da loja: ${l.slug}`,
      'Endereço da loja',
      { titulo: 'Excluir a loja para sempre?', botao: 'Excluir', perigo: true },
    );
    if (digitado === null) return;
    if (digitado.trim() !== l.slug) {
      this.snack.open('O endereço digitado não confere. Nada foi apagado.', 'OK', { duration: 6000 });
      return;
    }
    await this.executar(`${API}/admin/lojas/${l.id}/excluir`, { confirmacao: digitado.trim() }, `${l.nome} excluída.`);
  }

  private async executar(url: string, corpo: object, ok: string): Promise<void> {
    this.ocupado.set(true);
    try {
      await firstValueFrom(this.http.post(url, corpo));
      this.lojas.reload();
      this.snack.open(ok, undefined, { duration: 3000 });
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.ocupado.set(false);
    }
  }
}
