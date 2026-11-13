import { expect, test } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { NOVA_SENHA } from './ambiente';

/** As seções da landing, na ordem da lista SECOES de conteudo/landing.ts (a landing refeita em 12/11/2026). */
const ORDEM = [
  'inicio', 'como-funciona', 'bracos', 'recursos', 'beneficios', 'conceitos', 'fabrica', 'catalogo', 'comparativo', 'integracoes', 'seguranca',
  'empresas', 'planos', 'contato', 'faq', 'comecar',
];

/** A home inteira em desktop e celular, claro e escuro — e a primeira dobra com a promessa, os CTAs e os quatro braços. */
test('a home refeita em desktop e celular, nos temas claro e escuro, com o principal na primeira dobra', async ({ page }) => {
  for (const [tela, largura, altura] of [['desktop', 1440, 900], ['celular', 390, 844]] as const) {
    for (const tema of ['light', 'dark'] as const) {
      await page.setViewportSize({ width: largura, height: altura });
      await page.emulateMedia({ colorScheme: tema, reducedMotion: 'reduce' });
      await page.goto('/');
      // A primeira dobra: no desktop, a promessa, os CTAs e os quatro braços inteiros; no celular, a promessa e os CTAs.
      await expect(page.getByRole('navigation', { name: 'Os quatro braços do dev.kit' }).getByRole('link')).toHaveCount(4);
      const dobra = await page.locator(tela === 'desktop' ? '#inicio .lp-faixa' : '#inicio .ctas').boundingBox();
      expect(dobra!.y + dobra!.height).toBeLessThanOrEqual(altura);
      // Os oito recursos do tour, um painel por aba; só o aberto aparece, e com menos movimento entra o poster (o primeiro quadro,
      // também o do GIF do Orquestrador): a captura não depende do vídeo carregar. O site é só escuro: o tema claro dá a mesma página.
      await expect(page.locator('#recursos [role="tabpanel"]')).toHaveCount(8);
      await expect(page.locator('#recursos [role="tabpanel"]:not([hidden]) .janela img')).toHaveCount(1);
      await page.screenshot({ path: `test-results/capturas/16-home-${tela}-${tema === 'light' ? 'claro' : 'escuro'}.png`, fullPage: true });
    }
  }
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.emulateMedia({ colorScheme: 'light', reducedMotion: 'reduce' });
  await page.goto('/');
  await page.screenshot({ path: 'test-results/capturas/15-primeira-dobra.png' });
  await page.locator('#recursos').screenshot({ path: 'test-results/capturas/17-recursos.png' });
  await page.locator('#bracos').screenshot({ path: 'test-results/capturas/20-produto-quatro-bracos.png' });
});

test('landing → seções e recursos da proposta → pedido de demonstração → o admin vê e exclui no dashboard', async ({ page, request }) => {
  const nome = `Visitante E2E ${Date.now()}`;

  // 1. A landing de venda, na ordem da landing refeita.
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('time de agentes de IA');
  await expect(page.locator('main > section')).toHaveCount(ORDEM.length);
  expect(await page.locator('main > section').evaluateAll((secoes) => secoes.map((s) => s.id))).toEqual(ORDEM);
  // O hero: o selo, a vitrine do desktop com o celular e os quatro braços. "Como funciona" ficou só com as sete etapas.
  await expect(page.locator('#inicio')).toContainText('Para times no Azure DevOps');
  await expect(page.getByRole('img', { name: /Amostra do dev\.kit no desktop e no celular/ })).toBeVisible();
  await expect(page.locator('#como-funciona ol > li')).toHaveCount(7);
  await expect(page.locator('#como-funciona video')).toHaveCount(0);
  // A faixa dos braços leva ao detalhe do Board, na seção Produto.
  await page.getByRole('navigation', { name: 'Os quatro braços do dev.kit' }).getByRole('link', { name: /^Board/ }).click();
  await expect(page).toHaveURL(/#braco-board$/);
  await expect(page.getByRole('article', { name: 'O Board no site e no celular' })).toBeInViewport();
  // O tour dos recursos: oito abas, uma aberta por vez — o Orquestrador (o GIF) abre, e o Cherry-pick, pela aba, toca o vídeo
  // dentro da moldura de janela.
  const abas = page.getByRole('tablist', { name: 'Recursos em destaque' });
  await expect(abas.getByRole('tab')).toHaveCount(8);
  await expect(page.locator('#recurso-orquestrador img')).toHaveAttribute('src', '/midia/orquestrador.gif');
  await expect(page.locator('#recursos video')).toHaveCount(0);
  await abas.getByRole('tab', { name: /07 Cherry-pick/ }).click();
  await expect(abas.getByRole('tab', { name: /07 Cherry-pick/ })).toHaveAttribute('aria-selected', 'true');
  const cherryPick = page.locator('#recurso-replicacao');
  await expect(cherryPick).toBeVisible();
  await expect(cherryPick.locator('.janela-barra')).toHaveText('dev.kit - Cherry-pick');
  await expect(cherryPick.locator('figcaption')).toHaveText('Marque os commits a replicar');
  await expect(cherryPick.locator('video source[type="video/webm"]')).toHaveAttribute('src', '/midia/replicacao.webm');
  await expect(page.locator('#recursos video')).toHaveCount(1);
  // A seta leva à aba seguinte (o foco junto), e o endereço antigo de um recurso abre a aba dele.
  await page.keyboard.press('ArrowRight');
  await expect(abas.getByRole('tab', { name: /08 Horas/ })).toBeFocused();
  await expect(page.locator('#recurso-board')).toBeVisible();
  await page.goto('/#recurso-design');
  await expect(abas.getByRole('tab', { name: /06 Design/ })).toHaveAttribute('aria-selected', 'true');
  await expect(page.locator('#recurso-design')).toBeVisible();
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

  // 1c. Os conceitos (agente, skill e rule, cada um com o exemplo) e a fábrica agêntica com o time agêntico, pelo rodapé.
  await page.getByRole('navigation', { name: 'Rodapé' }).getByRole('link', { name: 'Conceitos' }).click();
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
