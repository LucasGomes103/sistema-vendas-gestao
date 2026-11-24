import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { Auth } from './core/auth';
import { exigeAdmin, exigeDono, exigeLogin, somenteVisitante } from './core/guards';
import { Shell } from './layout/shell';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Entrar',
    canActivate: [somenteVisitante],
    loadComponent: () => import('./features/login/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'cadastro',
    title: 'Cadastre a sua loja',
    canActivate: [somenteVisitante],
    loadComponent: () => import('./features/cadastro/cadastro-page').then((m) => m.CadastroPage),
  },
  {
    path: 'confirmar-email',
    title: 'Confirmar e-mail',
    loadComponent: () => import('./features/cadastro/confirmar-email-page').then((m) => m.ConfirmarEmailPage),
  },
  {
    path: '',
    component: Shell,
    canActivate: [exigeLogin],
    children: [
      // Entregador só usa a agenda de entregas (o Shell também garante isso depois de carregar a sessão).
      { path: '', pathMatch: 'full', redirectTo: () => (inject(Auth).ehEntregador() ? 'entregas' : 'estoque') },
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
      {
        path: 'funil',
        title: 'Funil',
        loadComponent: () => import('./features/funil/funil-page').then((m) => m.FunilPage),
      },
      {
        path: 'pedidos',
        title: 'Pedidos',
        loadComponent: () => import('./features/pedidos/pedidos-page').then((m) => m.PedidosPage),
      },
      {
        path: 'entregas',
        title: 'Entregas',
        loadComponent: () => import('./features/entregas/entregas-page').then((m) => m.EntregasPage),
      },
      {
        path: 'caixa',
        title: 'Caixa',
        canActivate: [exigeDono],
        loadComponent: () => import('./features/caixa/caixa-page').then((m) => m.CaixaPage),
      },
      {
        path: 'metricas',
        title: 'Métricas',
        canActivate: [exigeDono],
        loadComponent: () => import('./features/metricas/metricas-page').then((m) => m.MetricasPage),
      },
      {
        path: 'config',
        title: 'Configuração',
        loadComponent: () => import('./features/config/config-page').then((m) => m.ConfigPage),
      },
      {
        path: 'assinatura',
        title: 'Assinatura',
        canActivate: [exigeDono],
        loadComponent: () => import('./features/assinatura/assinatura-page').then((m) => m.AssinaturaPage),
      },
      {
        path: 'usuarios',
        title: 'Usuários',
        canActivate: [exigeDono],
        loadComponent: () => import('./features/usuarios/usuarios-page').then((m) => m.UsuariosPage),
      },
      {
        path: 'admin',
        title: 'Painel da plataforma',
        canActivate: [exigeAdmin],
        loadComponent: () => import('./features/admin/visao-geral-page').then((m) => m.VisaoGeralPage),
      },
      {
        path: 'admin/lojas',
        title: 'Lojas da plataforma',
        canActivate: [exigeAdmin],
        loadComponent: () => import('./features/admin/admin-page').then((m) => m.AdminPage),
      },
      {
        path: 'admin/lojas/:id',
        title: 'Loja',
        canActivate: [exigeAdmin],
        loadComponent: () => import('./features/admin/loja-detalhe-page').then((m) => m.LojaDetalhePage),
      },
      {
        path: 'admin/auditoria',
        title: 'Auditoria',
        canActivate: [exigeAdmin],
        loadComponent: () => import('./features/admin/auditoria-page').then((m) => m.AuditoriaPage),
      },
      {
        path: 'conversas',
        title: 'Conversas',
        loadComponent: () => import('./features/conversas/conversas-page').then((m) => m.ConversasPage),
      },
      {
        path: 'conversas/:id',
        title: 'Conversa',
        loadComponent: () => import('./features/conversas/conversas-page').then((m) => m.ConversasPage),
      },
      {
        path: 'estoque/tanques/:id',
        title: 'Tanque',
        loadComponent: () => import('./features/estoque/tanque-page').then((m) => m.TanquePage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
