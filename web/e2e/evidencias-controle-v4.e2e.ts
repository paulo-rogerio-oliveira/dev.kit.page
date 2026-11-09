import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { createHash, createHmac } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { crc32 } from 'node:zlib';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/**
 * (controle-v4, item 5) As EVIDÊNCIAS ponta a ponta das correções do board remoto de 07/11 e do "Gerar artefato", com o usuário
 * de teste logado pela tela de login, no desktop (1440x900) e no celular (390x844), sobre a API real (SQLite temporário, fila e
 * provisão simuladas — playwright.evidencias.config.ts). Cada passo vira uma linha da TRANSCRIÇÃO em HTML (passos, esperado e
 * obtido), com as capturas, em test-results/evidencias-controle-v4.
 *
 * A senha vem de DEVKIT_TESTE_SENHA e só vai à API e ao campo de senha: não entra na transcrição, nos nomes nem nas mensagens.
 * Sem ela, o roteiro usa um usuário fictício (a lacuna fica registrada na transcrição).
 *
 * O que a máquina publica — o retrato — é o que o dev.kit monta (`RetratoRemoto.Montar` do git.kit), lido de DEVKIT_E2E_RETRATO
 * (o JSON gerado pelo código do desktop) ou, sem ele, de evidencias-controle-v4.retrato.json; as datas andam para agora. A
 * máquina responde aos comandos do site (o ↻ do catálogo e o Gerar artefato) pelas MESMAS rotas que o dev.kit usa.
 */
const pasta = 'test-results/evidencias-controle-v4';
const senhaDoTeste = process.env.DEVKIT_TESTE_SENHA ?? '';
const LOGIN = senhaDoTeste ? 'test@senior.com.br' : 'usuario.ficticio@exemplo.test';
const SENHA = senhaDoTeste || NOVA_SENHA;
const MAQUINA = 'maquina-de-teste-controle-v4';
const DESKTOP = { width: 1440, height: 900 };
const CELULAR = { width: 390, height: 844 };
const TELA_DESKTOP = 'desktop 1440x900';
const TELA_CELULAR = 'celular 390x844';
const PREVIEW = 'https://dev-kit-page-controle-v4.eastus2.azurestaticapps.net';
const SHA = '2412df94c1b7a0e5d3f6a8b9c0d1e2f3a4b5c6d7';

type Passo = { ajuste: string; tela: string; passos: string[]; esperado: string; obtido: string; capturas: string[]; ok: boolean };
const roteiro: Passo[] = [];
let atual: Passo | null = null;
let numeroDaCaptura = 0;

/** O texto que sai na transcrição: nunca a senha, mesmo numa mensagem de falha. */
function limpo(texto: string) {
  return SENHA ? texto.split(SENHA).join('<senha>') : texto;
}

async function etapa(page: Page, ajuste: string, tela: string, passos: string[], esperado: string, corpo: () => Promise<string>) {
  const passo: Passo = { ajuste, tela, passos, esperado, obtido: '', capturas: [], ok: false };
  roteiro.push(passo);
  atual = passo;
  try {
    // Os avisos do passo anterior (somem sozinhos em 6 s) não cobrem as capturas deste.
    for (const fechar of await page.getByRole('button', { name: 'Fechar aviso' }).all()) await fechar.click({ timeout: 2_000 }).catch(() => undefined);
    passo.obtido = limpo(await corpo());
    passo.ok = true;
  } catch (falha) {
    passo.obtido = limpo(`FALHOU: ${(falha as Error).message.split('\n').slice(0, 3).join(' ')}`);
    throw falha;
  } finally {
    atual = null;
  }
}

async function capturar(page: Page, nome: string, paginaInteira = false) {
  const arquivo = `${String(++numeroDaCaptura).padStart(2, '0')}-${nome}.png`;
  await page.screenshot({ path: `${pasta}/${arquivo}`, fullPage: paginaInteira });
  atual?.capturas.push(arquivo);
  return arquivo;
}

/** O TOTP de RFC 6238 (SHA-1, 6 dígitos, 30 s) da chave Base32 — o código do aplicativo autenticador. */
function totp(chave: string, passo = Math.floor(Date.now() / 30_000)) {
  const alfabeto = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = '';
  for (const c of chave) bits += alfabeto.indexOf(c).toString(2).padStart(5, '0');
  const bytes = Buffer.from(bits.match(/.{8}/g)!.map(b => parseInt(b, 2)));
  const contador = Buffer.alloc(8);
  contador.writeBigInt64BE(BigInt(passo));
  const hash = createHmac('sha1', bytes).update(contador).digest();
  const offset = hash[hash.length - 1] & 15;
  return ((hash.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).toString().padStart(6, '0');
}

/** Espera o próximo passo de 30 s: o mesmo código não autoriza duas vezes. */
async function proximoPasso() {
  await new Promise(r => setTimeout(r, 30_500 - (Date.now() % 30_000)));
}

/** Um zip de verdade (entradas guardadas, sem compressão): o pacote que a máquina enviaria, com o release.json do carimbo. */
function zip(arquivos: Record<string, string>) {
  const locais: Buffer[] = [];
  const central: Buffer[] = [];
  let deslocamento = 0;
  for (const [nome, texto] of Object.entries(arquivos)) {
    const dados = Buffer.from(texto, 'utf8');
    const caminho = Buffer.from(nome, 'utf8');
    const crc = crc32(dados);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0); local.writeUInt16LE(20, 4); local.writeUInt16LE(0x0800, 6); local.writeUInt16LE(0, 8);
    local.writeUInt16LE(0, 10); local.writeUInt16LE(0x21, 12); local.writeUInt32LE(crc, 14); local.writeUInt32LE(dados.length, 18);
    local.writeUInt32LE(dados.length, 22); local.writeUInt16LE(caminho.length, 26); local.writeUInt16LE(0, 28);
    const entrada = Buffer.alloc(46);
    entrada.writeUInt32LE(0x02014b50, 0); entrada.writeUInt16LE(20, 4); entrada.writeUInt16LE(20, 6); entrada.writeUInt16LE(0x0800, 8);
    entrada.writeUInt16LE(0, 10); entrada.writeUInt16LE(0, 12); entrada.writeUInt16LE(0x21, 14); entrada.writeUInt32LE(crc, 16);
    entrada.writeUInt32LE(dados.length, 20); entrada.writeUInt32LE(dados.length, 24); entrada.writeUInt16LE(caminho.length, 28);
    entrada.writeUInt32LE(deslocamento, 42);
    locais.push(local, caminho, dados);
    central.push(entrada, caminho);
    deslocamento += local.length + caminho.length + dados.length;
  }
  const diretorio = Buffer.concat(central);
  const fim = Buffer.alloc(22);
  fim.writeUInt32LE(0x06054b50, 0); fim.writeUInt16LE(Object.keys(arquivos).length, 8); fim.writeUInt16LE(Object.keys(arquivos).length, 10);
  fim.writeUInt32LE(diretorio.length, 12); fim.writeUInt32LE(deslocamento, 16);
  return Buffer.concat([...locais, diretorio, fim]);
}

const sha256 = (dados: Buffer) => createHash('sha256').update(dados).digest('hex');

/** O usuário de teste numa empresa, com a fila do controle remoto pronta (o admin libera e o worker da provisão conclui). */
async function prepararUsuario(request: APIRequestContext) {
  await garantirSenhaDefinitiva(request);
  const admin = { Authorization: `Bearer ${(await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json()).token}` };
  const empresa = await (await request.post(`${API}/api/empresas`, { headers: admin, data: { nome: 'Empresa de teste do controle-v4', plano: 'Empresarial', assentos: 5 } })).json() as { id: number };
  const criado = await request.post(`${API}/api/usuarios`, { headers: admin, data: { login: LOGIN, nome: 'Usuário de teste', papel: 'dev', empresaId: empresa.id } });
  expect(criado.status(), await criado.text()).toBe(201);
  const { usuario, senhaTemporaria } = await criado.json() as { usuario: { id: number }; senhaTemporaria: string };
  const liberacao = await request.put(`${API}/api/usuarios/${usuario.id}/recursos/provisao`, { headers: admin, data: { liberado: true } });
  expect(liberacao.ok(), await liberacao.text()).toBeTruthy();
  const temporaria = await (await request.post(`${API}/api/auth/login`, { data: { login: LOGIN, senha: senhaTemporaria } })).json() as { token: string };
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    headers: { Authorization: `Bearer ${temporaria.token}` }, data: { senhaAtual: senhaTemporaria, novaSenha: SENHA },
  });
  expect(troca.ok(), `a troca da senha temporária pela do usuário de teste foi recusada: ${limpo(await troca.text())}`).toBeTruthy();
  const { token } = await troca.json() as { token: string };
  await expect.poll(async () => {
    const usuarios = await (await request.get(`${API}/api/usuarios`, { headers: admin })).json() as Array<{ login: string; fila?: { estado: string } | null }>;
    return usuarios.find(u => u.login === LOGIN)?.fila?.estado;
  }, { message: 'a fila do controle remoto do usuário (worker da provisão simulado)', timeout: 120_000, intervals: [1000] }).toBe('pronto');
  return { Authorization: `Bearer ${token}` };
}

/** A versão de design que a fala e o snapshot citam: a proposta chat-remoto v016, no projeto de design da empresa. */
async function enviarDesign(request: APIRequestContext, page: Page, headers: Record<string, string>) {
  const png = process.env.DEVKIT_E2E_PROPOSTA ? readFileSync(process.env.DEVKIT_E2E_PROPOSTA)
    : await (async () => { await page.setContent('<h1 style="font:32px Segoe UI">chat-remoto v016 · proposta</h1>'); return page.screenshot(); })();
  const resposta = await request.post(`${API}/api/design/versoes`, { headers, multipart: {
    repositorio: 'https://github.com/empresa-demo/dev.kit.page', tela: 'chat-remoto', nome: 'Chat remoto', numero: '16', tipo: 'proposta',
    origem: 'proposta', tema: 'light', nivel: 'empresa', branch: 'feature/dev/projeto-controle-v4', commit: 'dbd5d6a', autorTipo: 'agente',
    runner: 'claude', descricao: 'Layout v016 do chat do Orquestrador.', imagem: { name: 'v016-proposta.png', mimeType: 'image/png', buffer: png },
  } });
  expect(resposta.status(), await resposta.text()).toBe(201);
  const { projeto } = await resposta.json() as { projeto: number };
  return `/api/design/${projeto}/chat-remoto/v16`;
}

/** O retrato do dev.kit, com as datas andando para agora e a captura apontando para a versão de design desta base. */
function retratoDoDevKit(linkDoDesign: string) {
  const origem = process.env.DEVKIT_E2E_RETRATO || new URL('./evidencias-controle-v4.retrato.json', import.meta.url);
  const original = JSON.parse(readFileSync(origem, 'utf8')) as { geradoEm: string };
  const atraso = Date.now() - Date.parse(original.geradoEm);
  const texto = JSON.stringify(original).replace(/\/api\/design\/\d+\/chat-remoto\/v16/g, linkDoDesign);
  const retrato = JSON.parse(texto, (_, valor) => typeof valor === 'string' && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d/.test(valor)
    ? new Date(Date.parse(valor) + atraso).toISOString() : valor) as {
    maquinaId: string; geradoEm: string; projetos: Array<{ slug: string; situacao: string }>; fluxos: Array<{ nome: string; situacao: string }>;
    pendencias: Array<{ id: string; tipo: string; titulo: string; escopo?: { ato: string } }>;
  };
  return { ...retrato, maquinaId: MAQUINA, geradoEm: new Date().toISOString(), origem: String(origem) };
}

async function publicarMaquina(request: APIRequestContext, headers: Record<string, string>, linkDoDesign: string) {
  const batimento = await request.post(`${API}/api/remoto/heartbeat`, { headers, data: {
    contrato: 1, maquinaId: MAQUINA, nome: 'Máquina de teste', versaoDevKit: '136', origem: 'servico', appAberto: true, estadoDoAgente: 'ativo', turnoEmAndamento: false,
  } });
  expect(batimento.ok(), await batimento.text()).toBeTruthy();
  const { origem, ...retrato } = retratoDoDevKit(linkDoDesign);
  const enviado = await request.put(`${API}/api/remoto/retrato`, { headers, data: retrato });
  expect(enviado.ok(), await enviado.text()).toBeTruthy();
  await publicarCatalogo(request, headers, linkDoDesign, null);
  const falar = (id: string, autor: string, texto: string, minutos: number, anexos?: unknown) => request.post(`${API}/api/remoto/conversa/falas`, { headers, data: {
    contrato: 1, maquinaId: MAQUINA, id, autor, origem: 'desktop', texto, ditaEm: new Date(Date.now() - minutos * 60_000).toISOString(), ...(anexos ? { anexos } : {}),
  } });
  expect((await falar('fd-1', 'dev', 'Como está o projeto controle-v4?', 30)).status()).toBe(201);
  expect((await falar('fo-1', 'orquestrador', 'Os itens 1, 2, 3, 4 e 6 estão integrados; o item 5 (evidências) está em execução.', 29)).status()).toBe(201);
  expect((await falar('fd-2', 'dev', 'Mostre a proposta do layout do chat do Orquestrador.', 12)).status()).toBe(201);
  expect((await falar('dv-1', 'orquestrador', 'Versão de design gravada: chat-remoto v016 (Proposta).', 10, [
    { titulo: 'chat-remoto v016 · Proposta', url: linkDoDesign },
    { titulo: 'chat-remoto v017 · Proposta', aviso: 'o dev.kit.page recusou a versão (o teto de versões do plano ou um dado inválido): ela fica só no dev.kit desta máquina.' },
  ])).status()).toBe(201);
  return { retrato, origem };
}

async function publicarCatalogo(request: APIRequestContext, headers: Record<string, string>, linkDoDesign: string, comandoId: string | null) {
  const resposta = await request.put(`${API}/api/remoto/catalogo`, { headers, data: {
    contrato: 1, maquinaId: MAQUINA, geradoEm: new Date().toISOString(), comandoId,
    workItems: [{ id: 812, titulo: 'Handshake do chat remoto', tipo: 'User Story', estado: 'Active' }, { id: 821, titulo: 'Correções do board remoto', tipo: 'Task', estado: 'Active' }],
    repositorios: [{ nome: 'github.com/empresa-demo/dev.kit.page', branchPadrao: 'main' }, { nome: 'github.com/empresa-demo/git.kit', branchPadrao: 'main' },
      { nome: 'dev.azure.com/outra-org/git.kit' }, { nome: 'github.com/empresa-demo/portal' }],
    fluxos: [
      { nome: 'Implementar com revisor', descricao: 'Implementação avaliada e revisão do responsável', id: 7, agentePadrao: 'claude:opus', parametros: [
        { nome: 'REVISOR', rotulo: 'Revisor', tipo: 'texto', usuario: true, obrigatorio: true },
        { nome: 'AMBIENTE', tipo: 'lista', opcoes: ['homologação', 'produção'], obrigatorio: true, padrao: 'homologação' },
        { nome: 'NOTIFICAR', tipo: 'sim-nao', obrigatorio: false } ] },
      { nome: 'Tarefa avulsa', descricao: 'Implementação avaliada', id: 3 },
    ],
    projetos: [{ slug: 'controle-v4', nome: 'controle-v4', situacao: 'aprovado' }],
    designs: [{ nome: 'github.com/empresa-demo/dev.kit.page', telas: 4, repositorio: 'github.com/empresa-demo/dev.kit.page', miniatura: linkDoDesign }],
    agentes: ['claude', 'claude:opus', 'claude:sonnet', 'kiro', 'kiro:auto', 'codex'],
    membros: ['Pessoa Revisora', 'Pessoa Dev', 'Pessoa Gestora'],
  } });
  expect(resposta.ok(), await resposta.text()).toBeTruthy();
}

type Comando = { id: string; tipo: string; texto: string; estado: string; dispositivo: string };

/**
 * A MÁQUINA atende ao próximo comando do site que passa no filtro, pelo protocolo do dev.kit: confirma o recebimento (e só
 * executa no primeiro), executa e responde concluído.
 */
async function atender(request: APIRequestContext, headers: Record<string, string>, filtro: (c: Comando) => boolean, executar: (c: Comando) => Promise<string>) {
  let comando: Comando | undefined;
  await expect.poll(async () => {
    const conversa = await (await request.get(`${API}/api/remoto/maquinas/${MAQUINA}/conversa`, { headers })).json() as { comandos: Comando[] };
    comando = conversa.comandos.find(c => filtro(c) && ['pendente', 'na-fila'].includes(c.estado));
    return !!comando;
  }, { message: 'o comando do site na fila da máquina', timeout: 60_000, intervals: [500] }).toBeTruthy();
  const recebido = await request.post(`${API}/api/remoto/comandos/${comando!.id}/recebido`, { headers, data: { contrato: 1, maquinaId: MAQUINA } });
  expect((await recebido.json() as { executar: boolean }).executar).toBeTruthy();
  const texto = await executar(comando!);
  const resposta = await request.post(`${API}/api/remoto/respostas`, { headers, data: {
    contrato: 1, maquinaId: MAQUINA, id: `ok-${comando!.id}`, comandoId: comando!.id, tipo: 'concluido', texto, geradaEm: new Date().toISOString(),
  } });
  expect(resposta.ok(), await resposta.text()).toBeTruthy();
  return comando!;
}

const ehArtefato = (c: Comando) => c.tipo === 'projeto' && c.texto.includes('"artefato"');

/** O executor da máquina: o zip do pacote .NET (carimbado com o release.json) e, se pedido, o preview, no projeto do site. */
async function gerarArtefatos(request: APIRequestContext, headers: Record<string, string>, projetoId: number, numero: number, comPreview: boolean) {
  const pacote = zip({
    'release.json': JSON.stringify({ produto: 'git.kit', versao: `136.${numero}`, commit: SHA, gerado: new Date().toISOString() }, null, 2),
    'LEIA-ME.txt': 'Pacote de demonstração do controle-v4 (item 5): só download, nunca produção.\n',
  });
  const enviado = await request.post(`${API}/api/projetos/${projetoId}/artefatos`, { headers, multipart: {
    repositorio: 'https://github.com/empresa-demo/git.kit', sha: SHA, hash: sha256(pacote),
    arquivo: { name: `git.kit-${SHA.slice(0, 7)}-${numero}.zip`, mimeType: 'application/zip', buffer: pacote },
  } });
  expect(enviado.status(), await enviado.text()).toBe(201);
  if (comPreview) {
    const preview = await request.post(`${API}/api/projetos/${projetoId}/artefatos`, { headers, data: {
      repositorio: 'https://github.com/empresa-demo/dev.kit.page', sha: 'dbd5d6a', url: PREVIEW, ambiente: 'controle-v4',
    } });
    expect(preview.status(), await preview.text()).toBe(201);
  }
  return { pacote, hash: sha256(pacote) };
}

function transcricao(contexto: { inicio: Date; origem: string; fluxos: string[]; ociosas: string }) {
  const esc = (t: string) => t.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  const linhas = roteiro.map((p, i) => `<tr class="${p.ok ? 'ok' : 'falha'}"><td>${i + 1}</td><td>${esc(p.ajuste)}</td><td>${esc(p.tela)}</td>
    <td><ol>${p.passos.map(s => `<li>${esc(s)}</li>`).join('')}</ol></td><td>${esc(p.esperado)}</td><td><b>${p.ok ? '✓' : '✗'}</b> ${esc(p.obtido)}</td>
    <td>${p.capturas.map(c => `<a href="${c}"><img src="${c}" alt="${c}"></a><br><small>${c}</small>`).join('<br>')}</td></tr>`).join('\n');
  const aprovados = roteiro.filter(p => p.ok).length;
  return `<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><title>Roteiro de evidências — controle-v4</title><style>
body{font:14px/1.45 "Segoe UI",sans-serif;margin:24px;color:#1b1f24;background:#fff} table{border-collapse:collapse;width:100%}
th,td{border:1px solid #d0d7de;padding:6px 8px;vertical-align:top;text-align:left} th{background:#f4f6f8}
tr.falha td{background:#fff1f0} img{max-width:240px;border:1px solid #d0d7de} ol{margin:0;padding-left:18px} code{background:#f4f6f8;padding:0 3px}
</style></head><body>
<h1>Roteiro de evidências ponta a ponta — projeto controle-v4 (item 5)</h1>
<p>Execução de ${esc(contexto.inicio.toLocaleString('pt-BR'))} · <b>${aprovados} de ${roteiro.length}</b> passos com o resultado esperado.</p>
<ul>
<li>Usuário: <b>${esc(LOGIN)}</b>, logado pela tela de login do site. ${senhaDoTeste ? 'A senha veio de DEVKIT_TESTE_SENHA e não é registrada aqui.' : '<b>Lacuna:</b> DEVKIT_TESTE_SENHA ausente — usuário fictício.'}</li>
<li>Ambiente: a API do branch (DevKitPage.Api, Development) com SQLite temporário, a fila do controle remoto simulada, o worker da provisão (DevKitPage.Provisao) com o provedor simulado e a web compilada (vite build + preview), no Microsoft Edge pelo Playwright.</li>
<li>Retrato da máquina: <code>${esc(contexto.origem)}</code>, montado pelo <code>RetratoRemoto.Montar</code> do git.kit. Fluxos publicados: ${esc(contexto.fluxos.join(' | '))}. ${esc(contexto.ociosas)}</li>
<li>A máquina responde aos comandos do site (↻ do catálogo e Gerar artefato) pelas rotas do dev.kit: <code>/recebido</code>, <code>/api/projetos/{id}/artefatos</code> e <code>/api/remoto/respostas</code>.</li>
</ul>
<table><thead><tr><th>#</th><th>Ajuste</th><th>Tela</th><th>Passos</th><th>Esperado</th><th>Obtido</th><th>Capturas</th></tr></thead><tbody>
${linhas}
</tbody></table></body></html>`;
}

test('evidências do controle-v4: ajustes 1 a 9 e o Gerar artefato, logado como o usuário de teste, no desktop e no celular', async ({ page, request }) => {
  mkdirSync(pasta, { recursive: true });
  const inicio = new Date();
  const contexto = { inicio, origem: '', fluxos: [] as string[], ociosas: '' };
  try {
    // 0. O ambiente: o usuário de teste, o design, o projeto no site, a máquina e o autenticador.
    const headers = await prepararUsuario(request);
    const linkDoDesign = await enviarDesign(request, page, headers);
    const projetoNoSite = await request.put(`${API}/api/projetos`, { headers, data: {
      slug: 'controle-v4', nome: 'controle-v4', nivel: 'empresa', time: null, repositorios: ['https://github.com/empresa-demo/dev.kit.page', 'https://github.com/empresa-demo/git.kit'],
      branch: 'feature/dev/projeto-controle-v4', semanaConvertida: false, definicao: 'Continuar o board remoto a partir das correções de 07/11.', anexos: [],
      autonomia: true, paralelismo: 3, destinoDaPr: 'feature/dev/semana7', situacao: 'aprovado', versao: null, itens: [],
    } });
    expect(projetoNoSite.ok(), await projetoNoSite.text()).toBeTruthy();
    const projetoId = (await projetoNoSite.json() as { id: number }).id;
    const { retrato, origem } = await publicarMaquina(request, headers, linkDoDesign);
    contexto.origem = origem;
    contexto.fluxos = retrato.fluxos.map(f => `${f.nome} (${f.situacao})`);
    contexto.ociosas = retrato.fluxos.some(f => /ocioso/i.test(f.nome)) ? 'ATENÇÃO: há sessão ociosa no retrato.' : 'Nenhuma sessão de gatilho ociosa no retrato.';
    const { chave } = await (await request.post(`${API}/api/remoto/segundo-fator/preparar`, { headers, data: { senha: SENHA } })).json() as { chave: string };
    expect((await request.post(`${API}/api/remoto/segundo-fator/confirmar`, { headers, data: { codigo: totp(chave) } })).ok()).toBeTruthy();
    const pendencias = retrato.pendencias.length;

    // 1. O login pela tela, com o usuário de teste.
    await page.setViewportSize(DESKTOP);
    await etapa(page, 'Login', TELA_DESKTOP, ['Abrir /login', `Informar o login ${LOGIN} e a senha do usuário de teste`, 'Entrar'],
      'O site entra com o usuário de teste e abre a área logada.', async () => {
        await page.goto('/login');
        await page.getByLabel('Login').fill(LOGIN);
        await page.getByLabel('Senha').fill(SENHA);
        await page.getByRole('button', { name: 'Entrar' }).click();
        await page.waitForURL(/\/(dashboard|conta|board)/, { timeout: 30_000 });
        await expect(page.getByText(LOGIN).first()).toBeVisible();
        await expect(page.getByText(/^Carregando/)).toHaveCount(0, { timeout: 20_000 });
        await capturar(page, 'desktop-logado');
        return `Entrou e abriu ${new URL(page.url()).pathname} com ${LOGIN}.`;
      });

    const chat = page.getByRole('region', { name: 'Orquestrador' });
    const painel = page.getByRole('complementary', { name: 'Board' });

    // ---------------- Desktop 1440x900 ----------------
    await etapa(page, '5 · layout v016, ↻ e Reiniciar no cabeçalho', TELA_DESKTOP, ['Abrir /board', 'Conferir o cabeçalho do Orquestrador e o topo'],
      'Sem rodapé; o ↻ do catálogo e o Reiniciar dev.kit no cabeçalho do Orquestrador; o topo só com a máquina.', async () => {
        await page.goto('/board');
        await expect(chat.getByRole('button', { name: 'Reiniciar dev.kit' })).toBeVisible({ timeout: 25_000 });
        await expect(chat.getByRole('button', { name: 'Atualizar catálogo do dev.kit' })).toBeVisible();
        await expect(chat.getByText('Texto do chat não aprova')).toBeVisible();
        await expect(page.locator('footer')).toHaveCount(0);
        await expect(page.locator('.br-top button')).toHaveCount(0);
        await capturar(page, 'desktop-layout-v016');
        return 'Cabeçalho com "↻" e "Reiniciar dev.kit"; nenhum rodapé; o topo sem botões.';
      });

    await etapa(page, '5 · ↻ atualiza o catálogo', TELA_DESKTOP, ['Clicar em ↻ (Atualizar catálogo do dev.kit)', 'A máquina recebe o comando "catalogo", publica o catálogo e responde'],
      'O pedido vai pela fila à máquina, e o ↻ volta a "atualizado agora há pouco".', async () => {
        await chat.getByRole('button', { name: 'Atualizar catálogo do dev.kit' }).click();
        const comando = await atender(request, headers, c => c.tipo === 'catalogo', async c => {
          await publicarCatalogo(request, headers, linkDoDesign, c.id);
          return 'Catálogo do chat atualizado.';
        });
        await expect(chat.getByRole('button', { name: 'Atualizar catálogo do dev.kit' })).toHaveAttribute('title', /atualizado agora há pouco/i, { timeout: 20_000 });
        await capturar(page, 'desktop-catalogo-atualizado');
        return `Comando ${comando.tipo} (dispositivo ${comando.dispositivo}) recebido e concluído pela máquina; título do ↻: "${await chat.getByRole('button', { name: 'Atualizar catálogo do dev.kit' }).getAttribute('title')}".`;
      });

    await etapa(page, '5 · caixa de 3 a 8 linhas', TELA_DESKTOP, ['Digitar 12 linhas na caixa do Orquestrador'],
      'A caixa começa com 3 linhas, cresce até 8 e então rola.', async () => {
        const caixa = chat.getByLabel('Mensagem ao Orquestrador');
        const inicial = (await caixa.boundingBox())!.height;
        await caixa.fill(Array.from({ length: 12 }, (_, i) => `Linha ${i + 1} do pedido ao Orquestrador`).join('\n'));
        const cheia = (await caixa.boundingBox())!.height;
        const rola = await caixa.evaluate(e => e.scrollHeight > e.clientHeight);
        expect(cheia).toBeGreaterThan(inicial * 2);
        expect(rola).toBeTruthy();
        await capturar(page, 'desktop-caixa-ate-8-linhas');
        await caixa.fill('');
        return `Altura inicial ${Math.round(inicial)} px, com 12 linhas ${Math.round(cheia)} px e barra de rolagem.`;
      });

    await etapa(page, '2 · miniatura e modal de imagem', TELA_DESKTOP, ['Ver a fala do Orquestrador com a versão de design', 'Clicar na miniatura'],
      'A miniatura da v016 aparece na fala (a v017 recusada vem com o aviso) e abre no modal.', async () => {
        const miniatura = chat.getByRole('button', { name: 'Ampliar chat-remoto v016 · Proposta' });
        await expect(miniatura.getByRole('img')).toBeVisible();
        await expect(chat.getByText(/chat-remoto v017 · Proposta: o dev.kit.page recusou/)).toBeVisible();
        await capturar(page, 'desktop-miniatura-no-chat');
        await miniatura.click();
        const modal = page.getByRole('dialog', { name: 'chat-remoto v016 · Proposta' });
        await expect(modal.getByRole('img', { name: 'chat-remoto v016 · Proposta' })).toBeVisible();
        const largura = await modal.getByRole('img').evaluate(e => (e as HTMLImageElement).naturalWidth);
        expect(largura).toBeGreaterThan(1);
        await capturar(page, 'desktop-imagem-no-modal');
        await modal.getByRole('button', { name: 'Fechar' }).click();
        return `A imagem real da versão (${largura} px de largura, servida pela API de design ${linkDoDesign}) abriu no modal.`;
      });

    await etapa(page, '3 · chips de modelo e de parâmetros, com seletor de usuário', TELA_DESKTOP,
      ['Trocar fluxo → "Implementar com revisor"', 'Enviar sem o Revisor', 'Abrir "Modelo do fluxo"', 'Abrir o parâmetro Revisor e buscar "revis"'],
      'O chip do modelo traz o agente padrão do fluxo (claude:opus); o obrigatório vazio segura o envio; o Revisor abre a busca nos membros do time.', async () => {
        await chat.getByRole('button', { name: 'Trocar fluxo' }).click();
        await page.getByRole('button', { name: /^Implementar com revisor/ }).click();
        await expect(chat.getByRole('button', { name: 'Modelo do fluxo' })).toContainText('claude:opus');
        await chat.getByLabel('Mensagem ao Orquestrador').fill('Implemente as correções do board remoto');
        await chat.getByLabel('Mensagem ao Orquestrador').press('Enter');
        await expect(chat.getByText('Preencha antes de enviar: Revisor.')).toBeVisible();
        await chat.getByRole('button', { name: 'Modelo do fluxo' }).click();
        await expect(page.getByRole('group', { name: 'Agentes da máquina' })).toBeVisible();
        await capturar(page, 'desktop-chip-do-modelo');
        await page.getByRole('group', { name: 'Agentes da máquina' }).getByRole('button', { name: 'kiro:auto' }).click();
        await chat.getByRole('button', { name: 'Parâmetro Revisor' }).click();
        await page.getByRole('textbox', { name: 'Revisor' }).fill('revis');
        await capturar(page, 'desktop-seletor-de-usuario');
        await page.getByRole('group', { name: 'Membros do time' }).getByRole('button', { name: 'Pessoa Revisora' }).click();
        await expect(chat.getByRole('button', { name: 'Parâmetro Revisor' })).toContainText('Revisor: Pessoa Revisora');
        await capturar(page, 'desktop-chips-preenchidos');
        return 'Modelo claude:opus trocado por kiro:auto; envio bloqueado com "Preencha antes de enviar: Revisor."; Revisor escolhido na busca: Pessoa Revisora.';
      });

    await etapa(page, '4 · repositório só pelo nome', TELA_DESKTOP, ['Abrir "@ Repositório"', 'Escolher o git.kit da empresa-demo e aplicar'],
      'A lista mostra só o nome; o dono aparece apenas nos dois "git.kit" de mesmo nome.', async () => {
        await chat.getByRole('button', { name: '@ Repositório' }).click();
        const repositorios = page.getByRole('group', { name: 'Do catálogo do dev.kit: repositório' });
        await expect(repositorios.getByRole('button')).toHaveText([/^dev\.kit\.page/, /^git\.kit empresa-demo/, /^git\.kit outra-org/, /^portal/]);
        const nomes = await repositorios.getByRole('button').allInnerTexts();
        await capturar(page, 'desktop-repositorio-pelo-nome');
        await repositorios.getByRole('button', { name: /^git\.kit empresa-demo/ }).click();
        await page.getByRole('button', { name: 'Aplicar contexto' }).click();
        await expect(chat.getByRole('button', { name: '@ Repositório' })).toHaveText('@ git.kit ▾');
        await capturar(page, 'desktop-compositor-com-repositorio');
        return `Itens da lista: ${nomes.map(n => n.replace(/\s+/g, ' ').trim()).join(' | ')}; o chip ficou "@ git.kit".`;
      });

    await etapa(page, '6 · seletores (referência no desktop)', TELA_DESKTOP, ['Abrir "# Work item" no desktop'],
      'No desktop o seletor abre junto da caixa, com os itens do catálogo.', async () => {
        await chat.getByRole('button', { name: '# Work item' }).click();
        const lista = page.getByRole('group', { name: 'Do catálogo do dev.kit: work item em andamento' });
        await expect(lista.getByRole('button').first()).toBeVisible();
        const itens = (await lista.getByRole('button').allInnerTexts()).map(t => t.replace(/\s+/g, ' ').trim());
        await capturar(page, 'desktop-seletor-work-item');
        await page.getByRole('button', { name: 'Cancelar' }).click();
        return `${itens.length} itens na lista de work items: ${itens.join(' | ')}.`;
      });

    await etapa(page, '1 · cartão fechável e filtro Dispensadas', TELA_DESKTOP,
      ['No painel Board, clicar no × do card "Disparo do devcli"', 'Abrir o filtro "Dispensadas"', 'Restaurar'],
      'O card sai de "Precisa de você", a dispensa fica gravada na API, e o filtro Dispensadas a traz de volta com Restaurar.', async () => {
        await chat.getByLabel('Mensagem ao Orquestrador').fill('');
        await expect(painel.getByRole('heading', { name: `Precisa de você · ${pendencias}` })).toBeVisible();
        await capturar(page, 'desktop-precisa-de-voce');
        await painel.getByRole('button', { name: 'Dispensar Disparo do devcli' }).click();
        await expect(painel.getByRole('heading', { name: `Precisa de você · ${pendencias - 1}` })).toBeVisible();
        await capturar(page, 'desktop-card-dispensado');
        await painel.getByRole('group', { name: 'Filtro das pendências' }).getByRole('button', { name: /Dispensadas/ }).click();
        await expect(painel.getByRole('button', { name: 'Restaurar' })).toBeVisible();
        await capturar(page, 'desktop-filtro-dispensadas');
        const decisoes = await (await request.get(`${API}/api/remoto/maquinas/${MAQUINA}/decisoes`, { headers })).json() as Array<{ pendencia: { titulo: string } }>;
        expect(decisoes.map(d => d.pendencia.titulo)).not.toContain('Disparo do devcli');
        await painel.getByRole('button', { name: 'Restaurar' }).click();
        await painel.getByRole('group', { name: 'Filtro das pendências' }).getByRole('button', { name: 'Pendentes' }).click();
        await expect(painel.getByRole('heading', { name: `Precisa de você · ${pendencias}` })).toBeVisible();
        return `De ${pendencias} para ${pendencias - 1} pendências; a API deixou de listar o card dispensado; Restaurar devolveu as ${pendencias}.`;
      });

    await etapa(page, '2 · miniatura do design e captura do snapshot', TELA_DESKTOP, ['Ver o card de Design do painel', 'Abrir "Decidir" na aprovação do item 4'],
      'O card de Design mostra a miniatura da última versão; a revisão da aprovação mostra a captura do snapshot.', async () => {
        await expect(painel.getByRole('button', { name: 'Ampliar github.com/empresa-demo/dev.kit.page · última versão' }).getByRole('img')).toBeVisible({ timeout: 20_000 });
        await painel.getByRole('button', { name: 'Ampliar github.com/empresa-demo/dev.kit.page · última versão' }).scrollIntoViewIfNeeded();
        await capturar(page, 'desktop-miniatura-do-design');
        await painel.getByRole('region', { name: 'Projeto controle-v4 · item 4' }).getByRole('button', { name: 'Decidir' }).click();
        await expect(page.getByRole('img', { name: 'chat-remoto · v016 proposta' })).toBeVisible({ timeout: 20_000 });
        await capturar(page, 'desktop-snapshot-com-captura');
        await page.getByRole('button', { name: '‹ Voltar ao Board' }).click();
        return 'Miniatura do design visível no painel; a revisão da aprovação carregou a captura chat-remoto · v016 da API de design.';
      });

    await etapa(page, '8 · projeto em fechamento e Mostrar concluídos', TELA_DESKTOP, ['Conferir os projetos do painel', 'Marcar "Mostrar concluídos"'],
      'Só o projeto em andamento aparece; o interruptor traz o em fechamento ("todos os itens integrados") e o concluído.', async () => {
        await expect(painel.getByRole('link', { name: 'Abrir controle-v4' })).toBeVisible();
        await expect(painel.getByRole('link', { name: 'Abrir remote-control-v3' })).toHaveCount(0);
        await painel.getByRole('link', { name: 'Abrir controle-v4' }).scrollIntoViewIfNeeded();
        await capturar(page, 'desktop-projetos-em-andamento');
        await painel.getByRole('checkbox', { name: /Mostrar concluídos \(2\)/ }).check();
        await expect(painel.getByRole('link', { name: 'Abrir remote-control-v3' })).toContainText('em fechamento');
        await expect(painel.getByRole('link', { name: 'Abrir board-v5' })).toContainText('incorporado ao projeto');
        await painel.getByRole('link', { name: 'Abrir remote-control-v3' }).scrollIntoViewIfNeeded();
        await capturar(page, 'desktop-mostrar-concluidos');
        await painel.getByRole('checkbox', { name: /Mostrar concluídos/ }).uncheck();
        return 'Sem o filtro: só controle-v4. Com "Mostrar concluídos (2)": remote-control-v3 "em fechamento · todos os itens integrados" e board-v5 "incorporado ao projeto".';
      });

    await etapa(page, '7 · Executando sem gatilhos ociosos', TELA_DESKTOP, ['Rolar o painel até "Executando"'],
      'A lista traz só as execuções do retrato: o avaliador e o fechamento ociosos (sessões de gatilho sem turno) ficam de fora.', async () => {
        const titulo = painel.getByRole('heading', { name: /^Executando · / });
        await titulo.scrollIntoViewIfNeeded();
        await expect(titulo).toHaveText(new RegExp(`^Executando · ${retrato.fluxos.length}`));
        if (retrato.fluxos.length > 5) await painel.locator('h2:has-text("Executando") ~ button.br-mais').first().click();
        const lista = painel.locator('h2:has-text("Executando") + section');
        for (const fluxo of retrato.fluxos) await expect(lista.getByText(fluxo.nome, { exact: true })).toBeVisible();
        await expect(painel.getByText(/ocioso/i)).toHaveCount(0);
        await lista.evaluate(e => e.scrollIntoView({ block: 'center' }));
        await capturar(page, 'desktop-executando');
        return `Executando · ${retrato.fluxos.length}: ${retrato.fluxos.map(f => f.nome).join(' | ')}; nenhuma sessão "ociosa" na tela.`;
      });

    await etapa(page, '9 · cartão autorizar com TOTP', TELA_DESKTOP,
      ['Abrir "Autorizar" no card do comando bloqueado #821', 'Conferir o escopo', 'Autorizar com segundo fator e informar o código do autenticador', 'Confirmar e autorizar'],
      'O escopo mostra o ato e o comando exato; a autorização exige o código TOTP e fica registrada com o dispositivo "navegador".', async () => {
        const cartao = painel.getByRole('region', { name: '#821 Correções do board remoto' });
        await cartao.scrollIntoViewIfNeeded();
        await capturar(page, 'desktop-cartao-autorizar');
        await cartao.getByRole('button', { name: 'Autorizar' }).click();
        const escopo = page.getByLabel('Escopo da autorização');
        await expect(escopo).toContainText('Executar o comando');
        await expect(escopo).toContainText('dotnet test tests/GitKit.Core.Tests/GitKit.Core.Tests.csproj');
        await page.getByRole('button', { name: 'Autorizar com segundo fator' }).click();
        await proximoPasso();
        await page.getByLabel('Código de 6 dígitos').fill(totp(chave));
        await capturar(page, 'desktop-autorizar-escopo-e-codigo');
        await page.getByRole('button', { name: 'Confirmar e autorizar' }).click();
        await expect(page.getByText(/segundo fator confirmado · pelo navegador/).first()).toBeVisible();
        await capturar(page, 'desktop-autorizado-pelo-navegador');
        await page.getByRole('button', { name: '‹ Voltar ao Board' }).click();
        return 'Escopo "Executar o comando" com dotnet test …GitKit.Core.Tests.csproj; autorizado com o código de 6 dígitos: "segundo fator confirmado · pelo navegador".';
      });

    let primeiroHash = '';
    await etapa(page, '10 · Gerar artefato (download e preview)', TELA_DESKTOP,
      ['Clicar em "Gerar artefato" no card do controle-v4', 'A máquina recebe o comando, envia o zip do pacote .NET e registra o preview', 'Conferir os links do card'],
      'O pedido vai pela fila; o desfecho volta "concluído"; o card mostra "Baixar" (pacote .NET com prazo) e "Abrir preview" (ambiente controle-v4, nunca produção).', async () => {
        const artefatos = painel.getByRole('group', { name: 'Artefatos de controle-v4' });
        await artefatos.scrollIntoViewIfNeeded();
        await artefatos.getByRole('button', { name: 'Gerar artefato' }).click();
        await expect(painel.getByRole('status', { name: 'Desfecho da criação' })).toBeVisible();
        await capturar(page, 'desktop-gerar-artefato-na-fila');
        const comando = await atender(request, headers, ehArtefato, async () => {
          primeiroHash = (await gerarArtefatos(request, headers, projetoId, 1, true)).hash;
          return `Artefato gerado pelo executor do dev.kit: pacote .NET de git.kit no ${SHA.slice(0, 7)} (zip conferido) e preview de dev.kit.page no ambiente controle-v4. Nunca em produção.`;
        });
        await expect(painel.getByRole('status', { name: 'Desfecho da criação' })).toContainText('concluído', { timeout: 20_000 });
        await expect(artefatos.getByRole('link', { name: /^Baixar o pacote de git\.kit/ })).toBeVisible({ timeout: 70_000 });
        await expect(artefatos.getByRole('link', { name: /^Abrir o preview de dev\.kit\.page/ })).toHaveAttribute('href', PREVIEW);
        // O pacote pequeno sai em bytes, e não "0 MB" (o defeito que esta evidência achou, corrigido no item 5).
        await expect(artefatos.getByRole('link', { name: /^Baixar o pacote de git\.kit/ })).toContainText(/\d+ bytes · expira em/);
        await capturar(page, 'desktop-artefato-links');
        return `Comando ${comando.tipo} ${comando.texto} (dispositivo ${comando.dispositivo}) concluído; o card mostra "${(await artefatos.getByRole('link').allInnerTexts()).map(t => t.replace(/\s+/g, ' ').trim()).join(' | ')}".`;
      });

    await etapa(page, '10 · o link de download baixa o zip', TELA_DESKTOP, ['Ler o href de "Baixar"', 'Baixar sem login e conferir o SHA-256'],
      'O link assinado e com prazo baixa sem login o MESMO zip enviado (SHA-256 igual).', async () => {
        const link = await painel.getByRole('group', { name: 'Artefatos de controle-v4' }).getByRole('link', { name: /^Baixar o pacote de git\.kit/ }).getAttribute('href');
        const baixado = await request.get(link!);
        expect(baixado.status()).toBe(200);
        const corpo = await baixado.body();
        const conferido = sha256(corpo);
        expect(conferido).toBe(primeiroHash);
        const nome = /filename="?([^";]+)/.exec(baixado.headers()['content-disposition'] ?? '')?.[1] ?? '';
        const destino = new URL(link!);
        const linkCurto = `${destino.origin}${destino.pathname.slice(0, 40)}…`;
        await page.setContent(`<meta charset="utf-8"><body style="font:15px/1.5 Segoe UI,sans-serif;margin:32px;background:#fff;color:#1b1f24">
          <h2>Download do artefato pelo link do card (sem login)</h2><dl>
          <dt>Link</dt><dd><code>${linkCurto}</code> (token cifrado com prazo, sem a pasta do armazém nem chave)</dd>
          <dt>HTTP</dt><dd>${baixado.status()} · ${baixado.headers()['content-type']}</dd><dt>Arquivo</dt><dd>${nome} · ${corpo.length} bytes</dd>
          <dt>SHA-256 enviado pela máquina</dt><dd><code>${primeiroHash}</code></dd><dt>SHA-256 do baixado</dt><dd><code>${conferido}</code> ${conferido === primeiroHash ? '✓ igual' : '✗ diferente'}</dd>
          <dt>Preview</dt><dd><code>${PREVIEW}</code> (ambiente controle-v4)</dd></dl></body>`);
        await capturar(page, 'desktop-download-conferido');
        await page.goto('/board');
        return `HTTP 200, ${nome}, ${corpo.length} bytes, SHA-256 igual ao enviado (${conferido.slice(0, 12)}…).`;
      });

    // ---------------- Celular 390x844 ----------------
    await page.setViewportSize(CELULAR);
    await etapa(page, '5 · layout v016 no celular', TELA_CELULAR, ['Abrir /board?aba=chat'],
      'O selo "decisões auditadas", o ↻ no cabeçalho e a miniatura na conversa, sem transbordo.', async () => {
        await page.goto('/board?aba=chat');
        await expect(chat.getByText('Chat não aprova · decisões auditadas')).toBeVisible({ timeout: 25_000 });
        await expect(chat.getByRole('button', { name: 'Atualizar catálogo do dev.kit' })).toBeVisible();
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
        await capturar(page, 'celular-chat');
        return 'Selo, ↻ e conversa visíveis; largura sem transbordo (scrollWidth ≤ 390).';
      });

    await etapa(page, '2 · modal da imagem em tela cheia', TELA_CELULAR, ['Tocar na miniatura da v016'],
      'A imagem abre no modal ocupando a tela.', async () => {
        await chat.getByRole('button', { name: 'Ampliar chat-remoto v016 · Proposta' }).click();
        const modal = page.getByRole('dialog', { name: 'chat-remoto v016 · Proposta' });
        await expect(modal.getByRole('img')).toBeVisible();
        await capturar(page, 'celular-imagem-no-modal');
        await modal.getByRole('button', { name: 'Fechar' }).click();
        return 'Modal aberto com a imagem da v016 e fechado pelo botão Fechar.';
      });

    await etapa(page, '6 · seletores no celular', TELA_CELULAR, ['Abrir "Trocar fluxo", "# Work item" e "@ Repositório"', 'Digitar "/" na caixa vazia'],
      'Cada seletor abre como folha fixa no alto, à vista e sem transbordo; o "/" digitado abre a lista de fluxos.', async () => {
        const listas = [
          ['Trocar fluxo', 'Do catálogo do dev.kit: fluxo, projeto ou design', 'celular-seletor-fluxos'],
          ['# Work item', 'Do catálogo do dev.kit: work item em andamento', 'celular-seletor-work-items'],
          ['@ Repositório', 'Do catálogo do dev.kit: repositório', 'celular-seletor-repositorios'],
        ] as const;
        const contagens: string[] = [];
        for (const [botao, grupo, arquivo] of listas) {
          await chat.getByRole('button', { name: botao }).click();
          const lista = page.getByRole('group', { name: grupo });
          await expect(lista.getByRole('button').first()).toBeInViewport();
          expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
          contagens.push(`${botao}: ${await lista.getByRole('button').count()} itens`);
          await capturar(page, arquivo);
          await page.getByRole('button', { name: 'Cancelar' }).click();
        }
        await chat.getByLabel('Mensagem ao Orquestrador').fill('/');
        await expect(page.getByRole('group', { name: 'Do catálogo do dev.kit: fluxo, projeto ou design' })).toBeInViewport();
        await capturar(page, 'celular-barra-abre-seletor');
        await page.getByRole('button', { name: 'Cancelar' }).click();
        return `${contagens.join('; ')}; o "/" digitado abriu a lista de fluxos.`;
      });

    await etapa(page, '3 · chips de modelo e parâmetros no celular', TELA_CELULAR, ['Trocar fluxo → "Implementar com revisor"', 'Abrir o parâmetro Revisor'],
      'Os chips do modelo e dos parâmetros aparecem no compositor do celular, e o Revisor abre a busca nos membros.', async () => {
        await chat.getByLabel('Mensagem ao Orquestrador').fill('');
        await chat.getByRole('button', { name: 'Trocar fluxo' }).click();
        await page.getByRole('button', { name: /^Implementar com revisor/ }).click();
        await expect(chat.getByRole('button', { name: 'Modelo do fluxo' })).toBeVisible();
        await capturar(page, 'celular-chips-do-fluxo');
        await chat.getByRole('button', { name: 'Parâmetro Revisor' }).click();
        await expect(page.getByRole('group', { name: 'Membros do time' })).toBeVisible();
        await capturar(page, 'celular-seletor-de-usuario');
        await page.getByRole('group', { name: 'Membros do time' }).getByRole('button', { name: 'Pessoa Revisora' }).click();
        return `Chip do modelo "${(await chat.getByRole('button', { name: 'Modelo do fluxo' }).innerText()).trim()}" e Revisor escolhido no celular.`;
      });

    await etapa(page, '4 · repositório só pelo nome no celular', TELA_CELULAR, ['Abrir "@ Repositório"'],
      'A folha mostra os repositórios pelo nome, com o dono só nos homônimos.', async () => {
        await chat.getByRole('button', { name: /^@ / }).click();
        const repositorios = page.getByRole('group', { name: 'Do catálogo do dev.kit: repositório' });
        await expect(repositorios.getByRole('button')).toHaveText([/^dev\.kit\.page/, /^git\.kit empresa-demo/, /^git\.kit outra-org/, /^portal/]);
        await capturar(page, 'celular-repositorio-pelo-nome');
        await page.getByRole('button', { name: 'Cancelar' }).click();
        return 'dev.kit.page | git.kit empresa-demo | git.kit outra-org | portal.';
      });

    await etapa(page, '5 · caixa de 3 a 8 linhas no celular', TELA_CELULAR, ['Digitar 10 linhas na caixa'],
      'A caixa cresce até 8 linhas e as mensagens ficam com o resto da altura.', async () => {
        const caixa = chat.getByLabel('Mensagem ao Orquestrador');
        const inicial = (await caixa.boundingBox())!.height;
        await caixa.fill(Array.from({ length: 10 }, (_, i) => `Linha ${i + 1}`).join('\n'));
        const cheia = (await caixa.boundingBox())!.height;
        expect(cheia).toBeGreaterThan(inicial * 2);
        await capturar(page, 'celular-caixa-cresce');
        await caixa.fill('');
        return `Altura ${Math.round(inicial)} px → ${Math.round(cheia)} px.`;
      });

    await etapa(page, '1 · × no card e filtro Dispensadas no celular', TELA_CELULAR, ['Abrir /board?aba=board', 'Tocar no × do card "Disparo do devcli"', 'Abrir "Dispensadas"', 'Restaurar'],
      'O × fecha o card no celular, e o filtro Dispensadas o devolve.', async () => {
        await page.goto('/board?aba=board');
        await expect(painel.getByRole('heading', { name: `Precisa de você · ${pendencias}` })).toBeVisible({ timeout: 25_000 });
        await capturar(page, 'celular-board-com-fechar');
        await painel.getByRole('button', { name: 'Dispensar Disparo do devcli' }).click();
        await expect(painel.getByRole('heading', { name: `Precisa de você · ${pendencias - 1}` })).toBeVisible();
        await painel.getByRole('group', { name: 'Filtro das pendências' }).getByRole('button', { name: /Dispensadas/ }).click();
        await expect(painel.getByRole('button', { name: 'Restaurar' })).toBeVisible();
        await capturar(page, 'celular-filtro-dispensadas');
        await painel.getByRole('button', { name: 'Restaurar' }).click();
        await painel.getByRole('group', { name: 'Filtro das pendências' }).getByRole('button', { name: 'Pendentes' }).click();
        await expect(painel.getByRole('heading', { name: `Precisa de você · ${pendencias}` })).toBeVisible();
        return `${pendencias} → ${pendencias - 1} com o ×; Restaurar devolveu as ${pendencias}.`;
      });

    await etapa(page, '8 · Mostrar concluídos no celular', TELA_CELULAR, ['Marcar "Mostrar concluídos"'],
      'O em fechamento e o concluído aparecem só com o interruptor.', async () => {
        await expect(painel.getByRole('link', { name: 'Abrir remote-control-v3' })).toHaveCount(0);
        await painel.getByRole('checkbox', { name: /Mostrar concluídos \(2\)/ }).check();
        await painel.getByRole('link', { name: 'Abrir remote-control-v3' }).scrollIntoViewIfNeeded();
        await expect(painel.getByRole('link', { name: 'Abrir remote-control-v3' })).toContainText('em fechamento');
        await capturar(page, 'celular-mostrar-concluidos');
        await painel.getByRole('checkbox', { name: /Mostrar concluídos/ }).uncheck();
        return 'remote-control-v3 "em fechamento" e board-v5 visíveis só com o interruptor.';
      });

    await etapa(page, '7 · Executando no celular', TELA_CELULAR, ['Rolar até "Executando"'],
      'A mesma lista do retrato, sem as sessões de gatilho ociosas.', async () => {
        const titulo = painel.getByRole('heading', { name: /^Executando · / });
        await titulo.scrollIntoViewIfNeeded();
        await expect(titulo).toHaveText(new RegExp(`^Executando · ${retrato.fluxos.length}`));
        await expect(painel.getByText(/ocioso/i)).toHaveCount(0);
        await painel.locator('h2:has-text("Executando") + section').evaluate(e => e.scrollIntoView({ block: 'center' }));
        await capturar(page, 'celular-executando');
        return `Executando · ${retrato.fluxos.length}, sem sessão ociosa.`;
      });

    await etapa(page, '9 · autorizar com TOTP no celular', TELA_CELULAR,
      ['Abrir "Autorizar" no card "Arquivar 2 task(s) pedidas pelo Orquestrador"', 'Conferir o escopo', 'Informar o código do autenticador e confirmar'],
      'O escopo mostra as tasks e o comando exatos; a autorização exige o TOTP e fica registrada com o dispositivo "celular".', async () => {
        const cartao = painel.getByRole('region', { name: 'Arquivar 2 task(s) pedidas pelo Orquestrador' });
        await cartao.scrollIntoViewIfNeeded();
        await capturar(page, 'celular-cartao-arquivar');
        await cartao.getByRole('button', { name: 'Autorizar' }).click();
        const escopo = page.getByLabel('Escopo da autorização');
        await expect(escopo).toContainText('Arquivar as tasks');
        await expect(escopo).toContainText('#901, #902');
        await page.getByRole('button', { name: 'Autorizar com segundo fator' }).click();
        await proximoPasso();
        await page.getByLabel('Código de 6 dígitos').fill(totp(chave));
        await capturar(page, 'celular-autorizar-escopo-e-codigo', true);
        await page.getByRole('button', { name: 'Confirmar e autorizar' }).click();
        await expect(page.getByText(/segundo fator confirmado · pelo celular/).first()).toBeVisible();
        await capturar(page, 'celular-autorizado-pelo-celular', true);
        await page.getByRole('button', { name: '‹ Voltar ao Board' }).click();
        const respostas = await (await request.get(`${API}/api/remoto/maquinas/${MAQUINA}/decisoes/respostas`, { headers })).json() as Array<{ acao: string; segundoFator: boolean; dispositivo: string }>;
        expect(respostas.map(r => [r.acao, r.segundoFator, r.dispositivo])).toEqual([['autorizar', true, 'navegador'], ['autorizar', true, 'celular']]);
        return `Caixa de saída que a máquina lê: ${respostas.map(r => `${r.acao}/${r.segundoFator ? '2FA' : 'sem 2FA'}/${r.dispositivo}`).join(', ')}.`;
      });

    await etapa(page, '10 · Gerar artefato no celular', TELA_CELULAR, ['Tocar em "Gerar artefato" no card do controle-v4', 'A máquina envia outro pacote', 'Conferir os links'],
      'O pedido sai do celular, o desfecho volta concluído, e o card lista o pacote novo e o preview.', async () => {
        const artefatos = painel.getByRole('group', { name: 'Artefatos de controle-v4' });
        await artefatos.scrollIntoViewIfNeeded();
        await artefatos.getByRole('button', { name: 'Gerar artefato' }).click();
        const comando = await atender(request, headers, ehArtefato, async () => {
          await gerarArtefatos(request, headers, projetoId, 2, false);
          return `Artefato gerado pelo executor do dev.kit: pacote .NET de git.kit no ${SHA.slice(0, 7)} (zip conferido). Nunca em produção.`;
        });
        await expect(painel.getByRole('status', { name: 'Desfecho da criação' })).toContainText('concluído', { timeout: 20_000 });
        await expect(artefatos.getByRole('link', { name: /^Baixar o pacote de git\.kit/ })).toHaveCount(2, { timeout: 70_000 });
        await artefatos.scrollIntoViewIfNeeded();
        await capturar(page, 'celular-artefato-links');
        return `Comando do dispositivo "${comando.dispositivo}" concluído; o card lista ${await artefatos.getByRole('link').count()} links (2 pacotes e o preview).`;
      });
  } finally {
    writeFileSync(`${pasta}/roteiro.json`, JSON.stringify({ usuario: LOGIN, ...contexto, roteiro }, null, 2), 'utf8');
    writeFileSync(`${pasta}/roteiro-evidencias-controle-v4.html`, transcricao(contexto), 'utf8');
  }
});
