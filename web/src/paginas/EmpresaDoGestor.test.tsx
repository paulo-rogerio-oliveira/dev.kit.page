import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { renderizar } from '../testes/renderizar';
import { escritas, tokenDoDev, tokenDoGestor, tokenValido } from '../testes/servidor';
import { api, ErroDaApi } from '../api/cliente';
import type { AssinanteDoItem, Pagina } from '../api/tipos';

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
    expect(await screen.findByRole('heading', { name: 'Saúde agora' })).toBeInTheDocument();
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

const assinantes = (nomes = ['Pessoa Demonstração'], total = nomes.length, numeroDaPagina = 1): Pagina<AssinanteDoItem> => ({
  itens: nomes.map((nome, indice) => ({ usuarioId: indice + 1, nome, assinadaEm: '2026-10-29T15:03:00Z' })), total, numeroDaPagina, tamanho: 20,
});

describe('Assinantes da skill (US #753)', () => {
  it('abre pelo teclado, mostra nome, total e data UTC, e devolve o foco ao fechar', async () => {
    vi.spyOn(api, 'assinantesDoItem').mockResolvedValue(assinantes());
    renderizar('/empresa/catalogo', tokenDoGestor());
    const botao = await screen.findByRole('button', { name: 'Ver assinantes de compilar-uau' });
    botao.focus();
    await userEvent.keyboard('{Enter}');
    expect(await screen.findByText('Pessoa Demonstração')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'compilar-uau' })).toHaveFocus();
    expect(screen.getByText('29/10/2026, 15:03 UTC')).toBeInTheDocument();
    expect(screen.getByText('1 usuário com assinatura registrada nesta empresa')).toBeInTheDocument();
    expect(screen.getByText(/não comprova a instalação/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Fechar' }));
    expect(botao).toHaveFocus();
    expect(screen.queryByText('Pessoa Demonstração')).not.toBeInTheDocument();
  });

  it('mostra carregamento, vazio, erro e permite tentar novamente', async () => {
    let resolver!: (dados: Pagina<AssinanteDoItem>) => void;
    const consulta = vi.spyOn(api, 'assinantesDoItem').mockImplementationOnce(() => new Promise((resolve) => { resolver = resolve; }))
      .mockRejectedValueOnce(new Error('Consulta indisponível.')).mockResolvedValue(assinantes([]));
    renderizar('/empresa/catalogo', tokenDoGestor());
    await userEvent.click(await screen.findByRole('button', { name: 'Ver assinantes de compilar-uau' }));
    expect(screen.getByRole('status')).toHaveTextContent('Carregando assinantes');
    await act(async () => resolver(assinantes([])));
    expect(await screen.findByText('Nenhum assinante registrado para esta skill.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Fechar' }));
    await userEvent.click(screen.getByRole('button', { name: 'Ver assinantes de compilar-uau' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Consulta indisponível.');
    await userEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }));
    expect(await screen.findByText('Nenhum assinante registrado para esta skill.')).toBeInTheDocument();
    expect(consulta).toHaveBeenCalledTimes(3);
  });

  it('pagina sem manter os usuários da página anterior', async () => {
    const consulta = vi.spyOn(api, 'assinantesDoItem').mockResolvedValueOnce(assinantes(['Pessoa Primeira'], 21)).mockResolvedValueOnce(assinantes(['Pessoa Última'], 21, 2));
    renderizar('/empresa/catalogo', tokenDoGestor());
    await userEvent.click(await screen.findByRole('button', { name: 'Ver assinantes de compilar-uau' }));
    expect(await screen.findByText('Pessoa Primeira')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Anterior' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Próxima' }));
    expect(await screen.findByText('Pessoa Última')).toBeInTheDocument();
    expect(screen.queryByText('Pessoa Primeira')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Próxima' })).toBeDisabled();
    expect(consulta).toHaveBeenLastCalledWith('token-gestor', 11, 2);
  });

  it('ignora a resposta atrasada ao trocar de skill e apaga dados ao receber 403', async () => {
    const { itensDaEmpresa } = await import('../testes/servidor');
    const original = itensDaEmpresa[1].tipo;
    itensDaEmpresa[1].tipo = 'skill';
    let resolver!: (dados: Pagina<AssinanteDoItem>) => void;
    vi.spyOn(api, 'assinantesDoItem').mockImplementationOnce(() => new Promise((resolve) => { resolver = resolve; }))
      .mockRejectedValueOnce(new ErroDaApi(403, 'Sem permissão.'));
    try {
      renderizar('/empresa/catalogo', tokenDoGestor());
      await userEvent.click(await screen.findByRole('button', { name: 'Ver assinantes de compilar-uau' }));
      await userEvent.click(screen.getByRole('button', { name: 'Ver assinantes de entrega-uau' }));
      expect(await screen.findByRole('alert')).toHaveTextContent('Sem permissão.');
      await act(async () => resolver(assinantes(['Pessoa Antiga'])));
      expect(screen.queryByText('Pessoa Antiga')).not.toBeInTheDocument();
      expect(screen.getByRole('heading', { name: 'entrega-uau' })).toBeInTheDocument();
    } finally { itensDaEmpresa[1].tipo = original; }
  });

  it('encerra a sessão em 401', async () => {
    vi.spyOn(api, 'assinantesDoItem').mockRejectedValue(new ErroDaApi(401, 'Sessão expirada.'));
    renderizar('/empresa/catalogo', tokenDoGestor());
    await userEvent.click(await screen.findByRole('button', { name: 'Ver assinantes de compilar-uau' }));
    expect(await screen.findByRole('heading', { name: /Entrar/ })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Fechar' })).not.toBeInTheDocument();
  });

  it('não oferece a consulta para dev nem para admin', async () => {
    const { unmount } = renderizar('/empresa/catalogo', tokenDoDev());
    expect(await screen.findByRole('heading', { name: 'Sua conta' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Ver assinantes/ })).not.toBeInTheDocument();
    unmount();
    sessionStorage.clear();
    renderizar('/empresa/catalogo', tokenValido());
    expect(await screen.findByRole('heading', { name: 'Saúde agora' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Ver assinantes/ })).not.toBeInTheDocument();
  });
});
