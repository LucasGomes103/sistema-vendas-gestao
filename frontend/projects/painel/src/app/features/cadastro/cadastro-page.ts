import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { firstValueFrom } from 'rxjs';
import { API, mensagemDeErro } from '../../core/api';

interface SlugSaida {
  slug: string;
  disponivel: boolean;
  erro: string | null;
}

/** "Aquário do Zé" → "aquario-do-ze" (o servidor confere de novo). */
export function sugerirSlug(nome: string): string {
  return nome
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 40)
    .replace(/-+$/, '');
}

/** Cadastro público: loja + dono. A conta só ativa quando o dono abre o link do e-mail. */
@Component({
  selector: 'app-cadastro-page',
  imports: [ReactiveFormsModule, RouterLink, MatAutocompleteModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: '../login/login-page.scss',
  template: `
    <div class="lado-marca">
      <img class="marca" src="icone.svg" alt="Sistema de Vendas" />
      <h1>Sua loja vendendo pelo WhatsApp, com estoque e pedidos no mesmo lugar.</h1>
      <ul>
        <li><mat-icon>storefront</mat-icon> Vitrine com endereço próprio</li>
        <li><mat-icon>smart_toy</mat-icon> Robô que responde preço, disponibilidade e frete</li>
        <li><mat-icon>payments</mat-icon> Pix e cartão direto na conta da loja</li>
      </ul>
    </div>

    <div class="lado-form">
      @if (enviadoPara(); as email) {
        <div class="cartao">
          <h2>Confira o seu e-mail</h2>
          <div class="ok"><mat-icon>mark_email_read</mat-icon>
            <span>Mandamos um link de confirmação para <strong>{{ email }}</strong>. Abra o link (vale 48 horas) para ativar a loja e entrar no painel.</span>
          </div>
          @if (!emailEnviado()) {
            <p class="sub">O envio de e-mail ainda não está ligado no servidor. A conta será ativada pelo suporte.</p>
          }
          <p class="rodape">Não chegou? Veja o spam ou <a routerLink="/login">peça outro link na tela de entrar</a>.</p>
        </div>
      } @else {
        <form class="cartao" [formGroup]="form" (ngSubmit)="cadastrar()">
          <h2>Cadastre a sua loja</h2>
          <p class="sub">Você confirma o e-mail e já entra no painel.</p>

          <mat-form-field appearance="outline">
            <mat-label>Nome da loja</mat-label>
            <mat-icon matPrefix>storefront</mat-icon>
            <input matInput formControlName="nomeLoja" (input)="aoMudarNome()" maxlength="120" />
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Endereço da vitrine</mat-label>
            <span matTextPrefix>…/</span>
            <input matInput formControlName="slug" (input)="slugEditado = true; conferirSlug()" (blur)="conferirSlug()" maxlength="40" />
            @if (slug(); as s) {
              <mat-hint [class.ruim]="!s.disponivel">{{ s.disponivel ? 'Disponível' : s.erro }}</mat-hint>
            } @else {
              <mat-hint>Letras minúsculas, números e hífen</mat-hint>
            }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Tipo de loja</mat-label>
            <mat-icon matPrefix>category</mat-icon>
            <input matInput formControlName="segmento" placeholder="Ex.: aquarismo, roupas, pet shop" maxlength="60" [matAutocomplete]="tipos" />
            <mat-autocomplete #tipos="matAutocomplete">
              @for (t of tiposSugeridos; track t) {
                <mat-option [value]="t">{{ t }}</mat-option>
              }
            </mat-autocomplete>
            <mat-hint>Define as palavras do sistema e como o robô apresenta a loja.</mat-hint>
            @if (form.controls.segmento.touched && form.controls.segmento.invalid) {
              <mat-error>Informe o tipo de loja.</mat-error>
            }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Seu nome</mat-label>
            <mat-icon matPrefix>person</mat-icon>
            <input matInput formControlName="nomeDono" autocomplete="name" maxlength="120" />
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>E-mail</mat-label>
            <mat-icon matPrefix>mail</mat-icon>
            <input matInput type="email" formControlName="email" autocomplete="username" />
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>Senha</mat-label>
            <mat-icon matPrefix>lock</mat-icon>
            <input matInput type="password" formControlName="senha" autocomplete="new-password" />
            <mat-hint>Pelo menos 8 caracteres, com letra e número</mat-hint>
          </mat-form-field>

          <mat-checkbox formControlName="aceiteTermos">
            Li e aceito os <a href="/termos" target="_blank">termos de uso</a> e a <a href="/privacidade" target="_blank">política de privacidade</a>
          </mat-checkbox>

          @if (erro(); as mensagem) {
            <div class="erro" role="alert"><mat-icon>error</mat-icon> {{ mensagem }}</div>
          }

          <button mat-flat-button type="submit" class="entrar" [disabled]="enviando()">
            @if (enviando()) {
              <mat-spinner diameter="20" />
            } @else {
              Criar conta
            }
          </button>

          <p class="rodape">Já tem conta? <a routerLink="/login">Entrar</a></p>
        </form>
      }
    </div>
  `,
})
export class CadastroPage {
  /** Sugestões do tipo de loja (pode escrever outro). Aquarismo mantém espécie, tanque e quarentena. */
  protected readonly tiposSugeridos = [
    'Aquarismo', 'Pet shop', 'Roupas e acessórios', 'Calçados', 'Autopeças', 'Cosméticos e perfumaria',
    'Alimentos e bebidas', 'Eletrônicos', 'Casa e decoração', 'Papelaria', 'Plantas e jardinagem', 'Outro',
  ];
  private readonly http = inject(HttpClient);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    nomeLoja: ['', [Validators.required, Validators.minLength(2)]],
    slug: ['', [Validators.required, Validators.minLength(3), Validators.pattern(/^[a-z0-9]+(-[a-z0-9]+)*$/)]],
    segmento: ['', [Validators.required, Validators.minLength(2)]],
    nomeDono: ['', [Validators.required, Validators.minLength(2)]],
    email: ['', [Validators.required, Validators.email]],
    senha: ['', [Validators.required, Validators.minLength(8)]],
    aceiteTermos: [false, Validators.requiredTrue],
  });

  protected slugEditado = false;
  protected readonly slug = signal<SlugSaida | null>(null);
  protected readonly enviando = signal(false);
  protected readonly erro = signal<string | null>(null);
  protected readonly enviadoPara = signal<string | null>(null);
  protected readonly emailEnviado = signal(true);
  private espera?: ReturnType<typeof setTimeout>;

  protected aoMudarNome(): void {
    if (this.slugEditado) return;
    this.form.controls.slug.setValue(sugerirSlug(this.form.controls.nomeLoja.value));
    this.conferirSlug();
  }

  /** Confere se o endereço está livre, 400 ms depois de parar de digitar. */
  protected conferirSlug(): void {
    clearTimeout(this.espera);
    const valor = this.form.controls.slug.value;
    if (valor.length < 3) {
      this.slug.set(null);
      return;
    }
    this.espera = setTimeout(async () => {
      try {
        this.slug.set(await firstValueFrom(this.http.get<SlugSaida>(`${API}/cadastro/slug/${encodeURIComponent(valor)}`)));
      } catch {
        this.slug.set(null);
      }
    }, 400);
  }

  protected async cadastrar(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.erro.set(this.form.controls.aceiteTermos.invalid ? 'Para criar a conta, aceite os termos e a política de privacidade.' : 'Confira os campos destacados.');
      return;
    }
    this.enviando.set(true);
    this.erro.set(null);
    try {
      const r = await firstValueFrom(this.http.post<{ email: string; emailEnviado: boolean }>(`${API}/cadastro`, this.form.getRawValue()));
      this.emailEnviado.set(r.emailEnviado);
      this.enviadoPara.set(r.email);
    } catch (e) {
      this.erro.set(mensagemDeErro(e));
    } finally {
      this.enviando.set(false);
    }
  }
}
