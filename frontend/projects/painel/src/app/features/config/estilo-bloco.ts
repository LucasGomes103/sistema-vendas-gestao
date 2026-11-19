/** Estilo dos blocos da tela de Configuração com formulário (cartão, WhatsApp oficial). */
export const ESTILO_BLOCO = `
  .bloco {
    display: grid;
    gap: 4px;
    margin-top: 16px;
    padding: 16px 20px 12px;

    .titulo {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      margin-bottom: 8px;
    }
    h2 {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 0;
      font: var(--mat-sys-title-medium);
      font-weight: 600;

      mat-icon {
        color: var(--mat-sys-primary);
      }
    }
    .ajuda {
      margin: -4px 0 8px;
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    ul {
      display: grid;
      gap: 4px;
      margin: 0 0 8px;
      padding: 0;
      list-style: none;
      font: var(--mat-sys-body-small);
      overflow-wrap: anywhere;
    }
    li {
      display: flex;
      align-items: center;
      gap: 6px;

      mat-icon {
        flex: none;
        font-size: 18px;
        width: 18px;
        height: 18px;
      }
    }
  }
  .grade {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 12px;
  }
  .inteiro {
    grid-column: 1 / -1;
  }
  .acoes {
    display: flex;
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: 8px;
    margin-bottom: 8px;
  }
  @media (max-width: 599px) {
    .grade {
      grid-template-columns: 1fr;
    }
  }
`;
