import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { API, ConversaResumo, Mensagem, mensagemDeErro } from '../../core/api';
import { atualizarACada } from '../../core/atualizar';

/**
 * Conversas do WhatsApp: lista à esquerda e chat à direita no desktop;
 * no celular, uma coisa de cada vez.
 */
@Component({
  selector: 'app-conversas-page',
  imports: [DatePipe, RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './conversas-page.html',
  styleUrl: './conversas-page.scss',
})
export class ConversasPage {
  /** Conversa aberta, vinda da rota /conversas/:id. */
  readonly id = input<string>();

  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  protected readonly conversas = httpResource<ConversaResumo[]>(() => `${API}/conversas`);
  protected readonly mensagens = httpResource<Mensagem[]>(() => (this.id() ? `${API}/conversas/${this.id()}/mensagens` : undefined));
  protected readonly whatsApp = httpResource<{ simulado: boolean; simuladorDisponivel: boolean; phoneNumberId: string | null; atendente: string; provedor: 'Meta' | 'Local' }>(() => `${API}/conversas/status-whatsapp`);

  protected readonly aberta = computed(() => this.conversas.value()?.find((c) => c.id === this.id()));
  protected readonly texto = signal('');
  protected readonly enviando = signal(false);
  protected readonly erro = signal<string | null>(null);

  // Simulador de cliente (só em desenvolvimento; as respostas dessas conversas nunca saem pela Meta).
  protected readonly simulando = signal(false);
  protected readonly simTelefone = signal('11987654321');
  protected readonly simNome = signal('Cliente teste');
  protected readonly simTexto = signal('');

  constructor() {
    atualizarACada(4000, () => {
      this.conversas.reload();
      if (this.id()) this.mensagens.reload();
    });
  }

  protected async enviar(): Promise<void> {
    const texto = this.texto().trim();
    const id = this.id();
    if (!texto || !id || this.enviando()) return;

    this.enviando.set(true);
    this.erro.set(null);
    try {
      await firstValueFrom(this.http.post(`${API}/conversas/${id}/mensagens`, { texto }));
      this.texto.set('');
      this.mensagens.reload();
      this.conversas.reload();
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
    } finally {
      this.enviando.set(false);
    }
  }

  protected teclaEnviar(evento: KeyboardEvent): void {
    if (evento.key === 'Enter' && !evento.shiftKey) {
      evento.preventDefault();
      void this.enviar();
    }
  }

  protected async simular(): Promise<void> {
    const texto = this.simTexto().trim();
    if (!texto) return;
    const aberta = this.aberta();
    await firstValueFrom(
      this.http.post(`${API}/dev/simular-mensagem`, {
        telefone: aberta?.telefone ?? this.simTelefone(),
        nome: aberta?.cliente ?? this.simNome(),
        texto,
      }),
    );
    this.simTexto.set('');
    // O worker processa a fila em até ~2 s.
    setTimeout(() => {
      this.conversas.reload();
      this.mensagens.reload();
    }, 2500);
  }

  protected async assumir(c: ConversaResumo): Promise<void> {
    await firstValueFrom(this.http.post(`${API}/conversas/${c.id}/assumir`, {}));
    this.conversas.reload();
  }

  protected async devolver(c: ConversaResumo): Promise<void> {
    await firstValueFrom(this.http.post(`${API}/conversas/${c.id}/devolver`, {}));
    this.conversas.reload();
  }

  protected abrir(c: ConversaResumo): void {
    void this.router.navigate(['/conversas', c.id]);
  }

  protected iniciais(nome: string): string {
    return nome
      .replace(/[^\p{L} ]/gu, '')
      .split(' ')
      .filter(Boolean)
      .slice(0, 2)
      .map((p) => p[0].toUpperCase())
      .join('') || '?';
  }

  protected horasRestantes(c: ConversaResumo): number {
    if (!c.janelaFechaEm) return 0;
    return Math.max(0, Math.floor((new Date(c.janelaFechaEm).getTime() - Date.now()) / 3_600_000));
  }
}
