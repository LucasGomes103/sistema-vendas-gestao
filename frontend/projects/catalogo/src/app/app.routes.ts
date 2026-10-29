import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', loadComponent: () => import('./inicio/inicio-page').then((m) => m.InicioPage) },
  { path: ':loja', loadComponent: () => import('./vitrine/vitrine-page').then((m) => m.VitrinePage) },
];
