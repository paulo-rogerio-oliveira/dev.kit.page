import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderizar } from '../testes/renderizar';
import { cadastros, tokenDoGestor, tokenValido } from '../testes/servidor';

describe('Empresas (US #381)', () => {
  it('lista as empresas com o código de adesão e os assentos ocupados', async () => {
    renderizar('/empresas', tokenValido());

    const tabela = (await screen.findByText('Empresa A')).closest('table')!;
    expect(within(tabela).getByText('DK-7QH4-M2XA')).toBeInTheDocument();
    expect(within(tabela).getByText('2 / 10')).toBeInTheDocument();
  });

  it('cria a empresa com o nome, o plano e os assentos', async () => {
    renderizar('/empresas', tokenValido());
    const formulario = await screen.findByRole('form', { name: 'Nova empresa' });

    await userEvent.type(within(formulario).getByLabelText('Nome'), 'Empresa B');
    await userEvent.clear(within(formulario).getByLabelText('Assentos'));
    await userEvent.type(within(formulario).getByLabelText('Assentos'), '25');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Criar empresa' }));

    await waitFor(() => expect(cadastros.empresas).toEqual([{ nome: 'Empresa B', plano: 'Empresarial', assentos: 25 }]));
  });

  it('convida o gestor e mostra a senha inicial uma vez', async () => {
    renderizar('/empresas', tokenValido());

    await userEvent.type(await screen.findByLabelText('Login do gestor de Empresa A'), 'gestor.a');
    await userEvent.click(screen.getByRole('button', { name: 'Convidar' }));

    expect(await screen.findByText('DkSENHA1234a1')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('o primeiro acesso exige a troca');
    expect(cadastros.gestores).toEqual([{ empresa: '4', login: 'gestor.a' }]);
  });

  it('o gestor que abre Empresas volta ao dashboard da empresa dele', async () => {
    renderizar('/empresas', tokenDoGestor());

    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Nova empresa' })).not.toBeInTheDocument();
  });
});
