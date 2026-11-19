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
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { API, Especie, mensagemDeErro } from '../../core/api';
import { Confirmar } from '../../core/confirmar';
import { Auth } from '../../core/auth';
import { EspecieDialog } from './especie-dialog';
import { Termos } from '../../core/termos';

/** O catálogo mostra o que tem no estoque; "sem estoque" fica à parte, só para editar ou excluir. */
type Filtro = 'disponiveis' | 'esgotadas';

@Component({
  selector: 'app-catalogo-page',
  imports: [CurrencyPipe, MatButtonModule, MatChipsModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './catalogo-page.html',
  styleUrl: './catalogo-page.scss',
})
export class CatalogoPage {
  protected readonly tx = inject(Termos);
  private readonly confirmar = inject(Confirmar);
  protected readonly auth = inject(Auth);
  private readonly dialog = inject(MatDialog);
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);

  protected readonly especies = httpResource<Especie[]>(() => `${API}/especies`);
  protected readonly busca = signal('');
  protected readonly filtro = signal<Filtro>('disponiveis');
  protected readonly enviandoFoto = signal<string | null>(null);

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

  /** Reduz a foto no navegador (lado maior até 1000 px, JPEG) antes de enviar: fica leve para o banco e a vitrine. */
  protected async enviarFoto(especie: Especie, evento: Event): Promise<void> {
    const input = evento.target as HTMLInputElement;
    const arquivo = input.files?.[0];
    input.value = '';
    if (!arquivo) return;

    this.enviandoFoto.set(especie.id);
    try {
      const dataUrl = await reduzirFoto(arquivo, 1000, 0.82);
      await firstValueFrom(this.http.put(`${API}/especies/${especie.id}/foto`, { dataUrl }));
      this.especies.reload();
      this.snack.open('Foto salva. Ela já aparece na vitrine.', undefined, { duration: 3000 });
    } catch (e) {
      this.snack.open(e instanceof Error ? e.message : mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.enviandoFoto.set(null);
    }
  }

  protected async removerFoto(especie: Especie): Promise<void> {
    if (!await this.confirmar.perguntar(`Remover a foto de "${especie.nomePopular}"?`, { botao: 'Remover', perigo: true })) return;
    try {
      await firstValueFrom(this.http.delete(`${API}/especies/${especie.id}/foto`));
      this.especies.reload();
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    }
  }

  protected async excluir(especie: Especie): Promise<void> {
    if (!await this.confirmar.perguntar('Ela sai do catálogo e da entrada de lote.', { titulo: `Excluir "${especie.nomePopular}"?`, botao: 'Excluir', perigo: true })) return;
    try {
      await firstValueFrom(this.http.delete(`${API}/especies/${especie.id}`));
      this.especies.reload();
      this.snack.open(`${this.tx.produto()} ${this.tx.v().produtoFeminino ? 'excluída' : 'excluído'}.`, undefined, { duration: 3000 });
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

async function reduzirFoto(arquivo: File, ladoMaximo: number, qualidade: number): Promise<string> {
  if (!arquivo.type.startsWith('image/')) throw new Error('Escolha um arquivo de imagem.');
  const bitmap = await createImageBitmap(arquivo).catch(() => {
    throw new Error('Não foi possível abrir essa imagem. Tente uma foto JPG ou PNG.');
  });
  const escala = Math.min(1, ladoMaximo / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(bitmap.width * escala);
  canvas.height = Math.round(bitmap.height * escala);
  canvas.getContext('2d')!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  return canvas.toDataURL('image/jpeg', qualidade);
}
