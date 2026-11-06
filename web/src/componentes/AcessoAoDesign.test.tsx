import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { vi } from 'vitest';
import { renderizar } from '../testes/renderizar';
import { API, escritas, servidor, tokenDoGestor } from '../testes/servidor';

afterEach(() => vi.restoreAllMocks());

describe('Acesso ao projeto de design', () => {
  it('o dono vê o próprio papel, quem tem acesso e dá acesso a um usuário da empresa com o papel', async () => {
    renderizar('/empresa/conhecimento?aba=design', tokenDoGestor());

    const painel = await screen.findByRole('region', { name: 'Acesso' });
    expect(await within(painel).findByText(/Seu papel:/)).toHaveTextContent('Seu papel: Dono — criou o projeto: dá e tira acesso, e envia versões');
    expect(within(painel).getByText('Gestor A (gestor.a)')).toBeInTheDocument();
    expect(within(painel).getByLabelText('Papel de ana@empresa.com')).toHaveValue('editor');

    const dar = within(painel).getByRole('form', { name: 'Dar acesso' });
    expect(within(dar).getByRole('button', { name: 'Dar acesso' })).toBeDisabled();
    await userEvent.selectOptions(within(dar).getByLabelText('Usuário da empresa'), 'bia@empresa.com');
    await userEvent.selectOptions(within(dar).getByLabelText('Papel do acesso'), 'editor');
    await userEvent.click(within(dar).getByRole('button', { name: 'Dar acesso' }));

    await waitFor(() => expect(escritas).toEqual([
      { metodo: 'POST', caminho: '/api/design/projetos/7/acessos', corpo: { usuario: 'bia@empresa.com', papel: 'editor' } },
    ]));
    expect(await within(painel).findByText('bia@empresa.com agora é editor do projeto.')).toBeInTheDocument();
    expect(within(painel).getByLabelText('Papel de bia@empresa.com')).toHaveValue('editor');
    expect(within(painel).getByLabelText('Usuário da empresa')).toBeDisabled();
  });

  it('o dono muda o papel e remove com confirmação; sem confirmar, nada é gravado', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    renderizar('/empresa/conhecimento?aba=design', tokenDoGestor());
    const painel = await screen.findByRole('region', { name: 'Acesso' });

    await userEvent.selectOptions(await within(painel).findByLabelText('Papel de ana@empresa.com'), 'leitor');
    await waitFor(() => expect(escritas).toEqual([{ metodo: 'PUT', caminho: '/api/design/projetos/7/acessos/2', corpo: { papel: 'leitor' } }]));

    await userEvent.click(within(painel).getByRole('button', { name: 'Remover ana@empresa.com' }));
    expect(confirmar).toHaveBeenLastCalledWith(expect.stringContaining('Remover o acesso de Ana Souza (ana@empresa.com) ao design de github.com/acme/portal?'));
    expect(escritas).toHaveLength(1);

    await userEvent.click(within(painel).getByRole('button', { name: 'Remover ana@empresa.com' }));
    await waitFor(() => expect(escritas.at(-1)).toEqual({ metodo: 'DELETE', caminho: '/api/design/projetos/7/acessos/2', corpo: null }));
    expect(await within(painel).findByText('Ninguém além do dono tem acesso a este projeto.')).toBeInTheDocument();
  });

  it('quem não é o dono vê a lista e o papel, sem os controles, e a recusa da API aparece com o motivo', async () => {
    renderizar('/empresa/conhecimento?aba=design', tokenDoGestor());
    const escopo = within(await screen.findByRole('group', { name: 'Escopo' }));
    await userEvent.click(escopo.getByRole('button', { name: 'Minhas' }));

    const painel = await screen.findByRole('region', { name: 'Acesso' });
    expect(await within(painel).findByText(/Seu papel:/)).toHaveTextContent('Seu papel: Leitor — vê e baixa as versões');
    expect(within(painel).getByText('Ana Souza (ana@empresa.com)')).toBeInTheDocument();
    expect(within(painel).queryByRole('form', { name: 'Dar acesso' })).not.toBeInTheDocument();
    expect(within(painel).queryByRole('button', { name: /Remover/ })).not.toBeInTheDocument();
    expect(within(painel).getByText(/Só o dono \(Ana Souza \(ana@empresa.com\)\) dá e tira acesso/)).toBeInTheDocument();

    servidor.use(http.post(`${API}/api/design/projetos/7/acessos`, () =>
      HttpResponse.json({ detail: 'O usuário bia@empresa.com não está na empresa do projeto.' }, { status: 404 })));
    await userEvent.click(escopo.getByRole('button', { name: 'Empresa' }));
    const doDono = await screen.findByRole('region', { name: 'Acesso' });
    await userEvent.selectOptions(await within(doDono).findByLabelText('Usuário da empresa'), 'bia@empresa.com');
    await userEvent.click(within(doDono).getByRole('button', { name: 'Dar acesso' }));
    expect(await screen.findByText('O usuário bia@empresa.com não está na empresa do projeto.')).toBeInTheDocument();
  });
});
