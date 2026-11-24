import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { firstValueFrom } from 'rxjs';
import { API, LojaAdmin, mensagemDeErro } from '../../core/api';

type Acao = 'plano' | 'cortesia' | 'estender_teste' | 'isentar' | 'cobrar';

/** Administrador muda a assinatura de uma loja: plano, cortesia, mais dias de teste, isentar ou voltar a cobrar. */
@Component({
  selector: 'app-assinatura-admin-dialog',
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Assinatura de {{ loja.nome }}</h2>
    <mat-dialog-content class="campos">
      <p class="atual">Hoje: {{ loja.plano ?? 'sem plano' }} · {{ loja.situacaoAssinatura }}</p>
      <mat-form-field appearance="outline" subscriptSizing="dynamic">
        <mat-label>O que fazer</mat-label>
        <mat-select [ngModel]="acao()" (ngModelChange)="acao.set($event)">
          <mat-option value="plano">Trocar o plano</mat-option>
          <mat-option value="cortesia">Dar cortesia</mat-option>
          <mat-option value="estender_teste">Mais dias de teste grátis</mat-option>
          <mat-option value="isentar">Isentar (sem mensalidade e sem limites)</mat-option>
          <mat-option value="cobrar">Voltar à cobrança normal</mat-option>
        </mat-select>
      </mat-form-field>
      @if (acao() !== 'isentar' && acao() !== 'cobrar') {
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Plano</mat-label>
          <mat-select [ngModel]="plano()" (ngModelChange)="plano.set($event)">
            <mat-option value="Essencial">Essencial (R$ 297)</mat-option>
            <mat-option value="Profissional">Profissional (R$ 497)</mat-option>
            <mat-option value="Completo">Completo (R$ 797)</mat-option>
          </mat-select>
        </mat-form-field>
      }
      @if (acao() === 'estender_teste' || acao() === 'cobrar') {
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>{{ acao() === 'cobrar' ? 'Dias para pagar (se não tem nada pago)' : 'Dias a mais de teste' }}</mat-label>
          <input matInput type="number" min="1" max="90" [ngModel]="dias()" (ngModelChange)="dias.set($event)" />
        </mat-form-field>
      }
      @if (acao() === 'cortesia') {
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Cortesia até (vazio = sem fim)</mat-label>
          <input matInput type="date" [ngModel]="ate()" (ngModelChange)="ate.set($event)" />
        </mat-form-field>
      }
      @if (acao() === 'cortesia' || acao() === 'isentar') {
        <mat-form-field appearance="outline" subscriptSizing="dynamic">
          <mat-label>Motivo (opcional)</mat-label>
          <input matInput [ngModel]="motivo()" (ngModelChange)="motivo.set($event)" maxlength="300" />
        </mat-form-field>
      }
      @if (acao() === 'plano' && loja.mpStatus === 'authorized') {
        <p class="aviso">A loja paga pelo Mercado Pago: a próxima cobrança já vem com o valor do plano novo.</p>
      }
      @if (erro()) {
        <p class="erro">{{ erro() }}</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Voltar</button>
      <button mat-flat-button (click)="salvar()" [disabled]="salvando()">Salvar</button>
    </mat-dialog-actions>
  `,
  styles: `
    .campos { display: grid; gap: 12px; min-width: min(380px, 80vw); }
    .atual { margin: 0; color: var(--mat-sys-on-surface-variant); }
    .aviso { margin: 0; color: var(--sv-alerta); }
    .erro { margin: 0; color: var(--mat-sys-error); }
  `,
})
export class AssinaturaAdminDialog {
  protected readonly loja = inject<LojaAdmin>(MAT_DIALOG_DATA);
  private readonly http = inject(HttpClient);
  private readonly ref = inject<MatDialogRef<AssinaturaAdminDialog, boolean>>(MatDialogRef);

  protected readonly acao = signal<Acao>('plano');
  protected readonly plano = signal(this.loja.plano ?? 'Profissional');
  protected readonly dias = signal(7);
  protected readonly ate = signal('');
  protected readonly motivo = signal('');
  protected readonly salvando = signal(false);
  protected readonly erro = signal<string | null>(null);

  protected async salvar(): Promise<void> {
    this.salvando.set(true);
    this.erro.set(null);
    try {
      // A data da cortesia vale até o fim do dia escolhido (horário de Brasília).
      const ate = this.ate() ? new Date(`${this.ate()}T23:59:00-03:00`).toISOString() : null;
      await firstValueFrom(
        this.http.post(`${API}/admin/lojas/${this.loja.id}/assinatura`, {
          acao: this.acao(),
          plano: this.plano(),
          dias: Number(this.dias()),
          ate,
          motivo: this.motivo() || null,
        }),
      );
      this.ref.close(true);
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
    } finally {
      this.salvando.set(false);
    }
  }
}
