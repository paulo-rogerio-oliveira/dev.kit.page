import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { ErroDaApi } from '../api/cliente';
import { participantes } from '../testes/servidor';
import { ParticipantesDaEquipe } from './ParticipantesDaEquipe';

describe('Participantes das equipes (US #556)', () => {
  function abrir(aoDecidir = vi.fn(() => Promise.resolve())) {
    const aoMudar = vi.fn(() => Promise.resolve());
    const aoExpirar = vi.fn();
    render(
      <ParticipantesDaEquipe participantes={participantes} usuarios={[{ id: 2, nome: 'Ana Souza' }, { id: 3, nome: 'Bia' }]}
        aoDecidir={aoDecidir} aoMudar={aoMudar} aoExpirar={aoExpirar} />,
    );
    return { aoDecidir, aoMudar, aoExpirar };
  }

  it('o gestor vê os pedidos pendentes e aprova, recusa ou remove', async () => {
    const { aoDecidir, aoMudar } = abrir();

    expect(screen.getByRole('heading', { name: /Participantes das equipes/ })).toHaveTextContent('1 aguardando');
    const pendente = screen.getByText('financeiro').closest('tr')!;
    expect(pendente).toHaveTextContent('Pendente');
    const aprovado = screen.getByText('vendas').closest('tr')!;
    expect(within(aprovado).queryByRole('button', { name: /^Aprovar/ })).not.toBeInTheDocument();

    await userEvent.click(within(pendente).getByRole('button', { name: 'Aprovar Ana Souza em financeiro' }));
    await userEvent.click(within(pendente).getByRole('button', { name: 'Recusar Ana Souza de financeiro' }));
    await userEvent.click(within(aprovado).getByRole('button', { name: 'Remover Ana Souza de vendas' }));

    expect(aoDecidir.mock.calls).toEqual([[ 'financeiro', 2, 'aprovado' ], [ 'financeiro', 2, 'recusado' ], [ 'vendas', 2, 'recusado' ]]);
    expect(aoMudar).toHaveBeenCalledTimes(3);
  });

  it('inclui alguém direto numa equipe, sem esperar o pedido', async () => {
    const { aoDecidir } = abrir();
    const formulario = screen.getByRole('form', { name: 'Incluir numa equipe' });

    await userEvent.selectOptions(within(formulario).getByLabelText('Usuário'), '3');
    await userEvent.type(within(formulario).getByLabelText('Equipe'), 'Vendas');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Incluir' }));

    await waitFor(() => expect(aoDecidir).toHaveBeenCalledWith('Vendas', 3, 'aprovado'));
    expect(await screen.findByText('Bia incluído na equipe vendas.')).toBeInTheDocument();
  });

  it('a recusa da API aparece; o 401 encerra a sessão', async () => {
    const proibida = vi.fn(() => Promise.reject(new ErroDaApi(403, 'Só o admin da empresa (o gestor) ou o admin do board decide quem participa de uma equipe.')));
    abrir(proibida);
    await userEvent.click(screen.getByRole('button', { name: 'Aprovar Ana Souza em financeiro' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Só o admin da empresa');

    const vencida = vi.fn(() => Promise.reject(new ErroDaApi(401, 'expirou')));
    const { aoExpirar } = abrir(vencida);
    await userEvent.click(screen.getAllByRole('button', { name: 'Aprovar Ana Souza em financeiro' })[1]);
    await waitFor(() => expect(aoExpirar).toHaveBeenCalled());
  });
});
