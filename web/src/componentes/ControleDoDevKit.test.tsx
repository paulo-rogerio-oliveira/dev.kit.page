import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { transferableAbortController } from 'node:util';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';
import type { ComandoNovo, ComandoRemoto, ConversaRemota, MaquinaRemota } from '../api/remoto';

// O fetch do Node exige o AbortSignal do próprio Node, em vez do construtor do jsdom.
beforeAll(() => vi.stubGlobal('AbortController', transferableAbortController().constructor));
afterAll(() => vi.unstubAllGlobals());

const maquina = (parcial: Partial<MaquinaRemota> = {}): MaquinaRemota => ({
  maquinaId: 'notebook', nome: 'Notebook', versaoDevKit: '95', origem: 'servico', appAberto: false, estadoDoAgente: 'parado',
  turnoEmAndamento: false, conectada: true, ultimoHeartbeatEm: new Date().toISOString(), retratoGeradoEm: null, ...parcial,
});
function comando(tipo: string, estado: ComandoRemoto['estado'], id: string = crypto.randomUUID()): ComandoRemoto {
  return { id, maquinaId: 'notebook', tipo, texto: '', dispositivo: 'navegador', estado, criadoEm: new Date().toISOString(),
    expiraEm: new Date(Date.now() + 600000).toISOString(), entregueEm: null, concluidoEm: null, erro: '' };
}

/** A API do Board, com o POST dos comandos gravado; `responder` decide a resposta de cada envio. */
function preparar(m: MaquinaRemota, conversa: ConversaRemota = { comandos: [], respostas: [], ultimoEvento: 0 },
  responder: (pedido: ComandoNovo, vez: number) => Response = pedido => HttpResponse.json(comando(pedido.tipo, 'na-fila', pedido.id), { status: 202 })) {
  const enviados: ComandoNovo[] = [];
  servidor.use(
    http.get(`${API}/api/remoto/maquinas`, () => HttpResponse.json([m])),
    http.get(`${API}/api/remoto/maquinas/:id/retrato`, () => new HttpResponse(null, { status: 404 })),
    http.get(`${API}/api/remoto/maquinas/:id/decisoes`, () => HttpResponse.json([])),
    http.get(`${API}/api/remoto/maquinas/:id/conversa`, () => HttpResponse.json(conversa)),
    http.get(`${API}/api/remoto/conversa/eventos`, () => new HttpResponse('', { headers: { 'Content-Type': 'text/event-stream' } })),
    http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
      const pedido = await request.json() as ComandoNovo;
      // O handshake do catálogo (o chat o pede ao conectar com o dev.kit aberto) não é Ligar nem Reiniciar.
      if (pedido.tipo === 'catalogo') return HttpResponse.json(comando(pedido.tipo, 'na-fila', pedido.id), { status: 202 });
      enviados.push(pedido);
      return responder(pedido, enviados.length);
    }),
  );
  return enviados;
}
async function abrir(rotulo: string) {
  renderizar('/board', tokenDoDev());
  const botao = await screen.findByRole('button', { name: rotulo });
  await waitFor(() => expect(botao).toBeEnabled());
  return botao;
}

describe('Ligar e Reiniciar o dev.kit pelo site (item 8)', () => {
  it('com o dev.kit fechado oferece Ligar e manda o comando ao serviço da máquina', async () => {
    const enviados = preparar(maquina());
    await userEvent.click(await abrir('Ligar dev.kit'));

    await waitFor(() => expect(enviados).toHaveLength(1));
    expect(enviados[0]).toMatchObject({ maquinaId: 'notebook', tipo: 'ligar', texto: '' });
    expect(enviados[0].confirmarReinicio).toBeFalsy();
    expect(await screen.findByText(/Ligar o dev.kit · .* · aguardando o serviço da máquina/)).toBeInTheDocument();
    // Um pedido por vez: o botão espera o desfecho do serviço.
    expect(screen.getByRole('button', { name: 'Ligar dev.kit' })).toBeDisabled();
  });

  it('com turno em andamento avisa, pede confirmação e só então reinicia — cancelar não manda nada', async () => {
    const enviados = preparar(maquina({ appAberto: true, estadoDoAgente: 'ativo', turnoEmAndamento: true }));
    const reiniciar = await abrir('Reiniciar dev.kit');

    await userEvent.click(reiniciar);
    const dialogo = await screen.findByRole('dialog', { name: 'Reiniciar o dev.kit com um turno em andamento?' });
    expect(within(dialogo).getByText(/Reiniciar interrompe esse turno/)).toBeInTheDocument();
    expect(within(dialogo).getByText(/As sessões voltam pelo restaurador/)).toBeInTheDocument();
    expect(within(dialogo).getByRole('button', { name: 'Cancelar' })).toHaveFocus();
    await userEvent.click(within(dialogo).getByRole('button', { name: 'Cancelar' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(enviados).toHaveLength(0);

    await userEvent.click(reiniciar);
    await userEvent.click(await screen.findByRole('button', { name: 'Reiniciar assim mesmo' }));
    await waitFor(() => expect(enviados).toHaveLength(1));
    expect(enviados[0]).toMatchObject({ tipo: 'reiniciar', confirmarReinicio: true });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('sem turno reinicia direto, sem confirmação', async () => {
    const enviados = preparar(maquina({ appAberto: true, estadoDoAgente: 'ativo' }));
    await userEvent.click(await abrir('Reiniciar dev.kit'));

    await waitFor(() => expect(enviados).toHaveLength(1));
    expect(enviados[0].tipo).toBe('reiniciar');
    expect(enviados[0].confirmarReinicio).toBeFalsy();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('o 409 de turno em andamento da API abre a confirmação, mesmo com o heartbeat antigo sem turno', async () => {
    const enviados = preparar(maquina({ appAberto: true, estadoDoAgente: 'ativo' }), undefined, (pedido, vez) => vez === 1
      ? HttpResponse.json({ detail: 'Há um turno do agente em andamento nesta máquina: reiniciar o interrompe.', codigo: 'turno-em-andamento' }, { status: 409 })
      : HttpResponse.json(comando(pedido.tipo, 'na-fila', pedido.id), { status: 202 }));
    await userEvent.click(await abrir('Reiniciar dev.kit'));

    await userEvent.click(await screen.findByRole('button', { name: 'Reiniciar assim mesmo' }));
    await waitFor(() => expect(enviados).toHaveLength(2));
    expect(enviados[0].confirmarReinicio).toBeFalsy();
    expect(enviados[1]).toMatchObject({ tipo: 'reiniciar', confirmarReinicio: true });
  });

  it('mostra o desfecho do serviço no cabeçalho e o pedido na conversa', async () => {
    const ligar = comando('ligar', 'concluido', 'l1');
    preparar(maquina({ appAberto: true, estadoDoAgente: 'ativo' }), { comandos: [ligar], ultimoEvento: 5, respostas: [{
      id: 'r1', maquinaId: 'notebook', comandoId: 'l1', tipo: 'concluido', geradaEm: new Date().toISOString(), recebidaEm: new Date().toISOString(),
      texto: 'dev.kit ligado sem janela: o Orquestrador e as sessões foram retomados.' }] });
    renderizar('/board?aba=chat', tokenDoDev());

    const faixa = await screen.findByText(/✓ Ligar o dev.kit ·/);
    expect(faixa.closest('[role="status"]')).toHaveTextContent('dev.kit ligado sem janela: o Orquestrador e as sessões foram retomados.');
    const log = within(screen.getByRole('log'));
    expect(log.getByText('Ligar o dev.kit')).toBeInTheDocument();
    expect(log.getByText(/Serviço da máquina · dev.kit/)).toBeInTheDocument();
    expect(log.queryByText('Tentar envio novamente')).not.toBeInTheDocument();
  });

  it('mostra a recusa do serviço com o motivo', async () => {
    const reiniciar = comando('reiniciar', 'recusado', 'x1');
    preparar(maquina({ appAberto: true, estadoDoAgente: 'ativo' }), { comandos: [reiniciar], ultimoEvento: 5, respostas: [{
      id: 'x-x1', maquinaId: 'notebook', comandoId: 'x1', tipo: 'recusado', geradaEm: new Date().toISOString(), recebidaEm: new Date().toISOString(),
      texto: 'Não executado: o caminho do dev.kit não está configurado no serviço.' }] });
    renderizar('/board', tokenDoDev());

    const faixa = (await screen.findByText(/Reiniciar o dev.kit · .* · não executado/)).closest('[role="status"]');
    expect(faixa).toHaveTextContent('o caminho do dev.kit não está configurado no serviço');
  });

  it('com a máquina desconectada não oferece nem Ligar nem Reiniciar', async () => {
    preparar(maquina({ conectada: false, ultimoHeartbeatEm: new Date(Date.now() - 600000).toISOString() }));
    renderizar('/board', tokenDoDev());

    const botao = await screen.findByRole('button', { name: 'Ligar dev.kit' });
    expect((await screen.findAllByText(/Máquina desconectada/)).length).toBeGreaterThan(0);
    expect(botao).toBeDisabled();
    expect(botao).toHaveAttribute('title', expect.stringContaining('serviço do dev.kit precisa estar de pé'));
  });

  it('a falha do envio aparece sem fingir que o serviço recebeu', async () => {
    preparar(maquina(), undefined, () => HttpResponse.json({ detail: 'Você ainda não tem a fila do controle remoto pronta.', codigo: 'sem-fila' }, { status: 409 }));
    await userEvent.click(await abrir('Ligar dev.kit'));

    const faixa = (await screen.findByText('Ligar o dev.kit · não enviado')).closest('[role="status"]');
    expect(faixa).toHaveTextContent('fila do controle remoto pronta');
    expect(screen.getByRole('button', { name: 'Ligar dev.kit' })).toBeEnabled();
  });
});
