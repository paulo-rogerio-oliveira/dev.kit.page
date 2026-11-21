// Executar por devcli segredos --executar usuario_test,senha_test -- node <arquivo>.
// Autorização da task artefato/item 6, mensagem 516: uma autenticação e leituras.
// Credenciais, token e corpos de resposta permanecem somente na memória.
import { randomUUID } from 'node:crypto';

const api = 'https://devkit-f0dydyh8bwgudaey.centralus-01.azurewebsites.net';
const execucao = randomUUID();
const papeis = new Set(['admin', 'gestor', 'dev', 'dono', 'editor', 'leitor']);
const repositorios = new Map([
  ['https://github.com/paulo-rogerio-oliveira/git.kit', 'git.kit'],
  ['https://github.com/paulo-rogerio-oliveira/dev.kit.page', 'dev.kit.page'],
]);
let chamadas = 0;

function emitir(evento, dados = {}) {
  console.log(JSON.stringify({ execucao, utc: new Date().toISOString(), evento, ...dados }));
}

function exigir(condicao, codigo) {
  if (!condicao) throw new Error(codigo);
}

function papel(valor) { return papeis.has(valor) ? valor : null; }

function origem(valor) {
  if (typeof valor !== 'string') return null;
  const normalizado = valor.trim().replace(/^git@github\.com:/i, 'https://github.com/')
    .replace(/\.git\/?$/i, '').replace(/\/$/, '').toLowerCase();
  return repositorios.get(normalizado) ?? null;
}

function identificador(valor) {
  return typeof valor === 'string' && /^[a-zA-Z0-9,._:;= /-]{1,160}$/.test(valor) ? valor : null;
}

async function pedir(cenario, caminho, { token, corpo, projeto } = {}) {
  // Somente estas operações são permitidas pelo roteiro; não há escrita cadastral.
  const metodo = corpo === undefined ? 'GET' : 'POST';
  exigir(caminho.startsWith('/') && !caminho.startsWith('//'), 'caminho-invalido');
  exigir(metodo === 'GET' || caminho === '/api/auth/login', 'escrita-nao-autorizada');
  const inicio = new Date().toISOString();
  const numero = ++chamadas;
  let resposta;
  try {
    resposta = await fetch(api + caminho, {
      method: metodo,
      redirect: 'error',
      signal: AbortSignal.timeout(25_000),
      headers: {
        Accept: 'application/json',
        ...(corpo === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: corpo === undefined ? undefined : JSON.stringify(corpo),
    });
  } catch (erro) {
    const codigo = identificador(erro.cause?.code) ?? (erro.name === 'TimeoutError' ? 'timeout' : 'rede');
    emitir('http', { numero, cenario, metodo, caminho, projeto, inicio, http: null, erroDeTransporte: codigo });
    throw new Error('transporte-indisponivel');
  }
  // Não imprimir headers integrais (Set-Cookie, SAS ou dados de autenticação).
  emitir('http', { numero, cenario, metodo, caminho, projeto, inicio, http: resposta.status,
    dataDoServidor: identificador(resposta.headers.get('date')),
    correlacaoServidor: identificador(resposta.headers.get('x-ms-request-id'))
      ?? identificador(resposta.headers.get('x-request-id'))
      ?? identificador(resposta.headers.get('request-id')),
    tipo: identificador(resposta.headers.get('content-type')) });
  let dados = null;
  const texto = await resposta.text();
  if (texto.length <= 4_000_000) {
    try { dados = JSON.parse(texto); } catch { /* Texto não JSON é registrado apenas pelo status/tipo. */ }
  }
  return { http: resposta.status, dados };
}

async function main() {
  const usuario = process.env.usuario_test;
  const senha = process.env.senha_test;
  exigir(typeof usuario === 'string' && usuario.length > 0, 'variavel-usuario-ausente');
  exigir(typeof senha === 'string' && senha.length > 0, 'variavel-senha-ausente');
  // Nenhum segredo global de assinatura é lido; somente o login da conta autorizada.
  emitir('inicio', { api, ator: 'conta-de-teste', autorizacao: 'mensagem-516',
    escopo: 'autenticacao-e-leitura', shaDaApiPublicada: 'nao-confirmado' });

  const saude = await pedir('saude-publica', '/health');
  emitir('saude', { respondeu: true, http: saude.http });
  const anonimo = await pedir('conta-sem-sessao', '/api/conta');
  exigir(anonimo.http === 401 || anonimo.http === 403, 'conta-anonima-nao-recusada');

  // Uma única tentativa de login; não testar senha errada nem causar bloqueio.
  const login = await pedir('autenticacao-conta-existente', '/api/auth/login', { corpo: { login: usuario, senha } });
  if (login.http !== 200) {
    const codigo = login.dados?.codigo;
    emitir('login-recusado', { http: login.http,
      codigo: typeof codigo === 'string' && /^[a-z0-9-]{1,70}$/i.test(codigo) ? codigo : null });
    throw new Error('autenticacao-nao-confirmada');
  }
  let token = login.dados?.token;
  exigir(typeof token === 'string' && token.length > 0, 'resposta-sem-token');
  const pendencia = login.dados?.deveTrocarSenha === true;
  emitir('autenticacao', { ator: 'conta-de-teste', papel: papel(login.dados?.papel),
    empresaVinculada: typeof login.dados?.empresa === 'string' && login.dados.empresa.length > 0,
    trocaDeSenhaPendente: pendencia });
  // Não exportar token, não renovar sessão global e não trocar a senha cadastrada.
  try {
    exigir(!pendencia, 'troca-de-senha-pendente-nao-autorizada');
    const conta = await pedir('cadastro-autenticado', '/api/conta', { token });
    exigir(conta.http === 200 && conta.dados && typeof conta.dados === 'object', 'cadastro-nao-consultavel');
    const identidade = String(conta.dados.login ?? '').trim().toLowerCase();
    exigir(identidade === String(login.dados.login ?? '').trim().toLowerCase(), 'cadastro-nao-corresponde-ao-login');
    emitir('cadastro', { ator: 'conta-de-teste', identidadeConferida: true,
      papel: papel(conta.dados.papel), empresaVinculada: Boolean(conta.dados.empresa),
      individual: conta.dados.individual === true, assinaturaAtiva: conta.dados.assinaturaAtiva === true });

    const projetos = await pedir('projetos-visiveis', '/api/projetos', { token });
    exigir(projetos.http === 200 && Array.isArray(projetos.dados), 'listagem-de-projetos-nao-consultavel');
    const pertinentes = projetos.dados.filter(p => Array.isArray(p.repositorios) && p.repositorios.some(r => origem(r)));
    emitir('vinculos', { projetosVisiveis: projetos.dados.length, projetosDosProdutos: pertinentes.length,
      leituraLimitada: pertinentes.length > 30 });
    for (const p of pertinentes.slice(0, 30)) {
      exigir(Number.isSafeInteger(p.id) && p.id > 0, 'id-de-projeto-invalido');
      const produtos = [...new Set(p.repositorios.map(origem).filter(Boolean))];
      emitir('projeto', { id: p.id, produtos, versao: Number.isSafeInteger(p.versao) ? p.versao : null,
        papel: papel(p.papel), donoPeloContrato: p.podeApagar === true });
      const detalhe = await pedir('releitura-projeto', `/api/projetos/${p.id}`, { token, projeto: p.id });
      exigir(detalhe.http === 200 && detalhe.dados?.id === p.id, 'releitura-de-projeto-inconsistente');
      const acessos = await pedir('acl-efetiva', `/api/projetos/${p.id}/acessos`, { token, projeto: p.id });
      emitir('acl', { projeto: p.id, http: acessos.http, papelEfetivo: papel(acessos.dados?.papel),
        acessosExplicitos: Array.isArray(acessos.dados?.acessos) ? acessos.dados.acessos.length : null });
      const artefatos = await pedir('artefatos-visiveis', `/api/projetos/${p.id}/artefatos`, { token, projeto: p.id });
      emitir('artefatos', { projeto: p.id, http: artefatos.http,
        total: Array.isArray(artefatos.dados) ? artefatos.dados.length : null,
        pacotesDoProduto: Array.isArray(artefatos.dados) ? artefatos.dados.filter(a => a.tipo === 'pacote' && origem(a.repositorio) === 'git.kit').length : null });
      if (artefatos.http !== 200) emitir('pendencia', { codigo: 'consulta-de-artefatos-recusada', projeto: p.id, http: artefatos.http });
    }
    const semSessao = await pedir('projetos-sem-sessao', '/api/projetos');
    exigir(semSessao.http === 401 || semSessao.http === 403, 'projetos-anonimos-nao-recusados');
    emitir('resultado', { cadastro: 'consultado-com-conta-existente', projetosDosProdutos: pertinentes.length,
      novasContas: 0, senhasAlteradas: 0, projetosGravados: 0, publicacoes: 0,
      swa: 'validacao-a-cargo-do-desenvolvedor-aguardando-evidencia', aceiteE2E: 'pendente' });
  } finally {
    token = '';
    if (login.dados) login.dados.token = '';
  }
}

main().catch(erro => {
  const codigo = typeof erro.message === 'string' && /^[a-z-]{1,90}$/.test(erro.message)
    ? erro.message : 'falha-interna-na-validacao';
  emitir('falha', { codigo, aceiteE2E: 'pendente' });
  process.exitCode = 1;
});
