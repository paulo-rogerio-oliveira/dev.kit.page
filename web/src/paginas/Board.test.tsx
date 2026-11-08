import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http, HttpResponse } from 'msw';
import { transferableAbortController } from 'node:util';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';
import type { MaquinaRemota, RetratoDaMaquina } from '../api/remoto';
import { destinoDa } from '../sessao';

// O fetch do Node exige o AbortSignal do próprio Node, em vez do construtor do jsdom.
beforeAll(() => vi.stubGlobal('AbortController', transferableAbortController().constructor));
afterAll(() => vi.unstubAllGlobals());

const maquina = (id = 'notebook'): MaquinaRemota => ({
  maquinaId: id, nome: id === 'notebook' ? 'Notebook' : 'Outra máquina', versaoDevKit: '79', origem: 'servico',
  appAberto: true, estadoDoAgente: 'ativo', turnoEmAndamento: false, conectada: true,
  ultimoHeartbeatEm: new Date().toISOString(), retratoGeradoEm: new Date().toISOString(),
});
const retrato = (m = maquina()): RetratoDaMaquina => ({
  maquina: m, recebidoEm: new Date().toISOString(),
  retrato: { contrato: 1, maquinaId: m.maquinaId, geradoEm: new Date().toISOString(),
    pendencias: [{ id: 'p1', tipo: 'pergunta', titulo: 'Integrar o item 20 no projeto?', pergunta: 'Escolha o destino', jaTentei: 'Nota 100 · regressão sem falhas', opcoes: ['Integrar', 'Aguardar'] }],
    projetos: [{ slug: 'projeto', nome: 'Projeto de demonstração', ondaAtual: 10, situacao: 'aprovado', itens: [
      { numero: 1, titulo: 'Base', onda: 1, situacao: 'integrado' }, { numero: 20, titulo: 'Layout', onda: 10, situacao: 'rodando' },
    ] }],
    fluxos: [{ id: 'f1', nome: 'Item 20 · layout do plano', etapa: 'Implementação', situacao: 'rodando', agente: 'codex' }],
  },
});

function responder(lista = [maquina()]) {
  servidor.use(
    http.get(`${API}/api/remoto/maquinas`, () => HttpResponse.json(lista)),
    http.get(`${API}/api/remoto/maquinas/:id/retrato`, ({ params }) => HttpResponse.json(retrato(maquina(String(params.id))))),
    http.get(`${API}/api/remoto/maquinas/:id/decisoes`, ({ params }) => HttpResponse.json(retrato(maquina(String(params.id))).retrato.pendencias!.map(p => ({ pendencia: p, snapshotId: 'a'.repeat(64), bloqueio: null, resposta: null })))),
    http.get(`${API}/api/remoto/maquinas/:id/conversa`, () => HttpResponse.json({ comandos: [], respostas: [], ultimoEvento: 0 })),
    http.get(`${API}/api/remoto/conversa/eventos`, () => new HttpResponse('', { headers: { 'Content-Type': 'text/event-stream' } })),
  );
}

describe('Board remoto', () => {
  it('preserva a notificação após login e recusa destino externo', () => {
    expect(destinoDa(tokenDoDev(), '/board?notificacao=' + 'a'.repeat(32))).toBe('/board?notificacao=' + 'a'.repeat(32));
    expect(destinoDa(tokenDoDev(), '//evil.test/board')).toBe('/conta');
  });

  it('abre diretamente a revisão da aprovação indicada pelo push sem autorizar nenhum ato', async () => {
    responder(); const id = 'a'.repeat(32);
    servidor.use(
      http.get(`${API}/api/remoto/push/entregas/${id}`, () => HttpResponse.json({ maquinaId: 'notebook', pendenciaId: 'p1', snapshotId: 'b'.repeat(64) })),
      http.get(`${API}/api/remoto/maquinas/:id/decisoes`, () => HttpResponse.json([{ pendencia: { id: 'p1', tipo: 'aprovacao', titulo: 'Revisar este snapshot?' }, snapshotId: 'b'.repeat(64), bloqueio: null, resposta: null }])),
      http.get(`${API}/api/remoto/segundo-fator`, () => HttpResponse.json({ ativo: true })),
    );
    renderizar('/board?notificacao=' + id, tokenDoDev());
    expect(await screen.findByRole('main', { name: 'Revisão da decisão' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Aprovar com segundo fator' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Confirmar e aprovar este SHA' })).not.toBeInTheDocument();
  });

  it('notificação de pergunta seleciona a máquina e dá foco à pergunta atual', async () => {
    responder([maquina(), maquina('outra')]); const id = 'c'.repeat(32);
    servidor.use(http.get(`${API}/api/remoto/push/entregas/${id}`, () => HttpResponse.json({ maquinaId: 'outra', pendenciaId: 'p1', snapshotId: 'a'.repeat(64) })));
    renderizar('/board?aba=chat&notificacao=' + id, tokenDoDev());
    await waitFor(() => expect(screen.getByLabelText('Máquina')).toHaveValue('outra'));
    await waitFor(() => expect(document.getElementById('board-decisao-p1')).toHaveFocus());
    expect(await screen.findByText(/Decisão aberta a partir da notificação/)).toBeInTheDocument();
    // O cartão apontado pela notificação já vem com os detalhes abertos.
    expect(within(document.getElementById('board-decisao-p1')!).getByRole('button', { name: 'Ocultar detalhes' })).toHaveAttribute('aria-expanded', 'true');
    expect(within(document.getElementById('board-decisao-p1')!).getByRole('button', { name: 'Integrar' })).toBeInTheDocument();
  });

  it('notificação resolvida ou de outra conta informa indisponibilidade sem abrir aprovação', async () => {
    responder(); const id = 'd'.repeat(32);
    servidor.use(http.get(`${API}/api/remoto/push/entregas/${id}`, () => HttpResponse.json({ detail: 'Esta decisão não está mais pendente ou não pertence à sua conta.' }, { status: 404 })));
    renderizar('/board?notificacao=' + id, tokenDoDev());
    expect(await screen.findByText(/Esta decisão não está mais pendente/)).toBeInTheDocument();
    expect(screen.queryByRole('main', { name: 'Revisão da decisão' })).not.toBeInTheDocument();
  });
  it('exige login e preserva o destino do desenvolvedor após autenticar', async () => {
    renderizar('/board');
    expect(await screen.findByRole('heading', { name: /Entrar/i })).toBeInTheDocument();
    expect(destinoDa(tokenDoDev(), '/board')).toBe('/board');
  });

  it('mostra o retrato, progresso, execuções e opções auditadas no Board e no Chat', async () => {
    responder();
    const chamadas: Request[] = [];
    const registrar = ({ request }: { request: Request }) => { chamadas.push(request.clone()); };
    servidor.events.on('request:start', registrar);
    try {
      renderizar('/board', tokenDoDev());
      // A decisão aparece só no painel (remote-control-v3, item 3): o chat não renderiza aprovação.
      expect(await screen.findAllByText('Integrar o item 20 no projeto?')).toHaveLength(1);
      expect(within(screen.getByRole('region', { name: 'Orquestrador' })).queryByText('Integrar o item 20 no projeto?')).not.toBeInTheDocument();
      // No painel o cartão nasce recolhido: as opções aparecem depois do Ver detalhes.
      expect(screen.queryAllByRole('button', { name: 'Integrar' })).toHaveLength(0);
      await userEvent.click(within(screen.getByRole('complementary', { name: 'Board' })).getByRole('button', { name: 'Ver detalhes' }));
      expect(screen.getAllByRole('button', { name: 'Integrar' })).toHaveLength(1);
      expect(within(screen.getByRole('complementary', { name: 'Board' })).getByText('Pergunta')).toBeInTheDocument();
      expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '1');
      expect(screen.getByText('Item 20 · layout do plano')).toBeInTheDocument();
      expect(screen.getByText(/Implementação · codex/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Enviar mensagem' })).toBeDisabled();
      await waitFor(() => expect(screen.getByLabelText('Mensagem ao Orquestrador')).toBeEnabled());
      // (item 8) Com o dev.kit aberto e a máquina conectada, o Reiniciar vai ao serviço — sem clique, nada é escrito. Ele mora
      // no cabeçalho do Orquestrador, com o selo do chat (proposta chat-remoto v016): o topo fica só com o estado da máquina, e
      // o rodapé saiu — o "Ligar e Reiniciar…" é a dica do botão.
      const orquestrador = within(screen.getByRole('region', { name: 'Orquestrador' }));
      expect(orquestrador.getByRole('button', { name: 'Reiniciar dev.kit' })).toBeEnabled();
      expect(orquestrador.getByRole('button', { name: 'Reiniciar dev.kit' })).toHaveAttribute('title', expect.stringContaining('Ligar e Reiniciar são executados pelo serviço do dev.kit na máquina.'));
      expect(document.querySelector('.br-top button')).toBeNull();
      expect(orquestrador.getByText('Texto do chat não aprova')).toBeInTheDocument();
      expect(orquestrador.getByText('Chat não aprova · decisões auditadas')).toBeInTheDocument();
      expect(document.querySelector('footer')).toBeNull();
      expect(screen.getByRole('button', { name: 'Trocar fluxo' })).toBeEnabled();
      expect(orquestrador.getByText('Mesma conversa do Board do dev.kit')).toBeInTheDocument();
      expect(chamadas.length).toBeGreaterThan(0);
      expect(chamadas.every(r => r.headers.has('Authorization'))).toBe(true);
      // (remote-control-v3) A única escrita sem clique é o handshake do catálogo dos atalhos: ele pede, não executa nada.
      const escritas = chamadas.filter(r => r.method !== 'GET');
      expect(escritas.length).toBeLessThanOrEqual(1);
      for (const r of escritas) {
        expect([r.method, new URL(r.url).pathname]).toEqual(['POST', '/api/remoto/comandos']);
        expect(await r.json()).toMatchObject({ tipo: 'catalogo', texto: '' });
      }
    } finally { servidor.events.removeListener('request:start', registrar); }
  });

  it('responde com o botão do snapshot e mostra o registro comum sem segundo fator', async () => {
    responder();
    let recebido: unknown;
    servidor.use(http.post(`${API}/api/remoto/maquinas/:id/decisoes/:pendencia`, async ({ request }) => {
      recebido = await request.json();
      return HttpResponse.json({ id: 1, maquinaId: 'notebook', pendenciaId: 'p1', snapshotId: 'a'.repeat(64), acao: 'responder', opcao: 'Aguardar', em: new Date().toISOString(), segundoFator: false, estado: 'registrada' });
    }));
    renderizar('/board', tokenDoDev());
    const board = within(screen.getByRole('complementary', { name: 'Board' }));
    await userEvent.click(await board.findByRole('button', { name: 'Ver detalhes' }));
    await userEvent.click(board.getByRole('button', { name: 'Aguardar' }));
    expect(recebido).toEqual({ snapshotId: 'a'.repeat(64), acao: 'responder', opcao: 'Aguardar' });
    expect(await board.findByRole('status')).toHaveTextContent('Resposta: Aguardar registrada');
    expect(board.queryByRole('button', { name: 'Aguardar' })).not.toBeInTheDocument();
  });

  it('o × dispensa o card só no site, o filtro Dispensadas o traz de volta e o Restaurar o devolve', async () => {
    responder();
    const pedidos: Array<{ pendencia: string; corpo: unknown }> = [];
    servidor.use(http.post(`${API}/api/remoto/maquinas/:id/decisoes/:pendencia`, async ({ request, params }) => {
      const corpo = await request.json() as { acao: string };
      pedidos.push({ pendencia: String(params.pendencia), corpo });
      return HttpResponse.json({ maquinaId: 'notebook', pendenciaId: String(params.pendencia), dispensada: corpo.acao === 'dispensar', em: new Date().toISOString() });
    }));
    renderizar('/board', tokenDoDev());
    const board = within(screen.getByRole('complementary', { name: 'Board' }));
    await userEvent.click(await board.findByRole('button', { name: 'Dispensar Integrar o item 20 no projeto?' }));
    await waitFor(() => expect(board.queryByRole('region', { name: 'Integrar o item 20 no projeto?' })).not.toBeInTheDocument());
    expect(pedidos).toEqual([{ pendencia: 'p1', corpo: { acao: 'dispensar' } }]);
    expect(board.getByRole('heading', { name: 'Precisa de você · 0' })).toBeInTheDocument();
    expect(board.getByText(/as outras foram dispensadas no site/)).toBeInTheDocument();

    await userEvent.click(within(board.getByRole('group', { name: 'Filtro das pendências' })).getByRole('button', { name: /Dispensadas/ }));
    const cartao = within(board.getByRole('region', { name: 'Integrar o item 20 no projeto?' }));
    await userEvent.click(cartao.getByRole('button', { name: 'Restaurar' }));
    expect(pedidos.at(-1)).toEqual({ pendencia: 'p1', corpo: { acao: 'restaurar' } });
    await userEvent.click(within(board.getByRole('group', { name: 'Filtro das pendências' })).getByRole('button', { name: 'Pendentes' }));
    expect(board.getByRole('heading', { name: 'Precisa de você · 1' })).toBeInTheDocument();
    expect(board.getByRole('region', { name: 'Integrar o item 20 no projeto?' })).toBeInTheDocument();
  });

  it('o painel de projetos lista os em andamento, e o Mostrar concluídos traz os em fechamento e os concluídos', async () => {
    responder();
    const item = (numero: number, situacao: string) => ({ numero, titulo: `Item ${numero}`, onda: 1, situacao });
    servidor.use(http.get(`${API}/api/remoto/maquinas/:id/retrato`, () => {
      const r = retrato();
      return HttpResponse.json({ ...r, retrato: { ...r.retrato, projetos: [
        { slug: 'andando', nome: 'Projeto em andamento', ondaAtual: 1, situacao: 'aprovado', itens: [item(1, 'integrado'), item(2, 'rodando')] },
        { slug: 'fechando', nome: 'Projeto em fechamento', ondaAtual: 1, situacao: 'fechando', itens: [item(1, 'integrado')] },
        { slug: 'feito', nome: 'Projeto concluído', ondaAtual: 1, situacao: 'concluido', itens: [item(1, 'integrado')] },
      ] } });
    }));
    renderizar('/board', tokenDoDev());
    const board = within(screen.getByRole('complementary', { name: 'Board' }));
    expect(await board.findByRole('link', { name: 'Abrir Projeto em andamento' })).toBeInTheDocument();
    expect(board.queryByRole('link', { name: 'Abrir Projeto em fechamento' })).not.toBeInTheDocument();
    expect(board.queryByRole('link', { name: 'Abrir Projeto concluído' })).not.toBeInTheDocument();
    await userEvent.click(board.getByRole('checkbox', { name: /Mostrar concluídos \(2\)/ }));
    expect(board.getByRole('link', { name: 'Abrir Projeto em fechamento' })).toHaveTextContent('em fechamento · todos os itens integrados');
    expect(board.getByRole('link', { name: 'Abrir Projeto concluído' })).toHaveTextContent('incorporado ao projeto');
  });

  it('a decisão já dispensada vem marcada da API e fica fora da contagem', async () => {
    responder();
    servidor.use(http.get(`${API}/api/remoto/maquinas/:id/decisoes`, ({ request }) => {
      expect(new URL(request.url).searchParams.get('dispensadas')).toBe('true');
      return HttpResponse.json(retrato().retrato.pendencias!.map(p => ({ pendencia: p, snapshotId: 'a'.repeat(64), bloqueio: null, resposta: null, dispensada: true })));
    }));
    renderizar('/board', tokenDoDev());
    const board = within(screen.getByRole('complementary', { name: 'Board' }));
    expect(await board.findByRole('heading', { name: 'Precisa de você · 0' })).toBeInTheDocument();
    expect(board.queryByRole('region', { name: 'Integrar o item 20 no projeto?' })).not.toBeInTheDocument();
    expect(within(board.getByRole('group', { name: 'Filtro das pendências' })).getByRole('button', { name: /Dispensadas/ })).toHaveTextContent('Dispensadas 1');
  });

  it('o card de Design mostra a miniatura da última versão e o clique abre o modal', async () => {
    responder();
    const png = Uint8Array.from(atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII='), c => c.charCodeAt(0));
    servidor.use(
      http.get(`${API}/api/remoto/maquinas/:id/catalogo`, () => HttpResponse.json({ maquinaId: 'notebook', recebidoEm: new Date().toISOString(), catalogo: {
        contrato: 1, maquinaId: 'notebook', geradoEm: new Date().toISOString(), comandoId: null, workItems: [], repositorios: [], fluxos: [], projetos: [],
        designs: [{ nome: 'dev.kit.page', telas: 2, repositorio: 'github.com/empresa/dev.kit.page', miniatura: '/api/design/dev-kit-page/chat-remoto/v16' }] } })),
      http.get(`${API}/api/design/dev-kit-page/chat-remoto/v16`, () => new HttpResponse(png, { headers: { 'Content-Type': 'image/png' } })),
    );
    renderizar('/board', tokenDoDev());
    const board = within(screen.getByRole('complementary', { name: 'Board' }));
    await userEvent.click(await board.findByRole('button', { name: 'Ampliar dev.kit.page · última versão' }));
    const modal = await screen.findByRole('dialog', { name: 'dev.kit.page · última versão' });
    expect(await within(modal).findByRole('img', { name: 'dev.kit.page · última versão' })).toHaveAttribute('src', 'blob:teste');
    await userEvent.click(within(modal).getByRole('button', { name: 'Fechar' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(board.getByRole('link', { name: 'Abrir o design de dev.kit.page' })).toBeInTheDocument();
  });

  it('a autorização mostra o escopo exato e só sai com o segundo fator, como autorizar', async () => {
    responder();
    let recebido: unknown;
    const escopo = { ato: 'executar-comando', alvo: '#515 Implementar', comando: 'dotnet test tests/App.Tests' };
    servidor.use(
      http.get(`${API}/api/remoto/maquinas/:id/decisoes`, () => HttpResponse.json([{ pendencia: { id: 'p1', tipo: 'autorizacao', titulo: 'Comando bloqueado', escopo }, snapshotId: 'c'.repeat(64), bloqueio: null, resposta: null }])),
      http.get(`${API}/api/remoto/segundo-fator`, () => HttpResponse.json({ ativo: true })),
      http.post(`${API}/api/remoto/maquinas/:id/decisoes/:pendencia`, async ({ request }) => { recebido = await request.json(); return HttpResponse.json({ id: 2, acao: 'autorizar', em: new Date().toISOString(), segundoFator: true, estado: 'registrada' }); }),
    );
    renderizar('/board', tokenDoDev());
    const board = within(screen.getByRole('complementary', { name: 'Board' }));
    expect(await board.findByText('Autorização')).toBeInTheDocument();
    await userEvent.click(board.getByRole('button', { name: 'Autorizar' }));
    const escopoNaTela = within(await screen.findByLabelText('Escopo da autorização'));
    expect(escopoNaTela.getByText('Executar o comando')).toBeInTheDocument();
    expect(escopoNaTela.getByText('dotnet test tests/App.Tests')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Autorizar com segundo fator' }));
    const confirmar = screen.getByRole('button', { name: 'Confirmar e autorizar' });
    expect(confirmar).toBeDisabled();
    expect(recebido).toBeUndefined();
    await userEvent.type(await screen.findByLabelText('Código de 6 dígitos'), '123456');
    await userEvent.click(confirmar);
    await waitFor(() => expect(recebido).toEqual({ snapshotId: 'c'.repeat(64), acao: 'autorizar', codigo: '123456' }));
  });

  it('revisa evidências e exige seis dígitos antes de confirmar o SHA', async () => {
    responder();
    const snapshot = { origem: 'feature/item-9', destino: 'feature/projeto', sha: 'a'.repeat(40), notas: [{ avaliador: 'Código', nota: 96, minima: 90 }], testesAprovados: 128, testesFalhos: 0, regressaoConcluida: true, arquivos: 4, commits: 2, alteracoes: '+ decisão', capturas: [] };
    let recebido: unknown;
    servidor.use(
      http.get(`${API}/api/remoto/maquinas/:id/decisoes`, () => HttpResponse.json([{ pendencia: { id: 'p1', tipo: 'aprovacao', titulo: 'Integrar?', snapshot }, snapshotId: 'b'.repeat(64), bloqueio: null, resposta: null }])),
      http.get(`${API}/api/remoto/segundo-fator`, () => HttpResponse.json({ ativo: true })),
      http.post(`${API}/api/remoto/maquinas/:id/decisoes/:pendencia`, async ({ request }) => { recebido = await request.json(); return HttpResponse.json({ id: 1, acao: 'aprovar', sha: snapshot.sha, em: new Date().toISOString(), segundoFator: true, estado: 'registrada' }); }),
    );
    renderizar('/board', tokenDoDev());
    const board = within(screen.getByRole('complementary', { name: 'Board' }));
    await board.findByRole('button', { name: 'Decidir' });
    const cartao = within(board.getByRole('region', { name: 'Integrar?' }));
    expect(cartao.getAllByRole('button').map(b => b.textContent)).toEqual(['×', 'Ver detalhes', 'Decidir']);
    expect(cartao.getByText('Nota 96 · regressão sem falhas')).toBeInTheDocument();
    await userEvent.click(cartao.getByRole('button', { name: 'Decidir' }));
    expect(await screen.findByText(snapshot.origem)).toBeInTheDocument();
    expect(screen.getByText(snapshot.destino)).toBeInTheDocument();
    expect(screen.getByText(snapshot.sha)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('link', { name: 'Ver alterações' }));
    expect(screen.getByText('+ decisão').closest('details')).toHaveAttribute('open');
    await userEvent.click(screen.getByRole('button', { name: 'Aprovar com segundo fator' }));
    const confirmar = screen.getByRole('button', { name: 'Confirmar e aprovar este SHA' });
    expect(confirmar).toBeDisabled();
    expect(recebido).toBeUndefined();
    await userEvent.type(await screen.findByLabelText('Código de 6 dígitos'), '123456');
    expect(confirmar).toBeEnabled();
    await userEvent.click(confirmar);
    expect(recebido).toEqual({ snapshotId: 'b'.repeat(64), acao: 'aprovar', codigo: '123456' });
    expect(await screen.findByRole('status')).toHaveTextContent('segundo fator confirmado');
  });

  it('registra pedido de ajuste sem solicitar código de segundo fator nem autorizar aprovação', async () => {
    responder();
    let recebido: unknown;
    servidor.use(
      http.get(`${API}/api/remoto/maquinas/:id/decisoes`, () => HttpResponse.json([{ pendencia: { id: 'p1', tipo: 'aprovacao', titulo: 'Revisar entrega?' }, snapshotId: 'c'.repeat(64), bloqueio: 'Regressão pendente ou com falhas: a aprovação está bloqueada.', resposta: null }])),
      http.get(`${API}/api/remoto/segundo-fator`, () => HttpResponse.json({ ativo: false })),
      http.post(`${API}/api/remoto/maquinas/:id/decisoes/:pendencia`, async ({ request }) => { recebido = await request.json(); return HttpResponse.json({ id: 2, acao: 'ajuste', em: new Date().toISOString(), segundoFator: false, estado: 'registrada' }); }),
    );
    renderizar('/board', tokenDoDev());
    await userEvent.click(await within(screen.getByRole('complementary', { name: 'Board' })).findByRole('button', { name: 'Decidir' }));
    expect(screen.getByRole('button', { name: 'Aprovar com segundo fator' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Pedir ajuste' }));
    expect(recebido).toEqual({ snapshotId: 'c'.repeat(64), acao: 'ajuste' });
    expect(await screen.findByRole('status')).toHaveTextContent('Pedido de ajuste registrada');
    expect(screen.getByRole('status')).toHaveTextContent('Nenhum ato foi autorizado.');
    expect(screen.queryByLabelText('Código de 6 dígitos')).not.toBeInTheDocument();
  });

  it('preserva rascunho e contexto do Chat ao revisar e voltar', async () => {
    responder();
    servidor.use(
      http.get(`${API}/api/remoto/maquinas/:id/decisoes`, () => HttpResponse.json([{ pendencia: { id: 'p1', tipo: 'aprovacao', titulo: 'Integrar?' }, snapshotId: 'b'.repeat(64), bloqueio: null, resposta: null }])),
      http.get(`${API}/api/remoto/segundo-fator`, () => HttpResponse.json({ ativo: true })),
    );
    renderizar('/board?aba=chat', tokenDoDev());
    await waitFor(() => expect(screen.getByLabelText('Mensagem ao Orquestrador')).toBeEnabled());
    const caixa = screen.getByLabelText('Mensagem ao Orquestrador');
    await userEvent.type(caixa, 'rascunho antes da revisão');
    await userEvent.click(screen.getByRole('button', { name: 'Trocar fluxo' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Fluxo, projeto ou design' }), 'Tela');
    await userEvent.click(screen.getByRole('button', { name: 'Aplicar contexto' }));
    await userEvent.click(screen.getByRole('button', { name: '@ Repositório' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Repositório' }), 'dev.kit.page');
    await userEvent.type(screen.getByRole('textbox', { name: 'Branch existente' }), 'feature/item-9');
    await userEvent.click(screen.getByRole('button', { name: 'Aplicar contexto' }));
    // A decisão fica só no painel de decisões: o chat não tem cartão de aprovação.
    const decidir = await within(screen.getByRole('complementary', { name: 'Board' })).findByRole('button', { name: 'Decidir' });
    expect(screen.queryByRole('button', { name: 'Revisar e aprovar' })).not.toBeInTheDocument();
    await userEvent.click(decidir);
    expect(screen.queryByRole('textbox', { name: 'Mensagem ao Orquestrador' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Voltar ao Board/ }));
    expect(screen.getByLabelText('Mensagem ao Orquestrador')).toBe(caixa);
    expect(caixa).toHaveValue('rascunho antes da revisão');
    expect(screen.getByRole('button', { name: 'Trocar fluxo' })).toHaveTextContent('Tela');
    expect(screen.getByRole('button', { name: '@ Repositório' })).toHaveTextContent('dev.kit.page');
    expect(screen.getByText('Branch existente · feature/item-9')).toBeInTheDocument();
  });

  it('permite revisar uma autorização vencida e registrar uma nova tentativa', async () => {
    responder();
    const antiga = { id: 1, acao: 'aprovar', em: new Date().toISOString(), segundoFator: true, estado: 'vencida' };
    let respostaAtual = antiga;
    servidor.use(
      http.get(`${API}/api/remoto/maquinas/:id/decisoes`, () => HttpResponse.json([{ pendencia: { id: 'p1', tipo: 'aprovacao', titulo: 'Renovar?' }, snapshotId: 'c'.repeat(64), bloqueio: null, resposta: respostaAtual }])),
      http.get(`${API}/api/remoto/segundo-fator`, () => HttpResponse.json({ ativo: true })),
      http.post(`${API}/api/remoto/maquinas/:id/decisoes/:pendencia`, () => { respostaAtual = { ...antiga, id: 2, estado: 'registrada' }; return HttpResponse.json(respostaAtual); }),
    );
    renderizar('/board', tokenDoDev());
    const board = within(screen.getByRole('complementary', { name: 'Board' }));
    await userEvent.click(await board.findByRole('button', { name: 'Decidir' }));
    expect(screen.getByRole('status')).toHaveTextContent('A autorização anterior venceu');
    await userEvent.click(screen.getByRole('button', { name: 'Aprovar com segundo fator' }));
    await userEvent.type(await screen.findByLabelText('Código de 6 dígitos'), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar e aprovar este SHA' }));
    expect(await screen.findByRole('status')).toHaveTextContent('aguardando a máquina');
    expect(screen.queryByRole('button', { name: 'Confirmar e aprovar este SHA' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Voltar ao Board/ }));
    expect(board.getByRole('status')).toHaveTextContent('aguardando a máquina');
    expect(board.queryByRole('button', { name: 'Decidir' })).not.toBeInTheDocument();
  });

  it('ignora POST atrasado ao trocar de máquina mesmo com o mesmo hash e identificador', async () => {
    responder([maquina(), maquina('outra')]);
    let liberar!: () => void;
    const espera = new Promise<void>(resolve => { liberar = resolve; });
    let inicio = false, concluiu = false;
    servidor.use(http.post(`${API}/api/remoto/maquinas/:id/decisoes/:pendencia`, async ({ params }) => {
      inicio = true; await espera;
      concluiu = true;
      return HttpResponse.json({ id: 1, maquinaId: params.id, pendenciaId: 'p1', snapshotId: 'a'.repeat(64), acao: 'responder', opcao: 'Aguardar', em: new Date().toISOString(), segundoFator: false, estado: 'registrada' });
    }));
    renderizar('/board', tokenDoDev());
    const board = within(screen.getByRole('complementary', { name: 'Board' }));
    await userEvent.click(await board.findByRole('button', { name: 'Ver detalhes' }));
    await userEvent.click(board.getByRole('button', { name: 'Aguardar' }));
    await waitFor(() => expect(inicio).toBe(true));
    await userEvent.selectOptions(screen.getByLabelText('Máquina'), 'outra');
    // A outra máquina monta os cartões de novo, recolhidos.
    await userEvent.click(await board.findByRole('button', { name: 'Ver detalhes' }));
    await board.findByRole('button', { name: 'Aguardar' });
    await act(async () => { liberar(); await espera; });
    await waitFor(() => expect(concluiu).toBe(true));
    await act(() => new Promise(resolve => setTimeout(resolve, 50)));
    expect(screen.getByLabelText('Máquina')).toHaveValue('outra');
    expect(board.getByRole('button', { name: 'Aguardar' })).toBeEnabled();
    expect(board.queryByText(/Resposta: Aguardar registrada/)).not.toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Orquestrador' })).queryByRole('button', { name: 'Aguardar' })).not.toBeInTheDocument();
  });

  it('navega entre Chat e Board mantendo somente essas duas abas', async () => {
    responder();
    renderizar('/board', tokenDoDev());
    await screen.findByText('Projeto de demonstração');
    const chat = screen.getByRole('tab', { name: 'Chat' });
    const board = screen.getByRole('tab', { name: 'Board' });
    expect(screen.getAllByRole('tab')).toHaveLength(2);
    await userEvent.click(chat);
    expect(chat).toHaveAttribute('aria-selected', 'true');
    await userEvent.keyboard('{ArrowRight}');
    expect(board).toHaveAttribute('aria-selected', 'true');
    expect(board).toHaveFocus();
    await userEvent.keyboard('{Home}');
    expect(chat).toHaveAttribute('aria-selected', 'true');
    await userEvent.keyboard('{End}');
    expect(board).toHaveAttribute('aria-selected', 'true');
    await userEvent.keyboard('{ArrowLeft}');
    expect(chat).toHaveAttribute('aria-selected', 'true');
    await userEvent.click(board);
    expect(board).toHaveAttribute('aria-selected', 'true');
    // A navegação continua só Chat e Board: o cabeçalho não leva a outras páginas (os projetos abrem pelo painel, item 4).
    expect(within(document.querySelector('.br-top') as HTMLElement).queryAllByRole('link')).toHaveLength(0);
  });

  it('mostra estado sem máquinas e permite recuperar uma falha', async () => {
    servidor.use(http.get(`${API}/api/remoto/maquinas`, () => HttpResponse.json({ detail: 'Serviço indisponível' }, { status: 503 })));
    renderizar('/board', tokenDoDev());
    expect(await screen.findByRole('alert')).toHaveTextContent('Serviço indisponível');
    responder([]);
    await userEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }));
    expect(await screen.findByText(/Nenhuma máquina cadastrada/)).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('diferencia máquina desconectada, agente pausado e ausência de retrato', async () => {
    responder([{ ...maquina(), conectada: false, appAberto: false, estadoDoAgente: 'pausado' }]);
    servidor.use(http.get(`${API}/api/remoto/maquinas/:id/retrato`, () => new HttpResponse(null, { status: 404 })));
    renderizar('/board', tokenDoDev());
    expect(await screen.findByText(/Aguardando o primeiro retrato/)).toBeInTheDocument();
    expect(screen.getByText(/O último retrato pode estar desatualizado/)).toBeInTheDocument();
    expect(screen.getByText(/Agente pausado/)).toBeInTheDocument();
    expect(screen.getAllByText(/dev.kit fechado/)).toHaveLength(2);
  });

  it('limpa o retrato ao trocar de máquina e ignora a resposta tardia da anterior', async () => {
    responder([maquina(), maquina('outra')]);
    let atrasar = false;
    servidor.use(http.get(`${API}/api/remoto/maquinas/:id/retrato`, async ({ params }) => {
      if (params.id === 'outra' && atrasar) await delay(150);
      const foto = retrato(maquina(String(params.id)));
      foto.retrato.projetos![0].nome = `Projeto ${params.id}`;
      return HttpResponse.json(foto);
    }));
    renderizar('/board', tokenDoDev());
    await screen.findByText('Projeto notebook');
    atrasar = true;
    await userEvent.selectOptions(screen.getByLabelText('Máquina'), 'outra');
    expect(screen.queryByText('Projeto notebook')).not.toBeInTheDocument();
    await userEvent.selectOptions(screen.getByLabelText('Máquina'), 'notebook');
    await screen.findByText('Projeto notebook');
    await act(() => new Promise(resolve => setTimeout(resolve, 200)));
    expect(screen.queryByText('Projeto outra')).not.toBeInTheDocument();
  });

  describe('fiel ao Board do dev.kit (US #858)', () => {
    const horas = (h: number) => new Date(Date.now() - h * 3600_000).toISOString();
    // Meia-noite de hoje + 1 min: "hoje" sem depender da hora em que o teste roda.
    const hojeCedo = () => { const d = new Date(); d.setHours(0, 1, 0, 0); return d.toISOString(); };
    const ontem = () => { const d = new Date(); d.setHours(12, 0, 0, 0); d.setDate(d.getDate() - 1); return d.toISOString(); };
    function comDatas() {
      responder();
      servidor.use(http.get(`${API}/api/remoto/maquinas/:id/retrato`, () => {
        const foto = retrato();
        foto.retrato.pendencias!.push({ id: 'p2', tipo: 'pergunta', titulo: 'Pendência antiga sem decisão', pergunta: 'De dez dias atrás', jaTentei: 'Nada ainda', projeto: 'projeto', item: 3 });
        foto.retrato.fluxos = [
          { id: 'f-hoje', nome: 'Execução de hoje', etapa: 'Implementação', situacao: 'rodando', desdeEm: hojeCedo() },
          { id: 'f-ontem', nome: 'Execução de ontem', etapa: 'Avaliação', situacao: 'fila', desdeEm: ontem() },
          { id: 'f-semana', nome: 'Execução de três dias', etapa: 'Fechamento', situacao: 'rodando', desdeEm: horas(72) },
          { id: 'f-antiga', nome: 'Execução de dez dias', etapa: 'Regressão', situacao: 'rodando', desdeEm: horas(240) },
          { id: 'f-sem-data', nome: 'Execução sem data', etapa: 'Planejamento', situacao: 'rodando' },
        ];
        foto.retrato.projetos = Array.from({ length: 7 }, (_, i) => ({ slug: `p${i}`, nome: `Projeto ${i + 1}`, ondaAtual: 1, situacao: 'aprovado', itens: [] }));
        return HttpResponse.json(foto);
      }));
    }
    const nomes = (painel: ReturnType<typeof within>) => painel.queryAllByText(/^Execução /).map((e: HTMLElement) => e.textContent);

    it('põe o painel de auxílio à direita do chat, na mesma ordem de leitura', async () => {
      responder();
      renderizar('/board', tokenDoDev());
      const painel = await screen.findByRole('complementary', { name: 'Board' });
      const chat = screen.getByRole('region', { name: 'Orquestrador' });
      expect(chat.compareDocumentPosition(painel) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(painel.closest('main')).toBe(chat.closest('main'));
      // No celular a aba Board traz o número de pendências, sem mudar o nome acessível da aba.
      await within(painel).findByText('Integrar o item 20 no projeto?');
      expect(screen.getByRole('tab', { name: 'Board' })).toHaveTextContent('Board1');
    });

    it('filtra as execuções por dia, preserva as pendências e guarda o período na URL', async () => {
      comDatas();
      renderizar('/board', tokenDoDev());
      const painel = within(await screen.findByRole('complementary', { name: 'Board' }));
      await painel.findByText('Execução de hoje');
      const filtro = within(painel.getByRole('group', { name: 'Filtro de dia' }));
      expect(filtro.getByRole('button', { name: 'Hoje' })).toHaveAttribute('aria-pressed', 'true');
      expect(nomes(painel)).toEqual(['Execução de hoje', 'Execução sem data']);
      await userEvent.click(filtro.getByRole('button', { name: 'Ontem' }));
      expect(nomes(painel)).toEqual(['Execução de ontem', 'Execução sem data']);
      // O período só existe no parâmetro dia: o botão pressionado depois do clique é a URL escrita.
      expect(filtro.getByRole('button', { name: 'Ontem' })).toHaveAttribute('aria-pressed', 'true');
      await userEvent.click(filtro.getByRole('button', { name: 'Semana' }));
      expect(nomes(painel)).toEqual(['Execução de hoje', 'Execução de ontem', 'Execução de três dias', 'Execução sem data']);
      await userEvent.click(filtro.getByRole('button', { name: 'Tudo' }));
      expect(nomes(painel)).toHaveLength(5);
      // As pendências nunca são filtradas por data.
      expect(painel.getByRole('heading', { name: 'Precisa de você · 2' })).toBeInTheDocument();
      expect(painel.getByText('Pendência antiga sem decisão')).toBeInTheDocument();
    });

    it('o período escolhido sobrevive ao recarregar pelo parâmetro dia', async () => {
      comDatas();
      renderizar('/board?dia=semana', tokenDoDev());
      const painel = within(await screen.findByRole('complementary', { name: 'Board' }));
      await painel.findByText('Execução de três dias');
      expect(painel.getByRole('button', { name: 'Semana' })).toHaveAttribute('aria-pressed', 'true');
      expect(painel.queryByText('Execução de dez dias')).not.toBeInTheDocument();
      expect(painel.getByRole('heading', { name: /Executando · 4/ })).toBeInTheDocument();
    });

    it('cartões nascem recolhidos, abrem com Ver detalhes e listas longas mostram Ver mais', async () => {
      comDatas();
      renderizar('/board?dia=tudo', tokenDoDev());
      const painel = within(await screen.findByRole('complementary', { name: 'Board' }));
      const cartao = within(await painel.findByRole('region', { name: 'Integrar o item 20 no projeto?' }));
      expect(cartao.queryByText('Escolha o destino')).not.toBeInTheDocument();
      expect(cartao.queryByRole('button', { name: 'Integrar' })).not.toBeInTheDocument();
      const detalhes = cartao.getByRole('button', { name: 'Ver detalhes' });
      expect(detalhes).toHaveAttribute('aria-expanded', 'false');
      await userEvent.click(detalhes);
      expect(cartao.getByRole('button', { name: 'Ocultar detalhes' })).toHaveAttribute('aria-expanded', 'true');
      expect(cartao.getByText('Escolha o destino')).toBeInTheDocument();
      expect(cartao.getByText('Nota 100 · regressão sem falhas')).toBeInTheDocument();
      expect(cartao.getByRole('button', { name: 'Integrar' })).toBeEnabled();
      // A pendência ainda sem dados de decisão também nasce recolhida, com o projeto e o item.
      const antiga = within(painel.getByRole('region', { name: 'Pendência antiga sem decisão' }));
      expect(antiga.getByText('projeto · item 3')).toBeInTheDocument();
      expect(antiga.queryByText('De dez dias atrás')).not.toBeInTheDocument();
      await userEvent.click(antiga.getByRole('button', { name: 'Ver detalhes' }));
      expect(antiga.getByText('De dez dias atrás')).toBeInTheDocument();
      // Sete projetos: cinco na tela e o Ver mais com o restante.
      expect(painel.getAllByRole('progressbar')).toHaveLength(5);
      await userEvent.click(painel.getByRole('button', { name: 'Ver mais (2)' }));
      expect(painel.getAllByRole('progressbar')).toHaveLength(7);
      expect(painel.getByRole('button', { name: 'Ver menos' })).toHaveAttribute('aria-expanded', 'true');
    });

    it('o aviso da notificação vira aviso com timer, fica em Avisos e o push está no painel', async () => {
      responder();
      renderizar('/board?notificacao=xyz', tokenDoDev());
      expect(await screen.findByText('Notificação inválida. Consulte as pendências do Board.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Fechar aviso' })).toBeInTheDocument();
      const painel = within(screen.getByRole('complementary', { name: 'Board' }));
      const avisos = painel.getByRole('button', { name: 'Avisos · 1' });
      expect(avisos).toHaveAttribute('aria-expanded', 'false');
      await userEvent.click(screen.getByRole('button', { name: 'Fechar aviso' }));
      expect(screen.queryByRole('button', { name: 'Fechar aviso' })).not.toBeInTheDocument();
      await userEvent.click(avisos);
      expect(painel.getByText('Notificação inválida. Consulte as pendências do Board.')).toBeInTheDocument();
      // O controle do push saiu da faixa do topo e mora no painel.
      expect(painel.getByText('Notificações neste dispositivo')).toBeInTheDocument();
    });
  });

  describe('projetos e projetos de design pelo site (item 4 do remote-control-v3)', () => {
    const catalogo = () => ({
      maquinaId: 'notebook', recebidoEm: new Date().toISOString(),
      catalogo: { contrato: 1, maquinaId: 'notebook', geradoEm: new Date().toISOString(), comandoId: null, workItems: [], fluxos: [],
        repositorios: [{ nome: 'github.com/acme/portal' }, { nome: 'github.com/acme/api' }], projetos: [],
        designs: [{ nome: 'github.com/acme/portal · main', telas: 2, repositorio: 'github.com/acme/portal' }] },
    });
    const unico = <T,>(lista: T[]) => { expect(lista).toHaveLength(1); return lista[0]; };
    /**
     * A fila e o dev.kit: o POST entra na-fila, e o desfecho que o teste escolhe (o concluido, ou a recusa) chega pelo SSE da
     * conversa — a resposta do dev.kit e o comando no estado final, como a API os publica.
     */
    function comFila(desfecho: (comando: { id: string; tipo: string; texto: string }) => { estado: string; texto: string }) {
      const enviados: Array<{ id: string; tipo: string; texto: string }> = [];
      const eventos: Array<{ id: number; tipo: string; maquinaId: string; comando: unknown; resposta: unknown; em: string }> = [];
      servidor.use(
        http.get(`${API}/api/remoto/maquinas/:id/catalogo`, () => HttpResponse.json(catalogo())),
        http.get(`${API}/api/design/uso`, () => HttpResponse.json({ versoes: 500, tetoDeVersoes: 500, bytes: 0, tetoDeBytes: 1, plano: 'Empresarial' })),
        http.get(`${API}/api/remoto/conversa/eventos`, ({ request }) => {
          const desde = Number(request.headers.get('Last-Event-ID') ?? 0);
          const corpo = eventos.filter(e => e.id > desde).map(e => `id: ${e.id}\nevent: ${e.tipo}\ndata: ${JSON.stringify(e)}\n\n`).join('');
          return new HttpResponse(corpo, { headers: { 'Content-Type': 'text/event-stream' } });
        }),
        http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
          const pedido = await request.json() as { id: string; tipo: string; texto: string };
          const base = { ...pedido, maquinaId: 'notebook', dispositivo: 'navegador', criadoEm: new Date().toISOString(),
            expiraEm: new Date(Date.now() + 900_000).toISOString(), entregueEm: null, concluidoEm: null, erro: '' };
          if (pedido.tipo === 'catalogo') return HttpResponse.json({ ...base, estado: 'na-fila' }, { status: 202 });
          enviados.push(pedido);
          const fim = desfecho(pedido);
          const em = new Date().toISOString();
          eventos.push({ id: eventos.length + 1, tipo: 'resposta', maquinaId: 'notebook', comando: null, em, resposta: { id: `r-${pedido.id}`, maquinaId: 'notebook',
            comandoId: pedido.id, tipo: fim.estado === 'concluido' ? 'concluido' : 'recusado', texto: fim.texto, geradaEm: em, recebidaEm: em } });
          eventos.push({ id: eventos.length + 1, tipo: 'comando', maquinaId: 'notebook', resposta: null, em, comando: { ...base, estado: fim.estado, concluidoEm: em } });
          return HttpResponse.json({ ...base, estado: 'na-fila' }, { status: 202 });
        }),
      );
      return enviados;
    }

    it('lista projetos e designs com o Abrir de cada um, sem escrever nada sem clique', async () => {
      responder(); comFila(() => ({ estado: 'concluido', texto: '' }));
      renderizar('/board', tokenDoDev());
      const painel = within(await screen.findByRole('complementary', { name: 'Board' }));
      expect(await painel.findByRole('link', { name: 'Abrir Projeto de demonstração' })).toHaveAttribute('href', '/projetos?slug=projeto');
      expect(await painel.findByRole('link', { name: 'Abrir o design de github.com/acme/portal · main' }))
        .toHaveAttribute('href', '/design?repositorio=github.com%2Facme%2Fportal');
      expect(painel.getAllByRole('link', { name: 'Ver todos' }).map(l => l.getAttribute('href'))).toEqual(['/projetos', '/design']);
      expect(painel.getByText('2 telas')).toBeInTheDocument();
    });

    it('cria o projeto pelo formulário com os repositórios do catálogo e mostra o desfecho do dev.kit, sem aprovar nem disparar', async () => {
      responder();
      const enviados = comFila(c => ({ estado: 'concluido', texto: `Projeto '${JSON.parse(c.texto).nome}' criado (portal-de-pedidos, branch feature/dev/projeto-portal-de-pedidos), em rascunho: o plano, a aprovação e o disparo seguem no dev.kit.` }));
      renderizar('/board', tokenDoDev());
      const painel = within(await screen.findByRole('complementary', { name: 'Board' }));
      await userEvent.click(await painel.findByRole('button', { name: '+ Novo projeto' }));
      const dialogo = within(screen.getByRole('dialog', { name: 'Novo projeto no dev.kit' }));
      const criar = dialogo.getByRole('button', { name: 'Criar no dev.kit' });
      expect(criar).toBeDisabled();
      await userEvent.type(dialogo.getByLabelText('Nome'), 'Portal de pedidos');
      await userEvent.click(await dialogo.findByRole('checkbox', { name: 'github.com/acme/api' }));
      await userEvent.type(dialogo.getByLabelText('Definição (opcional)'), 'Pedidos no portal');
      expect(criar).toBeEnabled();
      await userEvent.click(criar);

      const pedido = unico(enviados);
      expect(pedido.tipo).toBe('projeto');
      expect(JSON.parse(pedido.texto)).toEqual({ nome: 'Portal de pedidos', repositorios: ['github.com/acme/api'], definicao: 'Pedidos no portal', nivel: 'usuario' });
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      // O desfecho é o do dev.kit, no painel; o chat não o repete.
      const desfecho = await painel.findByRole('status', { name: 'Desfecho da criação' }, { timeout: 6000 });
      await waitFor(() => expect(desfecho).toHaveTextContent('Novo projeto'), { timeout: 6000 });
      await waitFor(() => expect(desfecho).toHaveTextContent("Projeto 'Portal de pedidos' criado"), { timeout: 6000 });
      expect(within(screen.getByRole('log', { name: 'Conversa com o Orquestrador' })).queryByText(/Portal de pedidos/)).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /Aprovar|Disparar/ })).not.toBeInTheDocument();
    }, 15_000);

    it('o design mostra o teto de versões cheio e a recusa do dev.kit antigo volta com o motivo', async () => {
      responder();
      const enviados = comFila(() => ({ estado: 'recusado', texto: "Não executado: o tipo de comando 'design' não é conhecido por esta versão do dev.kit." }));
      renderizar('/board', tokenDoDev());
      const painel = within(await screen.findByRole('complementary', { name: 'Board' }));
      await userEvent.click(await painel.findByRole('button', { name: '+ Novo design' }));
      const dialogo = within(screen.getByRole('dialog', { name: 'Novo projeto de design' }));
      // O repositório que ainda não tem design vem escolhido; o teto cheio é avisado (regra de impacto #25).
      expect(dialogo.getByLabelText('Repositório (catálogo da máquina)')).toHaveValue('github.com/acme/api');
      expect(await dialogo.findByRole('alert')).toHaveTextContent('500 de 500 versões de design');
      await userEvent.click(dialogo.getByRole('button', { name: 'Criar no dev.kit' }));

      expect(JSON.parse(unico(enviados).texto)).toEqual({ repositorio: 'github.com/acme/api' });
      const desfecho = await painel.findByRole('status', { name: 'Desfecho da criação' }, { timeout: 6000 });
      await waitFor(() => expect(desfecho).toHaveTextContent('Novo design'), { timeout: 6000 });
      await waitFor(() => expect(desfecho).toHaveTextContent("o tipo de comando 'design' não é conhecido por esta versão do dev.kit"), { timeout: 6000 });
      expect(within(desfecho).getByText(/Novo design · .* · recusado/)).toBeInTheDocument();
    }, 15_000);

    it('a API que não conhece o tipo recusa e o formulário mostra o motivo, sem fechar', async () => {
      responder(); comFila(() => ({ estado: 'concluido', texto: '' }));
      servidor.use(http.post(`${API}/api/remoto/comandos`, async ({ request }) => {
        const pedido = await request.json() as { tipo: string };
        return pedido.tipo === 'catalogo'
          ? HttpResponse.json({ ...pedido, estado: 'na-fila', criadoEm: new Date().toISOString(), expiraEm: new Date(Date.now() + 900_000).toISOString() }, { status: 202 })
          : HttpResponse.json({ detail: 'O tipo é um de: mensagem, ligar, reiniciar, catalogo.' }, { status: 400 });
      }));
      renderizar('/board', tokenDoDev());
      const painel = within(await screen.findByRole('complementary', { name: 'Board' }));
      await userEvent.click(await painel.findByRole('button', { name: '+ Novo design' }));
      const dialogo = within(screen.getByRole('dialog', { name: 'Novo projeto de design' }));
      await userEvent.click(dialogo.getByRole('button', { name: 'Criar no dev.kit' }));
      expect(await dialogo.findByText(/Não enviado\. O tipo é um de: mensagem, ligar, reiniciar, catalogo/)).toBeInTheDocument();
      expect(screen.getByRole('dialog', { name: 'Novo projeto de design' })).toBeInTheDocument();
      await userEvent.keyboard('{Escape}');
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });

  it('encerra a sessão quando a API responde 401', async () => {
    servidor.use(http.get(`${API}/api/remoto/maquinas`, () => new HttpResponse(null, { status: 401 })));
    renderizar('/board', tokenDoDev());
    await waitFor(() => expect(screen.getByRole('heading', { name: /Entrar/i })).toBeInTheDocument());
  });
});
