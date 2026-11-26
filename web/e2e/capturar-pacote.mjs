// Prepara somente o ambiente local de UMA captura devcli; não agrega builds, testes ou capturas.
import { spawn, execFileSync } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { once } from 'node:events';

const estado = process.argv[2] ?? 'gerado';
const largura = Number(process.argv[3] ?? 1440);
const altura = Number(process.argv[4] ?? 1100);
const aplicar = process.argv.includes('--aplicar');
if (!['gerado', 'gerando', 'falhou', 'local', 'vencido', 'leitor', 'anteriores'].includes(estado) || ![390, 1440].includes(largura))
  throw new Error('Informe um estado da demonstração e a largura 390 ou 1440.');
process.env.DEVKIT_DEMO_PACOTE_PORTA = '4186';
const { servidor } = await import('./servidor-pacote.mjs');
if (!servidor.listening) await once(servidor, 'listening');
const clone = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const sha = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: clone, encoding: 'utf8', windowsHide: true }).trim();
try {
  const argumentos = ['design', '--projeto', 'github-com-paulo-rogerio-oliveira-dev-kit-page-1ac8edc17aa0853b',
    '--capturar', '--tela', 'artefato-geracao-board', '--estado', `pacote-${estado}-${largura}`,
    '--url', `http://127.0.0.1:4186/__pacote?estado=${estado}`, '--largura', String(largura), '--altura', String(altura),
    '--tema', 'light', '--branch', 'feature/dev/projeto-fixes-item-6', '--commit', sha,
    '--descricao', `Board real do SHA ${sha}; estado ${estado}, largura ${largura}; dados isolados, referência Board v20.`, '--json'];
  if (aplicar) argumentos.push('--aplicar');
  const filho = spawn('C:/dk/devcli.exe', argumentos, { cwd: resolve(clone, '..'), stdio: 'inherit', windowsHide: true });
  const codigo = await new Promise((pronto, falhou) => { filho.once('error', falhou); filho.once('exit', pronto); });
  process.exitCode = codigo ?? 1;
} finally { servidor.closeAllConnections(); await new Promise(pronto => servidor.close(pronto)); }
