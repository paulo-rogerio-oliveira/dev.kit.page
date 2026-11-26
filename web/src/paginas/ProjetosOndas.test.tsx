import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse, delay } from 'msw';
import { vi } from 'vitest';
import { api } from '../api/cliente';
import type { EdicaoDoItem, Projeto, ResumoDoProjeto } from '../api/projetos';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';

const p: Projeto = { id: 5, slug: 'fixes', nome: 'fixes', nivel: 'usuario', time: '', repositorios: ['org/app'],
  branch: 'feature/projeto-fixes', semanaConvertida: false, definicao: 'Definição', anexos: [], autonomia: true,
  paralelismo: 3, destinoDaPr: 'develop', situacao: 'aprovado', versao: 7, autor: 'Dev', atualizadoEm: '2026-11-25T12:00:00Z',
  podeApagar: true, papel: 'editor', featureId: 0, epicId: 0, iteracao: '', board: 'nao',
  fluxoPadrao: '14', agente: 'codex', modelo: 'gpt-6.1-sol', modelos: [{ agente: 'codex', modelo: 'gpt-6.1-sol' }],
  itens: [{ numero: 1, titulo: 'Cliente', descricao: 'Original', fluxo: '14', repositorios: [], componentes: ['web'],
    dependencias: [], onda: 1, workItemId: 0, situacao: 'planejado', tipo: '', prioridade: 1, horas: 0, branch: '' }] };
const resumo = (projeto: Projeto): ResumoDoProjeto => ({ ...projeto, totalDeItens: 1, itensConcluidos: 0,
  itensEmExecucao: 0, itensComPergunta: 0, itensFalhos: 0, progresso: 0, temPendencias: false, papel: projeto.papel! });
beforeEach(() => {
  servidor.use(
    http.get(`${API}/api/projetos/resumos`, () => HttpResponse.json([resumo(p), resumo({ ...p, id: 6, slug: 'outro', nome: 'Outro' })])),
    http.get(`${API}/api/projetos/:id`, ({ params }) => HttpResponse.json({ ...p, id: Number(params.id), nome: Number(params.id) === 5 ? 'fixes' : 'Outro' })),
    http.get(`${API}/api/projetos/:id/prontidao-disparo`, () => HttpResponse.json({ pendencia: 'Projeto sem matriz definida.' })),
    http.get(`${API}/api/projetos/:id/acessos`, () => HttpResponse.json({ recurso: 'projeto', projeto: 5, nome: 'fixes', papel: 'editor', dono: { id: 1, login: 'ana', nome: 'Ana' }, acessos: [], candidatos: [] })),
  );
});
afterEach(() => vi.restoreAllMocks());

it.each([1, 2])('entrada sem slug ou id oferece abertura do selecionado com %i projeto(s)', async quantidade => {
  servidor.use(http.get(`${API}/api/projetos/resumos`, () => HttpResponse.json(
    [resumo(p), resumo({ ...p, id: 6, slug: 'outro', nome: 'Outro' })].slice(0, quantidade))));
  renderizar('/projetos?nivel=usuario&extra=1', tokenDoDev());
  const abrir = await screen.findByRole('link', { name: 'Abrir projeto selecionado fixes' });
  expect(abrir).toHaveAttribute('href', '/projetos?nivel=usuario&extra=1&projeto=5');
  await userEvent.click(abrir);
  await waitFor(() => expect(within(screen.getByRole('article', { name: 'Detalhe do projeto' }))
    .getByRole('heading', { name: 'fixes' })).toHaveFocus());
});

it('destaque acompanha a primeira onda não integrada e conserva a onda real quando filtrada', async () => {
  servidor.use(http.get(`${API}/api/projetos/5`, () => HttpResponse.json({ ...p, itens: [
    { ...p.itens[0], situacao: 'integrado' },
    { ...p.itens[0], numero: 2, titulo: 'Serviço', onda: 2 },
    { ...p.itens[0], numero: 3, titulo: 'Entrega', onda: 3 },
  ] })));
  renderizar('/projetos?slug=fixes&visao=ondas', tokenDoDev());
  expect(await screen.findByRole('region', { name: 'Onda 2' })).toHaveAttribute('aria-current', 'step');
  expect(screen.getByRole('region', { name: 'Onda 1' })).not.toHaveAttribute('aria-current');
  await userEvent.type(screen.getByLabelText('Buscar item'), 'Entrega');
  expect(screen.getByRole('region', { name: 'Onda 3' })).not.toHaveAttribute('aria-current');
});

it('consulta resumos e somente o detalhe ativo; monta painéis ao abrir e reutiliza cache por versão', async () => {
  const lista = vi.spyOn(api, 'projetos');
  const detalhe = vi.spyOn(api, 'projeto');
  const matrizes = vi.spyOn(api, 'matrizes');
  renderizar('/projetos?slug=fixes&nivel=usuario&extra=1', tokenDoDev());
  await screen.findByText('Definição');
  expect(lista).not.toHaveBeenCalled();
  expect(detalhe).toHaveBeenCalledTimes(1);
  expect(detalhe.mock.calls[0][1]).toBe(5);
  expect(matrizes).not.toHaveBeenCalled();
  expect(screen.queryByRole('heading', { name: '1 · Cliente' })).not.toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: 'Ondas' }));
  expect(await screen.findByRole('heading', { name: '1 · Cliente' })).toBeInTheDocument();
  expect(detalhe).toHaveBeenCalledTimes(1);
  await userEvent.click(screen.getByRole('button', { name: 'Plano' }));
  await userEvent.click(screen.getByRole('button', { name: /Outro/ }));
  await waitFor(() => expect(detalhe).toHaveBeenCalledTimes(2));
  await userEvent.click(screen.getByRole('button', { name: /fixes/ }));
  await screen.findByText('Definição');
  expect(detalhe).toHaveBeenCalledTimes(2);
  expect(screen.getByRole('link', { name: 'Abrir projeto fixes' })).toHaveAttribute('href', '/projetos?nivel=usuario&extra=1&visao=plano&projeto=5');
});

it('aborta resposta antiga ao trocar de projeto e a tardia não substitui o atual', async () => {
  let cancelada = false;
  servidor.use(http.get(`${API}/api/projetos/5`, async ({ request }) => {
    request.signal.addEventListener('abort', () => { cancelada = true; });
    await delay(120); return HttpResponse.json(p);
  }));
  renderizar('/projetos?slug=fixes', tokenDoDev());
  // Enquanto o primeiro detalhe está pendente, um novo filtro troca o projeto ativo.
  await screen.findByText('Carregando detalhe do projeto…');
  await userEvent.selectOptions(screen.getByLabelText('Situação do projeto'), 'aguardando');
  await waitFor(() => expect(screen.queryByRole('article', { name: 'Detalhe do projeto' })).not.toBeInTheDocument());
  // A ausência de seleção também cancela a consulta em andamento.
  await waitFor(() => expect(cancelada).toBe(true));
});

it('cancelar e Escape não enviam gravação; retorna foco ao card', async () => {
  const gravar = vi.spyOn(api, 'editarItemDoProjeto');
  renderizar('/projetos?slug=fixes&visao=ondas', tokenDoDev());
  const botao = await screen.findByRole('button', { name: 'Editar item 1' });
  await userEvent.click(botao);
  const dialogo = screen.getByRole('dialog', { name: 'Editar item 1' });
  await userEvent.type(within(dialogo).getByLabelText('Título'), ' rascunho');
  fireEvent(dialogo, new Event('cancel', { bubbles: false, cancelable: true }));
  expect(gravar).not.toHaveBeenCalled();
  expect(botao).toHaveFocus();
  await userEvent.click(botao);
  await userEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
  expect(gravar).not.toHaveBeenCalled();
});

it('409 conserva rascunho; reler não substitui campos nem troca versão silenciosamente', async () => {
  const pedidos: EdicaoDoItem[] = [];
  servidor.use(http.put(`${API}/api/projetos/5/itens/1`, async ({ request }) => {
    pedidos.push(await request.json() as EdicaoDoItem);
    return HttpResponse.json({ detail: 'O projeto mudou' }, { status: 409 });
  }));
  renderizar('/projetos?slug=fixes&visao=ondas', tokenDoDev());
  await userEvent.click(await screen.findByRole('button', { name: 'Editar item 1' }));
  await userEvent.clear(screen.getByLabelText('Título'));
  await userEvent.type(screen.getByLabelText('Título'), 'Meu rascunho');
  await userEvent.click(screen.getByRole('button', { name: 'Salvar item' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('O projeto mudou');
  expect(screen.getByLabelText('Título')).toHaveValue('Meu rascunho');
  expect(screen.getByRole('button', { name: 'Salvar item' })).toBeDisabled();
  servidor.use(http.get(`${API}/api/projetos/5`, () => HttpResponse.json({ ...p, versao: 8, itens: [{ ...p.itens[0], titulo: 'Outra pessoa' }] })));
  await userEvent.click(screen.getByRole('button', { name: 'Reler versão atual' }));
  await screen.findByText('Versão 8: Outra pessoa');
  expect(screen.getByLabelText('Título')).toHaveValue('Meu rascunho');
  expect(screen.getByRole('button', { name: 'Salvar item' })).toBeDisabled();
  expect(pedidos).toHaveLength(1);
  expect(pedidos[0].versaoEsperada).toBe(7);
});

it('salvar atualiza detalhe e versão com contrato mínimo sem aprovar nem disparar', async () => {
  const comando = vi.spyOn(api, 'enviarComandoRemoto');
  let pedido: EdicaoDoItem | null = null;
  servidor.use(http.put(`${API}/api/projetos/5/itens/1`, async ({ request }) => {
    pedido = await request.json() as EdicaoDoItem;
    return HttpResponse.json({ ...p, versao: 8, situacao: 'aguardando', itens: [{ ...p.itens[0], titulo: pedido.titulo }] });
  }));
  renderizar('/projetos?slug=fixes&situacao=aprovado&visao=ondas', tokenDoDev());
  await userEvent.click(await screen.findByRole('button', { name: 'Editar item 1' }));
  await userEvent.clear(screen.getByLabelText('Título')); await userEvent.type(screen.getByLabelText('Título'), 'Atualizado');
  await userEvent.click(screen.getByRole('button', { name: 'Salvar item' }));
  expect(await screen.findByRole('heading', { name: '1 · Atualizado' })).toBeInTheDocument();
  expect(screen.getByLabelText('Situação do projeto')).toHaveValue('aprovado');
  expect(screen.getByLabelText('Projeto ativo')).toHaveValue('5');
  expect(pedido).toEqual({ versaoEsperada: 7, titulo: 'Atualizado', descricao: 'Original', fluxo: '14', agente: '', modelo: '', dependencias: [], componentes: ['web'], prioridade: 1 });
  expect(comando).not.toHaveBeenCalled();
});

it.each([401, 403, 503])('trata falha %s da consulta sem usar snapshot de outro projeto', async status => {
  servidor.use(http.get(`${API}/api/projetos/5`, () => HttpResponse.json({ detail: 'Acesso indisponível' }, { status })));
  renderizar('/projetos?slug=fixes', tokenDoDev());
  if (status === 401) expect(await screen.findByRole('heading', { name: /Entrar/i })).toBeInTheDocument();
  else expect(await screen.findByRole('alert')).toHaveTextContent('Acesso indisponível');
  expect(screen.queryByRole('button', { name: 'Editar item 1' })).not.toBeInTheDocument();
});

it('pedido enfileirado não afirma disparo e acompanha a recusa posterior do host', async () => {
  servidor.use(
    http.get(`${API}/api/projetos/5`, () => HttpResponse.json({ ...p, itens: [{ ...p.itens[0], agente: 'codex', modelo: 'gpt-6.1-sol' }] })),
    http.get(`${API}/api/projetos/5/prontidao-disparo`, () => HttpResponse.json({ pendencia: null })),
    http.get(`${API}/api/remoto/maquinas`, () => HttpResponse.json([{ maquinaId: 'host-demo', nome: 'Host isolado' }])),
    http.post(`${API}/api/remoto/comandos`, async ({ request }) => { const pedido = await request.json() as { id: string };
      return HttpResponse.json({ id: pedido.id, estado: 'na-fila', erro: '' }); }),
    http.get(`${API}/api/remoto/comandos/:id`, ({ params }) => HttpResponse.json({ id: params.id, estado: 'recusado', erro: 'A matriz mudou no host.' })),
  );
  renderizar('/projetos?slug=fixes&visao=ondas', tokenDoDev());
  const disparar = await screen.findByRole('button', { name: 'Disparar projeto' });
  await waitFor(() => expect(disparar).toBeEnabled());
  await userEvent.click(disparar);
  const enviar = await screen.findByRole('button', { name: 'Enviar pedido' });
  await waitFor(() => expect(enviar).toBeEnabled()); await userEvent.click(enviar);
  expect(await screen.findByText(/Pedido .*na-fila/)).toHaveTextContent('A execução é confirmada pelo host');
  expect(await screen.findByText(/A matriz mudou no host/)).toHaveTextContent('recusado');
  expect(screen.queryByText(/Projeto disparado/)).not.toBeInTheDocument();
});

it('falha de envio preserva ID do pedido para tentar de novo sem duplicar', async () => {
  const ids: string[] = [];
  servidor.use(
    http.get(`${API}/api/remoto/maquinas`, () => HttpResponse.json([{ maquinaId: 'host-demo', nome: 'Host isolado' }])),
    http.post(`${API}/api/remoto/comandos`, async ({ request }) => { ids.push((await request.json() as { id: string }).id);
      return HttpResponse.json({ detail: 'Fila indisponível' }, { status: 503 }); }),
  );
  renderizar('/projetos?slug=fixes', tokenDoDev());
  const enviar = await screen.findByRole('button', { name: 'Gerar pacote' });
  await userEvent.click(enviar);
  expect(await screen.findByRole('alert')).toHaveTextContent('Fila indisponível');
  await userEvent.click(enviar);
  await waitFor(() => expect(ids).toHaveLength(2));
  expect(ids[0]).toBe(ids[1]);
  expect(screen.queryByRole('button', { name: 'Enviar pedido' })).not.toBeInTheDocument();
});

it('leitor não recebe editor e item sem modelo mantém disparo bloqueado', async () => {
  servidor.use(http.get(`${API}/api/projetos/5`, () => HttpResponse.json({ ...p, papel: 'leitor' })));
  renderizar('/projetos?slug=fixes&visao=ondas', tokenDoDev());
  expect(await screen.findByRole('button', { name: 'Disparar projeto' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Editar item 1' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: '+ Item' })).not.toBeInTheDocument();
  expect(screen.getByText(/Há itens sem agente\/modelo selecionado/)).toBeInTheDocument();
});
