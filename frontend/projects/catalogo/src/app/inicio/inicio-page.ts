import { ChangeDetectionStrategy, Component } from '@angular/core';

/** Raiz do catálogo: cada loja tem a própria vitrine em /{loja}. */
@Component({
  selector: 'app-inicio-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1>Catálogo</h1>
    <p>Cada loja tem a própria vitrine. Acesse pelo link da loja, por exemplo <a href="/demo">/demo</a>.</p>
  `,
  styles: `
    :host { display: block; max-width: 640px; margin: 0 auto; padding: 16px; font-family: system-ui, sans-serif; }
  `,
})
export class InicioPage {}
