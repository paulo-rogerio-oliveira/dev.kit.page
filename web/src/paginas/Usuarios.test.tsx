import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { API, gestaoDeUsuarios, servidor, tokenDoDev, tokenDoGestor, tokenValido } from '../testes/servidor';

describe('Usuários (US #405)', () => {
  it('lista os usuários com o papel, a empresa e a situação — sem senha nenhuma', async () => {
    renderizar('/usuarios', tokenValido());

    const linha = (await screen.findByText('ana.dev')).closest('tr')!;
    expect(within(linha).getByText('Ana Souza')).toBeInTheDocument();
    expect(within(linha).getByText('Dev')).toBeInTheDocument();
    expect(within(linha).getByText('Empresa A')).toBeInTheDocument();
    expect(within(linha).getByText('Troca de senha pendente')).toBeInTheDocument();
    // O admin não se bloqueia: a linha dele não tem o botão.
    const admin = screen.getByRole('cell', { name: 'admin' }).closest('tr')!;
    expect(within(admin).queryByRole('button', { name: /Bloquear/ })).not.toBeInTheDocument();
  });

  it('cria o usuário e mostra a senha temporária uma única vez', async () => {
    renderizar('/usuarios', tokenValido());
    const formulario = await screen.findByRole('form', { name: 'Novo usuário' });
    await screen.findByRole('option', { name: 'Empresa A' });

    await userEvent.type(within(formulario).getByLabelText('Login'), 'bia.dev');
    await userEvent.type(within(formulario).getByLabelText('Nome'), 'Bia Lima');
    await userEvent.selectOptions(within(formulario).getByLabelText('Empresa do novo usuário'), '4');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Criar usuário' }));

    const aviso = await screen.findByText('DkTEMPORARIA99a1');
    expect(aviso.closest('[role="status"]')).toHaveTextContent('ela não aparece de novo; o primeiro acesso exige a troca');
    expect(gestaoDeUsuarios).toEqual([{ metodo: 'POST', caminho: '/api/usuarios', corpo: { login: 'bia.dev', nome: 'Bia Lima', papel: 'dev', empresaId: 4 } }]);
    expect(within(formulario).getByLabelText('Login')).toHaveValue('');

    // Uma ação qualquer depois tira a senha da tela: ela não volta.
    await userEvent.click(screen.getByRole('button', { name: 'Editar ana.dev' }));
    expect(screen.queryByText('DkTEMPORARIA99a1')).not.toBeInTheDocument();
  });

  it('o gestor sem empresa recebe o erro da API por campo', async () => {
    servidor.use(http.post(`${API}/api/usuarios`, () => HttpResponse.json(
      { title: 'Confira os dados do usuário.', errors: { empresaId: ['O gestor precisa de uma empresa.'] } }, { status: 400 })));
    renderizar('/usuarios', tokenValido());
    const formulario = await screen.findByRole('form', { name: 'Novo usuário' });

    await userEvent.type(within(formulario).getByLabelText('Login'), 'gestor.b');
    await userEvent.selectOptions(within(formulario).getByLabelText('Papel'), 'gestor');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Criar usuário' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('O gestor precisa de uma empresa.');
  });

  it('edita o nome, o papel e a empresa', async () => {
    renderizar('/usuarios', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Editar ana.dev' }));
    const nome = screen.getByLabelText('Nome de ana.dev');
    await userEvent.clear(nome);
    await userEvent.type(nome, 'Ana S.');
    await userEvent.selectOptions(screen.getByLabelText('Papel de ana.dev'), 'gestor');
    await userEvent.click(screen.getByRole('button', { name: 'Salvar' }));

    await waitFor(() => expect(gestaoDeUsuarios).toEqual([{ metodo: 'PUT', caminho: '/api/usuarios/2', corpo: { nome: 'Ana S.', papel: 'gestor', empresaId: 4 } }]));
    expect(await screen.findByText('Usuário ana.dev atualizado.')).toBeInTheDocument();
  });

  it('bloqueia e redefine a senha (com confirmação), mostrando a nova temporária', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/usuarios', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Bloquear ana.dev' }));
    expect(await screen.findByText(/ana.dev bloqueado: ele não entra até ser desbloqueado/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Redefinir a senha de ana.dev' }));

    expect(await screen.findByText('DkREDEFINIDA77a1')).toBeInTheDocument();
    expect(confirmar).toHaveBeenCalled();
    expect(gestaoDeUsuarios.map((g) => [g.metodo, g.caminho, g.corpo])).toEqual([
      ['PUT', '/api/usuarios/2/bloqueio', { bloqueado: true }],
      ['POST', '/api/usuarios/2/senha', null],
    ]);
    confirmar.mockRestore();
  });

  it('sem confirmar, a senha não é redefinida', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(false);
    renderizar('/usuarios', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Redefinir a senha de ana.dev' }));

    expect(gestaoDeUsuarios).toEqual([]);
    confirmar.mockRestore();
  });

  it('o admin chega aos usuários pelo dashboard', async () => {
    renderizar('/dashboard', tokenValido());

    await userEvent.click(await screen.findByRole('link', { name: 'Usuários' }));

    expect(await screen.findByRole('heading', { name: 'Novo usuário' })).toBeInTheDocument();
  });

  it('o gestor que abre Usuários volta ao dashboard; o dev, à página inicial', async () => {
    const { unmount } = renderizar('/usuarios', tokenDoGestor());
    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Usuários' })).not.toBeInTheDocument();
    unmount();
    sessionStorage.clear();

    renderizar('/usuarios', tokenDoDev());
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('agente de IA');
  });
});
