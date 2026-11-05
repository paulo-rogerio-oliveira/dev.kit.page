import { expect, test, type APIRequestContext, type Browser, type Page } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA, PORTA_DA_WEB } from './ambiente';

const WEB = `http://localhost:${PORTA_DA_WEB}`;

/** Troca a senha inicial (ou temporária) pela definitiva e devolve o token dela. */
async function definitiva(request: APIRequestContext, login: string, senha: string) {
  const inicial = (await (await request.post(`${API}/api/auth/login`, { data: { login, senha } })).json()) as { token: string };
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    data: { senhaAtual: senha, novaSenha: NOVA_SENHA }, headers: { Authorization: `Bearer ${inicial.token}` },
  });
  expect(troca.ok()).toBeTruthy();
  return ((await troca.json()) as { token: string }).token;
}

/** Cada usuário no seu navegador (contexto próprio): a sessão de um não passa para o outro. */
async function entrar(browser: Browser, login: string): Promise<Page> {
  const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
  await page.goto(`${WEB}/login`);
  await page.getByLabel('Login').fill(login);
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/(dashboard|conta)$/);
  return page;
}

/** A captura só depois das imagens das duas versões (o blob baixado com o token): sem isso, sai a moldura vazia. */
async function capturar(page: Page, arquivo: string) {
  const comparacao = page.getByRole('region', { name: 'Versões de Checkout' });
  await expect(comparacao.getByRole('img', { name: 'v001 captura (720x450)', exact: true })).toBeVisible();
  await expect(comparacao.getByRole('img', { name: 'v002 proposta (720x450)', exact: true })).toBeVisible();
  await page.screenshot({ path: `test-results/capturas/${arquivo}`, fullPage: true });
}

/** Uma tela de demonstração fotografada pelo próprio navegador: nenhum PNG entra no repositório. */
async function tela(browser: Browser, cor: string): Promise<Buffer> {
  const pagina = await browser.newPage({ viewport: { width: 720, height: 450 } });
  await pagina.setContent(`<body style="margin:0;font-family:sans-serif;background:#f4f4f6"><div style="height:40px;background:#2a2a30"></div>
    <div style="margin:28px 32px"><h2 style="margin:0 0 16px;color:#222">Checkout</h2>
    <button style="padding:14px 48px;border:0;border-radius:6px;font-size:16px;color:#fff;background:${cor}">Pagar</button></div></body>`);
  const png = await pagina.screenshot();
  await pagina.close();
  return png;
}

/**
 * O ACESSO ao projeto de design no navegador, com as capturas da entrega: o gestor envia as versões (fica DONO), a
 * colega sem acesso tem o envio recusado com o motivo, o dono dá a ela o papel editor no painel Acesso, ela passa a ver
 * e a enviar, e o dono a remove com confirmação. A "base" é a mesma tela com o painel novo oculto.
 */
test('o dono dá e tira acesso no painel Acesso, e o envio sem papel é recusado com o motivo', async ({ browser, request }) => {
  // 1. A empresa, o gestor (o dono) e a dev.
  await garantirSenhaDefinitiva(request);
  const admin = { Authorization: `Bearer ${((await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json()) as { token: string }).token}` };
  const sufixo = Date.now();
  const empresa = (await (await request.post(`${API}/api/empresas`, { data: { nome: `Empresa Acesso ${sufixo}`, plano: 'Empresarial', assentos: 5 }, headers: admin })).json()) as { id: number };
  const gestor = `gestor.acesso.${sufixo}`;
  const convite = (await (await request.post(`${API}/api/empresas/${empresa.id}/gestores`, { data: { login: gestor }, headers: admin })).json()) as { senhaInicial: string };
  const doGestor = { Authorization: `Bearer ${await definitiva(request, gestor, convite.senhaInicial)}` };
  const dev = `bia.acesso.${sufixo}`;
  const criado = (await (await request.post(`${API}/api/usuarios`, { data: { login: dev, nome: 'Bia Lima', papel: 'dev', empresaId: empresa.id }, headers: admin })).json()) as { senhaTemporaria: string };
  const daDev = { Authorization: `Bearer ${await definitiva(request, dev, criado.senhaTemporaria)}` };

  const enviar = (headers: Record<string, string>, numero: number, tipo: string, png: Buffer, base?: number) => request.post(`${API}/api/design/versoes`, {
    headers,
    multipart: {
      repositorio: 'https://github.com/acme/portal', tela: 'checkout', nome: 'Checkout', numero: String(numero), tipo,
      origem: tipo === 'proposta' ? 'proposta' : 'web', alvo: 'http://localhost:5173/checkout', tema: 'light', branch: 'feature/dev/semana7',
      commit: '3f9c2a1', autorTipo: 'agente', runner: 'claude', ...(base ? { base: String(base) } : {}),
      imagem: { name: 'checkout.png', mimeType: 'image/png', buffer: png },
    },
  });
  expect((await enviar(doGestor, 1, 'captura', await tela(browser, '#9a9aa2'))).status()).toBe(201);
  expect((await enviar(doGestor, 2, 'proposta', await tela(browser, '#e2261c'), 1)).status()).toBe(201);

  // 2. A dev sem acesso: não vê o projeto, e o envio volta 403 com o motivo (o usuário, o projeto e o papel exigido).
  expect(await (await request.get(`${API}/api/design/projetos`, { headers: daDev })).json()).toEqual([]);
  const recusa = await enviar(daDev, 3, 'captura', await tela(browser, '#2a7de1'), 2);
  expect(recusa.status()).toBe(403);
  const motivo = ((await recusa.json()) as { detail: string }).detail;
  expect(motivo).toContain(`O usuário ${dev} não pode enviar versões ao projeto de design https://github.com/acme/portal`);
  expect(motivo).toContain('exige o papel editor');

  // 3. O dono abre a aba Design: a base (o painel novo oculto) e o painel Acesso.
  const doDono = await entrar(browser, gestor);
  await doDono.goto(`${WEB}/empresa/conhecimento?aba=design`);
  const painel = doDono.getByRole('region', { name: 'Acesso' });
  await expect(painel.getByText(/Seu papel:/)).toContainText('Dono');
  await expect(doDono.getByRole('region', { name: 'Versões de Checkout' }).getByRole('article')).toHaveCount(2);
  const oculto = await doDono.addStyleTag({ content: '.design-acesso { display: none !important; }' });
  await capturar(doDono, '95-design-acesso-base.png');
  await oculto.evaluate((estilo) => estilo.parentNode?.removeChild(estilo));
  await expect(painel.getByText('Ninguém além do dono tem acesso a este projeto.')).toBeVisible();
  await capturar(doDono, '96-design-acesso-dono.png');

  // 4. Dar acesso: a dev como editora.
  await painel.getByLabel('Usuário da empresa').selectOption(dev);
  await painel.getByLabel('Papel do acesso').selectOption('editor');
  await painel.getByRole('button', { name: 'Dar acesso' }).click();
  await expect(painel.getByText(`Bia Lima (${dev}) agora é editor do projeto.`)).toBeVisible();
  await expect(painel.getByLabel(`Papel de ${dev}`)).toHaveValue('editor');
  await capturar(doDono, '97-design-acesso-concedido.png');

  // 5. A editora vê o projeto, sem os controles do dono, e o envio dela passa.
  const daEditora = await entrar(browser, dev);
  await daEditora.goto(`${WEB}/empresa/conhecimento?aba=design`);
  const painelDela = daEditora.getByRole('region', { name: 'Acesso' });
  await expect(painelDela.getByText(/Seu papel:/)).toContainText('Editor');
  await expect(painelDela.getByRole('form', { name: 'Dar acesso' })).toHaveCount(0);
  await capturar(daEditora, '98-design-acesso-editora.png');
  expect((await enviar(daDev, 3, 'captura', await tela(browser, '#2a7de1'), 2)).status()).toBe(201);

  // 6. O dono remove, com confirmação: ela deixa de ver o projeto.
  doDono.once('dialog', (dialogo) => void dialogo.accept());
  await painel.getByRole('button', { name: `Remover ${dev}` }).click();
  await expect(painel.getByText(`Bia Lima (${dev}) não tem mais acesso ao projeto.`)).toBeVisible();
  await capturar(doDono, '99-design-acesso-removido.png');
  expect(await (await request.get(`${API}/api/design/projetos`, { headers: daDev })).json()).toEqual([]);
});
