// Evidência complementar ao devcli design (boards-estados, item 5): o viewport real no navegador, sem editar nem cortar
// a imagem. O Edge headless do capturador pode impor largura mínima à janela em --window-size=390; aqui o Playwright
// abre a página em 390 de verdade, confere que nada passa da largura e grava a imagem na entrega da task.
// (r3) O estado "landing" grava a seção do Board web da landing inteira, com a frase de aprovar e disparar revista.
// Uso: node e2e/conferir-viewport-boards-estados.mjs <estado> <largura> <altura> [light|dark]
import { chromium } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { mkdir } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { once } from 'node:events';

const [estado = 'aguardando-disparo', largura = '390', altura = '980', tema = 'light'] = process.argv.slice(2);
if (!['aguardando-disparo', 'aprovar', 'enviado', 'em-execucao', 'manual', 'leitor', 'board', 'landing'].includes(estado)
  || !['390', '1440'].includes(largura) || !['light', 'dark'].includes(tema))
  throw new Error('Estado, viewport ou tema inválido.');
process.env.DEVKIT_DEMO_BOARDS_PORTA = '4188';
const { servidor } = await import('./servidor-boards-estados.mjs');
if (!servidor.listening) await once(servidor, 'listening');
const clone = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const sha = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: clone, encoding: 'utf8', windowsHide: true }).trim();
const navegador = await chromium.launch({ channel: 'msedge', headless: true });
try {
  const pagina = await navegador.newPage({ viewport: { width: Number(largura), height: Number(altura) }, colorScheme: tema });
  // A landing é pública: abre sem sessão. As outras telas passam pela entrada da demonstração, que põe a sessão de teste.
  await pagina.goto(estado === 'landing' ? 'http://127.0.0.1:4188/' : `http://127.0.0.1:4188/__boards?estado=${estado}`);
  const secao = estado === 'landing' ? pagina.getByRole('region', { name: 'Projetos, decisões e conversa no navegador' }) : null;
  if (secao) await secao.getByRole('list', { name: 'O que é preciso' }).getByText(/viram pedido à máquina/).waitFor();
  else if (estado === 'board') await pagina.getByRole('link', { name: 'Abrir Projetos em qualquer nível do Boards' }).waitFor();
  else await pagina.getByRole('article', { name: 'Detalhe do projeto' }).waitFor();
  if (estado === 'aprovar') await pagina.getByRole('group', { name: /^Aprovar plano · / }).waitFor();
  if (estado === 'enviado') await pagina.getByText(/^Pedido .*: na-fila\./).waitFor();
  await pagina.waitForFunction(() => !document.body.textContent.includes('Conferindo matriz de impacto'));
  const medidas = await pagina.evaluate(() => ({ viewport: innerWidth, documento: document.documentElement.scrollWidth,
    esquema: getComputedStyle(document.documentElement).colorScheme,
    excedentes: [...document.querySelectorAll('body *')].filter(e => e.getBoundingClientRect().right > innerWidth + 0.5 && e.getBoundingClientRect().width > 0)
      .slice(0, 8).map(e => ({ tag: e.tagName, classe: String(e.className), direita: Math.round(e.getBoundingClientRect().right) })) }));
  const pasta = resolve(clone, '../entrega/viewport-web');
  await mkdir(pasta, { recursive: true });
  const arquivo = resolve(pasta, `boards-estados-${estado}-${largura}-${tema}-${sha.slice(0, 7)}.png`);
  // A seção da landing sai da página inteira, recortada na caixa dela: assim o cabeçalho fixo fica no topo do documento e
  // não cobre o começo da seção, como cobriria na captura do elemento rolado para o topo da janela.
  if (secao) await pagina.screenshot({ path: arquivo, fullPage: true, clip: await secao.evaluate(e => {
    const r = e.getBoundingClientRect(); return { x: r.left + scrollX, y: r.top + scrollY, width: r.width, height: r.height }; }) });
  else await pagina.screenshot({ path: arquivo });
  process.stdout.write(JSON.stringify({ sha, estado, largura, altura, tema, medidas, arquivo,
    origem: estado === 'landing' ? 'site real; seção do Board web da landing; viewport Playwright' : 'site real; dados isolados; viewport Playwright; complementar ao devcli design' }) + '\n');
  if (medidas.viewport !== Number(largura) || medidas.documento > Number(largura)) process.exitCode = 1;
} finally { await navegador.close(); servidor.closeAllConnections(); await new Promise(pronto => servidor.close(pronto)); }
