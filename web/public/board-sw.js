// Nenhuma resposta autenticada, retrato ou sessão é armazenada no cache.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
// O servidor envia apenas o identificador opaco. Nunca renderiza texto recebido no payload.
self.addEventListener('push', event => {
  let id;
  try { id = event.data?.json()?.entregaId; } catch { return; }
  if (typeof id !== 'string' || !/^[a-f0-9]{32}$/.test(id)) return;
  event.waitUntil(self.registration.showNotification('dev.kit · decisão pendente', {
    body: 'Há uma decisão pendente. Abra o dev.kit para conferir.',
    icon: '/board-192.png', badge: '/board-192.png',
    tag: `devkit-decisao-${id}`, data: { entregaId: id },
  }));
});
self.addEventListener('notificationclick', event => {
  event.notification.close();
  const id = event.notification.data?.entregaId;
  if (typeof id !== 'string' || !/^[a-f0-9]{32}$/.test(id)) return;
  const url = new URL(`/board?notificacao=${id}`, self.location.origin).href;
  event.waitUntil((async () => {
    const janelas = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    const board = janelas.find(c => { const u = new URL(c.url); return u.origin === self.location.origin && u.pathname === '/board'; });
    if (board) { await board.navigate(url); await board.focus(); }
    else await self.clients.openWindow(url);
  })());
});
self.addEventListener('pushsubscriptionchange', event => {
  // O worker não tem JWT: uma renovação pede nova associação autenticada ao abrir o Board.
  event.waitUntil((async () => {
    await event.newSubscription?.unsubscribe();
    for (const c of await self.clients.matchAll({ type: 'window' })) c.postMessage({ tipo: 'push-alterado' });
  })());
});
self.addEventListener('fetch', event => {
  if (event.request.mode !== 'navigate') return;
  const url = new URL(event.request.url);
  if (url.origin !== self.location.origin || url.pathname !== '/board') return;
  event.respondWith(fetch(event.request).catch(() => new Response(
    '<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>dev.kit — sem conexão</title><style>:root{color-scheme:light;--bg:#fafbfc;--fg:#1d2129;--link:#2f6fde;--line:#e3e6eb;--surface:#fff}@media(prefers-color-scheme:dark){:root{color-scheme:dark;--bg:#1b1e24;--fg:#f1f2f5;--link:#9bbdff;--line:#343943;--surface:#242830}}*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;font:16px/1.5 "Segoe UI",system-ui,sans-serif;background:var(--bg);color:var(--fg);padding:24px}.board-offline{max-width:480px;padding:24px;border:1px solid var(--line);border-radius:12px;background:var(--surface);overflow-wrap:anywhere}h1{font-size:22px;margin:0 0 16px}a{color:var(--link)}</style><body><main class="board-offline"><h1>Board sem conexão</h1><p>Conecte-se à internet para consultar o estado atualizado da máquina. Nenhum retrato foi salvo neste dispositivo.</p><a href="/board">Tentar novamente</a></main></body></html>',
    { headers: { 'Content-Type': 'text/html; charset=utf-8' } },
  )));
});
