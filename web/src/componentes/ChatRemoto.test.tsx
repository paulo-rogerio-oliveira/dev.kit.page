import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http, HttpResponse } from 'msw';
import { transferableAbortController } from 'node:util';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';
import type { CatalogoDaMaquina, ComandoNovo, ComandoRemoto, ConversaRemota, EventoDaConversa, FalaRemota, MaquinaRemota } from '../api/remoto';

beforeAll(() => vi.stubGlobal('AbortController', transferableAbortController().constructor));
afterAll(() => vi.unstubAllGlobals());

const maquina = (id = 'notebook'): MaquinaRemota => ({
  maquinaId: id, nome: id, versaoDevKit: '88', origem: 'servico', appAberto: false, estadoDoAgente: 'ativo',
  turnoEmAndamento: false, conectada: true, ultimoHeartbeatEm: new Date().toISOString(), retratoGeradoEm: null,
});
function comando(estado: ComandoRemoto['estado'] = 'na-fila', id = crypto.randomUUID()): ComandoRemoto {
  return { id, maquinaId: 'notebook', tipo: 'mensagem', texto: `Mensagem ${estado}`, dispositivo: 'navegador', estado,
    criadoEm: new Date().toISOString(), expiraEm: new Date(Date.now() + 600000).toISOString(), entregueEm: null, concluidoEm: null, erro: '' };
}
function preparar(conversa: ConversaRemota = { comandos: [], respostas: [], ultimoEvento: 10 }, maquinas = [maquina()]) {
  servidor.use(
    http.get(`${API}/api/remoto/maquinas`, () => HttpResponse.json(maquinas)),
    http.get(`${API}/api/remoto/maquinas/:id/retrato`, () => new HttpResponse(null, { status: 404 })),
    http.get(`${API}/api/remoto/maquinas/:id/decisoes`, () => new HttpResponse(null, { status: 404 })),
    http.get(`${API}/api/remoto/maquinas/:id/conversa`, () => HttpResponse.json(conversa)),
    http.get(`${API}/api/remoto/conversa/eventos`, () => new HttpResponse('', { headers: { 'Content-Type': 'text/event-stream' } })),
  );
}
async function abrir() {
  renderizar('/board?aba=chat', tokenDoDev());
  await waitFor(() => expect(screen.getByLabelText('Mensagem ao Orquestrador')).toBeEnabled());
}

it('exibe origem e estados da API sem usar o heartbeat como confirmação de entrega', async () => {
  preparar({ comandos: [comando('entregue'), comando('expirado'), comando(), comando('pendente'), comando('recusado')],
    respostas: [{ id: 'r1', maquinaId: 'notebook', comandoId: null, tipo: 'mensagem', texto: 'Resposta real recebida', geradaEm: new Date().toISOString(), recebidaEm: new Date().toISOString() }], ultimoEvento: 10 });
  await abrir();
  const log = screen.getByRole('log');
  expect(within(log).getByText('✓ Entregue')).toBeInTheDocument();
  expect(within(log).getByText('Expirado')).toBeInTheDocument();
  expect(within(log).getByText('Enviado')).toBeInTheDocument();
  expect(within(log).getByText('Registrado')).toBeInTheDocument();
  expect(within(log).getByText('Recusado')).toBeInTheDocument();
  expect(within(log).getByText('Resposta real recebida')).toBeInTheDocument();
  expect(within(log).getAllByText(/Você · remoto · navegador/)).toHaveLength(5);
  expect(within(log).getByText(/Orquestrador · dev.kit/)).toBeInTheDocument();
  expect(screen.getByText('○ dev.kit fora do ar')).toBeInTheDocument();
});

it('separa mensagens pelo dia real e usa Hoje somente nas mensagens de hoje', async () => {
  const hoje = comando('entregue');
  const anterior = new Date(); anterior.setDate(anterior.getDate() - 2);
  const antiga = { ...comando('expirado'), criadoEm: anterior.toISOString() };
  preparar({ comandos: [hoje, antiga], respostas: [], ultimoEvento: 10 });
  await abrir();
  const log = within(screen.getByRole('log'));
  expect(log.getByText(`${anterior.toLocaleDateString('pt-BR')} · mesma conversa do dev.kit`)).toBeInTheDocument();
  expect(log.getAllByText('Hoje · mesma conversa do dev.kit')).toHaveLength(1);
  expect(log.getAllByRole('article')).toHaveLength(2);
});

function fala(autor: FalaRemota['autor'], texto: string, ditaEm: Date, extra: Partial<FalaRemota> = {}): FalaRemota {
  return { id: crypto.randomUUID().replaceAll('-', ''), maquinaId: 'notebook', autor, origem: 'desktop', texto, comandoId: null,
    ditaEm: ditaEm.toISOString(), recebidaEm: ditaEm.toISOString(), ...extra };
}

it('mostra as falas digitadas no desktop e a resposta do Orquestrador, na ordem em que foram ditas (item 8 do v3)', async () => {
  const site = comando('respondido');
  const antes = new Date(Date.now() - 120000);
  const depois = new Date(Date.now() - 60000);
  preparar({ comandos: [site], respostas: [], ultimoEvento: 10, falas: [
    fala('orquestrador', 'Resposta do Orquestrador no desktop', depois),
    fala('dev', 'Fala digitada no dev.kit', antes),
    // A fala de um comando do site já aparece como o próprio comando: não repete.
    fala('dev', site.texto, new Date(), { origem: 'remoto', comandoId: site.id }),
  ] });
  await abrir();
  const artigos = within(screen.getByRole('log')).getAllByRole('article');
  expect(artigos).toHaveLength(3);
  expect(artigos[0]).toHaveTextContent('Você · no dev.kit');
  expect(artigos[0]).toHaveTextContent('Fala digitada no dev.kit');
  expect(artigos[0]).toHaveClass('br-voce');
  expect(artigos[1]).toHaveTextContent('Orquestrador · dev.kit');
  expect(artigos[1]).toHaveTextContent('Resposta do Orquestrador no desktop');
  expect(artigos[1]).not.toHaveClass('br-voce');
  expect(artigos[2]).toHaveTextContent('Você · remoto · navegador');
});

// ----- Só a conversa (remote-control-v3, item 3) -----

it('o chat não renderiza decisão: a aprovação fica só no painel de decisões', async () => {
  preparar({ comandos: [comando('respondido')], respostas: [], ultimoEvento: 10 });
  let lidas = false;
  servidor.use(http.get(`${API}/api/remoto/maquinas/:id/decisoes`, () => { lidas = true; return HttpResponse.json([
    { pendencia: { id: 'p1', tipo: 'aprovacao', titulo: 'Integrar o item 4?' }, snapshotId: 'b'.repeat(64), bloqueio: null, resposta: null },
    { pendencia: { id: 'p2', tipo: 'pergunta', titulo: 'Seguir com o Fechamento?', opcoes: ['Seguir', 'Aguardar'] }, snapshotId: 'c'.repeat(64), bloqueio: null, resposta: null },
  ]); }));
  await abrir();
  await waitFor(() => expect(lidas).toBe(true));
  await act(() => new Promise(resolve => setTimeout(resolve, 50)));
  const log = within(screen.getByRole('log'));
  expect(log.getAllByRole('article')).toHaveLength(1);
  expect(log.queryByText('Integrar o item 4?')).not.toBeInTheDocument();
  expect(log.queryByText('Seguir com o Fechamento?')).not.toBeInTheDocument();
  expect(log.queryByRole('button', { name: 'Revisar e aprovar' })).not.toBeInTheDocument();
  expect(log.queryByRole('button', { name: 'Pedir ajuste' })).not.toBeInTheDocument();
  expect(log.queryByRole('button', { name: 'Seguir' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Revisar e aprovar' })).not.toBeInTheDocument();
});

it('não mostra os turnos automáticos do Orquestrador nem repete o mesmo aviso empilhado', async () => {
  const minutos = (n: number) => new Date(Date.now() - n * 60000);
  const aviso = 'Aviso do dev.kit: o item 4 aguarda a sua aprovação no painel de decisões.';
  const ultimoAviso = minutos(10);
  const site = comando('respondido');
  preparar({ comandos: [{ ...site, criadoEm: minutos(5).toISOString() }], ultimoEvento: 10,
    respostas: [{ id: 'r1', maquinaId: 'notebook', comandoId: site.id, tipo: 'mensagem', texto: 'Resposta à pergunta do site', geradaEm: minutos(4).toISOString(), recebidaEm: minutos(4).toISOString() }],
    falas: [
      fala('dev', 'Qual é a situação da onda 2?', minutos(50), { id: 'fd-1' }),
      fala('orquestrador', 'A onda 2 tem três itens integrados.', minutos(49), { id: 'fo-1-1' }),
      // Os turnos de lote (o git.kit marca com fl-): a resposta aos avisos, retratos e gatilhos não é conversa.
      fala('orquestrador', 'Retrato atualizado: 3 pendências.', minutos(40), { id: 'fl-a-1' }),
      fala('orquestrador', 'Gatilho "Avaliador" concluído para o item 6: nota 92.', minutos(30), { id: 'fl-b-1' }),
      // O dev.kit sem a marca: o mesmo aviso, três vezes, fica uma vez só — a última.
      fala('orquestrador', aviso, minutos(20), { id: 'fo-2-1' }),
      fala('orquestrador', `${aviso} `, minutos(15), { id: 'fo-3-1' }),
      fala('orquestrador', aviso, ultimoAviso, { id: 'fo-4-1' }),
      // A fala do dev nunca se junta, mesmo repetida.
      fala('dev', 'ok', minutos(9), { id: 'fd-2' }),
      fala('dev', 'ok', minutos(8), { id: 'fd-3' }),
    ] });
  await abrir();
  const log = within(screen.getByRole('log'));
  expect(log.queryByText(/Retrato atualizado/)).not.toBeInTheDocument();
  expect(log.queryByText(/Gatilho "Avaliador"/)).not.toBeInTheDocument();
  expect(log.getAllByText(aviso)).toHaveLength(1);
  const artigos = log.getAllByRole('article');
  expect(artigos.map(a => a.querySelector('p')?.textContent?.trim())).toEqual([
    'Qual é a situação da onda 2?', 'A onda 2 tem três itens integrados.', aviso, 'ok', 'ok',
    `${site.texto} Respondido pelo dev.kit.`, 'Resposta à pergunta do site',
  ]);
  expect(artigos[2].querySelector('time')).toHaveAttribute('dateTime', ultimoAviso.toISOString());
});

it('a fala de turno automático que chega pelo SSE não entra no chat', async () => {
  preparar();
  const automatica = fala('orquestrador', 'Aviso do dev.kit: gatilho concluído.', new Date(), { id: 'fl-c-1' });
  const conversa = fala('orquestrador', 'Resposta de conversa.', new Date(), { id: 'fo-c-1' });
  const quadro = (id: number, f: FalaRemota) => `id: ${id}\nevent: fala\ndata: ${JSON.stringify({ id, tipo: 'fala', maquinaId: 'notebook', comando: null, resposta: null, em: f.recebidaEm, fala: f })}\n\n`;
  servidor.use(http.get(`${API}/api/remoto/conversa/eventos`, () => new HttpResponse(quadro(11, automatica) + quadro(12, conversa),
    { headers: { 'Content-Type': 'text/event-stream' } })));
  await abrir();
  expect(await screen.findByText('Resposta de conversa.')).toBeInTheDocument();
  expect(screen.queryByText('Aviso do dev.kit: gatilho concluído.')).not.toBeInTheDocument();
  expect(within(screen.getByRole('log')).getAllByRole('article')).toHaveLength(1);
});

it('a fala do desktop que chega pelo SSE entra no chat sem recarregar, uma vez só', async () => {
  preparar();
  const nova = fala('dev', 'Digitada agora no desktop', new Date());
  const evento: EventoDaConversa = { id: 11, tipo: 'fala', maquinaId: 'notebook', comando: null, resposta: null, em: nova.recebidaEm, fala: nova };
  servidor.use(http.get(`${API}/api/remoto/conversa/eventos`, () => new HttpResponse(
    `id: 11\nevent: fala\ndata: ${JSON.stringify(evento)}\n\nid: 11\nevent: fala\ndata: ${JSON.stringify(evento)}\n\n`,
    { headers: { 'Content-Type': 'text/event-stream' } })));
  await abrir();
  expect(await screen.findByText('Digitada agora no desktop')).toBeInTheDocument();
  expect(within(screen.getByRole('log')).getAllByRole('article')).toHaveLength(1);
  expect(screen.getByText(/Você · no dev.kit/)).toBeInTheDocument();
});

it('preserva ID e rascunho após falha de rede e não envia duas vezes por toque duplo', async () => {
  preparar();
  const pedidos: ComandoNovo[] = [];
  servidor.use(http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
    const pedido = await request.json() as ComandoNovo;
    pedidos.push(pedido);
    await delay(50);
    if (pedidos.length === 1) return HttpResponse.error();
    return HttpResponse.json({ ...comando(), ...pedido });
  }));
  await abrir();
  const caixa = screen.getByLabelText('Mensagem ao Orquestrador');
  await userEvent.type(caixa, 'Como está o projeto?');
  await userEvent.dblClick(screen.getByRole('button', { name: 'Enviar mensagem' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Envio não confirmado');
  expect(pedidos).toHaveLength(1);
  expect(caixa).toHaveValue('Como está o projeto?');
  await userEvent.click(screen.getByRole('button', { name: 'Enviar mensagem' }));
  await waitFor(() => expect(caixa).toHaveValue(''));
  expect(pedidos).toHaveLength(2);
  expect(pedidos[0].id).toBe(pedidos[1].id);
  expect(pedidos[1]).toMatchObject({ maquinaId: 'notebook', tipo: 'mensagem', dispositivo: 'navegador', texto: 'Como está o projeto?' });
  expect(screen.getByText('Enviado')).toBeInTheDocument();
});

it('recebe SSE fragmentado, não duplica eventos e retoma do cursor na reconexão', async () => {
  preparar();
  const cursores: string[] = [];
  const c = comando('entregue');
  const evento: EventoDaConversa = { id: 11, tipo: 'comando', maquinaId: 'notebook', comando: c, resposta: null, em: c.criadoEm };
  servidor.use(http.get(`${API}/api/remoto/conversa/eventos`, ({ request }) => {
    cursores.push(request.headers.get('Last-Event-ID') ?? '');
    const encoder = new TextEncoder();
    const frame = `id: 11\r\nevent: comando\r\ndata: ${JSON.stringify(evento)}\r\n\r\n`;
    return new HttpResponse(new ReadableStream({ start(controller) {
      const bytes = encoder.encode(frame);
      controller.enqueue(bytes.slice(0, 58)); controller.enqueue(bytes.slice(58));
      controller.enqueue(encoder.encode(frame + 'event: ping\ndata: null\n\nevent: ping\ndata: \n\ndata:\n\n')); controller.close();
    } }), { headers: { 'Content-Type': 'text/event-stream' } });
  }));
  await abrir();
  await screen.findByText('✓ Entregue');
  expect(within(screen.getByRole('log')).getAllByRole('article')).toHaveLength(1);
  await waitFor(() => expect(cursores.length).toBeGreaterThanOrEqual(2), { timeout: 4500 });
  expect(cursores.slice(0, 2)).toEqual(['10', '11']);
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

it('mantém a entrega confirmada pelo SSE quando o POST responde depois com estado anterior', async () => {
  preparar();
  let canal: ReadableStreamDefaultController<Uint8Array>;
  const encoder = new TextEncoder();
  servidor.use(
    http.get(`${API}/api/remoto/conversa/eventos`, ({ request }) => new HttpResponse(new ReadableStream({ start(controller) {
      canal = controller;
      request.signal.addEventListener('abort', () => { try { controller.close(); } catch { /* Já encerrado. */ } });
    } }), { headers: { 'Content-Type': 'text/event-stream' } })),
    http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
      const pedido = await request.json() as ComandoNovo;
      const entregue = { ...comando('entregue'), ...pedido };
      canal.enqueue(encoder.encode(`data: ${JSON.stringify({ id: 11, tipo: 'comando', maquinaId: 'notebook', comando: entregue })}\n\n`));
      await delay(150);
      return HttpResponse.json({ ...entregue, estado: 'na-fila' });
    }),
  );
  await abrir();
  await userEvent.type(screen.getByLabelText('Mensagem ao Orquestrador'), 'Pedido acompanhado');
  await userEvent.click(screen.getByRole('button', { name: 'Enviar mensagem' }));
  await waitFor(() => expect(screen.getByLabelText('Mensagem ao Orquestrador')).toHaveValue(''));
  expect(screen.getByText('✓ Entregue')).toBeInTheDocument();
  expect(screen.queryByText('Enviado')).not.toBeInTheDocument();
});

it('reconectar a conversa preserva o ID de um envio sem confirmação', async () => {
  preparar();
  const ids: string[] = [];
  servidor.use(
    http.get(`${API}/api/remoto/conversa/eventos`, () => HttpResponse.json({ detail: 'Canal temporariamente indisponível' }, { status: 503 })),
    http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
      const pedido = await request.json() as ComandoNovo; ids.push(pedido.id);
      return HttpResponse.error();
    }),
  );
  await abrir();
  await userEvent.type(screen.getByLabelText('Mensagem ao Orquestrador'), 'Mantenha este envio');
  await userEvent.click(screen.getByRole('button', { name: 'Enviar mensagem' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Enviar mensagem' })).toBeEnabled());
  await userEvent.click(screen.getByRole('button', { name: 'Reconectar conversa' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Enviar mensagem' })).toBeEnabled());
  await userEvent.click(screen.getByRole('button', { name: 'Enviar mensagem' }));
  await waitFor(() => expect(ids).toHaveLength(2));
  expect(ids[1]).toBe(ids[0]);
});

it('o envio no celular registra o dispositivo e a sessão termina se o POST responder 401', async () => {
  preparar();
  vi.spyOn(window, 'matchMedia').mockImplementation(consulta => ({ matches: consulta === '(max-width:700px)' }) as MediaQueryList);
  let dispositivo: string | undefined;
  servidor.use(http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
    dispositivo = (await request.json() as ComandoNovo).dispositivo;
    return new HttpResponse(null, { status: 401 });
  }));
  try {
    await abrir();
    await userEvent.type(screen.getByLabelText('Mensagem ao Orquestrador'), 'Mensagem móvel');
    await userEvent.click(screen.getByRole('button', { name: 'Enviar mensagem' }));
    expect(await screen.findByRole('heading', { name: /Entrar/i })).toBeInTheDocument();
    expect(dispositivo).toBe('celular');
  } finally { vi.restoreAllMocks(); }
});

it('ignora conversa e envio tardios ao trocar de máquina', async () => {
  preparar(undefined, [maquina(), maquina('outra')]);
  servidor.use(http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
    const pedido = await request.json() as ComandoNovo; await delay(150);
    return HttpResponse.json({ ...comando(), ...pedido });
  }));
  await abrir();
  await userEvent.type(screen.getByLabelText('Mensagem ao Orquestrador'), 'Só para notebook');
  await userEvent.click(screen.getByRole('button', { name: 'Enviar mensagem' }));
  await userEvent.selectOptions(screen.getByLabelText('Máquina'), 'outra');
  await waitFor(() => expect(screen.getByLabelText('Mensagem ao Orquestrador')).toHaveValue(''));
  await act(() => new Promise(resolve => setTimeout(resolve, 200)));
  expect(screen.queryByText('Só para notebook')).not.toBeInTheDocument();
});

it('aplica / # @ e branch existente, Enter envia e Shift+Enter mantém a quebra', async () => {
  preparar();
  let pedido: ComandoNovo | undefined;
  servidor.use(http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
    pedido = await request.json() as ComandoNovo; return HttpResponse.json({ ...comando(), ...pedido });
  }));
  await abrir();
  const caixa = screen.getByLabelText('Mensagem ao Orquestrador');
  await userEvent.click(caixa); await userEvent.keyboard('/');
  await userEvent.type(screen.getByRole('textbox', { name: 'Fluxo, projeto ou design' }), 'Tela');
  await userEvent.click(screen.getByRole('button', { name: 'Aplicar contexto' }));
  await userEvent.click(screen.getByRole('button', { name: '# Work item' }));
  await userEvent.type(screen.getByRole('textbox', { name: 'Work item' }), '7');
  await userEvent.click(screen.getByRole('button', { name: 'Aplicar contexto' }));
  await userEvent.click(screen.getByRole('button', { name: '@ Repositório' }));
  await userEvent.type(screen.getByRole('textbox', { name: 'Repositório' }), 'dev.kit.page');
  await userEvent.type(screen.getByRole('textbox', { name: 'Branch existente' }), 'feature/item-7');
  await userEvent.click(screen.getByRole('button', { name: 'Aplicar contexto' }));
  await userEvent.click(caixa); await userEvent.type(caixa, 'Mostre');
  await userEvent.keyboard('{Shift>}{Enter}{/Shift}'); await userEvent.type(caixa, 'o projeto');
  expect(pedido).toBeUndefined();
  await userEvent.keyboard('{Enter}');
  await waitFor(() => expect(pedido?.texto).toBe('/ Tela\n# 7\n@ dev.kit.page\nBranch existente: feature/item-7\nMostre\no projeto'));
});

it.each([409, 422, 429, 503])('mostra recusa HTTP %s preservando o rascunho', async status => {
  preparar();
  servidor.use(http.post(`${API}/api/remoto/comandos`, () => HttpResponse.json({ detail: 'Recusa de demonstração' }, { status })));
  await abrir();
  await userEvent.type(screen.getByLabelText('Mensagem ao Orquestrador'), 'Mensagem recusada');
  await userEvent.click(screen.getByRole('button', { name: 'Enviar mensagem' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Recusa de demonstração');
  expect(screen.getByLabelText('Mensagem ao Orquestrador')).toHaveValue('Mensagem recusada');
  expect(screen.queryByText('Enviado')).not.toBeInTheDocument();
});

// ----- O catálogo dos atalhos (o handshake do chat, remote-control-v3) -----

function catalogo(recebidoEm = new Date().toISOString()): CatalogoDaMaquina {
  return { maquinaId: 'notebook', recebidoEm, catalogo: { contrato: 1, maquinaId: 'notebook', geradoEm: recebidoEm, comandoId: null,
    workItems: [{ id: 812, titulo: 'Handshake do chat remoto', tipo: 'User Story', estado: 'Active' }, { id: 815, titulo: 'Filtro de dia', tipo: 'Bug', estado: 'Active' }],
    repositorios: [{ nome: 'github.com/empresa/dev.kit.page', branchPadrao: 'main' }, { nome: 'github.com/empresa/git.kit' }],
    fluxos: [{ nome: 'Tarefa avulsa', descricao: 'Implementação avaliada' }],
    projetos: [{ slug: 'remote-control-v3', nome: 'remote-control-v3', situacao: 'aprovado' }],
    designs: [{ nome: 'github.com/empresa/dev.kit.page · main', telas: 3 }] } };
}
function pedidoDoCatalogo(estado: ComandoRemoto['estado'], id: string = crypto.randomUUID()): ComandoRemoto {
  return { ...comando(estado), id, tipo: 'catalogo', texto: '' };
}

it('com o dev.kit fechado carrega os atalhos do último catálogo, mostra a data e não pede handshake sozinho', async () => {
  const doisDias = new Date(Date.now() - 2 * 86_400_000).toISOString();
  preparar();
  const pedidos: ComandoNovo[] = [];
  servidor.use(
    http.get(`${API}/api/remoto/maquinas/:id/catalogo`, () => HttpResponse.json(catalogo(doisDias))),
    http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
      const pedido = await request.json() as ComandoNovo; pedidos.push(pedido);
      return HttpResponse.json(pedido.tipo === 'catalogo' ? pedidoDoCatalogo('na-fila', pedido.id) : { ...comando(), ...pedido }, { status: 202 });
    }),
  );
  await abrir();
  expect(await screen.findByText(/dev.kit fechado: último catálogo recebido/)).toBeInTheDocument();
  expect(screen.getByText(/há 2 dias/)).toHaveAttribute('dateTime', doisDias);

  // / : os fluxos, os projetos e os designs do catálogo; a escolha aplica e fecha.
  await userEvent.click(screen.getByRole('button', { name: 'Trocar fluxo' }));
  const barra = within(screen.getByRole('group', { name: 'Do catálogo do dev.kit: fluxo, projeto ou design' }));
  expect(barra.getByText('Fluxos')).toBeInTheDocument();
  expect(barra.getByText('Projetos')).toBeInTheDocument();
  expect(barra.getByRole('button', { name: /github.com\/empresa\/dev.kit.page · main 3 telas/ })).toBeInTheDocument();
  await userEvent.click(barra.getByRole('button', { name: /^remote-control-v3/ }));
  expect(screen.getByRole('button', { name: 'Trocar fluxo' })).toHaveTextContent('/ projeto remote-control-v3');

  // # : o texto filtra os work items em andamento.
  await userEvent.click(screen.getByRole('button', { name: '# Work item' }));
  await userEvent.type(screen.getByRole('textbox', { name: 'Work item' }), '812');
  const itens = within(screen.getByRole('group', { name: 'Do catálogo do dev.kit: work item em andamento' }));
  expect(itens.queryByRole('button', { name: /#815/ })).not.toBeInTheDocument();
  await userEvent.click(itens.getByRole('button', { name: /#812 · Handshake do chat remoto/ }));
  expect(screen.getByRole('button', { name: '# Work item' })).toHaveTextContent('# 812');

  // @ : a escolha preenche e o branch ainda é informado antes de aplicar.
  await userEvent.click(screen.getByRole('button', { name: '@ Repositório' }));
  await userEvent.click(within(screen.getByRole('group', { name: 'Do catálogo do dev.kit: repositório' })).getByRole('button', { name: 'github.com/empresa/git.kit' }));
  expect(screen.getByRole('textbox', { name: 'Repositório' })).toHaveValue('github.com/empresa/git.kit');
  await userEvent.type(screen.getByRole('textbox', { name: 'Branch existente' }), 'feature/item-1');
  await userEvent.click(screen.getByRole('button', { name: 'Aplicar contexto' }));

  const caixa = screen.getByLabelText('Mensagem ao Orquestrador');
  await userEvent.click(caixa); await userEvent.type(caixa, 'Mostre{Enter}');
  await waitFor(() => expect(pedidos.find(p => p.tipo === 'mensagem')?.texto)
    .toBe('/ projeto remote-control-v3\n# 812\n@ github.com/empresa/git.kit\nBranch existente: feature/item-1\nMostre'));
  expect(pedidos.filter(p => p.tipo === 'catalogo')).toHaveLength(0);

  // O Atualizar pede mesmo assim: o serviço guarda o pedido, e o dev.kit responde quando abrir.
  await userEvent.click(screen.getByRole('button', { name: 'Atualizar catálogo do dev.kit' }));
  await waitFor(() => expect(pedidos.filter(p => p.tipo === 'catalogo')).toHaveLength(1));
  expect(pedidos.find(p => p.tipo === 'catalogo')).toMatchObject({ maquinaId: 'notebook', texto: '' });
  expect(await screen.findByText(/o dev.kit responde quando abrir/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Atualizar catálogo do dev.kit' })).toBeDisabled();
});

it('com o dev.kit aberto pede o catálogo ao conectar, relê quando o pedido conclui e não mostra o handshake no chat', async () => {
  preparar(undefined, [{ ...maquina(), appAberto: true }]);
  let canal: ReadableStreamDefaultController<Uint8Array>;
  let publicado = false;
  const encoder = new TextEncoder();
  const pedidos: ComandoNovo[] = [];
  servidor.use(
    http.get(`${API}/api/remoto/maquinas/:id/catalogo`, () => publicado ? HttpResponse.json(catalogo()) : HttpResponse.json({ detail: 'sem catálogo' }, { status: 404 })),
    http.get(`${API}/api/remoto/conversa/eventos`, ({ request }) => new HttpResponse(new ReadableStream({ start(controller) {
      canal = controller;
      request.signal.addEventListener('abort', () => { try { controller.close(); } catch { /* Já encerrado. */ } });
    } }), { headers: { 'Content-Type': 'text/event-stream' } })),
    http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
      const pedido = await request.json() as ComandoNovo; pedidos.push(pedido);
      const naFila = pedidoDoCatalogo('na-fila', pedido.id);
      setTimeout(() => {
        publicado = true;
        const agora = new Date().toISOString();
        const resposta = { id: `ok-${pedido.id}`, maquinaId: 'notebook', comandoId: pedido.id, tipo: 'concluido', texto: 'Catálogo do chat atualizado: 2 work items.', geradaEm: agora, recebidaEm: agora };
        canal.enqueue(encoder.encode(`id: 11\nevent: resposta\ndata: ${JSON.stringify({ id: 11, tipo: 'resposta', maquinaId: 'notebook', comando: null, resposta, em: agora })}\n\n`));
        canal.enqueue(encoder.encode(`id: 12\nevent: comando\ndata: ${JSON.stringify({ id: 12, tipo: 'comando', maquinaId: 'notebook', comando: { ...naFila, estado: 'concluido', concluidoEm: agora }, resposta: null, em: agora })}\n\n`));
      }, 50);
      return HttpResponse.json(naFila, { status: 202 });
    }),
  );
  await abrir();
  expect(await screen.findByText(/Catálogo do dev.kit · atualizado/)).toBeInTheDocument();
  expect(screen.getByText(/agora há pouco/)).toBeInTheDocument();
  expect(pedidos).toHaveLength(1);
  expect(pedidos[0]).toMatchObject({ maquinaId: 'notebook', tipo: 'catalogo', texto: '' });
  expect(within(screen.getByRole('log')).queryByText(/Catálogo do chat atualizado/)).not.toBeInTheDocument();
  expect(within(screen.getByRole('log')).queryAllByRole('article')).toHaveLength(0);

  await userEvent.click(screen.getByRole('button', { name: '# Work item' }));
  expect(screen.getByRole('button', { name: /#815 · Filtro de dia/ })).toHaveTextContent('Bug · Active');
  // O catálogo novo não pede de novo ao reabrir o painel.
  expect(pedidos).toHaveLength(1);
});

it('o dev.kit antigo recusa o handshake: o chat avisa para atualizar, não mostra a recusa e não insiste ao abrir', async () => {
  const recusado = pedidoDoCatalogo('recusado', 'h-antigo');
  preparar({ comandos: [recusado], ultimoEvento: 10, respostas: [{ id: 'x-h-antigo', maquinaId: 'notebook', comandoId: 'h-antigo', tipo: 'recusado',
    texto: "Não executado: o tipo de comando 'catalogo' não é conhecido por esta versão do dev.kit.", geradaEm: recusado.criadoEm, recebidaEm: recusado.criadoEm }] },
  [{ ...maquina(), appAberto: true }]);
  const pedidos: ComandoNovo[] = [];
  servidor.use(http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
    pedidos.push(await request.json() as ComandoNovo); return new HttpResponse(null, { status: 500 });
  }));
  await abrir();
  expect(await screen.findByRole('alert')).toHaveTextContent(/não é conhecido por esta versão do dev.kit. Atualize o dev.kit desta máquina/);
  expect(within(screen.getByRole('log')).queryByText(/Não executado/)).not.toBeInTheDocument();
  expect(screen.getByText(/Atalhos sem catálogo/)).toBeInTheDocument();
  await act(() => new Promise(resolve => setTimeout(resolve, 100)));
  expect(pedidos).toHaveLength(0);

  // Sem catálogo, o atalho segue aceitando o texto livre.
  await userEvent.click(screen.getByRole('button', { name: '@ Repositório' }));
  expect(screen.getByText(/Sem catálogo do dev.kit desta máquina/)).toBeInTheDocument();
  expect(screen.queryByRole('group', { name: /Do catálogo do dev.kit/ })).not.toBeInTheDocument();
});
