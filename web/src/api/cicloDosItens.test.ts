import { beforeEach, describe, expect, it } from 'vitest';
import { transferableAbortController } from 'node:util';
import { http, HttpResponse } from 'msw';
import { API, servidor } from '../testes/servidor';
import { api, ErroDaApi } from './cliente';
import { recursoDoCicloDosItens } from './cicloDosItens';
import type { DecisaoHumanaDoItem, ImpactoDaDecisaoDoItem, PedidoDeDecisaoDoItem } from './cicloDosItens';
import type { Projeto } from './projetos';

const projeto: Projeto = {
  id: 7, slug: 'isolado', nome: 'Isolado', nivel: 'usuario', time: '', repositorios: ['https://exemplo.test/repo'],
  branch: 'feature/dev/projeto-isolado', semanaConvertida: false, definicao: 'Pedido', anexos: ['design/tela/v15/hash'],
  autonomia: true, paralelismo: 2, destinoDaPr: 'develop', situacao: 'aprovado', versao: 4, autor: 'dev',
  atualizadoEm: '2026-11-21T12:00:00Z', podeApagar: true, featureId: 0, epicId: 0, iteracao: '', board: 'nao', papel: 'editor',
  itens: [{ numero: 2, titulo: 'Item', descricao: 'Trabalho parcial', fluxo: '', repositorios: [], componentes: [],
    dependencias: [], onda: 1, workItemId: 0, situacao: 'rodando', tipo: '', prioridade: 0, horas: 0, branch: 'item-2' }],
};
const simulacao: PedidoDeDecisaoDoItem = { pedido: '68bb100e-cc4e-46dc-9dd0-6959a38bc254', acao: 'cancelar-item', versaoEsperada: 4 };
const confirmacao: PedidoDeDecisaoDoItem = { ...simulacao, aplicar: true, confirmar: true,
  motivo: 'Preservar a entrega parcial', confirmacao: 'isolado/2/cancelar-item/remota:4' };

function impacto(pedido: PedidoDeDecisaoDoItem): ImpactoDaDecisaoDoItem {
  const recibo: DecisaoHumanaDoItem = {
    pedido: pedido.pedido, acao: pedido.acao, autor: 'identidade autenticada na API', confirmacao: pedido.confirmacao ?? '',
    motivo: pedido.motivo?.trim() ?? '', em: '2026-11-21T12:01:00Z', versaoEsperada: '4', estadoAnterior: 'rodando',
    snapshot: 'snapshot portátil', paradaPendente: true, geracao: 1,
  };
  return { simulacao: !pedido.aplicar, projeto: { ...projeto, versao: pedido.aplicar ? 5 : 4,
    itens: [{ ...projeto.itens[0], situacao: 'cancelado', decisaoHumana: recibo }] },
    item: 2, dependentes: [3], grafoEfetivo: [], consequencia: 'Reservas dependem de parada confirmada pelo host.' };
}

beforeEach(() => {
  servidor.use(http.get(`${API}/api/projetos/recursos`, () => HttpResponse.json({ recursos: [recursoDoCicloDosItens] })));
});

describe('cliente do contrato humano de item, sem tela', () => {
  it('negocia capacidade e envia versão/pedido/identidade sem transformar simulação em aplicação', async () => {
    const recebidos: PedidoDeDecisaoDoItem[] = [];
    servidor.use(http.post(`${API}/api/projetos/7/itens/2/decisao`, async ({ request }) => {
      expect(request.headers.get('Authorization')).toBe('Bearer sessao');
      expect(request.headers.get('X-DevKit-Ciclo-Item')).toBe('1');
      const corpo = await request.json() as PedidoDeDecisaoDoItem;
      recebidos.push(corpo);
      return HttpResponse.json(impacto(corpo));
    }));
    const previa = await api.decidirItemDoProjeto('sessao', 7, 2, simulacao);
    expect(previa.simulacao).toBe(true);
    expect(recebidos).toEqual([simulacao]);
    const aplicado = await api.decidirItemDoProjeto('sessao', 7, 2, confirmacao);
    expect(recebidos).toEqual([simulacao, confirmacao]);
    expect(aplicado.simulacao).toBe(false);
    expect(aplicado.projeto.itens[0].decisaoHumana?.paradaPendente).toBe(true);
    expect(aplicado.projeto.itens[0].situacao).toBe('cancelado');
    expect(projeto.itens[0].situacao).toBe('rodando');
    expect(recebidos[1]).not.toHaveProperty('autor');
    expect(recebidos[1]).not.toHaveProperty('pid');
    expect(recebidos[1]).not.toHaveProperty('caminhoLocal');
  });

  it.each([401, 403, 404, 409, 426])('propaga recusa%s sem repetir nem assumir decisão aplicada', async status => {
    let chamadas = 0;
    servidor.use(http.post(`${API}/api/projetos/7/itens/2/decisao`, () => {
      chamadas++;
      return HttpResponse.json({ detail: 'Recusa verificável; releia.' }, { status });
    }));
    await expect(api.decidirItemDoProjeto('sessao', 7, 2, confirmacao)).rejects.toMatchObject({ status, message: 'Recusa verificável; releia.' });
    expect(chamadas).toBe(1);
    expect(projeto.itens[0].decisaoHumana).toBeUndefined();
  });

  it('capacidade ausente recusa antes do POST e não usa mecanismo alternativo', async () => {
    let escritas = 0;
    servidor.use(http.get(`${API}/api/projetos/recursos`, () => HttpResponse.json({ recursos: [] })),
      http.post(`${API}/api/projetos/7/itens/2/decisao`, () => { escritas++; return HttpResponse.json(impacto(confirmacao)); }));
    await expect(api.decidirItemDoProjeto('sessao', 7, 2, confirmacao)).rejects.toMatchObject({ status: 426 });
    expect(escritas).toBe(0);
  });

  it('API anterior sem descoberta devolve404, sem enviar decisão', async () => {
    let escritas = 0;
    servidor.use(http.get(`${API}/api/projetos/recursos`, () => HttpResponse.json({ detail: 'API anterior' }, { status: 404 })),
      http.post(`${API}/api/projetos/7/itens/2/decisao`, () => { escritas++; return HttpResponse.json(impacto(confirmacao)); }));
    await expect(api.decidirItemDoProjeto('sessao', 7, 2, confirmacao)).rejects.toBeInstanceOf(ErroDaApi);
    expect(escritas).toBe(0);
  });

  it('aceite sem recibo não comprova aplicação e a releitura é explícita', async () => {
    let leituras = 0;
    servidor.use(http.post(`${API}/api/projetos/7/itens/2/decisao`, () => HttpResponse.json({ ...impacto(confirmacao), projeto })),
      http.get(`${API}/api/projetos/7`, ({ request }) => {
        leituras++;
        expect(request.headers.get('X-DevKit-Ciclo-Item')).toBe('1');
        return HttpResponse.json(projeto);
      }));
    await expect(api.decidirItemDoProjeto('sessao', 7, 2, confirmacao)).rejects.toMatchObject({ status: 409, message: expect.stringContaining('aceite da fila') });
    expect(leituras).toBe(0);
    expect(await api.projetoComCicloDosItens('sessao', 7)).toEqual(projeto);
    expect(leituras).toBe(1);
  });

  it.each(['pedido', 'acao', 'confirmacao', 'motivo'] as const)('recibo com%s divergente exige releitura', async campo => {
    const resposta = impacto(confirmacao);
    const recibo = resposta.projeto.itens[0].decisaoHumana!;
    servidor.use(http.post(`${API}/api/projetos/7/itens/2/decisao`, () => HttpResponse.json({ ...resposta,
      projeto: { ...resposta.projeto, itens: [{ ...resposta.projeto.itens[0], decisaoHumana: { ...recibo, [campo]: 'divergente' } }] } })));
    await expect(api.decidirItemDoProjeto('sessao', 7, 2, confirmacao)).rejects.toMatchObject({ status: 409 });
  });

  it('leitura versionada mantém recibos e reservas portáteis sem confirmar parada pelo navegador', async () => {
    const resposta = impacto(confirmacao).projeto;
    resposta.itens[0].reservasDeEscrita = { reservas: [{ maquina: 'host', instancia: 'i', sessao: 's', tentativa: 't' }], paradas: [] };
    servidor.use(http.get(`${API}/api/projetos/7`, ({ request }) => {
      expect(request.headers.get('Authorization')).toBe('Bearer sessao');
      expect(request.headers.get('X-DevKit-Ciclo-Item')).toBe('1');
      return HttpResponse.json(resposta);
    }));
    const lido = await api.projetoComCicloDosItens('sessao', 7);
    expect(lido.itens[0].reservasDeEscrita?.paradas).toEqual([]);
    expect(lido.itens[0].decisaoHumana?.pedido).toBe(confirmacao.pedido);
    expect(lido.papel).toBe('editor');
  });

  it('cancelamento da requisição não retorna êxito fictício', async () => {
    // Mesmo sinal nativo do fetch do Node, como nos testes de Board/ChatRemoto.
    const cancelamento = transferableAbortController();
    cancelamento.abort();
    await expect(api.decidirItemDoProjeto('sessao', 7, 2, confirmacao, cancelamento.signal)).rejects.toMatchObject({ name: 'AbortError' });
  });
});
