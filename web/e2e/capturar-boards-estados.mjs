// Prepara somente o ambiente local de UMA captura devcli (boards-estados, item 5); não agrega builds, testes ou capturas.
// Uso: node e2e/capturar-boards-estados.mjs <tela> <estado> <largura> <altura> <tema> [--aplicar]
import { spawn, execFileSync } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { once } from 'node:events';

const [tela, estado, largura, altura, tema] = process.argv.slice(2);
const aplicar = process.argv.includes('--aplicar');
const telas = ['boards-estados-site-light', 'boards-estados-site-dark', 'boards-estados-site-aprovar-light', 'boards-estados-site-aprovar-dark',
  'boards-estados-site-mobile-light', 'boards-estados-site-mobile-dark', 'boards-estados-board'];
if (!telas.includes(tela) || !['aguardando-disparo', 'aprovar', 'enviado', 'em-execucao', 'manual', 'leitor', 'board'].includes(estado)
  || !['390', '1440'].includes(largura) || !/^\d{3,4}$/.test(altura ?? '') || !['light', 'dark'].includes(tema))
  throw new Error('Informe a tela, o estado da demonstração, a largura 390 ou 1440, a altura e o tema light ou dark.');
process.env.DEVKIT_DEMO_BOARDS_PORTA = '4187';
const { servidor } = await import('./servidor-boards-estados.mjs');
if (!servidor.listening) await once(servidor, 'listening');
const clone = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const sha = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: clone, encoding: 'utf8', windowsHide: true }).trim();
try {
  const argumentos = ['design', '--projeto', 'github-com-paulo-rogerio-oliveira-dev-kit-page-1ac8edc17aa0853b',
    '--capturar', '--tela', tela, '--estado', `${estado}-${largura}`,
    // Em 390 a página vai numa moldura do tamanho exato: o capturador headless impõe largura mínima à janela.
    '--url', largura === '390' ? `http://127.0.0.1:4187/__moldura?estado=${estado}&largura=${largura}&altura=${altura}`
      : `http://127.0.0.1:4187/__boards?estado=${estado}`, '--largura', largura, '--altura', altura,
    '--tema', tema, '--branch', 'feature/dev/projeto-boards-estados-item-5', '--commit', sha,
    '--descricao', `Recaptura real do SHA ${sha} (boards-estados, item 5); estado ${estado}, ${largura} px, tema ${tema}; dist real e dados isolados em memória, sem credencial nem escrita externa${largura === '390' ? '; mobile em iframe real de 390 px, sem escala' : ''}${tema === 'dark' ? '; o site só tem o tema claro (color-scheme: light), e a preferência escura do sistema não muda as cores' : ''}.`,
    '--json'];
  if (aplicar) argumentos.push('--aplicar');
  const filho = spawn('C:/dk/devcli.exe', argumentos, { cwd: resolve(clone, '..'), stdio: 'inherit', windowsHide: true });
  const codigo = await new Promise((pronto, falhou) => { filho.once('error', falhou); filho.once('exit', pronto); });
  process.exitCode = codigo ?? 1;
} finally { servidor.closeAllConnections(); await new Promise(pronto => servidor.close(pronto)); }
