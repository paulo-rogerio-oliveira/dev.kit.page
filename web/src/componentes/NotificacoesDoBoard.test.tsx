import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { AvisosDoBoard, duracaoDoAviso, HistoricoDeAvisos, NotificacoesDoBoard, pendenciasNovas, useAvisosDoBoard } from './NotificacoesDoBoard';
import { CHAVE_DO_PUSH, cancelarPushAoSair } from '../boardPush';
import { API, servidor } from '../testes/servidor';

const contexto = vi.hoisted(() => ({ sessao: { token: 'teste' }, sair: vi.fn() }));
vi.mock('../sessao', () => ({ useSessao: () => contexto }));
const id = 'a'.repeat(32);
const permissao = vi.fn();
const unsubscribe = vi.fn();
const subscribe = vi.fn();
const getSubscription = vi.fn();
const registro = { active: {}, pushManager: { subscribe, getSubscription }, getNotifications: vi.fn(async () => []) };
const sub = { options: { applicationServerKey: new Uint8Array([4]).buffer }, unsubscribe,
  toJSON: () => ({ endpoint: 'https://fcm.googleapis.com/demo', keys: { p256dh: 'pub', auth: 'auth' } }) };
// O global localStorage do Node 26 não é o armazenamento de uma origem do navegador.
// O cenário usa um adaptador Storage isolado, como já faz com PushManager e Notification.
const itens = new Map<string, string>();
const armazenamento: Storage = {
  get length() { return itens.size; }, clear: () => itens.clear(),
  getItem: chave => itens.get(chave) ?? null, setItem: (chave, valor) => { itens.set(chave, valor); },
  removeItem: chave => { itens.delete(chave); }, key: indice => [...itens.keys()][indice] ?? null,
};

beforeEach(() => {
  itens.clear(); vi.stubGlobal('localStorage', armazenamento); vi.clearAllMocks();
  vi.stubGlobal('isSecureContext', true); vi.stubGlobal('PushManager', class {});
  vi.stubGlobal('Notification', { permission: 'granted', requestPermission: permissao });
  Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: Object.assign(new EventTarget(), { getRegistration: vi.fn(async () => registro) }) });
  permissao.mockResolvedValue('granted'); unsubscribe.mockResolvedValue(true); subscribe.mockResolvedValue(sub); getSubscription.mockResolvedValue(null);
  servidor.use(http.get(`${API}/api/remoto/push`, () => HttpResponse.json({ disponivel: true, chavePublica: 'BA' })));
});
afterEach(() => { vi.unstubAllGlobals(); Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: undefined }); });

it('pede permissão somente após toque, assina e cancela na API antes de desligar', async () => {
  const operacoes: string[] = [];
  servidor.use(
    http.post(`${API}/api/remoto/push/assinaturas`, async ({ request }) => { operacoes.push('assinar'); expect(request.headers.get('Authorization')).toBe('Bearer teste'); expect(await request.json()).toEqual(sub.toJSON()); return HttpResponse.json({ id }); }),
    http.delete(`${API}/api/remoto/push/assinaturas/${id}`, () => { operacoes.push('cancelar'); return new HttpResponse(null, { status: 204 }); }),
  );
  render(<NotificacoesDoBoard />);
  await userEvent.click(screen.getByText('Notificações neste dispositivo'));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Ativar notificações' })).toBeEnabled());
  expect(permissao).not.toHaveBeenCalled(); expect(subscribe).not.toHaveBeenCalled();
  await userEvent.click(screen.getByRole('button', { name: 'Ativar notificações' }));
  expect(await screen.findByText('Ativadas')).toBeInTheDocument(); expect(localStorage.getItem(CHAVE_DO_PUSH)).toBe(id);
  expect(subscribe).toHaveBeenCalledWith({ userVisibleOnly: true, applicationServerKey: new Uint8Array([4]) });
  getSubscription.mockResolvedValue(sub);
  await userEvent.click(screen.getByRole('button', { name: 'Desligar notificações' }));
  expect(await screen.findByText('Desligadas')).toBeInTheDocument(); expect(unsubscribe).toHaveBeenCalledOnce();
  expect(localStorage.getItem(CHAVE_DO_PUSH)).toBeNull(); expect(operacoes).toEqual(['assinar', 'cancelar']);
});

it('permissão negada não cria assinatura nem chama subscribe', async () => {
  permissao.mockResolvedValue('denied'); render(<NotificacoesDoBoard />);
  await userEvent.click(screen.getByText('Notificações neste dispositivo'));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Ativar notificações' })).toBeEnabled());
  await userEvent.click(screen.getByRole('button', { name: 'Ativar notificações' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Permissão não concedida'); expect(subscribe).not.toHaveBeenCalled();
});

it('falha no cancelamento conserva o estado ativado e permite repetir', async () => {
  localStorage.setItem(CHAVE_DO_PUSH, id); getSubscription.mockResolvedValue(sub);
  servidor.use(http.get(`${API}/api/remoto/push/assinaturas/${id}`, () => HttpResponse.json({ id })),
    http.delete(`${API}/api/remoto/push/assinaturas/${id}`, () => HttpResponse.json({ detail: 'Sem conexão com o serviço' }, { status: 503 })));
  render(<NotificacoesDoBoard />); await userEvent.click(screen.getByText('Notificações neste dispositivo'));
  await userEvent.click(await screen.findByRole('button', { name: 'Desligar notificações' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Sem conexão'); expect(screen.getByText('Ativadas')).toBeInTheDocument();
  expect(unsubscribe).not.toHaveBeenCalled(); expect(localStorage.getItem(CHAVE_DO_PUSH)).toBe(id);
});

it('falha ao registrar desfaz a assinatura do navegador', async () => {
  servidor.use(http.post(`${API}/api/remoto/push/assinaturas`, () => HttpResponse.json({ detail: 'Serviço indisponível' }, { status: 503 })));
  render(<NotificacoesDoBoard />); await userEvent.click(screen.getByText('Notificações neste dispositivo'));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Ativar notificações' })).toBeEnabled());
  await userEvent.click(screen.getByRole('button', { name: 'Ativar notificações' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Serviço indisponível'); expect(unsubscribe).toHaveBeenCalledOnce();
  expect(localStorage.getItem(CHAVE_DO_PUSH)).toBeNull();
});

it('sair sem rede cancela a assinatura local para não receber avisos da conta anterior', async () => {
  localStorage.setItem(CHAVE_DO_PUSH, id); getSubscription.mockResolvedValue(sub);
  servidor.use(http.delete(`${API}/api/remoto/push/assinaturas/${id}`, () => HttpResponse.error()));
  await cancelarPushAoSair('teste'); expect(unsubscribe).toHaveBeenCalledOnce(); expect(localStorage.getItem(CHAVE_DO_PUSH)).toBeNull();
});

it('navegador sem suporte explica a limitação e não solicita permissão', async () => {
  vi.stubGlobal('isSecureContext', false); render(<NotificacoesDoBoard />);
  await userEvent.click(screen.getByText('Notificações neste dispositivo'));
  expect(await screen.findByText(/Notificações indisponíveis neste navegador/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Ativar notificações' })).toBeDisabled(); expect(permissao).not.toHaveBeenCalled();
});

describe('aviso do site com timer (US #858)', () => {
  // Um Board mínimo: dispara avisos, mostra os da tela e o histórico do painel.
  function Painel() {
    const { avisos, avisar, encerrar } = useAvisosDoBoard();
    return <>
      <button onClick={() => avisar('Nova pendência: Integrar?', 'pendencia:p1')}>pendência</button>
      <button onClick={() => avisar('Decisão aberta a partir da notificação.')}>outro</button>
      <AvisosDoBoard avisos={avisos} encerrar={encerrar} />
      <HistoricoDeAvisos avisos={avisos} />
    </>;
  }
  const naTela = () => document.querySelector<HTMLElement>('.br-avisos')!;
  const avancar = (ms: number) => act(() => { vi.advanceTimersByTime(ms); });
  beforeEach(() => { vi.useFakeTimers(); });
  afterEach(() => { vi.useRealTimers(); });

  it('some sozinho depois do tempo definido e continua listado em Avisos', () => {
    render(<Painel />);
    fireEvent.click(screen.getByRole('button', { name: 'outro' }));
    expect(within(naTela()).getByText('Decisão aberta a partir da notificação.')).toBeInTheDocument();
    avancar(duracaoDoAviso - 1);
    expect(within(naTela()).queryByText('Decisão aberta a partir da notificação.')).toBeInTheDocument();
    avancar(1);
    expect(naTela()).toBeEmptyDOMElement();
    fireEvent.click(screen.getByRole('button', { name: 'Avisos · 1' }));
    expect(screen.getByText('Decisão aberta a partir da notificação.')).toBeInTheDocument();
  });

  it('pausa com o mouse e com o foco, e retoma o tempo que faltava', () => {
    render(<Painel />);
    fireEvent.click(screen.getByRole('button', { name: 'outro' }));
    avancar(4_000);
    fireEvent.mouseEnter(naTela());
    avancar(30_000);
    expect(within(naTela()).getByText('Decisão aberta a partir da notificação.')).toBeInTheDocument();
    fireEvent.mouseLeave(naTela());
    act(() => screen.getByRole('button', { name: 'Fechar aviso' }).focus());
    avancar(30_000);
    expect(within(naTela()).getByText('Decisão aberta a partir da notificação.')).toBeInTheDocument();
    act(() => screen.getByRole('button', { name: 'outro' }).focus());
    avancar(1_999);
    expect(naTela()).not.toBeEmptyDOMElement();
    avancar(1);
    expect(naTela()).toBeEmptyDOMElement();
  });

  it('agrupa os avisos iguais num só, com contador, e recomeça o tempo', () => {
    render(<Painel />);
    fireEvent.click(screen.getByRole('button', { name: 'pendência' }));
    fireEvent.click(screen.getByRole('button', { name: 'pendência' }));
    expect(within(naTela()).getAllByText('Nova pendência: Integrar?')).toHaveLength(1);
    expect(within(naTela()).getByText('×2')).toBeInTheDocument();
    avancar(4_000);
    fireEvent.click(screen.getByRole('button', { name: 'pendência' }));
    expect(within(naTela()).getByText('×3')).toBeInTheDocument();
    avancar(4_000);
    expect(within(naTela()).getByText('Nova pendência: Integrar?')).toBeInTheDocument();
    avancar(2_000);
    expect(naTela()).toBeEmptyDOMElement();
    fireEvent.click(screen.getByRole('button', { name: 'Avisos · 1' }));
    expect(screen.getByText('Nova pendência: Integrar? ×3')).toBeInTheDocument();
  });

  it('o × fecha o aviso na hora, sem tirá-lo do histórico', () => {
    render(<Painel />);
    fireEvent.click(screen.getByRole('button', { name: 'outro' }));
    fireEvent.click(screen.getByRole('button', { name: 'pendência' }));
    expect(screen.getAllByRole('button', { name: 'Fechar aviso' })).toHaveLength(2);
    fireEvent.click(screen.getAllByRole('button', { name: 'Fechar aviso' })[0]);
    expect(screen.getAllByRole('button', { name: 'Fechar aviso' })).toHaveLength(1);
    const historico = screen.getByRole('button', { name: 'Avisos · 2' });
    expect(historico).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(historico);
    expect(historico).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
  });

  it('com menos movimento pedido pelo sistema, o aviso aparece sem animação', () => {
    vi.stubGlobal('matchMedia', (consulta: string) => ({ matches: consulta === '(prefers-reduced-motion: reduce)', addEventListener: vi.fn(), removeEventListener: vi.fn() }));
    render(<Painel />);
    fireEvent.click(screen.getByRole('button', { name: 'outro' }));
    expect(naTela()).not.toHaveClass('br-avisos-animados');
    expect(naTela().querySelector('.br-aviso-tempo')).toBeNull();
  });

  it('avisa só as pendências que chegaram depois do primeiro retrato', () => {
    const p = (id: string) => ({ id, tipo: 'pergunta', titulo: id });
    expect(pendenciasNovas(null, [p('a'), p('b')])).toEqual([]);
    expect(pendenciasNovas(new Set(['a']), [p('a'), p('b')]).map(x => x.id)).toEqual(['b']);
    expect(pendenciasNovas(new Set(['a', 'b']), [p('b')])).toEqual([]);
  });
});
