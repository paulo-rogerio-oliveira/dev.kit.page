// Sondagem pública antes do cenário de navegador: sem login, dados simulados ou alteração de CORS.
import { createHash, randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const api = 'https://devkit-f0dydyh8bwgudaey.centralus-01.azurewebsites.net';
let origem = 'http://localhost:5173'; // server.port do vite.config.ts deste repositório.
const execucao = randomUUID();
const modo = process.argv[2];
const origemPublicada = process.argv[3] === '--origem-publicada';
let etapa = 'preflight-local';
const emitir = (evento, dados) => console.log(JSON.stringify({ execucao, utc: new Date().toISOString(), evento, dados }));
const exigir = (condicao, codigo) => { if (!condicao) throw new Pendencia(codigo); };
class Pendencia extends Error {}

async function consultar(cenario, caminho, opcoes = {}) {
  const inicio = new Date().toISOString();
  const resposta = await fetch(api + caminho, { ...opcoes, redirect: 'manual', signal: AbortSignal.timeout(10_000) });
  const correlacao = resposta.headers.get('x-request-id');
  emitir('http', { cenario, metodo: opcoes.method ?? 'GET', caminho, inicio, http: resposta.status,
    dataDoServidor: resposta.headers.get('date'), correlacao: /^[a-f0-9-]{8,100}$/i.test(correlacao ?? '') ? correlacao : null });
  await resposta.body?.cancel();
  return resposta;
}
async function conferirCors(cenario, caminho, metodo, cabecalho) {
  const resposta = await consultar(cenario, caminho, { method: 'OPTIONS', headers: {
    Origin: origem, 'Access-Control-Request-Method': metodo, 'Access-Control-Request-Headers': cabecalho,
  } });
  const permitida = resposta.headers.get('access-control-allow-origin');
  const metodos = (resposta.headers.get('access-control-allow-methods') ?? '').split(',').map(v => v.trim());
  const cabecalhos = (resposta.headers.get('access-control-allow-headers') ?? '').split(',').map(v => v.trim().toLowerCase());
  const origemPermitida = permitida === origem || permitida === '*';
  // Fetch, CORS-preflight fetch: GET/HEAD/POST não exigem presença em Allow-Methods.
  // Os cabeçalhos não seguros continuam sujeitos à permissão; Authorization não aceita wildcard.
  // https://fetch.spec.whatwg.org/#cors-preflight-fetch
  const metodoSafelisted = ['GET', 'HEAD', 'POST'].includes(metodo);
  const metodoPermitido = metodoSafelisted || metodos.includes(metodo) || metodos.includes('*');
  const cabecalhoPermitido = cabecalhos.includes(cabecalho) || cabecalho !== 'authorization' && cabecalhos.includes('*');
  emitir('cors', { cenario, origemSolicitada: origem, origemPermitida, metodoPermitido, cabecalhoPermitido,
    metodoSolicitado: metodo, metodoSafelisted, metodosAnunciados: metodos.filter(v => /^[A-Za-z*]+$/.test(v)),
    http: resposta.status, autenticacaoExercitada: false });
  exigir(resposta.ok && origemPermitida && metodoPermitido && cabecalhoPermitido, 'cors-nao-confirma-origem-consultada');
}
try {
  exigir((process.argv.length === 3 || process.argv.length === 4 && origemPublicada)
    && ['--simular', '--consultar'].includes(modo), 'modo-invalido');
  const repositorio = fileURLToPath(new URL('../../..', import.meta.url));
  const sha = execFileSync('git', ['rev-parse', 'HEAD'], {
    cwd: repositorio, encoding: 'utf8', windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'],
  }).trim();
  if (origemPublicada) {
    const workflow = readFileSync(join(repositorio, '.github/workflows/azure-static-web-apps-black-forest-00e9a2410.yml'), 'utf8');
    const configurada = workflow.match(/^\s+VITE_SITE_URL:\s*(https:\/\/[^\s#]+)\s*$/m);
    exigir(configurada, 'url-publica-ausente-no-workflow');
    const url = new URL(configurada[1]);
    exigir(url.protocol === 'https:' && !url.username && !url.password && url.hostname.endsWith('.azurestaticapps.net'), 'url-publica-do-workflow-invalida');
    origem = url.origin;
  }
  const hashDoRoteiro = createHash('sha256').update(readFileSync(fileURLToPath(import.meta.url))).digest('hex');
  emitir('plano', { modo, api, origemConsultada: origem,
    fonteDaOrigem: origemPublicada ? 'workflow VITE_SITE_URL existente' : 'vite.config.ts server.port',
    shaDoRepositorio: sha, hashDoRoteiro,
    gravacoesRemotas: 0, contasCriadas: 0, credenciaisLidas: false, shaDaApiPublicada: 'nao-confirmado',
    swa: 'manual-pelo-desenvolvedor', objetivo: 'preparacao-independente-do-cliente-web' });
  if (modo === '--simular') emitir('simulacao', { requisicoesHttp: 0, gravacoes: 0 });
  else {
    etapa = 'consulta-http-publica';
    exigir((await consultar('saude-publica', '/health')).status === 200, 'api-sem-saude-200');
    await conferirCors('preflight-login', '/api/auth/login', 'POST', 'content-type');
    await conferirCors('preflight-artefatos', '/api/projetos/1/artefatos', 'GET', 'authorization');
    emitir('resultado', { preflight: 'conferido', navegadorExercitado: false, loginRealizado: false, aceiteE2E: 'pendente' });
  }
} catch (erro) {
  emitir('pendencia', { codigo: erro instanceof Pendencia ? erro.message : erro.name, etapa,
    campo: etapa === 'preflight-local' ? 'identidade local do clone/roteiro' : 'saude e permissao CORS da origem consultada',
    fonte: etapa === 'preflight-local' ? 'metadados locais' : 'respostas HTTP desta sondagem',
    gravacoesRemotas: 0, aceiteE2E: 'pendente' });
  process.exitCode = 1;
}
