import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: ':loja', loadComponent: () => import('./vitrine/vitrine-page').then((m) => m.VitrinePage) },
];
