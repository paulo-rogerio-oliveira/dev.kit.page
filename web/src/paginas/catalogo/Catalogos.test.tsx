import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../../testes/renderizar';
import { API, catalogoBasico, catalogoUau, escritas, fluxoDeExemplo, relatorioDePublicacao, requisicoes, servidor, tokenValido } from '../../testes/servidor';

/** Os testes que digitam o formulário inteiro: o userEvent digita tecla a tecla, e o editor remonta a cada uma. */
const DIGITACAO_LONGA = 20_000;

/** Abre o detalhe do catálogo Equipe UAU na aba Catálogos. */
async function abrirUau() {
  renderizar('/catalogo?aba=catalogos', tokenValido());
  await userEvent.click(await screen.findByRole('button', { name: 'Abrir Equipe UAU' }));
  return screen.findByRole('region', { name: 'Itens' });
}

describe('Aba Catálogos do admin (US #461)', () => {
  it('lista os catálogos — o básico primeiro — com a versão, as pendências e as empresas', async () => {
    renderizar('/catalogo?aba=catalogos', tokenValido());

    const lista = await screen.findByRole('region', { name: 'Catálogos cadastrados' });
    const linhas = within(lista).getAllByRole('row').slice(1);
    expect(linhas[0]).toHaveTextContent(/Catálogo básico.*basico · básico.*2.*Todas/);
    expect(linhas[1]).toHaveTextContent(/Equipe UAU.*equipe-uau.*—.*2 pendente\(s\).*Empresa A/);
    expect(screen.getByRole('tab', { name: 'Catálogos' })).toHaveAttribute('aria-selected', 'true');
  });

  it('cria o catálogo pelo painel lateral e abre o detalhe dele', async () => {
    renderizar('/catalogo?aba=catalogos', tokenValido());
    const formulario = await screen.findByRole('form', { name: 'Novo catálogo' });

    await userEvent.type(within(formulario).getByLabelText('Slug'), 'Equipe-Nova');
    await userEvent.type(within(formulario).getByLabelText('Nome'), 'Equipe Nova');
    await userEvent.type(within(formulario).getByLabelText('Descrição'), 'O catálogo novo.');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Criar catálogo' }));

    await waitFor(() => expect(escritas).toEqual([
      { metodo: 'POST', caminho: '/api/catalogos', corpo: { slug: 'equipe-nova', nome: 'Equipe Nova', descricao: 'O catálogo novo.' } },
    ]));
  });

  it('recusa o slug inválido antes da API, e mostra o detail do 409 da API', async () => {
    servidor.use(http.post(`${API}/api/catalogos`, () => HttpResponse.json({ title: 'Conflito', detail: 'Já existe um catálogo com o slug "equipe-uau".' }, { status: 409 })));
    renderizar('/catalogo?aba=catalogos', tokenValido());
    const formulario = await screen.findByRole('form', { name: 'Novo catálogo' });
    await userEvent.type(within(formulario).getByLabelText('Nome'), 'X');

    await userEvent.type(within(formulario).getByLabelText('Slug'), 'a');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Criar catálogo' }));
    expect(await within(formulario).findByRole('alert')).toHaveTextContent('de 2 a 40 caracteres');
    expect(escritas).toEqual([]);

    await userEvent.clear(within(formulario).getByLabelText('Slug'));
    await userEvent.type(within(formulario).getByLabelText('Slug'), 'equipe-uau');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Criar catálogo' }));
    await waitFor(() => expect(within(formulario).getByRole('alert')).toHaveTextContent('Já existe um catálogo com o slug "equipe-uau".'));
  });

  it('o detalhe do básico vale para todas as empresas e, vazio, oferece a importação do GitHub', async () => {
    renderizar('/catalogo?aba=catalogos&catalogo=basico', tokenValido());

    expect(await screen.findByText(/Vale para todas as empresas/)).toBeInTheDocument();
    expect(screen.queryByRole('form', { name: 'Relacionar empresa' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Excluir' })).not.toBeInTheDocument();

    await userEvent.click(await screen.findByRole('button', { name: 'Importar do GitHub' }));

    expect(await screen.findByText('Catálogo básico importado da catalogo-v2: 58 itens.')).toBeInTheDocument();
    expect(escritas).toEqual([{ metodo: 'POST', caminho: '/api/catalogos/basico/importar', corpo: null }]);
  });

  it('relaciona uma empresa e mostra a colisão que a API recusa (409)', async () => {
    await abrirUau();
    const empresas = screen.getByRole('region', { name: 'Empresas' });
    expect(within(empresas).getByText('Empresa A')).toBeInTheDocument();
    // A Empresa A já está relacionada: só a B sobra no seletor.
    await within(empresas).findByRole('option', { name: 'Empresa B' });
    expect(within(empresas).queryByRole('option', { name: 'Empresa A' })).not.toBeInTheDocument();

    servidor.use(http.post(`${API}/api/catalogos/:slug/empresas`, () => HttpResponse.json(
      { title: 'Colisão', detail: 'A Empresa B já recebe skill:compilar-uau pelo catálogo Equipe Outra.' }, { status: 409 })));
    await userEvent.selectOptions(within(empresas).getByLabelText('Empresa a relacionar'), '5');
    await userEvent.click(within(empresas).getByRole('button', { name: 'Relacionar' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('A Empresa B já recebe skill:compilar-uau pelo catálogo Equipe Outra.');
  });

  it('desrelaciona a empresa com confirmação', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(true);
    await abrirUau();

    await userEvent.click(screen.getByRole('button', { name: 'Desrelacionar Empresa A' }));

    await waitFor(() => expect(escritas).toEqual([{ metodo: 'DELETE', caminho: '/api/catalogos/equipe-uau/empresas/4', corpo: null }]));
    confirmar.mockRestore();
  });

  it('lista os itens com o tipo, a categoria e a situação, e filtra pelos chips', async () => {
    const itens = await abrirUau();

    const linhas = await waitFor(() => {
      const r = within(itens).getAllByRole('row').slice(1);
      expect(r).toHaveLength(2);
      return r;
    });
    expect(linhas[0]).toHaveTextContent(/Skill.*compilar-uau.*UAU.*—.*Novo/);
    expect(within(itens).getByText('2 pendente(s)')).toBeInTheDocument();

    await userEvent.click(within(itens).getByRole('button', { name: 'Fluxos 1' }));
    expect(within(itens).getAllByRole('row')).toHaveLength(2);
    expect(within(itens).getByText('entrega-uau')).toBeInTheDocument();
  });

  it('cria a skill pelo formulário e manda o JSON do dev.kit, na ordem dos campos', { timeout: DIGITACAO_LONGA }, async () => {
    await abrirUau();

    await userEvent.click(screen.getByRole('button', { name: 'Nova skill' }));
    const editor = screen.getByRole('form', { name: 'Nova skill' });
    await userEvent.type(within(editor).getByLabelText('Nome'), 'testar-uau');
    await userEvent.type(within(editor).getByLabelText('Descrição'), 'Roda os testes do UAU');
    await userEvent.type(within(editor).getByLabelText('Categoria'), 'UAU');
    await userEvent.type(within(editor).getByLabelText('Conteúdo'), '# Testar');
    await userEvent.type(within(editor).getByLabelText('Argumentos (um por linha)'), 'filtro');
    await userEvent.click(within(editor).getByLabelText('É comando (/nome no chat)'));
    await userEvent.click(within(editor).getByRole('button', { name: 'Salvar rascunho' }));

    await waitFor(() => expect(escritas).toHaveLength(1));
    const { caminho, corpo } = escritas[0] as { caminho: string; corpo: { tipo: string; conteudo: string; categoria: string } };
    expect(caminho).toBe('/api/catalogos/equipe-uau/itens');
    expect(corpo.tipo).toBe('skill');
    expect(corpo.categoria).toBe('UAU');
    expect(corpo.conteudo).toBe(JSON.stringify({
      name: 'testar-uau', description: 'Roda os testes do UAU', content: '# Testar', arguments: ['filtro'], repositories: [],
      isCommand: true, isBackground: false, isPinned: false,
    }, null, 2));
    expect(await screen.findByText(/Skill testar-uau criado como rascunho/)).toBeInTheDocument();
  });

  it('não manda a skill sem nome nem conteúdo: mostra os erros do app', async () => {
    await abrirUau();

    await userEvent.click(screen.getByRole('button', { name: 'Nova skill' }));
    await userEvent.click(screen.getByRole('button', { name: 'Salvar rascunho' }));

    expect(screen.getByRole('alert')).toHaveTextContent('A skill está sem nome (campo "name").');
    expect(escritas).toEqual([]);
  });

  it('edita a skill: o formulário vem do conteúdo guardado e volta pelo PUT', { timeout: DIGITACAO_LONGA }, async () => {
    await abrirUau();

    await userEvent.click(await screen.findByRole('button', { name: 'Editar compilar-uau' }));
    const editor = await screen.findByRole('form', { name: 'Editar skill compilar-uau' });
    expect(within(editor).getByLabelText('Nome')).toHaveValue('compilar-uau');
    expect(within(editor).getByLabelText('Conteúdo')).toHaveValue('# Compilar\nRode o build do UAU.');
    expect(within(editor).getByLabelText('Argumentos (um por linha)')).toHaveValue('branch');
    expect(within(editor).getByLabelText('É comando (/nome no chat)')).toBeChecked();

    await userEvent.type(within(editor).getByLabelText('Descrição'), ' (release)');
    await userEvent.click(within(editor).getByRole('button', { name: 'Salvar rascunho' }));

    await waitFor(() => expect(escritas.map((e) => e.caminho)).toEqual(['/api/catalogos/equipe-uau/itens/21']));
    expect(JSON.parse((escritas[0].corpo as { conteudo: string }).conteudo).description).toBe('Compila o UAU (release)');
  });

  it('o editor do fluxo confere o JSON e mostra a prévia dos passos', async () => {
    await abrirUau();

    await userEvent.click(screen.getByRole('button', { name: 'Novo fluxo' }));
    const editor = screen.getByRole('form', { name: 'Novo fluxo' });
    expect(within(editor).getByText(/devcli fluxos --exportar/)).toBeInTheDocument();
    const json = within(editor).getByLabelText('JSON do fluxo');

    fireEvent.change(json, { target: { value: '{ "formato": "outro" }' } });
    await userEvent.click(within(editor).getByRole('button', { name: 'Salvar rascunho' }));
    expect(within(editor).getByRole('alert')).toHaveTextContent('o campo "formato" tem de ser "devkit-fluxo"');
    expect(escritas).toEqual([]);

    fireEvent.change(json, { target: { value: JSON.stringify(fluxoDeExemplo, null, 2) } });
    const previa = within(editor).getByRole('list', { name: 'Prévia dos passos' });
    expect(within(previa).getAllByRole('listitem').map((l) => l.textContent)).toEqual(['task → Task', 'e1 → Step → compilar-uau']);

    await userEvent.click(within(editor).getByRole('button', { name: 'Salvar rascunho' }));
    await waitFor(() => expect(escritas).toHaveLength(1));
    expect(escritas[0].corpo).toEqual({ tipo: 'fluxo', conteudo: JSON.stringify(fluxoDeExemplo, null, 2) });
  });

  it('o 409 da API ao salvar (nome do básico) aparece no editor', { timeout: DIGITACAO_LONGA }, async () => {
    servidor.use(http.post(`${API}/api/catalogos/:slug/itens`, () => HttpResponse.json(
      { title: 'Colisão', detail: 'Já existe a skill "commitar-e-enviar" no catálogo básico.' }, { status: 409 })));
    await abrirUau();

    await userEvent.click(screen.getByRole('button', { name: 'Nova skill' }));
    const editor = screen.getByRole('form', { name: 'Nova skill' });
    await userEvent.type(within(editor).getByLabelText('Nome'), 'commitar-e-enviar');
    await userEvent.type(within(editor).getByLabelText('Conteúdo'), 'x');
    await userEvent.click(within(editor).getByRole('button', { name: 'Salvar rascunho' }));

    expect(await within(editor).findByRole('alert')).toHaveTextContent('Já existe a skill "commitar-e-enviar" no catálogo básico.');
  });

  it('retira o item com confirmação', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(true);
    await abrirUau();

    await userEvent.click(await screen.findByRole('button', { name: 'Retirar compilar-uau' }));

    await waitFor(() => expect(escritas).toEqual([{ metodo: 'DELETE', caminho: '/api/catalogos/equipe-uau/itens/21', corpo: null }]));
    expect(confirmar.mock.calls[0][0]).toMatch(/nunca foi publicado/);
    confirmar.mockRestore();
  });

  it('Publicar mostra o relatório; o 409 lista a dependência que falta e não sai da conferência', async () => {
    servidor.use(http.post(`${API}/api/catalogos/:slug/publicar`, () => HttpResponse.json({
      ...relatorioDePublicacao, podePublicar: false,
      problemas: ['fluxo:entrega-uau usa skill:deploy-uau, que não está neste catálogo nem no básico.'],
    }, { status: 409 })));
    await abrirUau();

    await userEvent.click(screen.getByRole('button', { name: 'Publicar' }));
    const publicacao = await screen.findByRole('region', { name: 'Publicar Equipe UAU' });
    const mudancas = await within(publicacao).findByRole('list', { name: 'Mudanças' });
    expect(within(mudancas).getAllByRole('listitem').map((l) => l.textContent)).toEqual(['+ skill:compilar-uau v1', '+ fluxo:entrega-uau v1']);
    expect(within(publicacao).getByLabelText('dev.kit mínimo')).toHaveValue(136);

    await userEvent.click(within(publicacao).getByRole('button', { name: 'Confirmar publicação' }));

    const problemas = await within(publicacao).findByRole('list', { name: 'Problemas' });
    expect(problemas).toHaveTextContent('fluxo:entrega-uau usa skill:deploy-uau, que não está neste catálogo nem no básico.');
    expect(within(publicacao).getByRole('button', { name: 'Confirmar publicação' })).toBeDisabled();
    expect(screen.getByRole('tab', { name: 'Catálogos' })).toHaveAttribute('aria-selected', 'true');
  });

  it('o dev.kit mínimo informado vai na conferência e na publicação', async () => {
    await abrirUau();
    await userEvent.click(screen.getByRole('button', { name: 'Publicar' }));
    const publicacao = await screen.findByRole('region', { name: 'Publicar Equipe UAU' });
    const minimo = await within(publicacao).findByLabelText('dev.kit mínimo');

    await userEvent.clear(minimo);
    await userEvent.type(minimo, '140');
    await userEvent.click(within(publicacao).getByRole('button', { name: 'Conferir de novo' }));

    await waitFor(() => expect(requisicoes.map((r) => r.searchParams.get('minDevKit'))).toEqual([null, '140']));
    await userEvent.click(within(publicacao).getByRole('button', { name: 'Confirmar publicação' }));
    await waitFor(() => expect(escritas).toEqual([{ metodo: 'POST', caminho: '/api/catalogos/equipe-uau/publicar', corpo: { minDevKit: 140 } }]));
  });

  it('publicado, vai para a aba Releases do catálogo, com a release nova', async () => {
    await abrirUau();

    await userEvent.click(screen.getByRole('button', { name: 'Publicar' }));
    const publicacao = await screen.findByRole('region', { name: 'Publicar Equipe UAU' });
    await within(publicacao).findByRole('list', { name: 'Mudanças' });
    await userEvent.click(within(publicacao).getByRole('button', { name: 'Confirmar publicação' }));

    expect(await screen.findByRole('article', { name: 'Release atual equipe-uau-v1' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Releases' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByLabelText('Catálogo')).toHaveValue('equipe-uau');
  });

  it('sem pendências, o Publicar fica desabilitado', async () => {
    servidor.use(http.get(`${API}/api/catalogos`, () => HttpResponse.json([catalogoBasico, { ...catalogoUau, pendentes: 0 }])));
    await abrirUau();

    expect(screen.getByRole('button', { name: 'Publicar' })).toBeDisabled();
    expect(screen.getByText('Nenhuma pendência: as máquinas têm o que está aqui.')).toBeInTheDocument();
  });
});
