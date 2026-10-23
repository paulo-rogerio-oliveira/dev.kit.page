import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { entregues } from '../testes/navegador';
import { API, downloads, servidor, tokenDoDev, tokenDoGestor, tokenValido } from '../testes/servidor';

describe('Vitrine do catálogo (US #425; visual da US #461)', () => {
  it('é pública e agrupa os itens por tecnologia, com o tipo e a versão, e a pílula da versão', async () => {
    renderizar('/catalogo');

    const git = await screen.findByRole('region', { name: 'Git' });
    expect(screen.getByRole('region', { name: 'Angular' })).toBeInTheDocument();
    expect(within(git).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(['revisar-alteracoes', 'tarefa-avulsa']);
    expect(within(git).getByText('Fluxo')).toHaveClass('insignia');
    expect(within(git).getByText('versão 2')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'Catálogo do dev.kit' })).toBeInTheDocument();
    expect(screen.getByText(/Chegam sozinhos a cada máquina quando sai uma versão nova/)).toBeInTheDocument();
    expect(screen.getByText('Versão 2 · publicada em 08/10/2026 · 3 itens')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/login');
    // Sem sessão não há abas: a vitrine é tudo o que se vê.
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
  });

  it('busca no nome e na descrição, com os chips contando o que cada um mostraria', async () => {
    renderizar('/catalogo');
    await screen.findByRole('region', { name: 'Git' });
    expect(screen.getByRole('button', { name: 'Todos 3' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Skills 2' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Fluxos 1' })).toBeInTheDocument();

    await userEvent.type(screen.getByRole('searchbox', { name: 'Buscar no catálogo' }), 'commit');

    expect(screen.getByText('revisar-alteracoes')).toBeInTheDocument();
    expect(screen.queryByText('compilar-angular')).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Angular' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Todos 1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Fluxos 0' })).toBeInTheDocument();
  });

  it('os chips filtram pelo tipo, e a categoria da barra lateral pela tecnologia', async () => {
    renderizar('/catalogo');
    await screen.findByRole('region', { name: 'Git' });

    await userEvent.click(screen.getByRole('button', { name: 'Fluxos 1' }));
    expect(screen.getByText('tarefa-avulsa')).toBeInTheDocument();
    expect(screen.queryByText('revisar-alteracoes')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Todos 3' }));
    const categorias = screen.getByRole('navigation', { name: 'Categorias' });
    await userEvent.click(within(categorias).getByRole('button', { name: 'Angular' }));
    expect(within(categorias).getByRole('button', { name: 'Angular' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('region', { name: 'Angular' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Git' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Todos 1' })).toBeInTheDocument();
  });

  it('o plan aparece como Rule: o chip "Rules", o selo e a categoria "Tech Plans" do manifesto exibida como "Rules"', async () => {
    servidor.use(http.get(`${API}/api/catalogo/base/vitrine`, () => HttpResponse.json({
      versao: 3, publicadaEm: '2026-10-22T12:00:00Z',
      grupos: [{ tecnologia: 'Tech Plans', itens: [{ tipo: 'plan', nome: 'geral', descricao: 'Os padrões do time', tecnologia: 'Tech Plans', versao: 1 }] }],
    })));

    renderizar('/catalogo');

    const rules = await screen.findByRole('region', { name: 'Rules' });
    expect(within(rules).getByText('Rule')).toHaveClass('insignia');
    expect(screen.getByText('Skills, Rules e fluxos que vêm com o dev.kit. Chegam sozinhos a cada máquina quando sai uma versão nova.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Rules 1' })).toBeInTheDocument();
    expect(within(screen.getByRole('navigation', { name: 'Categorias' })).getByRole('button', { name: 'Rules' })).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(/tech ?plans?/i);
  });

  it('com o catálogo indisponível (503), diz por quê', async () => {
    servidor.use(http.get(`${API}/api/catalogo/base/vitrine`, () => HttpResponse.json({ detail: 'indisponível' }, { status: 503 })));

    renderizar('/catalogo');

    expect(await screen.findByText(/O catálogo está indisponível no momento/)).toBeInTheDocument();
  });

  it('o dev logado vê só a vitrine, mesmo pedindo a aba Releases', async () => {
    renderizar('/catalogo?aba=releases', tokenDoDev());

    expect(await screen.findByRole('region', { name: 'Git' })).toBeInTheDocument();
    expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
  });
});

describe('Releases do catálogo (US #425, a antiga /catalogo-base; aba da US #461)', () => {
  it('o admin vê as abas, e a rota antiga leva à aba Releases do básico', async () => {
    renderizar('/catalogo-base', tokenValido());

    expect(await screen.findByRole('tab', { name: 'Releases' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: 'Vitrine' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Catálogos' })).toBeInTheDocument();
    expect(await screen.findByRole('article', { name: 'Release atual catalogo-v2' })).toBeInTheDocument();
    expect(screen.getByLabelText('Catálogo')).toHaveValue('basico');
  });

  it('o card da release atual: a tag com o selo Atual, a data, os números e o SHA-256 abreviado com o copiar', async () => {
    renderizar('/catalogo?aba=releases', tokenValido());

    const atual = await screen.findByRole('article', { name: 'Release atual catalogo-v2' });
    expect(within(atual).getByText('Atual')).toHaveClass('insignia-sucesso');
    expect(within(atual).getByText(/^Publicada em 08\/10\/2026/)).toBeInTheDocument();
    expect(within(atual).getByText('Itens').nextSibling).toHaveTextContent('4');
    expect(within(atual).getByText('Retirados').nextSibling).toHaveTextContent('1');
    expect(within(atual).getByText('3,0 MB')).toBeInTheDocument();
    expect(within(atual).getByText('bbbbbbbbbbbb…')).toBeInTheDocument();

    await userEvent.click(within(atual).getByRole('button', { name: 'Copiar o SHA-256 da catalogo-v2' }));
    expect(entregues.copiado).toBe('b'.repeat(64));
  });

  it('o histórico lista as releases, a mais nova primeiro, com o rodapé', async () => {
    renderizar('/catalogo?aba=releases', tokenValido());

    const historico = await screen.findByRole('region', { name: 'Histórico' });
    const linhas = within(historico).getAllByRole('row').slice(1);
    expect(linhas[0]).toHaveTextContent(/catalogo-v2.*4.*1.*3,0 MB.*bbbbbbbbbbbb/);
    expect(linhas[1]).toHaveTextContent(/catalogo-v1.*1,0 MB.*—/);
    expect(within(historico).getByText('Releases anteriores aparecem aqui conforme forem publicadas.')).toBeInTheDocument();
  });

  it('Ver manifesto abre o JSON da release; Baixar zip baixa com o token', async () => {
    renderizar('/catalogo?aba=releases', tokenValido());
    const atual = await screen.findByRole('article', { name: 'Release atual catalogo-v2' });

    await userEvent.click(within(atual).getByRole('button', { name: 'Ver manifesto' }));
    const dialogo = await screen.findByRole('dialog', { name: 'Manifesto da catalogo-v2' });
    expect(dialogo).toHaveTextContent('"nome": "revisar-alteracoes"');
    await userEvent.click(within(dialogo).getByRole('button', { name: 'Fechar' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await userEvent.click(within(atual).getByRole('button', { name: /Baixar zip/ }));
    await waitFor(() => expect(entregues.arquivos.map((a) => a.nome)).toEqual(['catalogo-v2.zip']));
    expect(downloads).toEqual(['Bearer token-ok']);
  });

  it('o seletor troca o catálogo das releases; o que nunca publicou diz isso', async () => {
    renderizar('/catalogo?aba=releases', tokenValido());
    await screen.findByRole('article', { name: 'Release atual catalogo-v2' });
    await screen.findByRole('option', { name: 'Equipe UAU' });

    await userEvent.selectOptions(screen.getByLabelText('Catálogo'), 'equipe-uau');

    expect(await screen.findByText('Nenhuma release deste catálogo publicada ainda.')).toBeInTheDocument();
  });

  it('o gestor que abre a rota antiga vai ao dashboard; o dev, à conta', async () => {
    const { unmount } = renderizar('/catalogo-base', tokenDoGestor());
    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
    unmount();
    sessionStorage.clear();

    renderizar('/catalogo-base', tokenDoDev());
    expect(await screen.findByRole('heading', { name: 'Sua conta' })).toBeInTheDocument();
  });
});
