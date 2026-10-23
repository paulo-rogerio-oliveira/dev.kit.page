import { expect, test } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { NOVA_SENHA } from './ambiente';

/** As seções da landing, na ordem da lista SECOES de conteudo/landing.ts (a proposta da home da US #467). */
const ORDEM = [
  'inicio', 'como-funciona', 'recursos', 'beneficios', 'conceitos', 'fabrica', 'catalogo', 'comparativo', 'integracoes', 'seguranca', 'empresas',
  'planos', 'contato', 'faq', 'comecar',
];

/** A home inteira nas quatro combinações da entrega (US #467): desktop e celular, claro e escuro. */
test('a home da proposta (US #467) em desktop e celular, nos temas claro e escuro', async ({ page }) => {
  for (const [tela, largura, altura] of [['desktop', 1440, 900], ['celular', 390, 844]] as const) {
    for (const tema of ['light', 'dark'] as const) {
      await page.setViewportSize({ width: largura, height: altura });
      await page.emulateMedia({ colorScheme: tema, reducedMotion: 'reduce' });
      await page.goto('/');
      // Os sete recursos desde a versão 5 (o Orquestrador, o provedor cognitivo e o Design entraram). O site é só escuro desde a
      // marca da versão 5: o tema claro do sistema dá a mesma página.
      await expect(page.locator('#recursos article')).toHaveCount(7);
      // Com menos movimento entra o poster (o primeiro quadro, também o do GIF do Orquestrador): a captura não depende do vídeo carregar.
      await expect(page.locator('#recursos .janela img')).toHaveCount(7);
      await page.screenshot({ path: `test-results/capturas/16-home-${tela}-${tema === 'light' ? 'claro' : 'escuro'}.png`, fullPage: true });
    }
  }
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.emulateMedia({ colorScheme: 'light', reducedMotion: 'reduce' });
  await page.goto('/');
  await page.locator('#recursos').screenshot({ path: 'test-results/capturas/17-recursos.png' });
});

test('landing → seções e recursos da proposta → pedido de demonstração → o admin vê e exclui no dashboard', async ({ page, request }) => {
  const nome = `Visitante E2E ${Date.now()}`;

  // 1. A landing de venda, na ordem da proposta da home (US #467).
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('agente de IA');
  await expect(page.locator('main > section')).toHaveCount(ORDEM.length);
  expect(await page.locator('main > section').evaluateAll((secoes) => secoes.map((s) => s.id))).toEqual(ORDEM);
  // O hero da proposta: o selo e a amostra do Board com a nota. "Como funciona" ficou só com as sete etapas.
  await expect(page.locator('#inicio')).toContainText('Para times no Azure DevOps');
  await expect(page.getByRole('img', { name: /Amostra do Board/ })).toBeVisible();
  await expect(page.locator('#como-funciona ol > li')).toHaveCount(7);
  await expect(page.locator('#como-funciona video')).toHaveCount(0);
  // Os sete recursos, com as abas 01–07: a aba leva ao bloco, e a mídia toca dentro da moldura de janela (seis vídeos e o
  // GIF do Orquestrador).
  await expect(page.locator('#recursos video')).toHaveCount(6);
  await expect(page.locator('#recurso-orquestrador img')).toHaveAttribute('src', '/midia/orquestrador.gif');
  await page.getByRole('navigation', { name: 'Recursos em destaque' }).getByRole('link', { name: '06 Cherry-pick' }).click();
  await expect(page).toHaveURL(/#recurso-replicacao$/);
  const cherryPick = page.locator('#recurso-replicacao');
  await expect(cherryPick.locator('.janela-barra')).toHaveText('dev.kit - Cherry-pick');
  await expect(cherryPick.locator('figcaption')).toHaveText('Marque os commits a replicar');
  await expect(cherryPick.locator('video source[type="video/webm"]')).toHaveAttribute('src', '/midia/replicacao.webm');
  await expect(page.locator('body')).not.toContainText(/dashboard/i);
  // A API do e2e não tem REPO_KEY: o botão de download diz que está indisponível, sem quebrar a página.
  await expect(page.getByRole('group', { name: 'Download do dev.kit', exact: true })).toContainText('Download indisponível no momento.');
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

  // 1c. Os conceitos (agente, skill e rule, cada um com o exemplo) e a fábrica agêntica com o time agêntico.
  await page.getByRole('navigation', { name: 'Seções' }).getByRole('link', { name: 'Conceitos' }).click();
  await expect(page).toHaveURL(/#conceitos$/);
  await expect(page.locator('#conceitos article')).toHaveCount(3);
  await expect(page.locator('#conceito-skill pre')).toContainText('name: compilar-angular');
  await page.locator('#conceitos').screenshot({ path: 'test-results/capturas/18-conceitos.png' });
  await page.getByRole('navigation', { name: 'Rodapé' }).getByRole('link', { name: 'Fábrica agêntica' }).click();
  await expect(page).toHaveURL(/#fabrica$/);
  await expect(page.getByRole('list', { name: 'Estações da fábrica' }).getByRole('listitem')).toHaveCount(6);
  await page.locator('#fabrica').screenshot({ path: 'test-results/capturas/19-fabrica-agentica.png' });
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
