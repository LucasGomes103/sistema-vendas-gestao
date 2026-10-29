import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'catalogo' },
  {
    path: 'catalogo',
    loadComponent: () => import('./features/catalogo/catalogo-page').then((m) => m.CatalogoPage),
  },
  {
    path: 'estoque',
    loadComponent: () => import('./features/estoque/estoque-page').then((m) => m.EstoquePage),
  },
];
