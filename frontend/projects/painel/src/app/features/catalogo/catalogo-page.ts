import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { API, Especie } from '../../core/api';
import { Auth } from '../../core/auth';
import { EspecieDialog } from './especie-dialog';

type Filtro = 'todas' | 'disponiveis' | 'esgotadas';

@Component({
  selector: 'app-catalogo-page',
  imports: [CurrencyPipe, MatButtonModule, MatChipsModule, MatIconModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './catalogo-page.html',
  styleUrl: './catalogo-page.scss',
})
export class CatalogoPage {
  protected readonly auth = inject(Auth);
  private readonly dialog = inject(MatDialog);

  protected readonly especies = httpResource<Especie[]>(() => `${API}/especies`);
  protected readonly busca = signal('');
  protected readonly filtro = signal<Filtro>('todas');

  protected readonly contagem = computed(() => {
    const lista = this.especies.value() ?? [];
    return {
      todas: lista.length,
      disponiveis: lista.filter((e) => e.disponivel > 0).length,
      esgotadas: lista.filter((e) => e.disponivel === 0).length,
    };
  });

  protected readonly visiveis = computed(() => {
    const termo = normalizar(this.busca());
    return (this.especies.value() ?? [])
      .filter((e) => {
        if (this.filtro() === 'disponiveis') return e.disponivel > 0;
        if (this.filtro() === 'esgotadas') return e.disponivel === 0;
        return true;
      })
      .filter((e) => !termo || normalizar(`${e.nomePopular} ${e.nomeCientifico ?? ''}`).includes(termo));
  });

  protected abrir(especie?: Especie): void {
    this.dialog
      .open(EspecieDialog, { data: especie ?? null, width: '520px', maxWidth: '95vw', autoFocus: 'first-tabbable' })
      .afterClosed()
      .subscribe((salvou) => salvou && this.especies.reload());
  }
}

function normalizar(texto: string): string {
  return texto
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .trim();
}
