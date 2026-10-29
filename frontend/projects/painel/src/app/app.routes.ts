import { Routes } from '@angular/router';
import { exigeLogin, somenteVisitante } from './core/guards';
import { Shell } from './layout/shell';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [somenteVisitante],
    loadComponent: () => import('./features/login/login-page').then((m) => m.LoginPage),
  },
  {
    path: '',
    component: Shell,
    canActivate: [exigeLogin],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'estoque' },
      {
        path: 'catalogo',
        title: 'Catálogo',
        loadComponent: () => import('./features/catalogo/catalogo-page').then((m) => m.CatalogoPage),
      },
      {
        path: 'estoque',
        title: 'Estoque',
        loadComponent: () => import('./features/estoque/estoque-page').then((m) => m.EstoquePage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
