import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { API, ConfigFunil, mensagemDeErro } from '../../core/api';
import { ConexaoWhatsAppComponent } from './conexao-whatsapp';
import { ConfigWhatsAppComponent } from './config-whatsapp';

/** Prazos do funil e respostas prontas. Mudar um prazo é editar aqui, não fazer deploy. */
@Component({
  selector: 'app-config-page',
  imports: [ConexaoWhatsAppComponent, ConfigWhatsAppComponent, ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './config-page.html',
  styleUrl: './config-page.scss',
})
export class ConfigPage {
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);

  protected readonly config = httpResource<ConfigFunil>(() => `${API}/config/funil`);
  protected readonly salvando = signal(false);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    horasAteFollowup: [2, [Validators.required, Validators.min(1), Validators.max(72)]],
    horasFollowup1: [20, [Validators.required, Validators.min(1), Validators.max(23)]],
    diasFollowup2: [3, [Validators.required, Validators.min(1), Validators.max(30)]],
    horasAtePerdido: [24, [Validators.required, Validators.min(1), Validators.max(168)]],
    textoFollowup1: ['', Validators.required],
    textoFollowup2: ['', Validators.required],
    textoFrete: ['', Validators.required],
    textoChamarVendedor: ['', Validators.required],
  });

  constructor() {
    effect(() => {
      const c = this.config.value();
      if (c) this.form.reset(c);
    });
  }

  protected async salvar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.salvando.set(true);
    try {
      const v = this.form.getRawValue();
      await firstValueFrom(
        this.http.put(`${API}/config/funil`, {
          ...v,
          horasAteFollowup: Number(v.horasAteFollowup),
          horasFollowup1: Number(v.horasFollowup1),
          diasFollowup2: Number(v.diasFollowup2),
          horasAtePerdido: Number(v.horasAtePerdido),
        }),
      );
      this.form.markAsPristine();
      this.snack.open('Configuração salva.', undefined, { duration: 3000 });
    } catch (e) {
      this.snack.open(mensagemDeErro(e), 'OK', { duration: 6000 });
    } finally {
      this.salvando.set(false);
    }
  }
}
