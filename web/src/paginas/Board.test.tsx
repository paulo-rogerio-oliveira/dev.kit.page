import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { delay, http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';
import type { MaquinaRemota, PainelRemoto } from '../api/remoto';
import { destinoDa } from '../sessao';

const maquina: MaquinaRemota = {
  maquinaId: '3f2b1c9e5d7a4b60a1c2d3e4f5061728', apelido: 'Notebook', versaoDevKit: '1.61.0',
  conectada: true, devKitAberto: true, agente: 'ativo', turnosEmAndamento: 1,
  ultimoHeartbeat: '2026-11-02T08:52:30Z', retratoRecebidoEm: '2026-11-02T08:52:01Z',
};
const painel: PainelRemoto = { maquina, retrato: {
  versao: 'v1', maquinaId: maquina.maquinaId, geradoEm: '2026-11-02T08:52:00Z',
  projetos: [{ slug: 'projeto', nome: 'projeto', situacao: 'aprovado', itens: 21, integrados: 20, rodando: 1, onda: 10, ondas: 10 }],
  fluxos: [{ id: 'f1', titulo: 'Item 20 · layout do plano', fluxo: 'Tela', etapa: 'Implementação', situacao: 'na-fila', projeto: 'projeto', item: 20, desde: '2026-11-02T08:40:00Z' }],
  pendencias: [{ id: 'p1', titulo: 'Integrar o item 20 no projeto?', tipo: 'aprovacao', resumo: 'Nota 100 · regressão sem falhas', opcoes: ['Aprovar'], projeto: 'projeto', item: 20, desde: '2026-11-02T08:50:00Z' }],
} };

function responder(dados: PainelRemoto | null = painel) {
  servidor.use(
    http.get(`${API}/api/remoto/v1/maquinas`, ({ request }) => {
      expect(request.headers.get('Authorization')).toBe('Bearer token-dev');
      return HttpResponse.json(dados ? [dados.maquina] : []);
    }),
    http.get(`${API}/api/remoto/v1/maquinas/:id`, () => HttpResponse.json(dados)),
  );
}

describe('Board remoto somente leitura', () => {
  it('lê o contrato autenticado, mostra cartões e mantém todos os comandos desabilitados', async () => {
    responder();
    const metodos: string[] = [];
    const observar = ({ request }: { request: Request }) => { if (request.url.includes('/api/remoto/')) metodos.push(request.method); };
    servidor.events.on('request:start', observar);
    try {
      renderizar('/board', tokenDoDev());
      expect(await screen.findByText('Integrar o item 20 no projeto?')).toBeInTheDocument();
      expect(screen.getByRole('progressbar', { name: 'Andamento de projeto' })).toHaveValue(20);
      expect(screen.getByText('onda 10 de 10 · 1 rodando')).toBeInTheDocument();
      expect(screen.getByText('na fila')).toBeInTheDocument();
      for (const nome of ['Ver detalhes', 'Decidir', 'Trocar fluxo', '# Work item', '@ Repositório', 'Claude Code · Opus', 'Enviar mensagem indisponível']) {
        expect(screen.getByRole('button', { name: nome })).toBeDisabled();
      }
      screen.getAllByRole('button', { name: 'Reiniciar dev.kit' }).forEach(b => expect(b).toBeDisabled());
      expect(screen.getByRole('textbox', { name: 'Mensagem ao Orquestrador' })).toBeDisabled();
      expect(screen.getByRole('heading', { name: 'Chat indisponível nesta fase' })).toBeInTheDocument();
      await userEvent.click(screen.getByRole('tab', { name: 'Chat' }));
      expect(screen.getByRole('tab', { name: 'Chat' })).toHaveAttribute('aria-selected', 'true');
      await userEvent.keyboard('{ArrowRight}');
      expect(screen.getByRole('tab', { name: 'Board' })).toHaveFocus();
      expect(metodos.length).toBeGreaterThanOrEqual(2);
      expect(metodos.every(m => m === 'GET')).toBe(true);
    } finally { servidor.events.removeListener('request:start', observar); }
  });

  it('trata máquina ausente, sem retrato, desconectada, app fechado e código futuro', async () => {
    responder({ maquina: { ...maquina, conectada: false, devKitAberto: false }, retrato: {
      ...painel.retrato!, pendencias: [], projetos: [], fluxos: [{ ...painel.retrato!.fluxos[0], situacao: 'novo-estado' }],
    } });
    const { unmount } = renderizar('/board', tokenDoDev());
    expect(await screen.findByText(/Máquina desconectada/)).toHaveTextContent('último estado conhecido');
    expect(screen.getAllByText(/dev.kit fechado/).length).toBeGreaterThan(0);
    expect(screen.getByText('Nenhuma pendência.')).toBeInTheDocument();
    expect(screen.getByText('Nenhum projeto publicado.')).toBeInTheDocument();
    expect(screen.getByText('novo-estado')).toHaveClass('board-chip-neutro');
    unmount();
    responder({ maquina, retrato: null });
    const segundo = renderizar('/board', tokenDoDev());
    expect(await screen.findByText(/ainda não publicou o retrato/)).toBeInTheDocument();
    segundo.unmount();
    responder(null);
    renderizar('/board', tokenDoDev());
    expect(await screen.findByText(/Nenhuma máquina conectada à sua conta/)).toBeInTheDocument();
  });

  it('permite tentar de novo após falha de rede', async () => {
    responder();
    servidor.use(http.get(`${API}/api/remoto/v1/maquinas`, () => HttpResponse.error()));
    renderizar('/board', tokenDoDev());
    expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível atualizar');
    responder();
    await userEvent.click(screen.getByRole('button', { name: 'Tentar novamente' }));
    expect(await screen.findByText('Integrar o item 20 no projeto?')).toBeInTheDocument();
  });

  it('troca a máquina sem mostrar resposta atrasada da anterior', async () => {
    const outra = { ...maquina, maquinaId: 'outro-id', apelido: 'Desktop' };
    servidor.use(
      http.get(`${API}/api/remoto/v1/maquinas`, () => HttpResponse.json([maquina, outra])),
      http.get(`${API}/api/remoto/v1/maquinas/:id`, async ({ params }) => {
        if (params.id === maquina.maquinaId) { await delay(150); return HttpResponse.json(painel); }
        return HttpResponse.json({ maquina: outra, retrato: null });
      }),
    );
    renderizar('/board', tokenDoDev());
    const seletor = await screen.findByRole('combobox', { name: 'Máquina' });
    await userEvent.selectOptions(seletor, outra.maquinaId);
    expect(await screen.findByText(/ainda não publicou o retrato/)).toBeInTheDocument();
    await new Promise(resolve => setTimeout(resolve, 200));
    expect(screen.queryByText('Integrar o item 20 no projeto?')).not.toBeInTheDocument();
    expect(seletor).toHaveValue(outra.maquinaId);
  });

  it('exige login, preserva o retorno ao Board para dev e rejeita troca de senha pendente', async () => {
    expect(destinoDa(tokenDoDev(), '/board')).toBe('/board');
    const primeiro = renderizar('/board');
    expect(await screen.findByRole('heading', { name: /Entrar/ })).toBeInTheDocument();
    primeiro.unmount();
    renderizar('/board', { ...tokenDoDev(), deveTrocarSenha: true });
    expect(await screen.findByRole('heading', { name: /senha/i })).toBeInTheDocument();
  });

  it('retorna ao login em 401 e mostra uma recusa 403 sem apresentar dados de outra conta', async () => {
    servidor.use(http.get(`${API}/api/remoto/v1/maquinas`, () => new HttpResponse(null, { status: 401 })));
    const primeiro = renderizar('/board', tokenDoDev());
    expect(await screen.findByRole('heading', { name: /Entrar/ })).toBeInTheDocument();
    primeiro.unmount();
    servidor.use(http.get(`${API}/api/remoto/v1/maquinas`, () => HttpResponse.json({ detail: 'Acesso recusado' }, { status: 403 })));
    renderizar('/board', tokenDoDev());
    expect(await screen.findByRole('alert')).toHaveTextContent('Acesso recusado');
    expect(screen.queryByText('Integrar o item 20 no projeto?')).not.toBeInTheDocument();
  });

  it('oferece instalação local, instruções e o prompt nativo quando disponível', async () => {
    responder(null);
    renderizar('/board', tokenDoDev());
    await userEvent.click(screen.getByRole('button', { name: 'Instalar app' }));
    const ajuda = screen.getByRole('dialog', { name: 'Instalar o Board' });
    expect(within(ajuda).getByText(/Adicionar à Tela de Início/)).toBeInTheDocument();
    await userEvent.click(within(ajuda).getByRole('button', { name: 'Fechar' }));
    const prompt = vi.fn().mockResolvedValue(undefined);
    const evento = new Event('beforeinstallprompt', { cancelable: true });
    Object.assign(evento, { prompt, userChoice: Promise.resolve({ outcome: 'accepted' }) });
    act(() => { window.dispatchEvent(evento); });
    await userEvent.click(screen.getByRole('button', { name: 'Instalar app' }));
    await waitFor(() => expect(prompt).toHaveBeenCalledOnce());
    act(() => { window.dispatchEvent(new Event('appinstalled')); });
    expect(await screen.findByRole('button', { name: 'App instalado' })).toBeDisabled();
  });
});
