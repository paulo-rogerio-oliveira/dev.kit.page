import { expect, test, type APIRequestContext, type Browser, type Page } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA, PORTA_DA_WEB } from './ambiente';

const WEB = `http://localhost:${PORTA_DA_WEB}`;

/** O texto do caso real que motivou a US #602. */
const CASO_REAL = 'A PR da task multirepo foi criada no repositório errado (dev.kit.page em vez de git.kit)';

/** Um PNG de 1x1 (a captura do relato). */
const CAPTURA_PNG = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';

const bearer = (token: string) => ({ Authorization: `Bearer ${token}` });

async function entrarPelaApi(request: APIRequestContext, login: string, senha: string) {
  const resposta = await request.post(`${API}/api/auth/login`, { data: { login, senha } });
  expect(resposta.ok()).toBeTruthy();
  return ((await resposta.json()) as { token: string }).token;
}

/** Troca a senha inicial (ou temporária) pela definitiva e devolve o token dela. */
async function definitiva(request: APIRequestContext, login: string, senha: string) {
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    data: { senhaAtual: senha, novaSenha: NOVA_SENHA }, headers: bearer(await entrarPelaApi(request, login, senha)),
  });
  expect(troca.ok()).toBeTruthy();
  return ((await troca.json()) as { token: string }).token;
}

/**
 * O dev ADERE à empresa pelo código (como no /aderir). O link de definir a senha vai por e-mail — no e2e, para o log —,
 * então o admin gera a senha temporária pela gestão de usuários, e o dev a troca pela definitiva. Devolve o token dele.
 */
async function devPorAdesao(request: APIRequestContext, admin: Record<string, string>, codigo: string, email: string, nome: string) {
  const adesao = await request.post(`${API}/api/conta/adesao`, { data: { codigo, email, nome } });
  expect(adesao.ok()).toBeTruthy();
  const usuarios = (await (await request.get(`${API}/api/usuarios`, { headers: admin })).json()) as { id: number; login: string }[];
  const usuario = usuarios.find((u) => u.login === email);
  expect(usuario).toBeDefined();
  const senha = await request.post(`${API}/api/usuarios/${usuario!.id}/senha`, { headers: admin });
  expect(senha.ok()).toBeTruthy();
  return definitiva(request, email, ((await senha.json()) as { senhaTemporaria: string }).senhaTemporaria);
}

/** A empresa nova, com o gestor de senha definitiva. */
async function empresaComGestor(request: APIRequestContext, admin: Record<string, string>, nome: string, gestor: string) {
  const empresa = (await (await request.post(`${API}/api/empresas`, { data: { nome, plano: 'Empresarial', assentos: 3 }, headers: admin })).json()) as {
    id: number; codigoDeAdesao: string;
  };
  const convite = await request.post(`${API}/api/empresas/${empresa.id}/gestores`, { data: { login: gestor }, headers: admin });
  expect(convite.ok()).toBeTruthy();
  await definitiva(request, gestor, ((await convite.json()) as { senhaInicial: string }).senhaInicial);
  return empresa;
}

/** Cada papel no seu navegador (contexto próprio): a sessão de um não passa para o outro. */
async function entrar(browser: Browser, login: string): Promise<Page> {
  const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
  await page.goto(`${WEB}/login`);
  await page.getByLabel('Login').fill(login);
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/(dashboard|conta)$/);
  return page;
}

/**
 * A US #602 no navegador, com as capturas da entrega: dois devs de duas empresas relatam pela API (o que o dev.kit e o
 * `devcli relatar` fazem); o admin geral vê os dois em /relatos, filtra, leva o do caso real a em análise e a resolvido e
 * responde; o gestor de cada empresa vê só o da dele; e o dev lê a resposta em GET /api/relatos/meus.
 */
test('devs relatam → o admin vê, filtra, muda o estado e responde → o gestor vê só o da empresa → o dev lê a resposta', async ({ browser, request }) => {
  // 1. Duas empresas com gestor, e um dev em cada uma, pela adesão por código.
  await garantirSenhaDefinitiva(request);
  const admin = bearer(await entrarPelaApi(request, 'admin', NOVA_SENHA));
  const sufixo = Date.now();
  const empresaA = `Empresa US602 A ${sufixo}`;
  const empresaB = `Empresa US602 B ${sufixo}`;
  const gestorA = `gestor.us602a.${sufixo}`;
  const a = await empresaComGestor(request, admin, empresaA, gestorA);
  const b = await empresaComGestor(request, admin, empresaB, `gestor.us602b.${sufixo}`);
  const devA = `ana.us602.${sufixo}@empresa-a.com`;
  const doDevA = bearer(await devPorAdesao(request, admin, a.codigoDeAdesao, devA, 'Ana US602'));
  const doDevB = bearer(await devPorAdesao(request, admin, b.codigoDeAdesao, `bia.us602.${sufixo}@empresa-b.com`, 'Bia US602'));

  // 2. Os relatos, como o dev.kit os manda: o defeito do caso real (com contexto e captura) e uma sugestão.
  const defeito = await request.post(`${API}/api/relatos`, {
    headers: doDevA,
    data: {
      categoria: 'defeito', texto: CASO_REAL, versaoDevKit: '137', chaveDoCliente: `us602-a-${sufixo}`, captura: CAPTURA_PNG,
      contexto: JSON.stringify({ versao: '137', task: 't_602', repositorios: ['git.kit', 'dev.kit.page'] }),
    },
  });
  expect(defeito.status()).toBe(201);
  const idDoDefeito = ((await defeito.json()) as { id: number; estado: string }).id;
  const sugestao = await request.post(`${API}/api/relatos`, {
    headers: doDevB, data: { categoria: 'sugestao', texto: `Mostrar o custo do turno no Board (${sufixo}).`, versaoDevKit: '137' },
  });
  expect(sugestao.status()).toBe(201);
  const idDaSugestao = ((await sugestao.json()) as { id: number }).id;
  // O reenvio da fila offline (a mesma chave) devolve o MESMO relato.
  const reenvio = await request.post(`${API}/api/relatos`, {
    headers: doDevA, data: { categoria: 'defeito', texto: CASO_REAL, chaveDoCliente: `us602-a-${sufixo}` },
  });
  expect(((await reenvio.json()) as { id: number }).id).toBe(idDoDefeito);
  // O dev não entra na gestão.
  expect((await request.get(`${API}/api/relatos`, { headers: doDevA })).status()).toBe(403);

  // 3. O admin geral vê os dois na tela.
  const paginaDoAdmin = await entrar(browser, 'admin');
  await paginaDoAdmin.getByRole('link', { name: 'Relatos' }).click();
  await expect(paginaDoAdmin.getByRole('heading', { name: 'Relatos', level: 1 })).toBeVisible();
  const lista = paginaDoAdmin.getByRole('table', { name: 'Relatos recebidos' });
  const linhaDoDefeito = lista.getByRole('row').filter({ has: paginaDoAdmin.getByRole('button', { name: `Abrir o relato ${idDoDefeito}:` }) });
  const linhaDaSugestao = lista.getByRole('row').filter({ has: paginaDoAdmin.getByRole('button', { name: `Abrir o relato ${idDaSugestao}:` }) });
  await expect(linhaDoDefeito).toContainText(empresaA);
  await expect(linhaDoDefeito).toContainText('Com captura');
  await expect(linhaDaSugestao).toContainText(empresaB);
  await paginaDoAdmin.screenshot({ path: 'test-results/capturas/80-relatos-lista-do-admin.png', fullPage: true });

  // 4. Filtra: a empresa B só traz a sugestão; a categoria Defeito na empresa A, o do caso real.
  await paginaDoAdmin.getByLabel('Filtrar por empresa').selectOption({ label: empresaB });
  await expect(linhaDaSugestao).toBeVisible();
  await expect(linhaDoDefeito).toHaveCount(0);
  await paginaDoAdmin.getByLabel('Filtrar por empresa').selectOption({ label: empresaA });
  await paginaDoAdmin.getByLabel('Filtrar por categoria').selectOption('defeito');
  await expect(linhaDoDefeito).toBeVisible();
  await expect(linhaDaSugestao).toHaveCount(0);
  await paginaDoAdmin.screenshot({ path: 'test-results/capturas/81-relatos-filtrados.png', fullPage: true });

  // 5. Abre o detalhe: o texto, o contexto e a captura; leva a em análise e a resolvido, e responde.
  await linhaDoDefeito.getByRole('button', { name: `Abrir o relato ${idDoDefeito}:` }).click();
  const detalhe = paginaDoAdmin.getByRole('article', { name: 'Detalhe do relato' });
  await expect(detalhe.getByText(CASO_REAL, { exact: true })).toBeVisible();
  await expect(detalhe.getByLabel('Contexto do relato')).toContainText('t_602');
  await expect(detalhe.getByRole('img', { name: `Captura de tela enviada com o relato ${idDoDefeito}` })).toBeVisible();
  await paginaDoAdmin.screenshot({ path: 'test-results/capturas/82-relato-detalhe.png', fullPage: true });

  const estados = detalhe.getByRole('group', { name: 'Estado do relato' });
  await estados.getByRole('button', { name: 'Em análise' }).click();
  await expect(paginaDoAdmin.getByText(`Relato ${idDoDefeito} marcado como em análise.`)).toBeVisible();
  await expect(linhaDoDefeito).toContainText('Em análise');
  await estados.getByRole('button', { name: 'Resolvido' }).click();
  await expect(paginaDoAdmin.getByText(`Relato ${idDoDefeito} marcado como resolvido.`)).toBeVisible();
  await expect(linhaDoDefeito).toContainText('Resolvido');

  const resposta = 'Corrigido: a PR da task multirepo agora vai para o repositório do item (git.kit).';
  await detalhe.getByLabel('Resposta').fill(resposta);
  await detalhe.getByRole('button', { name: 'Responder' }).click();
  await expect(detalhe.getByTestId('resposta-atual')).toContainText('Respondido por');
  await expect(detalhe.getByTestId('resposta-atual')).toContainText(resposta);
  await expect(linhaDoDefeito).toContainText('Respondido');
  await paginaDoAdmin.screenshot({ path: 'test-results/capturas/83-relato-respondido.png', fullPage: true });

  // 6. O gestor da empresa A vê só o da empresa dele, e sem o filtro de empresa.
  const paginaDoGestor = await entrar(browser, gestorA);
  await paginaDoGestor.getByRole('link', { name: 'Relatos' }).click();
  const listaDoGestor = paginaDoGestor.getByRole('table', { name: 'Relatos recebidos' });
  await expect(listaDoGestor.getByRole('button', { name: `Abrir o relato ${idDoDefeito}:` })).toBeVisible();
  await expect(listaDoGestor.getByRole('button', { name: `Abrir o relato ${idDaSugestao}:` })).toHaveCount(0);
  await expect(paginaDoGestor.getByLabel('Filtrar por empresa')).toHaveCount(0);
  await paginaDoGestor.screenshot({ path: 'test-results/capturas/84-relatos-do-gestor.png', fullPage: true });

  // 7. O dev lê a resposta nos relatos dele.
  const meus = (await (await request.get(`${API}/api/relatos/meus`, { headers: doDevA })).json()) as {
    id: number; estado: string; resposta: string; respondidoEm: string | null;
  }[];
  expect(meus).toHaveLength(1);
  expect(meus[0]).toMatchObject({ id: idDoDefeito, estado: 'resolvido', resposta });
  expect(meus[0].respondidoEm).not.toBeNull();
});
