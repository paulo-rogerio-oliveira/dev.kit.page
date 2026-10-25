import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../../testes/renderizar';
import { API, escritas, itemDoBasico, requisicoes, servidor, tokenValido } from '../../testes/servidor';

/**
 * A aba Itens do catálogo (US #687, proposta admin-catalogo v010): a lista compacta por categoria, com a busca no
 * conteúdo e o escopo; o editor de artefato com o SKILL.md, as linhas alteradas, as diferenças, o histórico e os
 * anexos; o rascunho que se salva sozinho; o Descartar e o Publicar vN.
 */
describe('Catálogo em lista + editor de artefato (US #687)', () => {
  it('o admin abre na aba Itens: a lista por categoria e o editor do primeiro item', async () => {
    renderizar('/catalogo', tokenValido());

    expect(await screen.findByRole('tab', { name: 'Itens' })).toHaveAttribute('aria-selected', 'true');
    const lista = within(await screen.findByRole('region', { name: 'Itens do catálogo' }));
    const grupo = within(await lista.findByRole('group', { name: '.NET' }));
    const linha = grupo.getByRole('button', { name: /compilar-net-framework/ });
    expect(linha).toHaveTextContent('rascunho');
    expect(linha).toHaveTextContent('v1');
    expect(screen.getByText(/Versão 2 publicada em 08\/10\/2026 · 4 itens/)).toBeInTheDocument();

    const editor = within(await screen.findByRole('region', { name: 'Editor de compilar-net-framework' }));
    expect(await editor.findByLabelText('Conteúdo (SKILL.md)')).toHaveValue(
      '---\nname: compilar-net-framework\ndescription: Compilar projetos .NET Framework\n---\n\n# Compilar\n\n1. Ache o MSBuild.');
    expect(editor.getByText('rascunho da v2 · não publicado')).toBeInTheDocument();
    expect(editor.getByRole('button', { name: 'Publicar v2' })).toBeInTheDocument();
    expect(editor.getByText(/Validação: frontmatter ok · nome único · tamanho/)).toHaveClass('validacao-ok');
    expect(editor.getByLabelText('Escopo')).toHaveDisplayValue('Base (todos)');
    expect(editor.getByLabelText('Stack')).toBeDisabled(); // só a Rule tem stack
  });

  it('a busca vai à API (procura no conteúdo) e o escopo filtra', async () => {
    renderizar('/catalogo', tokenValido());
    await screen.findByRole('region', { name: 'Itens do catálogo' });

    await userEvent.type(screen.getByRole('searchbox', { name: 'Buscar por nome, descrição ou conteúdo' }), 'nodeReuse');
    await waitFor(() => expect(requisicoes.filter((r) => r.pathname === '/api/catalogos/itens').at(-1)?.searchParams.get('busca')).toBe('nodeReuse'));

    await userEvent.selectOptions(screen.getByLabelText('Filtrar por escopo'), 'empresa');
    await waitFor(() => expect(requisicoes.filter((r) => r.pathname === '/api/catalogos/itens').at(-1)?.searchParams.get('escopo')).toBe('empresa'));
  });

  it('as diferenças, o histórico e os anexos do item', async () => {
    renderizar('/catalogo', tokenValido());
    const editor = within(await screen.findByRole('region', { name: 'Editor de compilar-net-framework' }));
    await editor.findByLabelText('Conteúdo (SKILL.md)');

    await userEvent.click(await editor.findByRole('tab', { name: 'Diferenças com a v1' }));
    expect(editor.getByLabelText('Diferenças com a versão publicada')).toHaveTextContent('+ 3. Use -m:1 -nodeReuse:false');
    await userEvent.click(editor.getByRole('tab', { name: 'Histórico' }));
    expect(editor.getByRole('list', { name: 'Histórico do item' })).toHaveTextContent('publicada a v1 (catalogo-v1)');
    await userEvent.click(editor.getByRole('tab', { name: 'Arquivos anexos 1' }));
    expect(editor.getByRole('button', { name: 'exemplo.log' })).toBeInTheDocument();
  });

  it('o rascunho se salva sozinho depois da pausa, e o Descartar volta à versão publicada', { timeout: 15_000 }, async () => {
    servidor.use(http.put(`${API}/api/catalogos/basico/itens/31`, async ({ request }) => {
      escritas.push({ metodo: 'PUT', caminho: '/api/catalogos/basico/itens/31', corpo: await request.json() });
      return HttpResponse.json({ ...itemDoBasico, hash: 'h31b', conteudo: '{}' });
    }));
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/catalogo', tokenValido());
    const editor = within(await screen.findByRole('region', { name: 'Editor de compilar-net-framework' }));
    const conteudo = await editor.findByLabelText('Conteúdo (SKILL.md)');

    await userEvent.click(conteudo);
    await userEvent.paste('\n2. Compile só o que mudou.');
    expect(editor.getByText(/alterações por salvar/)).toBeInTheDocument();

    await waitFor(() => expect(escritas.find((e) => e.metodo === 'PUT')).toBeDefined(), { timeout: 4000 });
    const salvo = escritas.find((e) => e.metodo === 'PUT')!.corpo as { tipo: string; conteudo: string };
    expect(salvo.tipo).toBe('skill');
    expect(JSON.parse(salvo.conteudo)).toMatchObject({ name: 'compilar-net-framework', content: '# Compilar\n\n1. Ache o MSBuild.\n2. Compile só o que mudou.' });

    await userEvent.click(editor.getByRole('button', { name: 'Descartar rascunho' }));
    await waitFor(() => expect(escritas.at(-1)).toEqual({ metodo: 'POST', caminho: '/api/catalogos/basico/itens/31/descartar', corpo: null }));
    expect(confirmar).toHaveBeenCalled();
    confirmar.mockRestore();
  });

  it('o frontmatter sem o nome não salva e a validação diz por quê', async () => {
    renderizar('/catalogo', tokenValido());
    const editor = within(await screen.findByRole('region', { name: 'Editor de compilar-net-framework' }));
    const conteudo = await editor.findByLabelText('Conteúdo (SKILL.md)');

    await userEvent.clear(conteudo);
    await userEvent.paste('sem frontmatter');

    expect(editor.getByText(/Validação: frontmatter com erro/)).toHaveClass('validacao-erro');
  });

  it('o "+ Novo item" abre o editor vazio do tipo escolhido', async () => {
    renderizar('/catalogo', tokenValido());
    await screen.findByRole('region', { name: 'Itens do catálogo' });

    await userEvent.click(screen.getByRole('button', { name: '+ Novo item' }));
    await userEvent.click(within(screen.getByRole('menu', { name: 'Novo item' })).getByRole('menuitem', { name: 'Rule' }));

    const editor = within(await screen.findByRole('region', { name: 'Editor de nova-rule' }));
    expect(editor.getByLabelText('Stack')).toBeEnabled();
    expect(editor.getByText('rascunho · nunca publicado')).toBeInTheDocument();
    expect(editor.getByLabelText('Conteúdo (Rule)')).toHaveValue('As regras do time.');
  });
});
