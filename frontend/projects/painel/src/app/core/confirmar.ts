import { ChangeDetectionStrategy, Component, Injectable, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { firstValueFrom } from 'rxjs';

export interface OpcoesConfirmar {
  titulo?: string;
  /** Texto do botão de confirmar (padrão "Confirmar"). */
  botao?: string;
  /** Texto do botão de voltar (padrão "Voltar"). */
  voltar?: string;
  /** Ação que apaga ou desfaz algo: botão em vermelho. */
  perigo?: boolean;
  /** Mostra um campo de texto opcional (ex.: motivo) e devolve o que foi digitado. */
  campo?: string;
}

interface Dados extends OpcoesConfirmar {
  texto: string;
}

/** Janela de confirmação do sistema, no lugar do confirm()/prompt() do navegador. */
@Component({
  selector: 'app-confirmar-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (d.titulo) {
      <h2 mat-dialog-title>{{ d.titulo }}</h2>
    }
    <mat-dialog-content>
      <p class="texto">{{ d.texto }}</p>
      @if (d.campo) {
        <mat-form-field appearance="outline" class="campo">
          <mat-label>{{ d.campo }}</mat-label>
          <input matInput [ngModel]="valor()" (ngModelChange)="valor.set($event)" (keydown.enter)="confirmar()" />
        </mat-form-field>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button [mat-dialog-close]="undefined">{{ d.voltar ?? 'Voltar' }}</button>
      <button mat-flat-button [class.perigo]="d.perigo" (click)="confirmar()">
        {{ d.botao ?? 'Confirmar' }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .texto {
      margin: 0;
      white-space: pre-line;
    }
    .campo {
      width: 100%;
      margin-top: 16px;
    }
    .perigo {
      --mat-button-filled-container-color: var(--mat-sys-error);
      --mat-button-filled-label-text-color: var(--mat-sys-on-error);
    }
  `,
})
export class ConfirmarDialog {
  protected readonly d = inject<Dados>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<ConfirmarDialog, { valor: string }>>(MatDialogRef);
  protected readonly valor = signal('');

  protected confirmar(): void {
    this.ref.close({ valor: this.valor() });
  }
}

@Injectable({ providedIn: 'root' })
export class Confirmar {
  private readonly dialog = inject(MatDialog);

  /** true se a pessoa confirmou. */
  async perguntar(texto: string, opcoes: OpcoesConfirmar = {}): Promise<boolean> {
    return (await this.abrir(texto, opcoes)) !== null;
  }

  /** Texto digitado no campo (pode ser vazio), ou null se voltou. */
  async pedirTexto(texto: string, campo: string, opcoes: OpcoesConfirmar = {}): Promise<string | null> {
    return this.abrir(texto, { ...opcoes, campo });
  }

  private async abrir(texto: string, opcoes: OpcoesConfirmar): Promise<string | null> {
    const ref = this.dialog.open<ConfirmarDialog, Dados, { valor: string }>(ConfirmarDialog, {
      data: { texto, ...opcoes },
      width: '440px',
      maxWidth: '95vw',
    });
    const r = await firstValueFrom(ref.afterClosed());
    return r ? r.valor : null;
  }
}
