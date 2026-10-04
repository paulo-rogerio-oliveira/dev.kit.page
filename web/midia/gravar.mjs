// Gera as mídias da landing (public/midia: NOME.mp4, NOME.webm e o poster NOME.jpg) e a imagem de
// compartilhamento (public/compartilhar.jpg) a partir do roteiro.mjs.
//
//   npm run midia                     (todas)        npm run midia -- agente fluxos   (só estas)
//
// Requer o Chromium do Playwright (npx playwright install chromium) e um ffmpeg com libx264 e
// libvpx-vp9 no PATH ou na variável FFMPEG. Cada cena é HTML com animações CSS; o gravador PARA as
// animações e fotografa quadro a quadro, avançando o relógio delas (24 fps) — o resultado não
// depende da velocidade da máquina. Ver docs/landing-conteudo.md.
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';
import { MIDIAS } from './roteiro.mjs';

const WEB = join(dirname(fileURLToPath(import.meta.url)), '..');
const CAPTURAS = join(WEB, 'public', 'capturas');
const SAIDA = join(WEB, 'public', 'midia');
const FFMPEG = process.env.FFMPEG || 'ffmpeg';
const FPS = 24;
const LARGURA = 1280;
const ALTURA = 720;

/** O orçamento do repositório (docs/arquitetura.md): por arquivo e no total da pasta. */
export const ORCAMENTO = { porArquivo: 1.5 * 1024 * 1024, total: 25 * 1024 * 1024 };

const imagens = new Map();
function dataUri(arquivo) {
  if (!imagens.has(arquivo)) imagens.set(arquivo, `data:image/png;base64,${readFileSync(join(CAPTURAS, arquivo)).toString('base64')}`);
  return imagens.get(arquivo);
}

const escapar = (texto) => texto.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

/** Um @keyframes com os pontos em SEGUNDOS da mídia inteira (uma linha do tempo só para tudo). */
function keyframes(nome, pontos, total) {
  const corpo = pontos
    .map(([t, css]) => `${Math.min(100, Math.max(0, (t / total) * 100)).toFixed(4)}% { ${css} }`)
    .join('\n');
  return `@keyframes ${nome} {\n${corpo}\n}\n.${nome} { animation: ${nome} ${total}s linear both; }\n`;
}

/** Aparece em [de, ate) — antes e depois, invisível. */
function janela(nome, de, ate, total, fade = 0.35) {
  const pontos = [[0, 'opacity: 0'], [Math.max(0, de - 0.001), 'opacity: 0'], [de, 'opacity: 0'], [de + fade, 'opacity: 1']];
  if (de === 0) pontos.splice(0, 4, [0, 'opacity: 1']);
  if (ate < total) pontos.push([ate - fade, 'opacity: 1'], [ate, 'opacity: 0'], [total, 'opacity: 0']);
  else pontos.push([total, 'opacity: 1']);
  return keyframes(nome, pontos, total);
}

/** O enquadramento (escala e deslocamento) que põe o centro do destaque no meio do vídeo. */
function enquadrar(tela, destaque, zoom) {
  const base = ALTURA / tela.altura;
  const escala = base * zoom;
  const [x, y, w, h] = destaque;
  const largura = tela.largura * escala;
  const altura = tela.altura * escala;
  let tx = largura <= LARGURA ? (LARGURA - largura) / 2 : LARGURA / 2 - escala * (x + w / 2);
  let ty = ALTURA / 2 - escala * (y + h / 2);
  if (largura > LARGURA) tx = Math.min(0, Math.max(LARGURA - largura, tx));
  ty = Math.min(0, Math.max(ALTURA - altura, ty));
  return `transform: translate(${tx.toFixed(1)}px, ${ty.toFixed(1)}px) scale(${escala.toFixed(4)})`;
}

function slideDeTela(slide, id, inicio, total) {
  const { tela, passos } = slide;
  let css = '';
  let html = '';
  const fim = inicio + slide.duracao;

  // Câmera: começa no primeiro passo (o poster já mostra o destaque) e viaja entre os passos.
  const camera = [[0, enquadrar(tela, passos[0].destaque, passos[0].zoom)]];
  const cursor = [];
  passos.forEach((p, i) => {
    const t = inicio + p.em;
    const alvo = enquadrar(tela, p.destaque, p.zoom);
    if (i > 0) camera.push([t, camera.at(-1)[1]], [t + 0.9, alvo]);
    const [x, y, w, h] = p.destaque;
    const ponta = `left: ${x + w * 0.7}px; top: ${y + h * 0.6}px`;
    if (i === 0) cursor.push([0, `left: ${x + w * 0.7 + 60}px; top: ${y + h * 0.6 + 50}px`], [inicio + 1, ponta]);
    else cursor.push([t, cursor.at(-1)[1]], [t + 1, ponta]);
    cursor.push([t + 1.1, `${ponta}; transform: scale(1)`], [t + 1.25, `${ponta}; transform: scale(0.82)`], [t + 1.4, `${ponta}; transform: scale(1)`]);

    const proximo = i + 1 < passos.length ? inicio + passos[i + 1].em : fim;
    const inicioDoDestaque = i === 0 && inicio === 0 ? 0 : t + 0.9;
    css += janela(`${id}-d${i}`, inicioDoDestaque, proximo, total, 0.25);
    css += janela(`${id}-l${i}`, i === 0 ? inicio : t, proximo, total, i === 0 && inicio === 0 ? 0 : 0.3);
    html += `<div class="destaque ${id}-d${i}" style="left:${x - 6}px;top:${y - 6}px;width:${w + 12}px;height:${h + 12}px"></div>`;
  });
  camera.push([total, camera.at(-1)[1]]);
  cursor.push([total, cursor.at(-1)[1]]);
  css += keyframes(`${id}-camera`, camera.map(([t, v]) => [t, `${v}; animation-timing-function: ease-in-out`]), total);
  css += keyframes(`${id}-cursor`, cursor, total);

  const legendas = passos.map((p, i) => `<div class="legenda ${id}-l${i}">${escapar(p.legenda)}</div>`).join('');
  return {
    css,
    html: `<div class="palco ${id}-camera"><div class="recorte" style="width:${tela.largura}px;height:${tela.altura}px">`
      + `<img src="${dataUri(tela.arquivo)}" alt="">${html}<svg class="cursor ${id}-cursor" viewBox="0 0 24 24"><path d="M3 2l7 19 2.5-7.5L20 11z"/></svg>`
      + `</div></div>${legendas}`,
  };
}

function slideDeTerminal(slide, id, inicio, total) {
  let css = janela(`${id}-legenda`, inicio, total, total, inicio === 0 ? 0 : 0.3);
  const linhas = slide.linhas.map((linha, i) => {
    const t = inicio + linha.em;
    css += janela(`${id}-t${i}`, t, total, total, 0.01);
    if (!linha.cmd) return `<div class="linha ${linha.classe ?? ''} ${id}-t${i}">${escapar(linha.out)}</div>`;
    const n = linha.cmd.length;
    const duracao = Math.min(1.4, n * 0.025);
    css += keyframes(`${id}-c${i}`, [[0, 'width: 0ch'], [t, `width: 0ch; animation-timing-function: steps(${n}, end)`], [t + duracao, `width: ${n}ch`], [total, `width: ${n}ch`]], total);
    return `<div class="linha ${id}-t${i}"><span class="prompt">PS C:\\ProjetosGIT\\t_1234&gt;</span> <span class="digitar ${id}-c${i}">${escapar(linha.cmd)}</span></div>`;
  });
  return {
    css,
    html: `<div class="terminal"><div class="barra"><i></i><i></i><i></i><span>${escapar(slide.titulo)}</span></div>`
      + `<div class="saida">${linhas.join('')}</div></div><div class="legenda ${id}-legenda">${escapar(slide.legenda)}</div>`,
  };
}

const ESTILO = `
* { box-sizing: border-box; }
html, body { margin: 0; width: ${LARGURA}px; height: ${ALTURA}px; overflow: hidden; background: #0d0d0d; font-family: 'Segoe UI', system-ui, sans-serif; }
.slide { position: absolute; inset: 0; overflow: hidden; background: radial-gradient(circle at 30% 20%, #1f1f1d, #0d0d0d 70%); }
.palco { position: absolute; left: 0; top: 0; transform-origin: 0 0; }
.recorte { position: relative; overflow: hidden; border-radius: 6px; }
.recorte img { position: absolute; left: 0; top: 0; }
.destaque { position: absolute; border: 3px solid #ff5347; border-radius: 10px; box-shadow: 0 0 0 4px rgba(255, 83, 71, 0.25), 0 0 24px rgba(255, 83, 71, 0.45); }
.cursor { position: absolute; width: 26px; height: 26px; fill: #ffffff; stroke: #0b0b0b; stroke-width: 1.4; filter: drop-shadow(0 2px 3px rgba(0,0,0,.6)); transform-origin: 0 0; }
.legenda { position: absolute; left: 50%; bottom: 28px; transform: translateX(-50%); max-width: 1120px; padding: 14px 26px; border-radius: 12px;
  background: rgba(13, 13, 13, 0.86); border: 1px solid rgba(255, 255, 255, 0.14); color: #ffffff; font-size: 27px; font-weight: 600; text-align: center; white-space: nowrap; }
.terminal { position: absolute; left: 60px; top: 36px; width: 1160px; height: 560px; border-radius: 12px; background: #0c0c0c; border: 1px solid #2c2c2a; box-shadow: 0 20px 60px rgba(0,0,0,.5); overflow: hidden; }
.barra { height: 40px; display: flex; align-items: center; gap: 8px; padding: 0 16px; background: #1a1a19; color: #c3c2b7; font-size: 16px; }
.barra i { width: 12px; height: 12px; border-radius: 50%; background: #383835; }
.barra i:first-child { background: #e8392b; }
.barra span { margin-left: 10px; }
.saida { padding: 18px 22px; font-family: 'Cascadia Mono', Consolas, monospace; font-size: 19px; line-height: 1.55; color: #e6e6e3; white-space: pre; }
.prompt { color: #3987e5; }
.digitar { display: inline-block; overflow: hidden; vertical-align: bottom; white-space: pre; }
.mudo { color: #898781; } .ok { color: #4cc38a; } .aviso { color: #ffb547; }
`;

/** O HTML da mídia inteira: os slides empilhados, cada um visível na sua janela de tempo. */
export function html(midia) {
  const total = midia.slides.reduce((s, slide) => s + slide.duracao, 0);
  let css = ESTILO;
  let corpo = '';
  let inicio = 0;
  midia.slides.forEach((slide, i) => {
    const id = `s${i}`;
    const parte = slide.tipo === 'tela' ? slideDeTela(slide, id, inicio, total) : slideDeTerminal(slide, id, inicio, total);
    css += janela(`${id}-slide`, inicio, i === midia.slides.length - 1 ? total : inicio + slide.duracao + 0.4, total, inicio === 0 ? 0 : 0.4) + parte.css;
    corpo += `<section class="slide ${id}-slide">${parte.html}</section>`;
    inicio += slide.duracao;
  });
  return { total, documento: `<!doctype html><html><head><meta charset="utf-8"><style>${css}</style></head><body>${corpo}</body></html>` };
}

function ffmpeg(...args) {
  const r = spawnSync(FFMPEG, ['-hide_banner', '-loglevel', 'error', '-y', ...args], { stdio: 'inherit' });
  if (r.status !== 0) throw new Error(`ffmpeg falhou (${r.status ?? r.error}): ${args.join(' ')}`);
}

async function gravar(pagina, midia) {
  const { total, documento } = html(midia);
  await pagina.setContent(documento, { waitUntil: 'load' });
  await pagina.evaluate(() => document.fonts.ready);
  await pagina.evaluate(() => document.getAnimations().forEach((a) => a.pause()));

  const quadros = mkdtempSync(join(tmpdir(), `midia-${midia.nome}-`));
  try {
    const n = Math.round(total * FPS);
    for (let i = 0; i < n; i++) {
      await pagina.evaluate((ms) => document.getAnimations().forEach((a) => { a.currentTime = ms; }), (i * 1000) / FPS);
      await pagina.screenshot({ path: join(quadros, `${String(i).padStart(4, '0')}.png`) });
    }
    const entrada = ['-framerate', String(FPS), '-i', join(quadros, '%04d.png')];
    const destino = (ext) => join(SAIDA, `${midia.nome}.${ext}`);
    ffmpeg(...entrada, '-c:v', 'libx264', '-preset', 'slow', '-crf', '27', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', '-an', destino('mp4'));
    ffmpeg(...entrada, '-c:v', 'libvpx-vp9', '-b:v', '0', '-crf', '40', '-row-mt', '1', '-deadline', 'good', '-cpu-used', '2', '-pix_fmt', 'yuv420p', '-an', destino('webm'));
    ffmpeg('-i', join(quadros, '0000.png'), '-q:v', '4', destino('jpg'));
    console.log(`${midia.nome}: ${n} quadros (${total}s) → mp4, webm, jpg`);
  } finally {
    rmSync(quadros, { recursive: true, force: true });
  }
}

/** A imagem de compartilhamento (Open Graph / Twitter card): 1200×630. */
async function compartilhar(navegador) {
  const pagina = await navegador.newPage({ viewport: { width: 1200, height: 630 } });
  await pagina.setContent(`<!doctype html><html><head><meta charset="utf-8"><style>
    body { margin: 0; width: 1200px; height: 630px; overflow: hidden; font-family: 'Segoe UI', system-ui, sans-serif; color: #fff;
      background: radial-gradient(circle at 20% 20%, #3a1b18, #0d0d0d 65%); }
    .texto { position: absolute; left: 64px; top: 72px; width: 560px; }
    .marca { font-size: 44px; font-weight: 700; } .marca b { color: #e8392b; }
    h1 { font-size: 52px; line-height: 1.12; margin: 28px 0 20px; }
    p { font-size: 25px; color: #c3c2b7; line-height: 1.45; margin: 0; }
    img { position: absolute; left: 650px; top: 90px; width: 640px; border-radius: 12px; border: 1px solid #2c2c2a; box-shadow: 0 24px 60px rgba(0,0,0,.6); }
  </style></head><body><div class="texto"><div class="marca">dev<b>.</b>kit</div>
    <h1>Do work item à Pull Request, com um agente de IA</h1>
    <p>Avaliadores com nota mínima, horas lançadas sozinhas e o seu código na sua máquina.</p></div>
    <img src="${dataUri('tela-agente.png')}" alt=""></body></html>`, { waitUntil: 'load' });
  await pagina.screenshot({ path: join(WEB, 'public', 'compartilhar.jpg'), type: 'jpeg', quality: 82 });
  await pagina.close();
  console.log('compartilhar.jpg: 1200×630');
}

function conferirOrcamento() {
  let total = 0;
  const acima = [];
  for (const midia of MIDIAS) {
    for (const ext of ['mp4', 'webm', 'jpg']) {
      const arquivo = join(SAIDA, `${midia.nome}.${ext}`);
      if (!existsSync(arquivo)) {
        acima.push(`${midia.nome}.${ext} (não gerado)`);
        continue;
      }
      const tamanho = statSync(arquivo).size;
      total += tamanho;
      console.log(`  ${midia.nome}.${ext}`.padEnd(22), `${(tamanho / 1024).toFixed(0)} KB`);
      if (tamanho > ORCAMENTO.porArquivo) acima.push(`${midia.nome}.${ext}`);
    }
  }
  console.log(`Total: ${(total / 1024 / 1024).toFixed(2)} MB (orçamento: 1,5 MB por arquivo, 25 MB no total)`);
  if (acima.length > 0 || total > ORCAMENTO.total) {
    throw new Error(`Fora do orçamento ou faltando: ${acima.join(', ') || 'total'} — reduza a duração ou suba o -crf (ver docs/arquitetura.md).`);
  }
}

const pedidas = process.argv.slice(2);
mkdirSync(SAIDA, { recursive: true });
const navegador = await chromium.launch();
try {
  const pagina = await navegador.newPage({ viewport: { width: LARGURA, height: ALTURA } });
  for (const midia of MIDIAS.filter((m) => pedidas.length === 0 || pedidas.includes(m.nome))) await gravar(pagina, midia);
  if (pedidas.length === 0) await compartilhar(navegador);
} finally {
  await navegador.close();
}
conferirOrcamento();
