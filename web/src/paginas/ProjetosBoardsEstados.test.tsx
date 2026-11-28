import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import type { ItemDoProjeto, Projeto, ResumoDoProjeto } from '../api/projetos';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';

/** (boards-estados, item 5) Aprovar e Disparar separados, a situação do disparo e o espelho só leitura no site. */
const item = (numero: number, onda: number, extra: Partial<ItemDoProjeto> = {}): ItemDoProjeto => ({
  numero, titulo: `Item ${numero}`, descricao: '', fluxo: '14', repositorios: [], componentes: ['web'], dependencias: [], onda,
  workItemId: 0, situacao: 'planejado', tipo: '', prioridade: 1, horas: 0, branch: '', agente: 'codex', modelo: 'gpt-6.1-sol', ...extra });

const aprovado: Projeto = {
  id: 9, slug: 'boards-estados', nome: 'Projetos em qualquer nível do Boards', nivel: 'usuario', time: '',
  repositorios: ['https://github.com/acme/dev.kit.page'], branch: 'feature/dev/projeto-boards-estados', semanaConvertida: false,
  definicao: 'Espelho em qualquer nível.', anexos: [], autonomia: true, paralelismo: 2, destinoDaPr: 'develop', situacao: 'aprovado',
  versao: 4, autor: 'Dev', atualizadoEm: '2026-11-27T12:00:00Z', podeApagar: true, papel: 'dono', featureId: 812, epicId: 0,
  iteracao: '', board: 'criar', agente: 'codex', modelo: 'gpt-6.1-sol', modelos: [{ agente: 'codex', modelo: 'gpt-6.1-sol' }],
  nivelNoBoard: 'feature', estadosNoBoard: { pai: { inicial: '', execucao: '', finalizacao: '' }, itens: { inicial: '', execucao: '', finalizacao: 'Resolved' } },
  disparadoEm: null,
  itens: [
    item(1, 1, { titulo: 'Espelho configurável', workItemId: 813, tipo: 'User Story' }),
    item(2, 2, { titulo: 'Separar Aprovar de Disparar', dependencias: [1], workItemId: 814, tipo: 'User Story' }),
    item(3, 3, { titulo: 'Aplicar os estados', dependencias: [1, 2], workItemId: 815, tipo: 'User Story' }),
    item(4, 3, { titulo: 'Corrigir o nível', dependencias: [1], workItemId: 816, tipo: 'Bug' }),
    item(5, 1, { titulo: 'Board web: Aprovar e Disparar', workItemId: 817, tipo: 'User Story' }),
    item(6, 3, { titulo: 'Desktop', dependencias: [2], workItemId: 818, tipo: 'User Story' }),
  ],
};
const aguardando: Projeto = { ...aprovado, situacao: 'aguardando', featureId: 0, itens: aprovado.itens.map(i => ({ ...i, workItemId: 0 })) };

const resumo = (p: Projeto, extra: Partial<ResumoDoProjeto> = {}): ResumoDoProjeto => ({
  id: p.id, slug: p.slug, nome: p.nome, nivel: p.nivel, time: p.time, situacao: p.situacao, versao: p.versao, atualizadoEm: p.atualizadoEm,
  papel: p.papel!, totalDeItens: p.itens.length, itensConcluidos: p.itens.filter(i => i.situacao === 'integrado').length,
  itensEmExecucao: p.itens.filter(i => i.situacao === 'rodando').length, itensComPergunta: 0, itensFalhos: 0, progresso: 0,
  temPendencias: false, disparadoEm: p.disparadoEm, ...extra });

function servir(p: Projeto, pendencia: string | null = null, outros: ResumoDoProjeto[] = []) {
  const prontidao: string[] = [];
  const comandos: { tipo: string; texto: string; maquinaId: string }[] = [];
  servidor.use(
    http.get(`${API}/api/projetos/resumos`, () => HttpResponse.json([resumo(p), ...outros])),
    http.get(`${API}/api/projetos/:id`, () => HttpResponse.json(p)),
    http.get(`${API}/api/projetos/:id/prontidao-disparo`, ({ request }) => {
      prontidao.push(new URL(request.url).searchParams.get('acao') ?? '');
      return HttpResponse.json({ pendencia });
    }),
    http.get(`${API}/api/projetos/:id/acessos`, () => HttpResponse.json({ recurso: 'projeto', projeto: p.id, nome: p.nome, papel: p.papel,
      dono: { id: 1, login: 'ana', nome: 'Ana' }, acessos: [], candidatos: [] })),
    http.get(`${API}/api/remoto/maquinas`, () => HttpResponse.json([{ maquinaId: 'desktop-paulo', nome: 'DESKTOP-PAULO' }])),
    http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
      const pedido = await request.json() as { id: string; tipo: string; texto: string; maquinaId: string };
      comandos.push(pedido);
      return HttpResponse.json({ ...pedido, estado: 'na-fila', erro: '' }, { status: 202 });
    }),
    http.get(`${API}/api/remoto/comandos/:id`, ({ params }) => HttpResponse.json({ id: params.id, estado: 'na-fila', erro: '' })),
  );
  return { prontidao, comandos };
}

const detalhe = async () => within(await screen.findByRole('article', { name: 'Detalhe do projeto' }));

it('aguardando aprovação: Aprovar plano manda o pedido tipado aprovar, sem disparar e sem afirmar a aprovação', async () => {
  const { prontidao, comandos } = servir(aguardando);
  renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  const aprovar = await screen.findByRole('button', { name: 'Aprovar plano' });
  await waitFor(() => expect(aprovar).toBeEnabled());
  expect(prontidao).toEqual(['aprovar']);
  expect(screen.queryByRole('button', { name: /Disparar/ })).not.toBeInTheDocument();
  expect(screen.getByText('Aguardando aprovação', { selector: '.projeto-chip' })).toBeInTheDocument();
  await userEvent.click(aprovar);
  expect(aprovar).toHaveAttribute('aria-expanded', 'true');
  const pedido = within(screen.getByRole('group', { name: 'Aprovar plano · Projetos em qualquer nível do Boards' }));
  expect(pedido.getByText(/Aprovar cria a Feature e os itens no Azure Boards como New e não dispara\./)).toBeInTheDocument();
  const enviar = pedido.getByRole('button', { name: 'Enviar pedido' });
  await waitFor(() => expect(enviar).toBeEnabled());
  await userEvent.click(enviar);
  expect(await screen.findByText(/Pedido .*: na-fila\./)).toHaveTextContent('A execução é confirmada pelo host.');
  expect(comandos).toEqual([expect.objectContaining({ tipo: 'projeto', maquinaId: 'desktop-paulo' })]);
  expect(JSON.parse(comandos[0].texto)).toEqual({ acao: 'aprovar', projeto: 'boards-estados' });
  // Enfileirar não aprova: a situação segue a do contrato até o host sincronizar.
  expect(screen.getByText('Aguardando aprovação', { selector: '.projeto-chip' })).toBeInTheDocument();
});

it('aprovado sem marca: situação aguardando disparo, aviso do que a aprovação criou e Disparar projeto com a ação disparar', async () => {
  const { prontidao, comandos } = servir(aprovado);
  renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  const disparar = await screen.findByRole('button', { name: 'Disparar projeto' });
  await waitFor(() => expect(disparar).toBeEnabled());
  expect(prontidao).toEqual(['disparar']);
  expect(screen.queryByRole('button', { name: 'Aprovar plano' })).not.toBeInTheDocument();
  expect(screen.getByText('Aprovado · aguardando disparo', { selector: '.projeto-chip' })).toBeInTheDocument();
  const d = await detalhe();
  expect(d.getByText(/Plano aprovado · 7 work items criados/)).toHaveTextContent('Plano aprovado · 7 work items criados no board como New.');
  expect(d.getByText(/Disparar abre a onda 1/)).toHaveTextContent('com autonomia, o Orquestrador segue as próximas');
  await userEvent.click(disparar);
  const pedido = within(screen.getByRole('group', { name: 'Disparar projeto · Projetos em qualquer nível do Boards' }));
  await userEvent.click(pedido.getByRole('button', { name: 'Enviar pedido' }));
  await waitFor(() => expect(comandos).toHaveLength(1));
  expect(JSON.parse(comandos[0].texto)).toEqual({ acao: 'disparar', projeto: 'boards-estados' });
});

it('o resumo mostra o espelho só leitura: o pai, os tipos dos itens e os estados configurados, com o padrão indicado', async () => {
  servir(aprovado);
  renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  const d = await detalhe();
  const metricas = d.getByText('itens integrados').closest('.projeto-metricas') as HTMLElement;
  expect(within(metricas).getByText('Feature #812')).toBeInTheDocument();
  expect(within(metricas).getByText('escolha do projeto · itens User Story e Bug')).toBeInTheDocument();
  expect(within(metricas).getByText('modelos')).toBeInTheDocument();
  const estados = metricas.querySelector('.projeto-estados-no-board') as HTMLElement;
  expect(estados).toHaveTextContent('Itens New → Active → Resolved (configurado)');
  expect(estados).toHaveTextContent('Pai New → Active → Resolved (padrão)');
  expect(within(estados).getByText('Resolved', { selector: 'strong' })).toBeInTheDocument();
  // Só leitura: nenhum controle de edição do espelho no site.
  expect(within(metricas).queryByRole('combobox')).not.toBeInTheDocument();
  expect(within(metricas).queryByRole('textbox')).not.toBeInTheDocument();
  expect(within(metricas).queryByRole('button')).not.toBeInTheDocument();
  // O card do item: o tipo, o id e o estado configurado para a fase, ao lado das dependências.
  expect(d.getByText('User Story #813 · New')).toBeInTheDocument();
  expect(d.getByText('Depende de: 1, 2')).toBeInTheDocument();
  expect(d.getByText('Bug #816 · New')).toBeInTheDocument();
});

it('em execução (marca do host) com autonomia: nenhuma ação de aprovar nem disparar, sem conferência nem aviso', async () => {
  const { prontidao } = servir({ ...aprovado, disparadoEm: '2026-11-27T13:00:00Z',
    itens: aprovado.itens.map(i => i.onda === 1 ? { ...i, situacao: 'rodando' } : i) });
  renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  expect(await screen.findByText('Em execução', { selector: '.projeto-chip' })).toBeInTheDocument();
  expect(await screen.findByRole('button', { name: 'Gerar pacote' })).toBeEnabled();
  expect(screen.getByRole('button', { name: 'Editar projeto' })).toBeEnabled();
  expect(screen.queryByRole('button', { name: /Disparar|Aprovar plano/ })).not.toBeInTheDocument();
  expect(screen.queryByText(/Plano aprovado/)).not.toBeInTheDocument();
  expect(screen.queryByText(/bloquead/)).not.toBeInTheDocument();
  expect((await detalhe()).getByText('User Story #813 · Active')).toBeInTheDocument();
  expect(prontidao).toEqual([]);
});

it('modo manual: a onda que espera vira Disparar onda N, pelo mesmo pedido disparar', async () => {
  const { comandos } = servir({ ...aprovado, autonomia: false, disparadoEm: '2026-11-27T13:00:00Z',
    itens: aprovado.itens.map(i => i.onda === 1 ? { ...i, situacao: 'integrado' } : i) });
  renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  const disparar = await screen.findByRole('button', { name: 'Disparar onda 2' });
  await waitFor(() => expect(disparar).toBeEnabled());
  expect((await detalhe()).getByText('User Story #813 · Resolved')).toBeInTheDocument();
  await userEvent.click(disparar);
  const pedido = within(screen.getByRole('group', { name: 'Disparar onda 2 · Projetos em qualquer nível do Boards' }));
  await userEvent.click(pedido.getByRole('button', { name: 'Enviar pedido' }));
  await waitFor(() => expect(comandos).toHaveLength(1));
  expect(JSON.parse(comandos[0].texto)).toEqual({ acao: 'disparar', projeto: 'boards-estados' });
});

it.each([aguardando, aprovado])('o leitor não vê ação de aprovar ou disparar ($situacao); Gerar pacote e Editar seguem como antes', async p => {
  const { prontidao } = servir({ ...p, papel: 'leitor' });
  renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  expect(await screen.findByRole('button', { name: 'Gerar pacote' })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Editar projeto' })).toBeDisabled();
  expect(screen.queryByRole('button', { name: /Aprovar plano|Disparar/ })).not.toBeInTheDocument();
  expect(screen.queryByText(/bloquead/)).not.toBeInTheDocument();
  expect(prontidao).toEqual([]);
});

it('a pendência da matriz bloqueia o Aprovar plano com o motivo, sem pedido', async () => {
  const { comandos } = servir(aguardando, 'Pendência matrix: Projeto sem matriz definida.');
  renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  const aprovar = await screen.findByRole('button', { name: 'Aprovar plano' });
  expect(await screen.findByText('Aprovação bloqueada · conferir matriz e plano')).toBeInTheDocument();
  expect(screen.getByText(/Projeto sem matriz definida/)).toBeInTheDocument();
  expect(aprovar).toBeDisabled();
  expect(comandos).toEqual([]);
});

it('o cliente antigo sem os campos novos: Feature pelo padrão, estados no padrão e em execução pelos itens', async () => {
  const antigo = { ...aprovado, nivelNoBoard: undefined, estadosNoBoard: undefined, disparadoEm: undefined, board: 'vincular', featureId: 700,
    itens: aprovado.itens.map(i => ({ ...i, tipo: '', situacao: i.numero === 1 ? 'rodando' : 'planejado' })) };
  servir(antigo);
  renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  expect(await screen.findByText('Em execução', { selector: '.projeto-chip' })).toBeInTheDocument();
  const d = await detalhe();
  expect(d.getByText('Feature #700')).toBeInTheDocument();
  expect(d.getByText('escolha do projeto · itens requisito ou Bug')).toBeInTheDocument();
  const estados = d.getByText(/Estados no board/).closest('p') as HTMLElement;
  expect(estados).toHaveTextContent('Itens New → Active → Closed (padrão)');
  expect(estados).toHaveTextContent('Pai New → Active → Resolved (padrão)');
  expect(d.getByText('Work item #813 · Active')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /Disparar|Aprovar plano/ })).not.toBeInTheDocument();
});

it('sem espelho: o resumo diz sem Azure Boards, sem estados, e o aviso não fala de work items', async () => {
  servir({ ...aprovado, board: 'nao', featureId: 0, nivelNoBoard: null, estadosNoBoard: null, itens: aprovado.itens.map(i => ({ ...i, workItemId: 0 })) });
  renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  const d = await detalhe();
  expect(d.getByText('Sem Azure Boards')).toBeInTheDocument();
  expect(d.queryByText(/Estados no board/)).not.toBeInTheDocument();
  expect(d.getByText(/Plano aprovado · sem espelho no Azure Boards\./)).toBeInTheDocument();
  expect(d.queryByText(/#81\d/)).not.toBeInTheDocument();
  expect(d.getAllByText('Sem dependências').length).toBeGreaterThan(0);
});

it('a lista e o filtro usam a situação derivada do resumo, preservando o filtro pela situação do contrato', async () => {
  servir(aprovado, null, [
    resumo({ ...aprovado, id: 10, slug: 'em-execucao', nome: 'Em execução pela marca' }, { disparadoEm: '2026-11-27T13:00:00Z' }),
    resumo({ ...aprovado, id: 11, slug: 'antigo', nome: 'Antigo iniciado' }, { disparadoEm: undefined, itensEmExecucao: 1 }),
    resumo({ ...aguardando, id: 12, slug: 'aguardando', nome: 'Aguardando' }),
  ]);
  renderizar('/projetos?situacao=aprovado', tokenDoDev());
  const lista = within(await screen.findByRole('complementary', { name: 'Projetos sincronizados' }));
  expect(lista.getByRole('button', { name: /Projetos em qualquer nível do Boards/ })).toHaveTextContent('Aprovado · aguardando disparo');
  expect(lista.getByRole('button', { name: /Em execução pela marca/ })).toHaveTextContent('Em execução');
  expect(lista.getByRole('button', { name: /Antigo iniciado/ })).toHaveTextContent('Em execução');
  expect(lista.queryByRole('button', { name: /^Aguardando/ })).not.toBeInTheDocument();
  expect(screen.getByRole('combobox', { name: 'Situação do projeto' })).toHaveValue('aprovado');
  expect(within(screen.getByRole('combobox', { name: 'Situação do projeto' })).getByRole('option', { name: 'Aprovado' })).toBeInTheDocument();
});

it('card compacto da proposta: situação, Editar, título e work item; fluxo, modelo e repositórios em Detalhes, recolhido', async () => {
  servir(aprovado);
  renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  const d = await detalhe();
  const onda1 = await d.findByRole('region', { name: 'Onda 1' });
  // Antes do disparo nenhuma onda está em andamento: a onda 1 não é destacada como a atual.
  expect(onda1).not.toHaveAttribute('aria-current');
  const card = within(onda1).getByRole('heading', { name: '1 · Espelho configurável' }).closest('.projeto-item') as HTMLElement;
  expect(within(card).getByText('Planejado')).toHaveClass('projeto-situacao', 'planejado');
  expect(within(card).getByRole('button', { name: 'Editar item 1' })).toBeEnabled();
  expect(card).toHaveTextContent('User Story #813 · New');
  const detalhes = within(card).getByText('Detalhes').closest('details')!;
  expect(detalhes.open).toBe(false);
  expect(within(detalhes).getByText('14 · item')).toBeInTheDocument();
  expect(within(detalhes).getByText('codex · gpt-6.1-sol')).toBeInTheDocument();
  expect(within(detalhes).getByText('acme/dev.kit.page')).toBeInTheDocument();
  // O seletor mostra o slug, como a proposta; o nome fica no título da página.
  expect(screen.getByRole('combobox', { name: 'Projeto ativo' })).toHaveDisplayValue('boards-estados');
  expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Projetos em qualquer nível do Boards');
});

it('disparado: a onda atual volta a ser destacada; pausado também, e o aguardando aprovação não', async () => {
  servir({ ...aprovado, disparadoEm: '2026-11-27T19:40:00Z' });
  const { unmount } = renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  let d = await detalhe();
  expect(await d.findByRole('region', { name: 'Onda 1' })).toHaveAttribute('aria-current', 'step');
  expect(d.getByRole('region', { name: 'Onda 2' })).not.toHaveAttribute('aria-current');
  unmount();
  servir({ ...aprovado, situacao: 'pausado', disparadoEm: '2026-11-27T19:40:00Z' });
  renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  d = await detalhe();
  expect(await d.findByRole('region', { name: 'Onda 1' })).toHaveAttribute('aria-current', 'step');
});

it('aguardando aprovação não destaca onda atual', async () => {
  servir(aguardando);
  renderizar('/projetos?slug=boards-estados&visao=ondas', tokenDoDev());
  const d = await detalhe();
  expect(await d.findByRole('region', { name: 'Onda 1' })).not.toHaveAttribute('aria-current');
});
