// Nenhuma resposta autenticada, retrato ou sessão é armazenada no cache.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', event => {
  if (event.request.mode !== 'navigate') return;
  const url = new URL(event.request.url);
  if (url.origin !== self.location.origin || url.pathname !== '/board') return;
  event.respondWith(fetch(event.request).catch(() => new Response(
    '<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>dev.kit — sem conexão</title><style>:root{color-scheme:light;--bg:#fafbfc;--fg:#1d2129;--link:#2f6fde;--line:#e3e6eb;--surface:#fff}@media(prefers-color-scheme:dark){:root{color-scheme:dark;--bg:#1b1e24;--fg:#f1f2f5;--link:#9bbdff;--line:#343943;--surface:#242830}}*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;font:16px/1.5 "Segoe UI",system-ui,sans-serif;background:var(--bg);color:var(--fg);padding:24px}.board-offline{max-width:480px;padding:24px;border:1px solid var(--line);border-radius:12px;background:var(--surface);overflow-wrap:anywhere}h1{font-size:22px;margin:0 0 16px}a{color:var(--link)}</style><body><main class="board-offline"><h1>Board sem conexão</h1><p>Conecte-se à internet para consultar o estado atualizado da máquina. Nenhum retrato foi salvo neste dispositivo.</p><a href="/board">Tentar novamente</a></main></body></html>',
    { headers: { 'Content-Type': 'text/html; charset=utf-8' } },
  )));
});
