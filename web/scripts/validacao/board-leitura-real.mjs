// Conta existente e páginas reais; sem seed, interceptação de respostas ou exportação de sessão.
// Executar --consultar somente com devcli segredos --executar usuario_test,senha_test -- node <arquivo>.
import { chromium } from '@playwright/test';
import { createHash, randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const api = 'https://devkit-f0dydyh8bwgudaey.centralus-01.azurewebsites.net';
const slug = 'teste-artefato-item6-t63-20261120';
const execucao = randomUUID();
const emitir = (evento, dados) => console.log(JSON.stringify({ execucao, utc: new Date().toISOString(), evento, dados }));
class Pendencia extends Error {}
const exigir = (condicao, codigo) => { if (!condicao) throw new Pendencia(codigo); };
let etapa = 'preparacao';
let navegador;
let contexto;
let bloqueadas = 0;
let violacao = false;

try {
  const modo = process.argv[2];
  exigir(process.argv.length === 3 && ['--simular', '--consultar'].includes(modo), 'modo-invalido');
  const repositorio = fileURLToPath(new URL('../../..', import.meta.url));
  const workflow = readFileSync(join(repositorio, '.github/workflows/azure-static-web-apps-black-forest-00e9a2410.yml'), 'utf8');
  const site = new URL(workflow.match(/VITE_SITE_URL:\s*(https:\/\/[^\s'"#]+)/)?.[1]);
  exigir(site.protocol === 'https:' && site.hostname.endsWith('.azurestaticapps.net') && !site.username && !site.password,
    'origem-do-workflow-invalida');
  const edge = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
  exigir(existsSync(edge), 'edge-existente-nao-encontrado');
  const sha = execFileSync('git', ['-C', repositorio, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
  const hash = createHash('sha256').update(readFileSync(fileURLToPath(import.meta.url))).digest('hex');
  emitir('plano', { modo, site: site.origin, api, shaDoRepositorio: sha, hashDoRoteiro: hash,
    fonteDaOrigem: 'workflow VITE_SITE_URL existente', shaDoSitePublicado: 'nao-confirmado', shaDaApiPublicada: 'nao-confirmado',
    autorizacao: 'mensagens-516-e-530', ator: 'conta-de-teste', perfil: 'efemero-isolado',
    capturas: false, traces: false, sessaoExportada: false, gravacoesDeDadosPermitidas: 0,
    swa: 'validacao-manual-pelo-desenvolvedor-pendente' });
  if (modo === '--simular') {
    emitir('simulacao', { navegadorAberto: false, requisicoesHttp: 0, credenciaisLidas: false, gravacoesRemotas: 0 });
  } else {
    exigir(!process.env.DEBUG && !process.env.PWDEBUG, 'depuracao-verbose-incompativel-com-secrets');
    const usuario = process.env.usuario_test;
    const senha = process.env.senha_test;
    exigir(usuario && senha, 'credenciais-nao-injetadas-pelo-devcli');
    // O processo do navegador não precisa herdar os secrets usados pelo controlador.
    const ambiente = Object.fromEntries(Object.entries(process.env).filter(([k]) =>
      !['usuario_test', 'senha_test', 'debug', 'pwdebug'].includes(k.toLowerCase())));
    navegador = await chromium.launch({ executablePath: edge, headless: true, env: ambiente });
    contexto = await navegador.newContext({ serviceWorkers: 'block', acceptDownloads: false, viewport: { width: 1365, height: 900 } });
    // Guarda de destino e escrita: nunca fabrica resposta nem libera CORS artificialmente.
    await contexto.route('**/*', async rota => {
      const pedido = rota.request();
      const url = new URL(pedido.url());
      const metodo = pedido.method();
      const destinoPermitido = [site.origin, api].includes(url.origin);
      const leitura = ['GET', 'HEAD', 'OPTIONS'].includes(metodo);
      const login = url.origin === api && url.pathname === '/api/auth/login' && metodo === 'POST';
      if (destinoPermitido && (leitura || login)) await rota.continue();
      else {
        bloqueadas++;
        // Sem URL, corpo ou headers: podem conter credenciais e identificadores privados.
        emitir('requisicao-bloqueada', { metodoPermitidoDeLeitura: leitura, destinoPermitido,
          motivo: leitura ? 'destino-fora-do-cenario' : 'gravacao-fora-do-login' });
        if (!leitura) violacao = true;
        await rota.abort('blockedbyclient');
      }
    });
    const pagina = await contexto.newPage();
    pagina.setDefaultTimeout(15_000);
    pagina.setDefaultNavigationTimeout(20_000);
    const caminhos = new Set(['/api/auth/login', '/api/conta', '/api/projetos', '/api/remoto/maquinas']);
    pagina.on('response', resposta => {
      const url = new URL(resposta.url());
      if (url.origin === api && caminhos.has(url.pathname)) {
        emitir('http', { metodo: resposta.request().method(), caminho: url.pathname, http: resposta.status(),
          dataDoServidor: resposta.headers()['date'] ?? null });
      }
    });
    const respostaDe = caminho => pagina.waitForResponse(r => {
      const url = new URL(r.url());
      return url.origin === api && url.pathname === caminho && r.request().method() !== 'OPTIONS';
    });
    etapa = 'login-pela-interface';
    await pagina.goto(site.origin + '/login', { waitUntil: 'domcontentloaded' });
    await pagina.getByLabel('Login', { exact: true }).fill(usuario);
    await pagina.getByLabel('Senha', { exact: true }).fill(senha);
    const [login] = await Promise.all([respostaDe('/api/auth/login'), pagina.getByRole('button', { name: 'Entrar', exact: true }).click()]);
    exigir(login.status() === 200, 'login-pela-interface-recusado');
    // Não ler o corpo do login, cookies, localStorage ou token. A aplicação mantém sua sessão no contexto efêmero.
    await pagina.waitForURL(url => url.pathname !== '/login');
    exigir(new URL(pagina.url()).pathname !== '/trocar-senha', 'troca-de-senha-nao-autorizada');
    etapa = 'cadastro-pela-interface';
    const [cadastro] = await Promise.all([respostaDe('/api/conta'), pagina.goto(site.origin + '/conta')]);
    exigir(cadastro.status() === 200, 'cadastro-nao-consultavel');
    const conta = await cadastro.json();
    exigir(String(conta.login ?? '').trim().toLowerCase() === usuario.trim().toLowerCase(), 'identidade-do-cadastro-divergente');
    await pagina.getByRole('heading', { name: 'Sua conta', exact: true }).waitFor();
    emitir('cadastro-na-interface', { identidadeConferida: true, papel: ['admin', 'gestor', 'dev'].includes(conta.papel) ? conta.papel : null,
      empresaVinculada: Boolean(conta.empresa), cabecalhoVisivel: true });
    etapa = 'projeto-pela-interface';
    const [projetos] = await Promise.all([respostaDe('/api/projetos'), pagina.goto(site.origin + '/projetos?slug=' + slug)]);
    exigir(projetos.status() === 200, 'projetos-nao-consultaveis');
    const lista = await projetos.json();
    exigir(Array.isArray(lista), 'contrato-da-listagem-de-projetos-invalido');
    const projeto = lista.find(p => p.id === 1 && p.slug === slug);
    exigir(projeto, 'projeto-de-teste-nao-acessivel');
    await pagina.getByText('TESTE ISOLADO artefato item 6 t63', { exact: true }).first().waitFor();
    emitir('projeto-na-interface', { projeto: 1, versao: projeto.versao, boardDesligado: projeto.board === 'nao',
      nomeVisivel: true, repositorios: Array.isArray(projeto.repositorios) ? projeto.repositorios.length : null });
    etapa = 'board-pela-interface';
    const [maquinas] = await Promise.all([respostaDe('/api/remoto/maquinas'), pagina.goto(site.origin + '/board')]);
    exigir(maquinas.status() === 200, 'maquinas-do-board-nao-consultaveis');
    const disponiveis = await maquinas.json();
    exigir(Array.isArray(disponiveis), 'contrato-da-listagem-de-maquinas-invalido');
    emitir('board-maquinas', { http: maquinas.status(), maquinasDisponiveis: disponiveis.length,
      maquinasCriadas: 0, outrasIdentidadesExercitadas: 0 });
    if (!disponiveis.length) {
      await pagina.getByText('Nenhuma máquina cadastrada.', { exact: false }).waitFor();
      emitir('dependencia-do-board', { codigo: 'nenhuma-maquina-cadastrada-para-a-conta', mensagemVisivel: true,
        fonte: 'GET /api/remoto/maquinas e interface real', geracaoExercitada: false });
    }
    exigir(!violacao, 'pagina-tentou-gravacao-fora-do-login-bloqueada');
    emitir('resultado', { navegadorReal: true, loginPelaInterface: true, cadastro: 'identidade-conferida', projeto: 1,
      consultaDoBoard: 'conferida', requisicoesBloqueadas: bloqueadas, gravacoesCadastrais: 0,
      artefatoGeradoPeloBoard: false, previewValidado: false, aceiteE2E: 'parcial',
      swa: 'manual-pelo-desenvolvedor-aguardando-evidencia' });
  }
} catch (erro) {
  // Mensagens do Playwright podem repetir parâmetros de fill: publicar somente códigos próprios.
  emitir('pendencia', { etapa, codigo: erro instanceof Pendencia ? erro.message : 'falha-de-navegador-ou-contrato',
    classe: erro instanceof Pendencia ? 'Pendencia' : erro?.name === 'TimeoutError' ? 'TimeoutError' : 'Error', aceiteE2E: 'pendente' });
  process.exitCode = 1;
} finally {
  await contexto?.close().catch(() => {});
  await navegador?.close().catch(() => {});
}
