import { ChangeDetectionStrategy, Component } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { MatTableModule } from '@angular/material/table';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { API, Especie } from '../../core/api';

@Component({
  selector: 'app-catalogo-page',
  imports: [CurrencyPipe, MatTableModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Catálogo</h1>
    @if (especies.isLoading()) {
      <mat-progress-bar mode="indeterminate" />
    } @else if (especies.error()) {
      <p class="erro">Não foi possível carregar as espécies. A API está rodando?</p>
    } @else {
      <table mat-table [dataSource]="especies.value() ?? []">
        <ng-container matColumnDef="nome">
          <th mat-header-cell *matHeaderCellDef>Nome popular</th>
          <td mat-cell *matCellDef="let e">{{ e.nomePopular }}</td>
        </ng-container>
        <ng-container matColumnDef="cientifico">
          <th mat-header-cell *matHeaderCellDef>Nome científico</th>
          <td mat-cell *matCellDef="let e"><em>{{ e.nomeCientifico }}</em></td>
        </ng-container>
        <ng-container matColumnDef="tamanho">
          <th mat-header-cell *matHeaderCellDef>Tamanho</th>
          <td mat-cell *matCellDef="let e">{{ e.tamanhoCm ? e.tamanhoCm + ' cm' : '' }}</td>
        </ng-container>
        <ng-container matColumnDef="preco">
          <th mat-header-cell *matHeaderCellDef>Preço</th>
          <td mat-cell *matCellDef="let e">{{ e.preco | currency: 'BRL' }}</td>
        </ng-container>
        <tr mat-header-row *matHeaderRowDef="colunas"></tr>
        <tr mat-row *matRowDef="let row; columns: colunas"></tr>
      </table>
    }
  `,
  styles: `
    .erro { color: var(--mat-sys-error); }
  `,
})
export class CatalogoPage {
  protected readonly colunas = ['nome', 'cientifico', 'tamanho', 'preco'];
  protected readonly especies = httpResource<Especie[]>(() => `${API}/especies`);
}
