import { ChangeDetectionStrategy, Component } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { API, Tanque } from '../../core/api';

@Component({
  selector: 'app-estoque-page',
  imports: [MatCardModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Estoque por tanque</h1>
    @if (tanques.isLoading()) {
      <mat-progress-bar mode="indeterminate" />
    } @else if (tanques.error()) {
      <p class="erro">Não foi possível carregar os tanques. A API está rodando?</p>
    } @else {
      <div class="grade">
        @for (t of tanques.value() ?? []; track t.id) {
          <mat-card appearance="outlined" [class.quarentena]="t.tipo === 'Quarentena'">
            <mat-card-header>
              <mat-card-title>{{ t.nome }}</mat-card-title>
              <mat-card-subtitle>{{ t.tipo }}{{ t.litros ? ' · ' + t.litros + ' L' : '' }}</mat-card-subtitle>
            </mat-card-header>
          </mat-card>
        } @empty {
          <p>Nenhum tanque cadastrado.</p>
        }
      </div>
    }
  `,
  styles: `
    .grade { display: grid; gap: 12px; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); }
    .quarentena { border-color: var(--mat-sys-tertiary); }
    .erro { color: var(--mat-sys-error); }
  `,
})
export class EstoquePage {
  protected readonly tanques = httpResource<Tanque[]>(() => `${API}/tanques`);
}
