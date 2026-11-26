// Evidência complementar ao devcli design: viewport real no navegador, sem editar/cortar a imagem.
// O Edge headless usado pelo capturador pode impor largura mínima à janela em --window-size=390.
import { chromium } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { mkdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { once } from 'node:events';
const estado = process.argv[2] ?? 'gerado';
const largura = Number(process.argv[3] ?? 390);
const altura = Number(process.argv[4] ?? 1800);
if (!['gerado', 'gerando', 'falhou', 'local', 'vencido', 'leitor'].includes(estado) || ![390, 1440].includes(largura)) throw new Error('Estado ou viewport inválido.');
process.env.DEVKIT_DEMO_PACOTE_PORTA = '4186';
const { servidor } = await import('./servidor-pacote.mjs');
if (!servidor.listening) await once(servidor, 'listening');
const clone = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const sha = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: clone, encoding: 'utf8', windowsHide: true }).trim();
const navegador = await chromium.launch({ channel: 'msedge', headless: true });
try {
  const pagina = await navegador.newPage({ viewport: { width: largura, height: altura } });
  await pagina.goto(`http://127.0.0.1:4186/__pacote?estado=${estado}`);
  await pagina.locator('.pacote-resultado').first().waitFor();
  await pagina.waitForFunction(() => !document.body.textContent.includes('Aguardando o dev.kit terminar a criação anterior.'));
  const medidas = await pagina.evaluate(() => ({ viewport: innerWidth, documento: document.documentElement.scrollWidth }));
  if (medidas.viewport !== largura || medidas.documento > largura) throw new Error(JSON.stringify(medidas));
  const pasta = resolve(clone, '../entrega/viewport-web');
  await mkdir(pasta, { recursive: true });
  const arquivo = resolve(pasta, `pacote-${estado}-${largura}-${sha}.png`);
  await pagina.screenshot({ path: arquivo });
  process.stdout.write(JSON.stringify({ sha, estado, largura, altura, medidas, arquivo, origem: 'Board real; dados isolados; viewport Playwright; complementar ao devcli design' }) + '\n');
} finally { await navegador.close(); servidor.closeAllConnections(); await new Promise(pronto => servidor.close(pronto)); }
