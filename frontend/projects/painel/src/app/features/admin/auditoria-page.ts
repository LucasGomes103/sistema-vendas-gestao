import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { API, AuditoriaAdmin } from '../../core/api';
import { AdminAbas, rotuloAcao } from './admin-comum';

/** Tudo o que os administradores da plataforma fizeram (últimos 500 registros). */
@Component({
  selector: 'app-auditoria-page',
  imports: [DatePipe, FormsModule, RouterLink, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, AdminAbas],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="sv-cabecalho-pagina">
      <div>
        <h1>Auditoria</h1>
        <p>Quem fez o quê nas lojas: editar, suspender, ativar, excluir e entrar como suporte.</p>
      </div>
      <mat-form-field appearance="outline" subscriptSizing="dynamic" class="busca">
        <mat-icon matPrefix>search</mat-icon>
        <mat-label>Buscar loja, e-mail ou ação</mat-label>
        <input matInput [ngModel]="busca()" (ngModelChange)="busca.set($event)" />
      </mat-form-field>
    </header>
    <app-admin-abas />

    @if (registros.isLoading() && !registros.hasValue()) {
      <mat-progress-bar mode="indeterminate" />
    }

    <section class="sv-card lista">
      @for (a of filtrados(); track a.id) {
        @let r = rotulo(a);
        <article class="linha">
          <mat-icon>{{ r.icone }}</mat-icon>
          <div>
            <p>
              <strong>{{ r.rotulo }}</strong>
              @if (a.lojaSlug) {
                ·
                @if (a.lojaId && a.acao !== 'excluir') {
                  <a [routerLink]="['/admin/lojas', a.lojaId]">/{{ a.lojaSlug }}</a>
                } @else {
                  /{{ a.lojaSlug }}
                }
              }
            </p>
            @if (a.detalhe) {
              <p class="detalhe">{{ a.detalhe }}</p>
            }
          </div>
          <small>{{ a.adminEmail }}<br />{{ a.criadoEm | date: 'dd/MM/yyyy HH:mm' }}</small>
        </article>
      } @empty {
        @if (registros.hasValue()) {
          <p class="vazio">Nada registrado ainda.</p>
        }
      }
    </section>
  `,
  styles: `
    .busca {
      width: min(360px, 100%);
    }
    .lista {
      display: grid;
      padding: 4px 20px;
    }
    .linha {
      display: grid;
      grid-template-columns: auto minmax(0, 1fr) auto;
      align-items: start;
      gap: 12px;
      padding: 12px 0;
      border-bottom: 1px solid var(--mat-sys-outline-variant);

      &:last-child {
        border-bottom: 0;
      }
      mat-icon {
        color: var(--mat-sys-on-surface-variant);
      }
      p {
        margin: 0;
        font: var(--mat-sys-body-medium);
        overflow-wrap: anywhere;
      }
      .detalhe,
      small {
        color: var(--mat-sys-on-surface-variant);
        font: var(--mat-sys-body-small);
      }
      small {
        text-align: right;
      }
    }
    .paradas a,
    .linha a {
      color: var(--mat-sys-primary);
    }
    .vazio {
      color: var(--mat-sys-on-surface-variant);
    }
    @media (max-width: 599px) {
      .linha {
        grid-template-columns: auto minmax(0, 1fr);

        small {
          grid-column: 2;
          text-align: left;
        }
      }
    }
  `,
})
export class AuditoriaPage {
  protected readonly registros = httpResource<AuditoriaAdmin[]>(() => `${API}/admin/auditoria?limite=500`);
  protected readonly busca = signal('');
  protected rotulo = rotuloAcao;

  protected readonly filtrados = computed(() => {
    const termo = this.busca().trim().toLowerCase();
    const todos = this.registros.value() ?? [];
    if (!termo) return todos;
    return todos.filter((a) =>
      [a.lojaSlug ?? '', a.adminEmail, a.detalhe ?? '', rotuloAcao(a).rotulo].some((t) => t.toLowerCase().includes(termo)),
    );
  });
}
