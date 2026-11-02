// O Board depende de internet. Não guarda páginas autenticadas, respostas da API nem credenciais.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (event.request.mode !== 'navigate' || url.origin !== self.location.origin || url.pathname !== '/board') return;
  event.respondWith(fetch(event.request).catch(() => new Response(
    '<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Board sem conexão</title><body><main><h1>O Board precisa de internet</h1><p>Conecte o dispositivo para consultar o estado atualizado da sua máquina. Nenhum retrato fica salvo offline.</p><a href="/board">Tentar novamente</a></main></body></html>',
    { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' } },
  )));
});
