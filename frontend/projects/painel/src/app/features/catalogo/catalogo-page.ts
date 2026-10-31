import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { HttpClient } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, Especie, mensagemDeErro } from '../../core/api';
import { Auth } from '../../core/auth';
import { EspecieDialog } from './especie-dialog';

/** O catálogo mostra o que tem no estoque; "sem estoque" fica à parte, só para editar ou excluir. */
type Filtro = 'disponiveis' | 'esgotadas';

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
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);

  protected readonly especies = httpResource<Especie[]>(() => `${API}/especies`);
  protected readonly busca = signal('');
  protected readonly filtro = signal<Filtro>('disponiveis');

  protected readonly contagem = computed(() => {
    const lista = this.especies.value() ?? [];
    return {
      disponiveis: lista.filter((e) => e.disponivel > 0).length,
      esgotadas: lista.filter((e) => e.disponivel === 0).length,
    };
  });

  protected readonly visiveis = computed(() => {
    const termo = normalizar(this.busca());
    return (this.especies.value() ?? [])
      .filter((e) => {
        return this.filtro() === 'disponiveis' ? e.disponivel > 0 : e.disponivel === 0;
      })
      .filter((e) => !termo || normalizar(`${e.nomePopular} ${e.nomeCientifico ?? ''}`).includes(termo));
  });

  protected abrir(especie?: Especie): void {
    this.dialog
      .open(EspecieDialog, { data: especie ?? null, width: '520px', maxWidth: '95vw', autoFocus: 'first-tabbable' })
      .afterClosed()
      .subscribe((salvou) => salvou && this.especies.reload());
  }

  protected async excluir(especie: Especie): Promise<void> {
    if (!confirm(`Excluir "${especie.nomePopular}"? Ela sai do catálogo e da entrada de lote.`)) return;
    try {
      await firstValueFrom(this.http.delete(`${API}/especies/${especie.id}`));
      this.especies.reload();
      this.snack.open('Espécie excluída.', undefined, { duration: 3000 });
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    }
  }
}

function normalizar(texto: string): string {
  return texto
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .trim();
}
