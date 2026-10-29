import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { HttpClient, httpResource } from '@angular/common/http';
import { CdkDrag, CdkDragDrop, CdkDropList, CdkDropListGroup } from '@angular/cdk/drag-drop';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { firstValueFrom } from 'rxjs';
import { API, CardFunil, EtapaCard, MotivoPerda, mensagemDeErro } from '../../core/api';
import { atualizarACada } from '../../core/atualizar';
import { MotivoPerdaDialog } from './motivo-perda-dialog';

interface Coluna {
  etapa: EtapaCard;
  titulo: string;
  icone: string;
  destaque?: 'quente' | 'ok' | 'perdido';
}

const COLUNAS: Coluna[] = [
  { etapa: 'Novo', titulo: 'Novo', icone: 'fiber_new' },
  { etapa: 'RoboAtendendo', titulo: 'Robô atendendo', icone: 'smart_toy' },
  { etapa: 'FollowUp', titulo: 'Follow-up', icone: 'schedule_send' },
  { etapa: 'Quente', titulo: 'Quente: chamar já', icone: 'local_fire_department', destaque: 'quente' },
  { etapa: 'AguardandoPagamento', titulo: 'Aguardando pagamento', icone: 'pending' },
  { etapa: 'ConferirPagamento', titulo: 'Conferir pagamento', icone: 'fact_check' },
  { etapa: 'Pago', titulo: 'Pago', icone: 'paid', destaque: 'ok' },
  { etapa: 'Entregue', titulo: 'Entregue', icone: 'local_shipping', destaque: 'ok' },
  { etapa: 'Perdido', titulo: 'Perdido', icone: 'block', destaque: 'perdido' },
];

const MOTIVOS: Record<MotivoPerda, string> = {
  SemResposta: 'Sem resposta',
  NaoPagou: 'Não pagou',
  AchouCaro: 'Achou caro',
  SemEstoque: 'Sem estoque',
  RegiaoSemEntrega: 'Região sem entrega',
  Desistiu: 'Desistiu',
};

/** Kanban do funil: os cards andam sozinhos pelas regras; o vendedor arrasta quando precisa. */
@Component({
  selector: 'app-funil-page',
  imports: [CdkDropListGroup, CdkDropList, CdkDrag, MatButtonModule, MatIconModule, MatProgressBarModule, MatTooltipModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './funil-page.html',
  styleUrl: './funil-page.scss',
})
export class FunilPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly snack = inject(MatSnackBar);

  protected readonly cards = httpResource<CardFunil[]>(() => `${API}/funil/cards`);
  protected readonly colunas = COLUNAS;
  protected readonly motivos = MOTIVOS;

  /** Movimento otimista: o card muda de coluna na hora e volta se a API recusar. */
  private readonly movidos = signal<Record<string, EtapaCard>>({});
  protected readonly agora = signal(Date.now());

  protected readonly porEtapa = computed(() => {
    const movidos = this.movidos();
    const mapa = new Map<EtapaCard, CardFunil[]>(COLUNAS.map((c) => [c.etapa, []]));
    for (const card of this.cards.value() ?? []) {
      const etapa = movidos[card.id] ?? card.etapa;
      mapa.get(etapa)?.push({ ...card, etapa });
    }
    return mapa;
  });

  protected readonly resumo = computed(() => {
    const m = this.porEtapa();
    return { quentes: m.get('Quente')?.length ?? 0, followup: m.get('FollowUp')?.length ?? 0 };
  });

  constructor() {
    atualizarACada(15000, () => {
      this.agora.set(Date.now());
      this.cards.reload();
    });
  }

  protected async soltar(evento: CdkDragDrop<EtapaCard, EtapaCard, CardFunil>): Promise<void> {
    const card = evento.item.data;
    const para = evento.container.data;
    if (para === card.etapa) return;

    let motivo: MotivoPerda | null = null;
    if (para === 'Perdido') {
      motivo = await firstValueFrom(this.dialog.open(MotivoPerdaDialog, { width: '380px' }).afterClosed());
      if (!motivo) return;
    }

    this.movidos.update((m) => ({ ...m, [card.id]: para }));
    try {
      await firstValueFrom(this.http.patch(`${API}/funil/cards/${card.id}/etapa`, { etapa: para, motivoPerda: motivo }));
      this.cards.reload();
    } catch (e) {
      this.snack.open(mensagemDeErro(e, 'Não foi possível mover o card.'), 'OK', { duration: 5000 });
    } finally {
      this.movidos.update((m) => {
        const { [card.id]: _, ...resto } = m;
        return resto;
      });
    }
  }

  protected abrir(card: CardFunil): void {
    void this.router.navigate(['/conversas', card.id]);
  }

  /** Texto da regra que vence a seguir, para ninguém se surpreender com uma mensagem automática. */
  protected regra(card: CardFunil): string | null {
    if (!card.proximaAcao || !card.proximaAcaoEm) return null;
    const em = this.tempo(new Date(card.proximaAcaoEm).getTime() - this.agora());
    switch (card.proximaAcao) {
      case 'MoverParaFollowup':
        return `Follow-up ${em}`;
      case 'EnviarFollowup1':
        return `FU 1 de 2 · ${em}`;
      case 'EnviarFollowup2':
        return `FU 2 de 2 · ${em}`;
      case 'MarcarPerdido':
        return `Perdido ${em}`;
      default:
        return null;
    }
  }

  /** "Sem dono há 12 min": cliente quente esperando um vendedor. */
  protected semDono(card: CardFunil): string | null {
    if (card.etapa !== 'Quente' || !card.chamouVendedorEm) return null;
    const minutos = Math.max(0, Math.floor((this.agora() - new Date(card.chamouVendedorEm).getTime()) / 60000));
    return minutos < 60 ? `há ${minutos} min` : `há ${Math.floor(minutos / 60)} h`;
  }

  private tempo(ms: number): string {
    if (ms <= 0) return 'agora';
    const min = Math.ceil(ms / 60000);
    if (min < 60) return `em ${min} min`;
    const h = Math.round(min / 60);
    return h < 48 ? `em ${h} h` : `em ${Math.round(h / 24)} dias`;
  }
}
