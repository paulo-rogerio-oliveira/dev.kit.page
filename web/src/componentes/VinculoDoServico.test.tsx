import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { transferableAbortController } from 'node:util';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';
import type { MaquinaRemota } from '../api/remoto';

// O fetch do Node exige o AbortSignal do próprio Node, em vez do construtor do jsdom.
beforeAll(() => vi.stubGlobal('AbortController', transferableAbortController().constructor));
afterAll(() => vi.unstubAllGlobals());

const maquina = (parcial: Partial<MaquinaRemota> = {}): MaquinaRemota => ({
  maquinaId: 'notebook', nome: 'Notebook', versaoDevKit: '125', origem: 'servico', appAberto: false, estadoDoAgente: 'parado',
  turnoEmAndamento: false, conectada: true, ultimoHeartbeatEm: new Date().toISOString(), retratoGeradoEm: null, ...parcial,
});

/** O Board com uma máquina; `desvincular` responde o DELETE do vínculo e guarda as máquinas desvinculadas. */
function preparar(m: MaquinaRemota, desvincular: () => Response = () => new HttpResponse(null, { status: 204 })) {
  const desvinculadas: string[] = [];
  servidor.use(
    http.get(`${API}/api/remoto/maquinas`, () => HttpResponse.json([{ ...m, vinculada: desvinculadas.length ? false : m.vinculada }])),
    http.get(`${API}/api/remoto/maquinas/:id/retrato`, () => new HttpResponse(null, { status: 404 })),
    http.get(`${API}/api/remoto/maquinas/:id/decisoes`, () => HttpResponse.json([])),
    http.get(`${API}/api/remoto/maquinas/:id/conversa`, () => HttpResponse.json({ comandos: [], respostas: [], ultimoEvento: 0 })),
    http.get(`${API}/api/remoto/conversa/eventos`, () => new HttpResponse('', { headers: { 'Content-Type': 'text/event-stream' } })),
    http.delete(`${API}/api/remoto/maquinas/:id/vinculo`, ({ params }) => {
      const resposta = desvincular();
      if (resposta.status === 204) desvinculadas.push(String(params.id));
      return resposta;
    }),
  );
  return desvinculadas;
}

async function cartao() {
  renderizar('/board', tokenDoDev());
  return screen.findByRole('region', { name: 'Vínculo do serviço' });
}

test('O serviço vinculado mostra o vínculo e Desvincular só revoga depois da confirmação', async () => {
  const desvinculadas = preparar(maquina({ vinculada: true }));
  const vinculo = await cartao();
  expect(vinculo).toHaveTextContent('Serviço vinculado');

  await userEvent.click(within(vinculo).getByRole('button', { name: 'Desvincular' }));
  expect(vinculo).toHaveTextContent('O heartbeat e a fila desta máquina param na hora');
  await userEvent.click(within(vinculo).getByRole('button', { name: 'Cancelar' }));
  expect(desvinculadas).toEqual([]);

  await userEvent.click(within(vinculo).getByRole('button', { name: 'Desvincular' }));
  await userEvent.click(within(vinculo).getByRole('button', { name: 'Desvincular o serviço' }));
  await waitFor(() => expect(screen.getByRole('region', { name: 'Vínculo do serviço' })).toHaveTextContent('Serviço sem vínculo'));
  expect(desvinculadas).toEqual(['notebook']);
  expect(await screen.findByText(/Serviço desvinculado: o heartbeat e a fila/)).toBeInTheDocument();
});

test('A recusa do Desvincular fica no cartão, que segue vinculado', async () => {
  preparar(maquina({ vinculada: true }), () => HttpResponse.json({ detail: 'Máquina sem vínculo, ou não é sua.' }, { status: 404 }));
  const vinculo = await cartao();

  await userEvent.click(within(vinculo).getByRole('button', { name: 'Desvincular' }));
  await userEvent.click(within(vinculo).getByRole('button', { name: 'Desvincular o serviço' }));

  expect(await within(vinculo).findByRole('alert')).toHaveTextContent('Não foi possível desvincular. Máquina sem vínculo, ou não é sua.');
  expect(within(vinculo).getByRole('button', { name: 'Desvincular o serviço' })).toBeEnabled();
});

test('Sem vínculo o cartão diz como vincular, e a API antiga (sem o campo) não mostra nada', async () => {
  preparar(maquina({ vinculada: false }));
  const vinculo = await cartao();
  expect(vinculo).toHaveTextContent('Serviço sem vínculo');
  expect(within(vinculo).queryByRole('button')).toBeNull();
});

test('A máquina da API antiga não ganha o cartão do vínculo', async () => {
  preparar(maquina());
  renderizar('/board', tokenDoDev());
  expect(await screen.findAllByText('Notebook')).not.toHaveLength(0);
  expect(screen.queryByRole('region', { name: 'Vínculo do serviço' })).toBeNull();
});
