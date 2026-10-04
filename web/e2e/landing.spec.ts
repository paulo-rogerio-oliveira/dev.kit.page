import { expect, test, type APIRequestContext } from '@playwright/test';
import { API, NOVA_SENHA, SENHA_INICIAL } from './ambiente';

/** As seções da landing, na ordem do critério 1 da US #283 (a lista SECOES de conteudo/landing.ts). */
const ORDEM = ['inicio', 'beneficios', 'como-funciona', 'recursos', 'integracoes', 'seguranca', 'contato', 'faq', 'comecar'];

/**
 * O admin com a senha definitiva. Os cenários dividem a mesma base: se o fluxo.spec.ts já trocou a
 * senha, ela entra; se este roda sozinho, a troca obrigatória é feita aqui pela API.
 */
async function garantirSenhaDefinitiva(request: APIRequestContext) {
  if ((await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).ok()) return;
  const inicial = await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: SENHA_INICIAL } });
  expect(inicial.ok()).toBeTruthy();
  const { token } = (await inicial.json()) as { token: string };
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    data: { senhaAtual: SENHA_INICIAL, novaSenha: NOVA_SENHA }, headers: { Authorization: `Bearer ${token}` },
  });
  expect(troca.ok()).toBeTruthy();
}

test('landing → seções e vídeo do hero → pedido de demonstração → o admin vê e exclui no dashboard', async ({ page, request }) => {
  const nome = `Visitante E2E ${Date.now()}`;

  // 1. A landing de venda, na ordem do critério 1, com o vídeo do hero.
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('agente de IA');
  await expect(page.locator('main > section')).toHaveCount(ORDEM.length);
  expect(await page.locator('main > section').evaluateAll((secoes) => secoes.map((s) => s.id))).toEqual(ORDEM);
  const videoDoHero = page.locator('#inicio video');
  await expect(videoDoHero).toHaveAttribute('poster', '/midia/hero.jpg');
  await expect(videoDoHero.locator('source[type="video/webm"]')).toHaveAttribute('src', '/midia/hero.webm');
  await expect(page.locator('#recursos video')).toHaveCount(7);
  await page.screenshot({ path: 'test-results/capturas/10-landing.png', fullPage: true });

  // 2. O CTA leva ao contato e o formulário valida antes de enviar.
  await page.locator('#inicio').getByRole('link', { name: 'Quero uma demonstração' }).click();
  await expect(page).toHaveURL(/#contato$/);
  const formulario = page.getByRole('form', { name: 'Pedido de demonstração' });
  await formulario.getByRole('button', { name: 'Quero uma demonstração' }).click();
  await expect(formulario.getByText('Informe o seu nome.')).toBeVisible();

  // 3. O pedido válido grava pela API pública.
  await formulario.getByLabel('Nome', { exact: true }).fill(nome);
  await formulario.getByLabel('E-mail', { exact: true }).fill('visitante.e2e@empresa.com.br');
  await formulario.getByLabel(/^Empresa/).fill('Empresa E2E');
  await formulario.getByLabel(/O que você quer ver/).fill('Quero ver o fluxo com avaliadores.');
  await formulario.getByRole('checkbox').check();
  await formulario.getByRole('button', { name: 'Quero uma demonstração' }).click();
  await expect(page.getByRole('heading', { name: 'Pedido recebido!' })).toBeVisible();
  await page.locator('#contato').screenshot({ path: 'test-results/capturas/11-pedido-enviado.png' });

  // 4. Logado como admin, o pedido aparece no dashboard e é excluído (eliminação a pedido do titular).
  await garantirSenhaDefinitiva(request);
  await page.getByRole('link', { name: 'Entrar' }).first().click();
  await page.getByLabel('Login').fill('admin');
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  const pedidos = page.getByRole('region', { name: 'Pedidos de demonstração' });
  await expect(pedidos.getByText(nome)).toBeVisible();
  await expect(pedidos.getByRole('link', { name: 'visitante.e2e@empresa.com.br' })).toBeVisible();
  await pedidos.screenshot({ path: 'test-results/capturas/12-pedidos-no-dashboard.png' });

  page.once('dialog', (dialogo) => void dialogo.accept());
  await pedidos.getByRole('button', { name: `Excluir o pedido de ${nome}` }).click();
  await expect(pedidos.getByText(nome)).toHaveCount(0);
});
