import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AlertasQuentes } from '../core/alertas';

/** Sino com os clientes quentes que ninguém assumiu ainda. */
@Component({
  selector: 'app-sino-alertas',
  imports: [MatBadgeModule, MatButtonModule, MatIconModule, MatMenuModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      mat-icon-button
      [matMenuTriggerFor]="menu"
      (menuOpened)="alertas.pedirPermissao()"
      [class.tem]="alertas.lista().length > 0"
      [class.urgente]="alertas.urgentes() > 0"
      [matBadge]="alertas.lista().length || null"
      [matBadgeColor]="alertas.urgentes() > 0 ? 'warn' : 'primary'"
      [matTooltip]="alertas.lista().length ? 'Clientes quentes sem dono' : 'Nenhum cliente esperando'"
      aria-label="Clientes quentes sem dono"
    >
      <mat-icon>{{ alertas.lista().length ? 'notifications_active' : 'notifications' }}</mat-icon>
    </button>

    <mat-menu #menu="matMenu" xPosition="after" class="sv-menu-alertas">
      <div class="titulo">Clientes quentes sem dono</div>
      @for (a of alertas.lista(); track a.conversaId) {
        <button mat-menu-item (click)="alertas.abrir(a)">
          <mat-icon [class.urgente]="a.nivel === 2">{{ a.nivel === 2 ? 'priority_high' : 'local_fire_department' }}</mat-icon>
          <span class="item">
            <strong>{{ a.cliente }}</strong>
            <small>há {{ tempo(alertas.minutos(a)) }}{{ a.motivo ? ' · ' + a.motivo : '' }}</small>
          </span>
        </button>
      } @empty {
        <p class="vazio">Ninguém esperando. Quando o robô chamar um vendedor, aparece aqui.</p>
      }
    </mat-menu>
  `,
  styles: `
    .tem mat-icon {
      color: var(--mat-sys-primary);
    }
    .urgente mat-icon {
      color: var(--mat-sys-error);
      animation: balanca 1.2s ease-in-out infinite;
    }
    @keyframes balanca {
      0%, 100% { transform: rotate(0); }
      20% { transform: rotate(-15deg); }
      40% { transform: rotate(12deg); }
      60% { transform: rotate(0); }
    }
    @media (prefers-reduced-motion: reduce) {
      .urgente mat-icon { animation: none; }
    }
    .titulo {
      padding: 8px 16px 4px;
      font: var(--mat-sys-title-small);
    }
    .item {
      display: flex;
      flex-direction: column;
      line-height: 1.25;
      small {
        color: var(--mat-sys-on-surface-variant);
        max-width: 260px;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
    }
    mat-icon.urgente {
      color: var(--mat-sys-error);
    }
    .vazio {
      max-width: 260px;
      padding: 4px 16px 12px;
      margin: 0;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
  `,
})
export class SinoAlertas {
  protected readonly alertas = inject(AlertasQuentes);

  protected tempo(min: number): string {
    return min < 60 ? `${min} min` : `${Math.floor(min / 60)} h ${min % 60} min`;
  }
}
