import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Auth } from './auth';

// inject() só funciona antes do primeiro await, por isso o Router é obtido no início.

export const exigeLogin: CanActivateFn = async () => {
  const auth = inject(Auth);
  const router = inject(Router);
  return (await auth.carregar()) || router.parseUrl('/login');
};

export const somenteVisitante: CanActivateFn = async () => {
  const auth = inject(Auth);
  const router = inject(Router);
  return !(await auth.carregar()) || router.parseUrl('/');
};
