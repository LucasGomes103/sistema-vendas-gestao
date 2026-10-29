import { RenderMode, ServerRoute } from '@angular/ssr';

export const serverRoutes: ServerRoute[] = [
  // O estoque muda todo dia: a vitrine é renderizada no servidor a cada acesso.
  { path: '**', renderMode: RenderMode.Server },
];
