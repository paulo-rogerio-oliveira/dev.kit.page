// Conta existente e páginas reais; sem seed, interceptação de respostas ou exportação de sessão.
// Executar --consultar somente com devcli segredos --executar usuario_test,senha_test -- node <arquivo>.
import { chromium } from '@playwright/test';
import { createHash, randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, statSync } from 'node:fs';
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
let pedidosDeCatalogoBloqueados = 0;
let violacao = false;
let downloadDoBoardConferido = false;

async function baixarPeloBoard(pagina, grupo, pacote, pasta) {
  exigir(pacote?.sha === '7e108e7fc3f4ff44bf105e7b9a5496007fbd0b20'
    && pacote.hash === 'e422f35a4d1682cd16966d2bff3e458366723dcae1611dd8d4f2a18b080b92b9'
    && pacote.tamanho === 39612553, 'pacote-do-board-diverge-do-cenario-conferido');
  // A lista pode renovar a assinatura enquanto o cartão é medido/capturado.
  // Selecionar o único link do SHA conhecido e conferir os bytes depois do clique.
  const link = grupo.getByRole('link').filter({ hasText: pacote.sha.slice(0, 7) });
  exigir(await link.count() === 1 && await link.isVisible(), 'link-do-registro3-nao-encontrado-no-cartao');
  const href = await link.getAttribute('href');
  exigir(href, 'link-do-registro3-sem-destino');
  const destino = new URL(href, pagina.url());
  exigir(destino.origin === api && !destino.username && !destino.password, 'destino-do-download-fora-do-cenario');
  const contextoDaPagina = pagina.context();
  let http = null;
  let bearerNoDownload = null;
  const observarResposta = resposta => {
    if (resposta.url() === destino.href && resposta.request().method() === 'GET') {
      http = resposta.status();
      bearerNoDownload = Boolean(resposta.request().headers().authorization);
    }
  };
  let resolver;
  let rejeitar;
  const iniciado = new Promise((resolve, reject) => { resolver = resolve; rejeitar = reject; });
  // Se o clique falhar antes da espera, não deixar timeout virar rejeição sem tratamento.
  void iniciado.catch(() => {});
  const paginas = new Set();
  const observarPagina = p => { paginas.add(p); p.on('download', resolver); };
  for (const p of contextoDaPagina.pages()) observarPagina(p);
  contextoDaPagina.on('page', observarPagina);
  contextoDaPagina.on('response', observarResposta);
  const prazo = setTimeout(() => rejeitar(new Pendencia('download-do-board-nao-iniciou')), 30_000);
  try {
    await link.click();
    const download = await iniciado;
    exigir(new URL(download.url()).href === destino.href, 'download-nao-corresponde-ao-link-do-registro3');
    const arquivo = join(pasta, 'artefato-3.zip');
    let prazoDaTransferencia;
    try {
      await Promise.race([download.saveAs(arquivo), new Promise((_, reject) => {
        prazoDaTransferencia = setTimeout(() => reject(new Pendencia('download-do-board-excedeu-o-prazo')), 60_000);
      })]);
    } finally { clearTimeout(prazoDaTransferencia); }
    exigir(await download.failure() === null, 'download-do-board-nao-concluiu');
    const tamanho = statSync(arquivo).size;
    exigir(tamanho === pacote.tamanho, 'download-do-board-com-tamanho-divergente');
    const hash = createHash('sha256').update(readFileSync(arquivo)).digest('hex');
    exigir(hash === pacote.hash, 'download-do-board-com-hash-divergente');
    exigir(http === null || http === 200, 'download-do-board-http-nao-aprovado');
    exigir(bearerNoDownload !== true, 'download-do-board-enviou-bearer');
    exigir(new URL(pagina.url()).pathname === '/board' && await grupo.isVisible(), 'contexto-do-board-nao-preservado');
    downloadDoBoardConferido = true;
    emitir('download-pelo-board', { projeto: 1, artefato: 3, http, bearerNoDownload, arquivo,
      hash, tamanho, sha: pacote.sha, contextoDoPainelPreservado: true,
      manifestos: 'mesmos-bytes-do-zip-conferido-no-comando534', novaLeituraDosManifestos: false });
  } finally {
    clearTimeout(prazo);
    contextoDaPagina.off('page', observarPagina);
    contextoDaPagina.off('response', observarResposta);
    for (const p of paginas) p.off('download', resolver);
  }
}

async function conferirPainel(pagina, maquinas, fotoPendente, artefatosPendentes, diagnostico, pasta) {
  exigir(maquinas.length === 1, 'painel-exige-identificar-maquina-do-cenario');
  const foto = await fotoPendente;
  exigir(foto.resposta, 'retrato-nao-recebido-no-navegador');
  emitir('http-painel', { cenario: 'retrato-da-maquina', metodo: 'GET', caminho: '/api/remoto/maquinas/<maquina>/retrato',
    http: foto.resposta.status(), dataDoServidor: foto.resposta.headers()['date'] ?? null });
  exigir(foto.resposta.status() === 200, 'retrato-do-board-recusado');
  const recebido = await foto.resposta.json();
  exigir(recebido.maquina?.maquinaId === maquinas[0].maquinaId, 'retrato-nao-corresponde-a-maquina-consultada');
  const retrato = recebido.retrato;
  exigir(retrato && Array.isArray(retrato.projetos), 'contrato-do-retrato-sem-projetos');
  const projeto = retrato.projetos.find(p => p.slug === slug);
  const data = typeof retrato.geradoEm === 'string' && /^\d{4}-\d\d-\d\dT[\d:.+-]+Z?$/.test(retrato.geradoEm) ? retrato.geradoEm : null;
  emitir('retrato-do-board', { maquinaConectada: maquinas[0].conectada === true, appAberto: maquinas[0].appAberto === true,
    retratoGeradoEm: data, projetoDeTestePresente: Boolean(projeto), capacidadeDeGerarInformada: projeto?.podeGerarArtefato === true,
    campoCapacidadePresente: Boolean(projeto && Object.hasOwn(projeto, 'podeGerarArtefato')),
    capacidade: typeof projeto?.podeGerarArtefato === 'boolean' ? projeto.podeGerarArtefato : null,
    repositoriosDoProjeto: Array.isArray(projeto?.repositorios) ? projeto.repositorios.length : null });
  const consulta = await artefatosPendentes;
  exigir(consulta.resposta, 'lista-de-artefatos-nao-recebida-no-navegador');
  emitir('http-painel', { cenario: 'artefatos-recentes', metodo: 'GET', caminho: '/api/projetos/artefatos',
    http: consulta.resposta.status(), dataDoServidor: consulta.resposta.headers()['date'] ?? null });
  exigir(consulta.resposta.status() === 200, 'lista-de-artefatos-do-board-recusada');
  const lista = await consulta.resposta.json();
  exigir(Array.isArray(lista), 'contrato-de-artefatos-do-board-invalido');
  const doProjeto = lista.find(p => p.projetoId === 1 && p.slug === slug);
  const pacote = doProjeto?.artefatos?.find(a => a.id === 3);
  emitir('artefato-do-projeto-na-api-do-board', { projeto: 1, artefato: pacote ? 3 : null,
    shaConferido: pacote?.sha === '7e108e7fc3f4ff44bf105e7b9a5496007fbd0b20',
    hashConferido: pacote?.hash === 'e422f35a4d1682cd16966d2bff3e458366723dcae1611dd8d4f2a18b080b92b9',
    tamanhoConferido: pacote?.tamanho === 39612553, downloadExercitado: false });
  if (!projeto) {
    emitir('dependencia-do-painel', { codigo: 'projeto-compartilhado-ausente-no-retrato-da-maquina', projeto: 1,
      fonte: 'GET real do retrato e GET de projetos', registroCompartilhadoConferido: true,
      maquinaAlterada: false, comandoRemotoEnviado: false, aceiteE2E: 'pendente' });
    return;
  }
  const grupo = pagina.getByRole('group', { name: 'Artefatos de TESTE ISOLADO artefato item 6 t63', exact: true });
  await grupo.waitFor();
  const gerar = grupo.getByRole('button', { name: 'Gerar artefato', exact: true });
  await gerar.waitFor();
  emitir('controles-do-painel', { projeto: 1, grupoVisivel: true, gerarHabilitado: await gerar.isEnabled(),
    acaoDeGeracaoAcionada: false });
  await pagina.setViewportSize({ width: 390, height: 844 });
  await grupo.waitFor({ state: 'visible' });
  const dimensoes = await grupo.evaluate(elemento => {
    const caixa = elemento.getBoundingClientRect();
    return { dentroDaLargura: caixa.left >= -1 && caixa.right <= window.innerWidth + 1,
      semOverflowInterno: elemento.scrollWidth <= elemento.clientWidth + 1 };
  });
  emitir('painel-390px', { projeto: 1, ...dimensoes, capturaRealizada: false, coberturaVisualGlobal: false });
  if (diagnostico) {
    exigir(!existsSync(pasta), 'pasta-do-diagnostico-ja-existe');
    mkdirSync(pasta, { recursive: true });
    const cartao = grupo.locator('..');
    exigir(await cartao.evaluate(el => el.classList.contains('br-proj-card')), 'container-nao-e-o-cartao-do-projeto');
    const textoDoCartao = (await cartao.innerText()).toLowerCase();
    exigir(![process.env.usuario_test, process.env.senha_test].filter(Boolean)
      .some(valor => textoDoCartao.includes(valor.toLowerCase())), 'cartao-contem-credencial-nao-capturado');
    const destinosDoCartao = await grupo.getByRole('link').evaluateAll(links => links.map(link => link.href));
    exigir(!destinosDoCartao.some(url => textoDoCartao.includes(url.toLowerCase())), 'cartao-expoe-url-nao-capturado');
    const geometria = await grupo.evaluate(elemento => {
      const caixa = elemento.getBoundingClientRect();
      const cartao = elemento.parentElement.getBoundingClientRect();
      const arredondar = numero => Math.round(numero * 100) / 100;
      const elementosQueExcedemOGrupo = [...elemento.querySelectorAll('a,button,small,code,select,input')].flatMap(el => {
        const r = el.getBoundingClientRect();
        if (r.left >= caixa.left - 1 && r.right <= caixa.right + 1) return [];
        const css = getComputedStyle(el);
        return [{ tag: el.tagName, esquerda: arredondar(r.left), direita: arredondar(r.right),
          foraDaViewport: r.left < -1 || r.right > innerWidth + 1,
          foraDoCartao: r.left < cartao.left - 1 || r.right > cartao.right + 1,
          margemEsquerda: css.marginLeft, margemDireita: css.marginRight, overflowX: css.overflowX }];
      }).slice(0, 20);
      return { viewport: innerWidth, larguraDoDocumento: document.documentElement.scrollWidth,
        grupoEsquerda: arredondar(caixa.left), grupoDireita: arredondar(caixa.right),
        larguraInterna: elemento.clientWidth, larguraRolavel: elemento.scrollWidth,
        cartaoEsquerda: arredondar(cartao.left), cartaoDireita: arredondar(cartao.right), elementosQueExcedemOGrupo };
    });
    emitir('geometria-do-cartao-390px', geometria);
    const estreita = join(pasta, 'cartao-390.png');
    await cartao.screenshot({ path: estreita });
    await pagina.setViewportSize({ width: 1365, height: 900 });
    const larga = join(pasta, 'cartao-1365.png');
    await cartao.screenshot({ path: larga });
    emitir('capturas-locais-do-cartao', { projeto: 1, arquivos: [estreita, larga],
      paginaInteira: false, sessaoExportada: false, coberturaVisualGlobal: false });
    await baixarPeloBoard(pagina, grupo, pacote, pasta);
  }
}

try {
  const modo = process.argv[2];
  const painel = process.argv[3] === '--painel';
  const diagnostico = process.argv.length === 7 && process.argv[6] === '--diagnostico';
  const maquinaAlvo = [6, 7].includes(process.argv.length) && process.argv[4] === '--maquina' ? process.argv[5] : null;
  exigir((process.argv.length === 3 || process.argv.length === 4 && painel
    || (process.argv.length === 6 || diagnostico) && painel && /^[a-f0-9]{32}$/.test(maquinaAlvo ?? ''))
    && ['--simular', '--consultar'].includes(modo), 'modo-invalido');
  const repositorio = fileURLToPath(new URL('../../..', import.meta.url));
  const pastaDiagnostico = join(repositorio, '../git.kit/anexos/board-diagnostico-item6', execucao);
  const workflow = readFileSync(join(repositorio, '.github/workflows/azure-static-web-apps-black-forest-00e9a2410.yml'), 'utf8');
  const site = new URL(workflow.match(/VITE_SITE_URL:\s*(https:\/\/[^\s'"#]+)/)?.[1]);
  exigir(site.protocol === 'https:' && site.hostname.endsWith('.azurestaticapps.net') && !site.username && !site.password,
    'origem-do-workflow-invalida');
  const edge = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
  exigir(existsSync(edge), 'edge-existente-nao-encontrado');
  const sha = execFileSync('git', ['-C', repositorio, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
  const hash = createHash('sha256').update(readFileSync(fileURLToPath(import.meta.url))).digest('hex');
  emitir('plano', { modo, painel, maquinaDoCenarioIndicada: Boolean(maquinaAlvo), site: site.origin, api, shaDoRepositorio: sha, hashDoRoteiro: hash,
    fonteDaOrigem: 'workflow VITE_SITE_URL existente', shaDoSitePublicado: 'nao-confirmado', shaDaApiPublicada: 'nao-confirmado',
    autorizacao: 'mensagens-516-e-530', ator: 'conta-de-teste', perfil: 'efemero-isolado',
    capturaDoCartaoPlanejada: diagnostico, downloadPeloBoardPlanejado: diagnostico,
    traces: false, sessaoExportada: false, gravacoesDeDadosPermitidas: 0,
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
    contexto = await navegador.newContext({ serviceWorkers: 'block', acceptDownloads: diagnostico, viewport: { width: 1365, height: 900 } });
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
        let catalogoDoCenario = false;
        if (maquinaAlvo && url.origin === api && url.pathname === '/api/remoto/comandos' && metodo === 'POST') {
          try {
            const corpo = pedido.postDataJSON();
            catalogoDoCenario = corpo?.tipo === 'catalogo' && corpo.maquinaId === maquinaAlvo
              && corpo.texto === '' && !corpo.sessao;
          } catch { /* Corpo desconhecido continua sendo uma gravação não permitida. */ }
        }
        if (catalogoDoCenario) pedidosDeCatalogoBloqueados++;
        // Sem URL, corpo ou headers: podem conter credenciais e identificadores privados.
        emitir('requisicao-bloqueada', { metodoPermitidoDeLeitura: leitura, destinoPermitido,
          motivo: catalogoDoCenario ? 'pedido-automatico-de-catalogo-do-cenario-nao-enviado'
            : leitura ? 'destino-fora-do-cenario' : 'gravacao-fora-do-login',
          comandoRemotoEnviado: false });
        if (!leitura && !catalogoDoCenario) violacao = true;
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
    // Instalar as esperas antes da navegação; nenhum corpo de resposta será substituído.
    // Converter timeout em dado evita rejeição sem tratamento caso não exista máquina.
    const fotoPendente = painel ? pagina.waitForResponse(r => {
      const url = new URL(r.url());
      return url.origin === api && /^\/api\/remoto\/maquinas\/[^/]+\/retrato$/.test(url.pathname)
        && (!maquinaAlvo || url.pathname === '/api/remoto/maquinas/' + maquinaAlvo + '/retrato')
        && r.request().method() === 'GET';
    }).then(resposta => ({ resposta }), () => ({})) : null;
    const artefatosPendentes = painel ? respostaDe('/api/projetos/artefatos')
      .then(resposta => ({ resposta }), () => ({})) : null;
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
    if (painel && disponiveis.length) {
      etapa = 'retrato-e-painel-de-artefatos';
      const alvo = maquinaAlvo ? disponiveis.find(m => m.maquinaId === maquinaAlvo) : null;
      exigir(!maquinaAlvo || alvo, 'maquina-do-cenario-nao-acessivel-a-conta');
      if (alvo && disponiveis.length > 1) {
        const seletor = pagina.getByRole('combobox', { name: 'Máquina', exact: true });
        // Não disparar change redundante: o handler do produto invalida a geração da leitura anterior.
        const jaSelecionada = await seletor.inputValue() === maquinaAlvo;
        if (!jaSelecionada) await seletor.selectOption(maquinaAlvo);
        emitir('selecao-da-maquina-do-cenario', { jaSelecionada, selecaoAlterada: !jaSelecionada });
      }
      await conferirPainel(pagina, alvo ? [alvo] : disponiveis, fotoPendente, artefatosPendentes, diagnostico, pastaDiagnostico);
    }
    exigir(!violacao, 'pagina-tentou-gravacao-fora-do-login-bloqueada');
    emitir('resultado', { navegadorReal: true, loginPelaInterface: true, cadastro: 'identidade-conferida', projeto: 1,
      consultaDoBoard: 'conferida', requisicoesBloqueadas: bloqueadas, pedidosDeCatalogoBloqueados,
      handshakeDeCatalogoExercitado: false, gravacoesCadastrais: 0,
      artefatoGeradoPeloBoard: false, downloadDoBoardConferido, previewValidado: false, aceiteE2E: 'parcial',
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
