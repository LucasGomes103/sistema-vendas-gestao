import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { API, Entrega, EntregaAcao, mensagemDeErro } from '../../core/api';
import { Auth } from '../../core/auth';
import { atualizarACada } from '../../core/atualizar';
import { AgendarDialog } from './agendar-dialog';

interface Grupo {
  titulo: string;
  icone: string;
  itens: Entrega[];
}

function isoLocal(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/**
 * Agenda de entregas e retiradas dos pedidos pagos. O vendedor agenda dia, horário e quem leva;
 * o entregador vê as do dia, abre o endereço no mapa e marca "saiu" e "entregue".
 */
@Component({
  selector: 'app-entregas-page',
  imports: [CurrencyPipe, DatePipe, RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="sv-cabecalho-pagina">
      <div>
        <h1>Entregas</h1>
        <p>
          @if (auth.ehEntregador()) {
            Suas entregas de hoje. Toque no endereço para abrir o mapa.
          } @else {
            Pedidos pagos entram aqui. Agende o dia, avise o cliente e marque a saída e a entrega.
          }
        </p>
      </div>
    </header>

    @if (entregas.isLoading() && !entregas.hasValue()) {
      <mat-progress-bar mode="indeterminate" />
    } @else if (entregas.error()) {
      <div class="sv-vazio">
        <mat-icon>cloud_off</mat-icon>
        <p>Não foi possível carregar as entregas.</p>
        <button mat-stroked-button (click)="entregas.reload()">Tentar de novo</button>
      </div>
    } @else {
      @for (g of grupos(); track g.titulo) {
        <section class="grupo">
          <h2><mat-icon>{{ g.icone }}</mat-icon> {{ g.titulo }} <span class="conta">{{ g.itens.length }}</span></h2>
          @for (e of g.itens; track e.id) {
            <article class="sv-card entrega" [class.atrasada]="atrasada(e)" [class.feita]="e.status === 'Entregue'">
              <header>
                <strong>#{{ e.numeroPedido }}</strong>
                <span class="sv-selo" [class.ok]="e.status === 'Entregue'" [class.alerta]="e.status === 'SaiuParaEntrega' || atrasada(e)" [class.info]="e.status === 'Agendada' || e.status === 'AAgendar'">
                  <mat-icon>{{ e.tipo === 'Entrega' ? 'local_shipping' : 'storefront' }}</mat-icon>{{ rotulo(e) }}
                </span>
                @if (e.conversaId && !auth.ehEntregador()) {
                  <a class="cliente" [routerLink]="['/conversas', e.conversaId]" matTooltip="Abrir conversa">{{ e.cliente }}</a>
                } @else {
                  <span class="cliente">{{ e.cliente }}</span>
                }
                <span class="total">{{ e.total | currency: 'BRL' }}</span>
              </header>
              <p class="itens">{{ e.itens }}</p>
              @if (e.tipo === 'Entrega' && e.endereco) {
                <a class="detalhe mapa" [href]="mapa(e)" target="_blank" rel="noopener"><mat-icon>place</mat-icon> {{ e.endereco }}{{ e.cep ? ' · CEP ' + cep(e.cep) : '' }}</a>
              }
              <p class="detalhe">
                <mat-icon>event</mat-icon>
                @if (e.status === 'Entregue') {
                  {{ e.tipo === 'Entrega' ? 'Entregue' : 'Retirado' }} em {{ e.entregueEm | date: "dd/MM 'às' HH:mm" }}
                } @else if (e.data) {
                  {{ dia(e.data) | date: "EEE, dd/MM" }}{{ e.janela ? ' · ' + e.janela : '' }}
                  @if (e.saiuEm) {
                    · saiu {{ e.saiuEm | date: 'HH:mm' }}
                  }
                } @else {
                  Combinar dia com o cliente
                }
                @if (e.entregador) {
                  · {{ e.entregador }}
                }
              </p>
              @if (e.observacao) {
                <p class="detalhe"><mat-icon>notes</mat-icon> {{ e.observacao }}</p>
              }
              <a class="detalhe" [href]="'tel:+' + e.telefone"><mat-icon>call</mat-icon> {{ e.telefone }}</a>

              @if (e.status !== 'Entregue') {
                <footer>
                  @if (!auth.ehEntregador()) {
                    <button mat-stroked-button [disabled]="ocupado()" (click)="agendar(e)"><mat-icon>event</mat-icon> {{ e.data ? 'Remarcar' : 'Agendar' }}</button>
                  }
                  @if (e.tipo === 'Entrega' && e.status !== 'SaiuParaEntrega') {
                    <button mat-stroked-button [disabled]="ocupado()" (click)="acao(e, 'saiu')"><mat-icon>local_shipping</mat-icon> Saiu para entrega</button>
                  }
                  <button mat-flat-button [disabled]="ocupado()" (click)="acao(e, 'entregue')">
                    <mat-icon>check</mat-icon> {{ e.tipo === 'Entrega' ? 'Entregue' : 'Retirado' }}
                  </button>
                </footer>
              }
            </article>
          }
        </section>
      } @empty {
        <div class="sv-vazio">
          <mat-icon>local_shipping</mat-icon>
          <p>{{ auth.ehEntregador() ? 'Nenhuma entrega para você hoje.' : 'Nenhuma entrega. Pedidos pagos aparecem aqui.' }}</p>
        </div>
      }
    }
  `,
  styles: `
    .grupo {
      display: grid;
      gap: 10px;
      max-width: 820px;
      margin-bottom: 24px;

      h2 {
        display: flex;
        align-items: center;
        gap: 8px;
        margin: 0;
        font: var(--mat-sys-title-medium);

        mat-icon {
          color: var(--mat-sys-primary);
        }
        .conta {
          color: var(--mat-sys-on-surface-variant);
          font: var(--mat-sys-label-large);
        }
      }
    }
    .entrega {
      display: grid;
      gap: 4px;
      padding: 12px 16px;

      &.atrasada {
        border-color: var(--sv-alerta-borda);
      }
      &.feita {
        opacity: 0.75;
      }
      header {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 8px;

        strong {
          font: var(--mat-sys-title-medium);
        }
        .cliente {
          color: inherit;
          font: var(--mat-sys-title-small);
        }
        .total {
          margin-left: auto;
          font: var(--mat-sys-title-small);
        }
      }
      p {
        margin: 0;
      }
      .detalhe {
        display: flex;
        align-items: center;
        gap: 6px;
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-body-small);
        text-decoration: none;

        mat-icon {
          flex: none;
          width: 16px;
          height: 16px;
          font-size: 16px;
        }
        &.mapa {
          color: var(--mat-sys-primary);
        }
      }
      footer {
        display: flex;
        flex-wrap: wrap;
        gap: 8px;
        margin-top: 8px;
      }
    }
  `,
})
export class EntregasPage {
  protected readonly auth = inject(Auth);
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);

  protected readonly entregas = httpResource<Entrega[]>(() => `${API}/entregas`);
  protected readonly ocupado = signal(false);
  private readonly hoje = isoLocal(new Date());

  protected readonly grupos = computed<Grupo[]>(() => {
    const lista = this.entregas.value() ?? [];
    const hoje = this.hoje;
    const abertas = lista.filter((e) => e.status !== 'Entregue');
    const grupos: Grupo[] = [
      { titulo: 'A agendar', icone: 'pending_actions', itens: abertas.filter((e) => !e.data) },
      { titulo: 'Atrasadas', icone: 'warning', itens: abertas.filter((e) => e.data && e.data < hoje) },
      { titulo: 'Hoje', icone: 'today', itens: abertas.filter((e) => e.data === hoje) },
      { titulo: 'Próximos dias', icone: 'date_range', itens: abertas.filter((e) => e.data && e.data > hoje) },
      {
        titulo: 'Feitas nos últimos 7 dias',
        icone: 'task_alt',
        itens: lista.filter((e) => e.status === 'Entregue').sort((a, b) => (b.entregueEm ?? '').localeCompare(a.entregueEm ?? '')),
      },
    ];
    return grupos.filter((g) => g.itens.length > 0);
  });

  constructor() {
    atualizarACada(15000, () => this.entregas.reload());
  }

  protected atrasada(e: Entrega): boolean {
    return e.status !== 'Entregue' && !!e.data && e.data < this.hoje;
  }

  protected rotulo(e: Entrega): string {
    switch (e.status) {
      case 'AAgendar':
        return e.tipo === 'Entrega' ? 'Entrega a agendar' : 'Retirada a agendar';
      case 'Agendada':
        return e.tipo === 'Entrega' ? 'Entrega agendada' : 'Retirada agendada';
      case 'SaiuParaEntrega':
        return 'Saiu para entrega';
      default:
        return e.tipo === 'Entrega' ? 'Entregue' : 'Retirado';
    }
  }

  protected dia(iso: string): Date {
    return new Date(`${iso}T12:00:00`);
  }

  protected cep(c: string): string {
    return c.replace(/^(\d{5})(\d{3})$/, '$1-$2');
  }

  protected mapa(e: Entrega): string {
    return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent([e.endereco, e.cep ? this.cep(e.cep) : null].filter(Boolean).join(', '))}`;
  }

  protected async agendar(e: Entrega): Promise<void> {
    const r = await firstValueFrom(
      this.dialog.open<AgendarDialog, Entrega, EntregaAcao>(AgendarDialog, { data: e, width: '460px', maxWidth: '95vw' }).afterClosed(),
    );
    if (!r) return;
    this.avisar(r, 'Agendado.');
  }

  protected async acao(e: Entrega, tipo: 'saiu' | 'entregue'): Promise<void> {
    const avisarCliente = !!e.conversaId;
    this.ocupado.set(true);
    try {
      const r = await firstValueFrom(this.http.post<EntregaAcao>(`${API}/entregas/${e.id}/${tipo}`, { avisarCliente }));
      this.avisar(r, tipo === 'saiu' ? 'Marcado como saiu para entrega.' : e.tipo === 'Entrega' ? 'Entrega concluída: card em Entregue.' : 'Retirada concluída.');
    } catch (err) {
      this.snack.open(mensagemDeErro(err), 'OK', { duration: 6000 });
    } finally {
      this.ocupado.set(false);
    }
  }

  private avisar(r: EntregaAcao, sucesso: string): void {
    const texto = r.aviso ?? (r.mensagemEnviada ? `${sucesso} O cliente foi avisado no WhatsApp.` : sucesso);
    this.snack.open(texto, 'OK', { duration: r.aviso ? 9000 : 4000 });
    this.entregas.reload();
  }
}
