import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderizar } from '../testes/renderizar';
import { entregues } from '../testes/navegador';
import { cadastros, tokenDoGestor, tokenValido } from '../testes/servidor';

describe('Empresas (US #381; visual da US #461)', () => {
  it('lista as empresas com o plano, os assentos em uso, o código de adesão e o gestor', async () => {
    renderizar('/empresas', tokenValido());

    const linha = (await screen.findByText('Empresa A')).closest('tr')!;
    expect(within(linha).getByText('Empresarial')).toHaveClass('insignia');
    expect(within(linha).getByText('3 de 10 em uso')).toBeInTheDocument();
    expect(within(linha).getByRole('progressbar', { name: 'Assentos em uso de Empresa A' })).toHaveAttribute('aria-valuenow', '3');
    expect(within(linha).getByText('DK-7QH4-M2XA')).toBeInTheDocument();
    expect(within(linha).getByText('Sem gestor')).toBeInTheDocument();
    expect(within(linha).getByText('E', { selector: '.avatar' })).toBeInTheDocument();

    const outra = screen.getByText('Empresa B').closest('tr')!;
    expect(within(outra).getByText('gestor.b')).toBeInTheDocument();
    expect(within(outra).queryByText('Sem gestor')).not.toBeInTheDocument();
    expect(within(outra).getByText('1 de 1 em uso')).toBeInTheDocument();

    // O cabeçalho resume a lista; a página usa o Topo com o menu do admin.
    expect(screen.getByRole('heading', { level: 1, name: 'Empresas' })).toBeInTheDocument();
    expect(screen.getByText('2 empresas · 11 assentos contratados')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Empresas' })).toHaveClass('link-ativo');
    expect(screen.getByRole('link', { name: 'Catálogo' })).toHaveAttribute('href', '/catalogo');
  });

  it('copia o código de adesão', async () => {
    renderizar('/empresas', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Copiar o código de adesão de Empresa A' }));

    expect(entregues.copiado).toBe('DK-7QH4-M2XA');
    expect(await screen.findByText('Copiado')).toBeInTheDocument();
  });

  it('cria a empresa pelo painel lateral, com o nome, o plano e os assentos', async () => {
    renderizar('/empresas', tokenValido());
    const painel = await screen.findByRole('complementary', { name: 'Nova empresa' });
    const formulario = within(painel).getByRole('form', { name: 'Nova empresa' });
    expect(within(painel).getByText('Ao criar, a empresa recebe um código de adesão para os devs entrarem.')).toBeInTheDocument();

    await userEvent.type(within(formulario).getByLabelText('Nome'), 'Empresa C');
    await userEvent.clear(within(formulario).getByLabelText('Assentos'));
    await userEvent.type(within(formulario).getByLabelText('Assentos'), '25');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Criar empresa' }));

    await waitFor(() => expect(cadastros.empresas).toEqual([{ nome: 'Empresa C', plano: 'Empresarial', assentos: 25 }]));
    expect(within(formulario).getByLabelText('Nome')).toHaveValue('');
  });

  it('convida o gestor pelo botão da linha e mostra a senha inicial uma vez', async () => {
    renderizar('/empresas', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Convidar o gestor de Empresa A' }));
    await userEvent.type(screen.getByLabelText('Login do gestor de Empresa A'), 'gestor.a');
    await userEvent.click(screen.getByRole('button', { name: 'Enviar convite' }));

    expect(await screen.findByText('DkSENHA1234a1')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('o primeiro acesso exige a troca');
    expect(cadastros.gestores).toEqual([{ empresa: '4', login: 'gestor.a' }]);
    expect(screen.queryByLabelText('Login do gestor de Empresa A')).not.toBeInTheDocument();
  });

  it('o convite se cancela sem chamar a API', async () => {
    renderizar('/empresas', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Convidar o gestor de Empresa A' }));
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(screen.queryByLabelText('Login do gestor de Empresa A')).not.toBeInTheDocument();
    expect(cadastros.gestores).toEqual([]);
  });

  it('o gestor que abre Empresas volta ao dashboard da empresa dele', async () => {
    renderizar('/empresas', tokenDoGestor());

    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Nova empresa' })).not.toBeInTheDocument();
  });
});
