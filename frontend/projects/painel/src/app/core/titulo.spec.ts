import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { TituloLagChat } from './titulo';

@Component({ template: '' })
class Vazia {}

describe('TituloLagChat', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'pedidos', title: 'Pedidos', component: Vazia },
          { path: 'sem-titulo', component: Vazia },
        ]),
        { provide: TitleStrategy, useClass: TituloLagChat },
      ],
    });
  });

  it('junta o título da página com o nome do produto', async () => {
    await TestBed.inject(Router).navigateByUrl('/pedidos');
    expect(TestBed.inject(Title).getTitle()).toBe('Pedidos · LAG CHAT');
  });

  it('usa só o nome do produto quando a rota não tem título', async () => {
    await TestBed.inject(Router).navigateByUrl('/sem-titulo');
    expect(TestBed.inject(Title).getTitle()).toBe('LAG CHAT');
  });
});
