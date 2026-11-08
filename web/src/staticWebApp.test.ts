import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

/** A configuração do Static Web App como ela vai no build (web/public é copiada para a raiz do dist). */
type Rota = { route: string; headers?: Record<string, string> };
const config = JSON.parse(readFileSync(resolve(dirname(fileURLToPath(import.meta.url)), '../public/staticwebapp.config.json'), 'utf8')) as {
  navigationFallback: { rewrite: string; exclude: string[] };
  routes: Rota[];
  globalHeaders?: Record<string, string>;
};
const cabecalho = (rota: string) => config.routes.find(r => r.route === rota)?.headers?.['Cache-Control'];

it('a página e as rotas do React revalidam a cada visita: a publicação nova chega sem o navegador segurar o index antigo', () => {
  expect(config.globalHeaders?.['Cache-Control']).toBe('no-cache');
  expect(config.navigationFallback.rewrite).toBe('/index.html');
});

it('os assets com hash no nome ficam no cache para sempre, e o service worker do board sempre revalida', () => {
  expect(cabecalho('/assets/*')).toBe('public, max-age=31536000, immutable');
  expect(config.navigationFallback.exclude).toContain('/assets/*');
  expect(cabecalho('/board-sw.js')).toBe('no-cache');
});
