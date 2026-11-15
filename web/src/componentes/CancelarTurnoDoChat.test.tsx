import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { transferableAbortController } from 'node:util';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';
import type { ComandoNovo, ComandoRemoto, RetratoDaMaquina } from '../api/remoto';

beforeAll(() => vi.stubGlobal('AbortController', transferableAbortController().constructor));
afterAll(() => vi.unstubAllGlobals());

function preparar(comTurno = true, conectada = true, historico?: ComandoRemoto) {
  const agora = new Date().toISOString();
  const maquina = { maquinaId: 'notebook', nome: 'Notebook', versaoDevKit: '164', origem: 'servico', appAberto: true,
    estadoDoAgente: 'ativo', turnoEmAndamento: true, conectada, ultimoHeartbeatEm: agora, retratoGeradoEm: agora };
  const foto: RetratoDaMaquina = { maquina, recebidoEm: agora, retrato: { contrato: 1, maquinaId: 'notebook', geradoEm: agora,
    projetos: [], pendencias: [], fluxos: [{ id: 'exec-task', nome: 'Task de teste', etapa: 'Implementação', situacao: 'rodando', conversavel: true }],
    ...(comTurno ? { turnos: [{ id: 'turno-orquestrador', desdeEm: agora }, { id: 'turno-task', desdeEm: agora, sessao: 'exec-task' }] } : {}),
  } };
  const conversa = { comandos: historico ? [historico] : [], respostas: [], falas: [], ultimoEvento: 0 };
  servidor.use(
    http.get(`${API}/api/remoto/maquinas`, () => HttpResponse.json([maquina])),
    http.get(`${API}/api/remoto/maquinas/:id/retrato`, () => HttpResponse.json(foto)),
    http.get(`${API}/api/remoto/maquinas/:id/decisoes`, () => HttpResponse.json([])),
    http.get(`${API}/api/remoto/maquinas/:id/conversa`, () => HttpResponse.json(conversa)),
    http.get(`${API}/api/remoto/maquinas/:id/sessoes/:sessao/conversa`, () => HttpResponse.json(conversa)),
    http.get(`${API}/api/remoto/conversa/eventos`, () => new HttpResponse('', { headers: { 'Content-Type': 'text/event-stream' } })),
  );
}
function comando(p: ComandoNovo, estado: ComandoRemoto['estado'] = 'na-fila'): ComandoRemoto {
  return { ...p, estado, criadoEm: new Date().toISOString(), expiraEm: new Date(Date.now() + 600000).toISOString(),
    entregueEm: null, concluidoEm: null, erro: '' };
}

it.each([false, true])('cancela o turno exato e aguarda confirmação na conversa da task=%s', async daTask => {
  preparar();
  const pedidos: ComandoNovo[] = [];
  servidor.use(http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
    const p = await request.json() as ComandoNovo;
    if (p.tipo === 'cancelar-turno') pedidos.push(p);
    return HttpResponse.json(comando(p), { status: 202 });
  }));
  renderizar(`/board?aba=chat${daTask ? '&task=exec-task' : ''}`, tokenDoDev());
  const botao = await screen.findByRole('button', { name: 'Cancelar turno' });
  await waitFor(() => expect(botao).toBeEnabled());
  expect(botao).toHaveTextContent('Cancelar');
  const ativa = within(screen.getByRole('article', { name: 'Mensagem ativa do agente' }));
  expect(ativa.getByRole('button', { name: 'Cancelar turno' })).toBe(botao);
  await userEvent.dblClick(botao);
  expect(pedidos).toHaveLength(1);
  expect(pedidos[0]).toMatchObject({ maquinaId: 'notebook', tipo: 'cancelar-turno', texto: daTask ? 'turno-task' : 'turno-orquestrador' });
  expect(pedidos[0].sessao).toBe(daTask ? 'exec-task' : undefined);
  expect(await ativa.findByText(/aguardando confirmação/)).toBeInTheDocument();
  expect(botao).toBeDisabled();
  expect(screen.queryByText(/Turno finalizado/)).not.toBeInTheDocument();
  expect(screen.queryByText('turno-task')).not.toBeInTheDocument();
});

it('falha de rede preserva o id e permite tentar novamente', async () => {
  preparar(); const pedidos: ComandoNovo[] = [];
  servidor.use(http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
    const p = await request.json() as ComandoNovo;
    if (p.tipo !== 'cancelar-turno') return HttpResponse.json(comando(p));
    pedidos.push(p);
    return pedidos.length === 1 ? HttpResponse.error() : HttpResponse.json(comando(p), { status: 202 });
  }));
  renderizar('/board?aba=chat', tokenDoDev());
  const botao = await screen.findByRole('button', { name: 'Cancelar turno' });
  await waitFor(() => expect(botao).toBeEnabled());
  await userEvent.click(botao);
  expect(await screen.findByRole('alert')).toHaveTextContent('Cancelamento não confirmado');
  await userEvent.click(botao);
  await screen.findByText(/Cancelamento solicitado/);
  expect(pedidos).toHaveLength(2); expect(pedidos[1].id).toBe(pedidos[0].id);
});

it('host antigo sem turno publicado não oferece cancelamento', async () => {
  preparar(false); renderizar('/board?aba=chat', tokenDoDev());
  await screen.findByLabelText('Mensagem ao Orquestrador');
  expect(screen.queryByRole('button', { name: 'Cancelar turno' })).not.toBeInTheDocument();
});

it('host desconectado mantém o cancelamento desabilitado', async () => {
  preparar(true, false); renderizar('/board?aba=chat', tokenDoDev());
  expect(await screen.findByRole('button', { name: 'Cancelar turno' })).toBeDisabled();
});

it.each(['concluido', 'recusado'] as const)('resultado durável %s aparece ao reabrir sem repetir a operação', async estado => {
  const p = comando({ id: 'c-anterior', maquinaId: 'notebook', tipo: 'cancelar-turno', texto: 'turno-orquestrador', dispositivo: 'navegador' }, estado);
  preparar(false, true, p); renderizar('/board?aba=chat', tokenDoDev());
  expect(await screen.findByText(estado === 'concluido' ? /Turno finalizado/ : /não foi executado/)).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Cancelar turno' })).not.toBeInTheDocument();
});
