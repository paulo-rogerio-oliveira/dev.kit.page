import { expect, test } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { NOVA_SENHA } from './ambiente';

/** As seções da landing, na ordem da lista SECOES de conteudo/landing.ts (a proposta da US #405, com as da US #283 e da US #381). */
const ORDEM = ['inicio', 'como-funciona', 'beneficios', 'recursos', 'comparativo', 'integracoes', 'seguranca', 'empresas', 'contato', 'faq', 'comecar'];

test('landing → seções e vídeo do hero → pedido de demonstração → o admin vê e exclui no dashboard', async ({ page, request }) => {
  const nome = `Visitante E2E ${Date.now()}`;

  // 1. A landing de venda, na ordem do critério 1, com o vídeo do hero.
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('agente de IA');
  await expect(page.locator('main > section')).toHaveCount(ORDEM.length);
  expect(await page.locator('main > section').evaluateAll((secoes) => secoes.map((s) => s.id))).toEqual(ORDEM);
  // (US #405) O hero da proposta: o selo, a amostra do Board com a nota; o vídeo do fluxo foi para "Como funciona".
  await expect(page.locator('#inicio')).toContainText('Para times no Azure DevOps');
  await expect(page.getByRole('img', { name: /Amostra do Board/ })).toBeVisible();
  const videoDoFluxo = page.locator('#como-funciona video');
  await expect(videoDoFluxo).toHaveAttribute('poster', '/midia/hero.jpg');
  await videoDoFluxo.scrollIntoViewIfNeeded();
  await expect(videoDoFluxo.locator('source[type="video/webm"]')).toHaveAttribute('src', '/midia/hero.webm');
  await expect(page.locator('#como-funciona ol > li')).toHaveCount(7);
  await expect(page.locator('#recursos video')).toHaveCount(7);
  await expect(page.locator('body')).not.toContainText(/dashboard/i);
  // A API do e2e não tem REPO_KEY: o botão de download diz que está indisponível, sem quebrar a página.
  await expect(page.getByRole('group', { name: 'Download do dev.kit' })).toContainText('Download indisponível no momento.');
  await page.screenshot({ path: 'test-results/capturas/10-landing.png', fullPage: true });

  // 1b. (US #381) O rodapé chega ao comparativo e às empresas.
  await page.getByRole('navigation', { name: 'Rodapé' }).getByRole('link', { name: 'Comparativo' }).click();
  await expect(page).toHaveURL(/#comparativo$/);
  await expect(page.locator('#comparativo table')).toBeVisible();
  await page.locator('#comparativo').screenshot({ path: 'test-results/capturas/13-comparativo.png' });
  await page.getByRole('navigation', { name: 'Rodapé' }).getByRole('link', { name: 'Empresas' }).click();
  await expect(page).toHaveURL(/#empresas$/);
  await page.locator('#empresas').screenshot({ path: 'test-results/capturas/14-para-empresas.png' });
  await page.locator('#como-funciona').screenshot({ path: 'test-results/capturas/15-como-funciona.png' });
  await page.goto('/');

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
