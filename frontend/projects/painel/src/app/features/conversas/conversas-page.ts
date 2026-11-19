import { ChangeDetectionStrategy, Component, ElementRef, afterRenderEffect, computed, inject, input, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { API, Atendente, ConversaResumo, Mensagem, mensagemDeErro } from '../../core/api';
import { Confirmar } from '../../core/confirmar';
import { atualizarACada } from '../../core/atualizar';
import { ReservasConversaComponent } from './reservas-conversa';
import { PedidoSimuladoDialog } from './pedido-simulado-dialog';
import { Auth } from '../../core/auth';

/**
 * Conversas do WhatsApp: lista à esquerda e chat à direita no desktop;
 * no celular, uma coisa de cada vez.
 */
@Component({
  selector: 'app-conversas-page',
  imports: [ReservasConversaComponent, DatePipe, RouterLink, MatButtonModule, MatMenuModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './conversas-page.html',
  styleUrl: './conversas-page.scss',
})
export class ConversasPage {
  private readonly confirmar = inject(Confirmar);
  /** Conversa aberta, vinda da rota /conversas/:id. */
  readonly id = input<string>();

  protected readonly auth = inject(Auth);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);

  protected readonly conversas = httpResource<ConversaResumo[]>(() => `${API}/conversas`);
  protected readonly mensagens = httpResource<Mensagem[]>(() => (this.id() ? `${API}/conversas/${this.id()}/mensagens` : undefined));
  protected readonly whatsApp = httpResource<{ simulado: boolean; simuladorDisponivel: boolean; phoneNumberId: string | null; atendente: string; provedor: 'Meta' | 'Local' }>(() => `${API}/conversas/status-whatsapp`);

  /** Dono e vendedores ativos, para o dono transferir a conversa. */
  protected readonly atendentes = httpResource<Atendente[]>(() => (this.auth.ehDono() ? `${API}/conversas/atendentes` : undefined));

  /** Todas, só as minhas ou as que ninguém pegou. Todos veem todas as conversas. */
  protected readonly filtro = signal<'todas' | 'minhas' | 'livres'>('todas');
  protected readonly lista = computed(() => {
    const todas = this.conversas.value() ?? [];
    const eu = this.auth.usuario()?.id;
    switch (this.filtro()) {
      case 'minhas':
        return todas.filter((c) => c.responsavelId === eu);
      case 'livres':
        return todas.filter((c) => !c.responsavelId);
      default:
        return todas;
    }
  });
  /** Conversas que ninguém pegou ainda: sempre à vista, para saber quantas faltam atender. */
  protected readonly livres = computed(() => (this.conversas.value() ?? []).filter((c) => !c.responsavelId).length);
  protected readonly minhas = computed(() => (this.conversas.value() ?? []).filter((c) => c.responsavelId === this.auth.usuario()?.id).length);

  protected readonly aberta = computed(() => this.conversas.value()?.find((c) => c.id === this.id()));
  /** A conversa aberta está com outro vendedor: dá para acompanhar, mas não responder (o dono pode). */
  protected readonly comOutro = computed(() => {
    const c = this.aberta();
    return !!c?.responsavelId && c.responsavelId !== this.auth.usuario()?.id && !this.auth.ehDono();
  });
  protected readonly texto = signal('');
  protected readonly enviando = signal(false);
  protected readonly erro = signal<string | null>(null);

  // Simulador de cliente (só em desenvolvimento; as respostas dessas conversas nunca saem pela Meta).
  protected readonly simulando = signal(false);
  protected readonly simTelefone = signal('11987654321');
  protected readonly simNome = signal('Cliente teste');
  protected readonly simTexto = signal('');

  private readonly caixaMensagens = viewChild<ElementRef<HTMLElement>>('caixaMensagens');
  private ultimaQuantidade = 0;

  constructor() {
    // Mensagem nova: desce até o fim da conversa, como no WhatsApp.
    afterRenderEffect(() => {
      const quantidade = this.mensagens.value()?.length ?? 0;
      const caixa = this.caixaMensagens()?.nativeElement;
      if (caixa && quantidade !== this.ultimaQuantidade) {
        this.ultimaQuantidade = quantidade;
        caixa.scrollTop = caixa.scrollHeight;
      }
    });

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

  /** Cliente simulado manda um pedido montado como na vitrine. */
  protected async pedidoDoCatalogo(): Promise<void> {
    const texto = await firstValueFrom(
      this.dialog.open<PedidoSimuladoDialog, unknown, string>(PedidoSimuladoDialog, { width: '480px', maxWidth: '95vw' }).afterClosed(),
    );
    if (texto) await this.simular(texto);
  }

  protected async simular(textoPronto?: string): Promise<void> {
    const texto = (textoPronto ?? this.simTexto()).trim();
    if (!texto) return;
    const aberta = this.aberta();
    await firstValueFrom(
      this.http.post(`${API}/dev/simular-mensagem`, {
        telefone: aberta?.telefone ?? this.simTelefone(),
        nome: aberta?.cliente ?? this.simNome(),
        texto,
      }),
    );
    if (!textoPronto) this.simTexto.set('');
    // O worker processa a fila em até ~2 s.
    setTimeout(() => {
      this.conversas.reload();
      this.mensagens.reload();
    }, 2500);
  }

  protected async assumir(c: ConversaResumo): Promise<void> {
    await this.acao(() => firstValueFrom(this.http.post(`${API}/conversas/${c.id}/assumir`, {})));
  }

  protected async devolver(c: ConversaResumo): Promise<void> {
    await this.acao(() => firstValueFrom(this.http.post(`${API}/conversas/${c.id}/devolver`, {})));
  }

  /** Dono passa a conversa para outra pessoa da equipe (ou deixa sem responsável, com null). */
  protected async transferir(c: ConversaResumo, a: Atendente | null): Promise<void> {
    if (a?.id === c.responsavelId) return;
    const pergunta = a
      ? `Passar a conversa com ${c.cliente} para ${a.nome}? ${a.nome} segue o atendimento${c.responsavel ? ` e ${c.responsavel} deixa de responder` : ''}.`
      : `Deixar a conversa com ${c.cliente} sem responsável? Ela volta para a fila e o primeiro vendedor que responder fica com ela.`;
    if (!await this.confirmar.perguntar(pergunta, { botao: a ? 'Transferir' : 'Liberar' })) return;
    await this.acao(() => firstValueFrom(this.http.post(`${API}/conversas/${c.id}/transferir`, { usuarioId: a?.id ?? null })));
  }

  private async acao(fazer: () => Promise<unknown>): Promise<void> {
    this.erro.set(null);
    try {
      await fazer();
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
    }
    this.conversas.reload();
  }

  /** "você" quando a conversa é de quem está logado. */
  protected nomeResponsavel(c: ConversaResumo): string {
    return c.responsavelId === this.auth.usuario()?.id ? 'você' : (c.responsavel ?? '');
  }

  protected async excluir(c: ConversaResumo, apagar = false): Promise<void> {
    const pergunta = c.simulada
      ? `Excluir a conversa simulada com ${c.cliente}? As mensagens serão apagadas.`
      : apagar
        ? `Apagar a conversa com ${c.cliente} do sistema? As mensagens somem do painel e não dá para desfazer. No celular nada muda.`
        : `Arquivar a conversa com ${c.cliente}? Ela sai da lista; se o cliente escrever de novo, abre uma conversa nova.`;
    if (!await this.confirmar.perguntar(pergunta, { botao: apagar ? 'Apagar' : 'Arquivar', perigo: apagar })) return;
    await firstValueFrom(this.http.delete(`${API}/conversas/${c.id}`, { params: { definitivo: apagar } }));
    this.conversas.reload();
    void this.router.navigate(['/conversas']);
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
