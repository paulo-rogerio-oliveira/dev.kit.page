import { act, screen, waitFor } from '@testing-library/react';
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
    http.get(`${API}/api/remoto/maquinas/:id/conversa`, () => HttpResponse.json({ comandos: [], respostas: [], ultimoEvento: 0 })),
    http.get(`${API}/api/remoto/conversa/eventos`, () => new HttpResponse('', { headers: { 'Content-Type': 'text/event-stream' } })),
  );
}

describe('Board remoto', () => {
  it('exige login e preserva o destino do desenvolvedor após autenticar', async () => {
    renderizar('/board');
    expect(await screen.findByRole('heading', { name: /Entrar/i })).toBeInTheDocument();
    expect(destinoDa(tokenDoDev(), '/board')).toBe('/board');
  });

  it('mostra o retrato, progresso e execuções; decisões continuam somente na máquina', async () => {
    responder();
    const chamadas: Request[] = [];
    const registrar = ({ request }: { request: Request }) => { chamadas.push(request); };
    servidor.events.on('request:start', registrar);
    try {
      renderizar('/board', tokenDoDev());
      expect(await screen.findByText('Integrar o item 20 no projeto?')).toBeInTheDocument();
      expect(screen.getByText('Opções: Integrar · Aguardar')).toBeInTheDocument();
      expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '1');
      expect(screen.getByText('Item 20 · layout do plano')).toBeInTheDocument();
      expect(screen.getByText(/Implementação · codex/)).toBeInTheDocument();
      for (const nome of ['Decidir', 'Ver detalhes', 'Enviar mensagem']) {
        expect(screen.getByRole('button', { name: nome })).toBeDisabled();
      }
      await waitFor(() => expect(screen.getByLabelText('Mensagem ao Orquestrador')).toBeEnabled());
      // (item 8) Com o dev.kit aberto e a máquina conectada, o Reiniciar vai ao serviço — sem clique, nada é escrito.
      expect(screen.getByRole('button', { name: 'Reiniciar dev.kit' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Trocar fluxo' })).toBeEnabled();
      expect(screen.getByText('Mesma conversa do Board do dev.kit')).toBeInTheDocument();
      expect(chamadas.length).toBeGreaterThan(0);
      expect(chamadas.every(r => r.method === 'GET' && r.headers.has('Authorization'))).toBe(true);
    } finally { servidor.events.removeListener('request:start', registrar); }
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
    expect(screen.queryByRole('link', { name: /Projetos|Fluxos|Infra/ })).not.toBeInTheDocument();
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

  it('encerra a sessão quando a API responde 401', async () => {
    servidor.use(http.get(`${API}/api/remoto/maquinas`, () => new HttpResponse(null, { status: 401 })));
    renderizar('/board', tokenDoDev());
    await waitFor(() => expect(screen.getByRole('heading', { name: /Entrar/i })).toBeInTheDocument());
  });
});
