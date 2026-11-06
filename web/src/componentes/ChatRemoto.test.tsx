import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http, HttpResponse } from 'msw';
import { transferableAbortController } from 'node:util';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';
import type { ComandoNovo, ComandoRemoto, ConversaRemota, EventoDaConversa, MaquinaRemota } from '../api/remoto';

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
  await userEvent.type(screen.getByRole('textbox', { name: 'Fluxo ou design' }), 'Tela');
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
