// Nenhuma resposta autenticada, retrato ou sessão é armazenada no cache.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', event => {
  if (event.request.mode !== 'navigate') return;
  const url = new URL(event.request.url);
  if (url.origin !== self.location.origin || url.pathname !== '/board') return;
  event.respondWith(fetch(event.request).catch(() => new Response(
    '<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>dev.kit — sem conexão</title><body style="font:16px system-ui;background:#242830;color:#f1f2f5;padding:24px"><h1>Board sem conexão</h1><p>Conecte-se à internet para consultar o estado atualizado da máquina. Nenhum retrato foi salvo neste dispositivo.</p><a href="/board" style="color:#9bbdff">Tentar novamente</a></body></html>',
    { headers: { 'Content-Type': 'text/html; charset=utf-8' } },
  )));
});
