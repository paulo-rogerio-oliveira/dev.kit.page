import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { renderizar } from '../testes/renderizar';
import { escritas, tokenDoDev, tokenDoGestor, tokenValido } from '../testes/servidor';

afterEach(() => vi.restoreAllMocks());

describe('Usuários da empresa (US #425)', () => {
  it('o gestor vê os assentos, o código de adesão e os usuários da empresa', async () => {
    renderizar('/empresa/usuarios', tokenDoGestor());

    expect(await screen.findByTestId('assentos')).toHaveTextContent('3 de 10 assentos ocupados');
    expect(screen.getByText('DK-7QH4-M2XA')).toBeInTheDocument();
    const tabela = screen.getByRole('table');
    expect(within(tabela).getAllByRole('row').slice(1).map((l) => l.firstChild!.textContent)).toEqual(['ana@empresa.com', 'bia@empresa.com', 'gestor.a']);
    // O gestor não bloqueia a si mesmo.
    expect(screen.queryByRole('button', { name: 'Bloquear gestor.a' })).not.toBeInTheDocument();
  });

  it('bloquear libera o assento; desbloquear volta a ocupar', async () => {
    renderizar('/empresa/usuarios', tokenDoGestor());

    await userEvent.click(await screen.findByRole('button', { name: 'Bloquear ana@empresa.com' }));
    expect(await screen.findByRole('status')).toHaveTextContent('ana@empresa.com bloqueado: o assento ficou livre.');
    await userEvent.click(screen.getByRole('button', { name: 'Desbloquear bia@empresa.com' }));

    await waitFor(() => expect(escritas.map((e) => [e.caminho, e.corpo])).toEqual([
      ['/api/empresa/usuarios/2/bloqueio', { bloqueado: true }],
      ['/api/empresa/usuarios/3/bloqueio', { bloqueado: false }],
    ]));
  });

  it('gera um código novo (com a confirmação) e muda a política de login', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/empresa/usuarios', tokenDoGestor());

    await userEvent.click(await screen.findByRole('button', { name: 'Gerar código novo' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Código novo: DK-NOVO-0002');
    await userEvent.selectOptions(screen.getByLabelText('Login no dev.kit'), 'sim');

    await waitFor(() => expect(escritas.at(-1)).toEqual({ metodo: 'PUT', caminho: '/api/empresa/politica', corpo: { loginObrigatorio: true } }));
    expect(screen.getByLabelText('Login no dev.kit')).toHaveValue('sim');
  });

  it('o dev vai para a conta; o admin, que não é de empresa, para o dashboard', async () => {
    const { unmount } = renderizar('/empresa/usuarios', tokenDoDev());
    expect(await screen.findByRole('heading', { name: 'Sua conta' })).toBeInTheDocument();
    unmount();
    sessionStorage.clear();

    renderizar('/empresa/usuarios', tokenValido());
    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
  });
});

describe('Catálogo da empresa (US #425)', () => {
  it('o gestor vê os itens, a versão, o autor e os assinantes, e retira o publicado', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/empresa/catalogo', tokenDoGestor());

    const tabela = (await screen.findByText('compilar-uau')).closest('table')!;
    const [skill, fluxo] = within(tabela).getAllByRole('row').slice(1);
    expect(skill).toHaveTextContent(/compilar-uau.*Skill.*3.*Ana Souza.*4.*Publicado/);
    expect(fluxo).toHaveTextContent(/entrega-uau.*Fluxo.*Retirado/);
    expect(within(fluxo).queryByRole('button', { name: /Retirar/ })).not.toBeInTheDocument();

    await userEvent.click(within(skill).getByRole('button', { name: 'Retirar compilar-uau' }));

    expect(await screen.findByRole('status')).toHaveTextContent('compilar-uau retirado do catálogo da empresa.');
    expect(escritas).toEqual([{ metodo: 'DELETE', caminho: '/api/catalogo/empresa/11', corpo: null }]);
  });

  it('sem confirmação, nada é retirado', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    renderizar('/empresa/catalogo', tokenDoGestor());

    await userEvent.click(await screen.findByRole('button', { name: 'Retirar compilar-uau' }));

    expect(escritas).toEqual([]);
  });
});
