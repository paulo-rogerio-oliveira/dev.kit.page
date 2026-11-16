import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { transferableAbortController } from 'node:util';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';
import { artefatosLocais, maquinaDoPreview } from '../testes/artefatosLocais';
import type { RetratoDaMaquina } from '../api/remoto';

beforeAll(() => vi.stubGlobal('AbortController', transferableAbortController().constructor));
afterAll(() => vi.unstubAllGlobals());

describe('preview local pelo Board', () => {
  it('envia snapshot pela fila, repete o mesmo Id após falha e só muda o card ao consultar o host', async () => {
    const maquina = maquinaDoPreview();
    const locais = artefatosLocais();
    Object.assign(locais.artefatos[0].previews[0], { situacao: 'nao-solicitado', pedido: 'nenhum', ciclo: '', versao: 0,
      url: null, diagnostico: 'Nenhum processo solicitado.', prontidao: { front: false, api: false, dependencias: false, jornada: false }, componentes: [] });
    const foto: RetratoDaMaquina = { maquina, recebidoEm: new Date().toISOString(), retrato: {
      contrato: 1, maquinaId: maquina.maquinaId, geradoEm: new Date().toISOString(), projetos: [
        { slug: 'demo', nome: 'Projeto demo', situacao: 'aprovado', ondaAtual: 1, itens: [], artefatosLocais: locais },
      ], fluxos: [], pendencias: [],
    } };
    const pedidos: Array<Record<string, unknown>> = [];
    servidor.use(
      http.get(`${API}/api/remoto/maquinas`, () => HttpResponse.json([maquina])),
      http.get(`${API}/api/remoto/maquinas/:id/retrato`, () => HttpResponse.json(foto)),
      http.get(`${API}/api/remoto/maquinas/:id/decisoes`, () => HttpResponse.json([])),
      http.get(`${API}/api/remoto/maquinas/:id/conversa`, () => HttpResponse.json({ comandos: [], respostas: [], ultimoEvento: 0 })),
      http.get(`${API}/api/remoto/conversa/eventos`, () => new HttpResponse('', { headers: { 'Content-Type': 'text/event-stream' } })),
      http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
        const pedido = await request.json() as Record<string, unknown>;
        if (pedido.tipo === 'projeto') {
          pedidos.push(pedido);
          if (pedidos.length === 1) return HttpResponse.json({ detail: 'Resposta da fila indisponível.' }, { status: 503 });
        }
        return HttpResponse.json({ ...pedido, estado: 'na-fila', criadoEm: new Date().toISOString(),
          expiraEm: new Date(Date.now() + 600_000).toISOString(), entregueEm: null, concluidoEm: null, erro: '' }, { status: 202 });
      }),
    );
    renderizar('/board', tokenDoDev());
    const board = within(screen.getByRole('complementary', { name: 'Board' }));
    const card = within(await board.findByRole('region', { name: 'Preview local site, geração 3' }));
    await userEvent.click(card.getByRole('button', { name: 'Iniciar preview no host' }));
    expect(await board.findByRole('alert')).toHaveTextContent('Envio não confirmado');
    await userEvent.click(card.getByRole('button', { name: 'Iniciar preview no host' }));
    await waitFor(() => expect(pedidos).toHaveLength(2));
    expect(pedidos[1]).toEqual(pedidos[0]);
    expect(pedidos[0].maquinaId).toBe(maquina.maquinaId);
    expect(JSON.parse(String(pedidos[0].texto))).toEqual({ acao: 'preview-local', projeto: 'demo', artefato: 3,
      receita: 'a'.repeat(64), aplicacao: 'site', operacao: 'iniciar', versao: 0, conjunto: 'e'.repeat(64) });
    for (const campo of ['sessao', 'fluxo', 'agente', 'parametros']) expect(pedidos[0]).not.toHaveProperty(campo);
    expect(card.getByText('não solicitado')).toBeInTheDocument();
    expect(card.queryByText('http://127.0.0.1:4317/')).not.toBeInTheDocument();

    Object.assign(locais.artefatos[0].previews[0], { situacao: 'iniciando', pedido: 'iniciar', ciclo: 'd'.repeat(32),
      versao: 1, diagnostico: 'Host preparando componentes.' });
    await userEvent.click(card.getByRole('button', { name: 'Consultar' }));
    expect(await board.findByText('iniciando')).toBeInTheDocument();
    expect(board.queryByText('http://127.0.0.1:4317/')).not.toBeInTheDocument();
    expect(pedidos).toHaveLength(2);
  });
});
