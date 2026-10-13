import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { API, gestaoDeUsuarios, servidor, tokenDoDev, tokenDoGestor, tokenValido, usuarios } from '../testes/servidor';

/** A linha da tabela de um usuário (pelo login, que aparece embaixo do nome ou como o próprio nome). */
async function linhaDe(login: string) {
  const tabela = (await screen.findByRole('table'));
  return within(tabela).getAllByRole('row').find((r) => within(r).queryByText(login) !== null)!;
}

/** Abre o menu ⋯ da linha e escolhe a ação. */
async function acao(login: string, rotulo: string) {
  await userEvent.click(await screen.findByRole('button', { name: `Ações de ${login}` }));
  await userEvent.click(within(screen.getByRole('menu', { name: `Ações de ${login}` })).getByRole('menuitem', { name: rotulo }));
}

describe('Usuários (US #405; visual da US #461)', () => {
  it('lista os usuários com o avatar, o nome, o papel, a empresa e a situação — sem senha nenhuma', async () => {
    renderizar('/usuarios', tokenValido());

    const ana = await linhaDe('ana.dev');
    expect(within(ana).getByText('Ana Souza')).toBeInTheDocument();
    expect(within(ana).getByText('AS', { selector: '.avatar' })).toBeInTheDocument();
    expect(within(ana).getByText('Dev')).toHaveClass('insignia-neutro');
    expect(within(ana).getByText('Empresa A')).toBeInTheDocument();
    expect(within(ana).getByText('Troca de senha pendente')).toHaveClass('insignia-info');

    // Sem nome, o login vira o nome e o "sem nome cadastrado" vai embaixo; o admin não tem empresa.
    const admin = await linhaDe('sem nome cadastrado');
    expect(within(admin).getByText('admin')).toBeInTheDocument();
    expect(within(admin).getByText('Admin')).toHaveClass('insignia-papel');
    expect(within(admin).getByText('—')).toBeInTheDocument();
    expect(screen.getByText('2 usuários · 1 admin, 1 dev')).toBeInTheDocument();
  });

  it('a linha do próprio admin não tem o Bloquear', async () => {
    renderizar('/usuarios', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Ações de admin' }));

    const menu = screen.getByRole('menu', { name: 'Ações de admin' });
    expect(within(menu).getAllByRole('menuitem').map((m) => m.textContent)).toEqual(['Editar', 'Redefinir senha']);
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
  });

  it('busca por nome ou login e filtra por papel e por empresa, no navegador', async () => {
    servidor.use(http.get(`${API}/api/usuarios`, () => HttpResponse.json([
      ...usuarios,
      { ...usuarios[1], id: 3, login: 'jose.gestor', nome: 'José Lima', papel: 'gestor', empresaId: 5, empresa: 'Empresa B', deveTrocarSenha: false },
    ])));
    renderizar('/usuarios', tokenValido());
    await screen.findByText('José Lima');
    expect(screen.getByText('3 usuários · 1 admin, 1 dev, 1 gestor')).toBeInTheDocument();

    await userEvent.type(screen.getByRole('searchbox', { name: 'Buscar por nome ou login' }), 'jose');
    expect(screen.getByText('José Lima')).toBeInTheDocument();
    expect(screen.queryByText('Ana Souza')).not.toBeInTheDocument();

    await userEvent.clear(screen.getByRole('searchbox'));
    await userEvent.selectOptions(screen.getByLabelText('Filtrar por papel'), 'dev');
    expect(screen.getByText('Ana Souza')).toBeInTheDocument();
    expect(screen.queryByText('José Lima')).not.toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText('Filtrar por papel'), '');
    await userEvent.selectOptions(screen.getByLabelText('Filtrar por empresa'), 'nenhuma');
    expect(screen.getByText('sem nome cadastrado')).toBeInTheDocument();
    expect(screen.queryByText('Ana Souza')).not.toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText('Filtrar por empresa'), '5');
    await userEvent.selectOptions(screen.getByLabelText('Filtrar por papel'), 'dev');
    expect(screen.getByText('Nenhum usuário com esses filtros.')).toBeInTheDocument();
  });

  // Digita o formulário inteiro tecla a tecla: mais lento que o padrão de 5 s quando a suíte inteira roda junto.
  it('cria o usuário pelo painel lateral e mostra a senha temporária uma única vez', { timeout: 15_000 }, async () => {
    renderizar('/usuarios', tokenValido());
    const painel = await screen.findByRole('complementary', { name: 'Novo usuário' });
    const formulario = within(painel).getByRole('form', { name: 'Novo usuário' });
    await within(formulario).findByRole('option', { name: 'Empresa A' });
    expect(within(painel).getByText('Usa o dev.kit')).toBeInTheDocument();
    expect(within(painel).getByText(/A senha temporária aparece uma única vez depois de criar/)).toBeInTheDocument();

    await userEvent.type(within(formulario).getByLabelText('Login'), 'bia.dev');
    await userEvent.type(within(formulario).getByLabelText('Nome'), 'Bia Lima');
    await userEvent.selectOptions(within(formulario).getByLabelText('Empresa do novo usuário'), '4');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Criar usuário' }));

    const aviso = await screen.findByText('DkTEMPORARIA99a1');
    expect(aviso.closest('[role="status"]')).toHaveTextContent('ela não aparece de novo; o primeiro acesso exige a troca');
    expect(gestaoDeUsuarios).toEqual([{ metodo: 'POST', caminho: '/api/usuarios', corpo: { login: 'bia.dev', nome: 'Bia Lima', papel: 'dev', empresaId: 4 } }]);
    expect(within(formulario).getByLabelText('Login')).toHaveValue('');

    // Uma ação qualquer depois tira a senha da tela: ela não volta.
    await acao('ana.dev', 'Editar');
    expect(screen.queryByText('DkTEMPORARIA99a1')).not.toBeInTheDocument();
  });

  it('a dica embaixo do papel muda com o papel escolhido', async () => {
    renderizar('/usuarios', tokenValido());
    const formulario = await screen.findByRole('form', { name: 'Novo usuário' });

    await userEvent.selectOptions(within(formulario).getByLabelText('Papel'), 'admin');

    expect(within(formulario).getByText('Administra tudo')).toBeInTheDocument();
    expect(within(formulario).getByLabelText('Empresa do novo usuário')).toBeDisabled();
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

  it('edita o nome, o papel e a empresa pelo menu ⋯', async () => {
    renderizar('/usuarios', tokenValido());

    await acao('ana.dev', 'Editar');
    const nome = screen.getByLabelText('Nome de ana.dev');
    await userEvent.clear(nome);
    await userEvent.type(nome, 'Ana S.');
    await userEvent.selectOptions(screen.getByLabelText('Papel de ana.dev'), 'gestor');
    await userEvent.click(screen.getByRole('button', { name: 'Salvar' }));

    await waitFor(() => expect(gestaoDeUsuarios).toEqual([{ metodo: 'PUT', caminho: '/api/usuarios/2', corpo: { nome: 'Ana S.', papel: 'gestor', empresaId: 4 } }]));
    expect(await screen.findByText('Usuário ana.dev atualizado.')).toBeInTheDocument();
  });

  it('bloqueia e redefine a senha (com confirmação) pelo menu, mostrando a nova temporária', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/usuarios', tokenValido());

    await acao('ana.dev', 'Bloquear');
    expect(await screen.findByText(/ana.dev bloqueado: ele não entra até ser desbloqueado/)).toBeInTheDocument();
    await acao('ana.dev', 'Redefinir senha');

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

    await acao('ana.dev', 'Redefinir senha');

    expect(gestaoDeUsuarios).toEqual([]);
    confirmar.mockRestore();
  });

  it('o admin chega aos usuários pelo dashboard', async () => {
    renderizar('/dashboard', tokenValido());

    await userEvent.click(await screen.findByRole('link', { name: 'Usuários' }));

    expect(await screen.findByRole('heading', { name: 'Novo usuário' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Usuários' })).toHaveClass('link-ativo');
  });

  it('o gestor que abre Usuários volta ao dashboard; o dev, à conta dele (US #425)', async () => {
    const { unmount } = renderizar('/usuarios', tokenDoGestor());
    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Usuários' })).not.toBeInTheDocument();
    unmount();
    sessionStorage.clear();

    renderizar('/usuarios', tokenDoDev());
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Sua conta');
  });
});
