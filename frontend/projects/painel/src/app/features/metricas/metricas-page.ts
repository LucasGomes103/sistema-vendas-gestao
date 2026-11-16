import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { CurrencyPipe, DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { API, mensagemDeErro } from '../../core/api';

interface Resumo {
  conversas: number;
  vendasPagas: number;
  conversao: number;
  receita: number;
  ticketMedio: number;
  itensVendidos: number;
  frete: number;
  pedidosCancelados: number;
  valorCancelado: number;
}

interface Relatorio {
  de: string;
  ate: string;
  hoje: string;
  agrupamento: 'Dia' | 'Semana' | 'Mes';
  incluiSimuladas: boolean;
  atual: Resumo;
  anterior: Resumo;
  anteriorDe: string;
  anteriorAte: string;
  vendas: { inicio: string; pedidos: number; receita: number }[];
  funil: { etapa: string; conversas: number }[];
  motivosPerda: { motivo: string; conversas: number }[];
  origens: { origem: string; vendas: number; receita: number }[];
  formas: { forma: string; pedidos: number; valor: number }[];
  especies: {
    especieId: string;
    especie: string;
    quantidade: number;
    receita: number;
    custo: number | null;
    margem: number | null;
    margemPercentual: number | null;
    custoCompleto: boolean;
    mortes: number;
  }[];
  tanques: {
    tanqueId: string;
    tanque: string;
    tipo: string;
    saldoInicio: number;
    saldoAtual: number;
    entradas: number;
    vendidos: number;
    mortes: number;
    giro: number | null;
  }[];
  fornecedores: { fornecedor: string; lotes: number; recebidos: number; mortes: number; mortalidade: number; custoCompras: number | null }[];
  caixa: {
    entradas: number;
    saidas: number;
    saldo: number;
    entradasPorCategoria: { categoria: string; valor: number }[];
    despesas: { categoria: string; valor: number }[];
    diasFechados: number;
    diferencaGaveta: number;
  };
  entregas: { entregues: number; retiradas: number; diasAteEntregar: number | null; emAberto: number; atrasadas: number };
}

type Atalho = '7' | '30' | 'mes' | 'mesAnterior' | '90' | 'personalizado';

const ETAPAS: Record<string, string> = {
  Novo: 'Novas conversas',
  RoboAtendendo: 'Robô atendeu',
  Quente: 'Quente (quis comprar)',
  AguardandoPagamento: 'Recebeu o Pix',
  Pago: 'Pago',
  Entregue: 'Entregue',
};

const MOTIVOS: Record<string, string> = {
  SemResposta: 'Sem resposta',
  NaoPagou: 'Não pagou',
  AchouCaro: 'Achou caro',
  SemEstoque: 'Sem estoque',
  RegiaoSemEntrega: 'Região sem entrega',
  Desistiu: 'Desistiu',
  SemMotivo: 'Sem motivo informado',
};

const RELATORIOS = [
  { id: 'pedidos', nome: 'Pedidos pagos', icone: 'receipt_long' },
  { id: 'vendas', nome: 'Vendas por período', icone: 'bar_chart' },
  { id: 'especies', nome: 'Espécies (vendas e margem)', icone: 'set_meal' },
  { id: 'estoque', nome: 'Estoque por tanque (giro)', icone: 'water' },
  { id: 'fornecedores', nome: 'Mortalidade por fornecedor', icone: 'local_shipping' },
  { id: 'caixa', nome: 'Lançamentos do caixa', icone: 'point_of_sale' },
];

function iso(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Métricas do período e relatórios em CSV (abre no Excel). Só o dono. */
@Component({
  selector: 'app-metricas-page',
  imports: [
    CurrencyPipe,
    DatePipe,
    DecimalPipe,
    NgTemplateOutlet,
    FormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatMenuModule,
    MatProgressBarModule,
    MatTooltipModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './metricas-page.html',
  styleUrl: './metricas-page.scss',
})
export class MetricasPage {
  protected readonly relatorios = RELATORIOS;
  protected readonly etapas = ETAPAS;
  protected readonly motivos = MOTIVOS;

  protected readonly atalho = signal<Atalho>('30');
  protected readonly de = signal(iso(new Date(Date.now() - 29 * 864e5)));
  protected readonly ate = signal(iso(new Date()));
  protected readonly simuladas = signal(false);

  private readonly consulta = computed(() => `de=${this.de()}&ate=${this.ate()}${this.simuladas() ? '&simuladas=true' : ''}`);
  protected readonly dados = httpResource<Relatorio>(() => (this.de() && this.ate() ? `${API}/metricas?${this.consulta()}` : undefined));
  protected readonly erro = computed(() => (this.dados.error() ? mensagemDeErro(this.dados.error(), 'Não foi possível carregar as métricas.') : null));

  protected exportar(relatorio: string): string {
    return `${API}/metricas/exportar/${relatorio}?${this.consulta()}`;
  }

  protected escolher(a: Atalho): void {
    this.atalho.set(a);
    const hoje = new Date();
    const dias = (n: number) => {
      this.de.set(iso(new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate() - (n - 1))));
      this.ate.set(iso(hoje));
    };
    switch (a) {
      case '7':
        return dias(7);
      case '30':
        return dias(30);
      case '90':
        return dias(90);
      case 'mes':
        this.de.set(iso(new Date(hoje.getFullYear(), hoje.getMonth(), 1)));
        this.ate.set(iso(hoje));
        return;
      case 'mesAnterior':
        this.de.set(iso(new Date(hoje.getFullYear(), hoje.getMonth() - 1, 1)));
        this.ate.set(iso(new Date(hoje.getFullYear(), hoje.getMonth(), 0)));
        return;
    }
  }

  /** Barras do gráfico de vendas: altura proporcional à maior receita do período. */
  protected readonly barras = computed(() => {
    const v = this.dados.value()?.vendas ?? [];
    const max = Math.max(0, ...v.map((p) => p.receita));
    return v.map((p) => ({ ...p, altura: max > 0 ? Math.max(p.receita > 0 ? 3 : 0, (p.receita / max) * 100) : 0 }));
  });
  protected readonly maiorReceita = computed(() => Math.max(0, ...(this.dados.value()?.vendas ?? []).map((p) => p.receita)));

  /** Rótulo do eixo: só alguns, para não encavalar. */
  protected mostrarRotulo(i: number, total: number): boolean {
    const passo = Math.ceil(total / 6);
    return i % passo === 0;
  }

  protected formatoGrupo(r: Relatorio): string {
    return r.agrupamento === 'Mes' ? 'MMM/yy' : 'dd/MM';
  }

  protected dataLocal(d: string): Date {
    return new Date(`${d}T12:00:00`);
  }

  protected largura(valor: number, max: number): number {
    return max > 0 ? Math.max(valor > 0 ? 2 : 0, (valor / max) * 100) : 0;
  }

  protected percentual(parte: number, total: number): number {
    return total > 0 ? (parte * 100) / total : 0;
  }

  /** Variação sobre o período anterior. Null quando não há base. */
  protected variacao(atual: number, anterior: number): number | null {
    return anterior === 0 ? null : ((atual - anterior) * 100) / anterior;
  }

  protected readonly especiesVendidas = computed(() => (this.dados.value()?.especies ?? []).filter((e) => e.quantidade > 0));
  protected readonly especiesComMorte = computed(() =>
    (this.dados.value()?.especies ?? []).filter((e) => e.mortes > 0).sort((a, b) => b.mortes - a.mortes),
  );
  protected readonly algumaSemCusto = computed(() => this.especiesVendidas().some((e) => !e.custoCompleto));
}
