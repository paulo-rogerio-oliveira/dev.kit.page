import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

function worker() {
  const eventos: Record<string, (e: unknown) => void> = {};
  const showNotification = vi.fn(async (_titulo: string, _opcoes: NotificationOptions) => {});
  const openWindow = vi.fn(async () => {});
  const janela = { url: 'https://site.test/board?aba=chat', navigate: vi.fn(async () => {}), focus: vi.fn(async () => {}) };
  const matchAll = vi.fn(async () => [janela]);
  const self = { location: { origin: 'https://site.test' }, registration: { showNotification }, clients: { matchAll, openWindow },
    addEventListener: (nome: string, acao: (e: unknown) => void) => { eventos[nome] = acao; } };
  runInNewContext(readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), '../public/board-sw.js'), 'utf8'), { self, URL });
  return { eventos, showNotification, openWindow, matchAll, janela };
}

it('push mostra somente texto fixo mesmo que o payload tenha conteúdo sensível e substitui duplicatas pela tag', async () => {
  const w = worker(); const espera = vi.fn(); const id = 'a'.repeat(32);
  for (let n = 0; n < 2; n++) w.eventos.push({ data: { json: () => ({ entregaId: id, titulo: 'Segredo', body: 'token privado', url: 'https://evil.test' }) }, waitUntil: espera });
  await Promise.all(espera.mock.calls.map(c => c[0]));
  expect(w.showNotification).toHaveBeenCalledTimes(2);
  const [titulo, dados] = w.showNotification.mock.calls[0] as unknown as [string, NotificationOptions];
  expect(titulo).toBe('dev.kit · decisão pendente'); expect(dados.body).toBe('Há uma decisão pendente. Abra o dev.kit para conferir.');
  expect(JSON.stringify([titulo, dados])).not.toMatch(/Segredo|token privado|evil/);
  expect(dados.tag).toBe(`devkit-decisao-${id}`); expect(dados.data).toEqual({ entregaId: id });
  expect(w.showNotification.mock.calls[1]).toEqual(w.showNotification.mock.calls[0]);
});

it('push inválido não renderiza texto arbitrário', () => {
  const w = worker();
  w.eventos.push({ data: { json: () => ({ entregaId: '../login' }) }, waitUntil: vi.fn() });
  w.eventos.push({ data: { json: () => { throw new Error('JSON'); } }, waitUntil: vi.fn() });
  expect(w.showNotification).not.toHaveBeenCalled();
});

it('clique abre a decisão no Board existente sem confiar em URL do payload', async () => {
  const w = worker(); const espera = vi.fn(); const close = vi.fn(); const id = 'b'.repeat(32);
  w.eventos.notificationclick({ notification: { data: { entregaId: id, url: 'https://evil.test' }, close }, waitUntil: espera });
  await espera.mock.calls[0][0];
  expect(close).toHaveBeenCalledOnce(); expect(w.janela.navigate).toHaveBeenCalledWith(`https://site.test/board?notificacao=${id}`);
  expect(w.janela.focus).toHaveBeenCalledOnce(); expect(w.openWindow).not.toHaveBeenCalled();
});

it('sem Board aberto, o clique abre uma janela autenticável na mesma origem', async () => {
  const w = worker(); w.matchAll.mockResolvedValue([]); const espera = vi.fn(); const id = 'c'.repeat(32);
  w.eventos.notificationclick({ notification: { data: { entregaId: id }, close: vi.fn() }, waitUntil: espera });
  await espera.mock.calls[0][0]; expect(w.openWindow).toHaveBeenCalledWith(`https://site.test/board?notificacao=${id}`);
});
