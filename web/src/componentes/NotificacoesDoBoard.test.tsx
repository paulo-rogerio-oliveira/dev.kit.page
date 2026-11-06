import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { NotificacoesDoBoard } from './NotificacoesDoBoard';
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
