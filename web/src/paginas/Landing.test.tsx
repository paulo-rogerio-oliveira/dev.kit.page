import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RECURSOS } from './Landing';
import { renderizar } from '../testes/renderizar';

describe('Landing', () => {
  it('abre sem login e apresenta todos os recursos do dev.kit', () => {
    renderizar('/');

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('O agente de IA trabalhando sobre a sua task');
    for (const recurso of RECURSOS) {
      expect(screen.getByRole('heading', { name: recurso.titulo })).toBeInTheDocument();
    }
    // Os sete recursos do pedido.
    expect(RECURSOS.map((r) => r.id)).toEqual(['agente', 'fluxos', 'replicacao', 'agendamento', 'depurador', 'devcli', 'board']);
  });

  it('o link de entrar leva ao login', async () => {
    renderizar('/');

    await userEvent.click(screen.getAllByRole('link', { name: 'Entrar' })[0]);

    expect(screen.getByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
  });
});
