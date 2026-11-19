import { expect, test, type Page } from '@playwright/test';
import { prepararMatrix } from './matrix-cenario';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/** Mede texto renderizado sobre fundos sólidos, compondo transparências dos ancestrais.
 * Não substitui a revisão visual de imagens, gradientes, foco e estados transitórios. */
async function conferirTema(page: Page) {
  expect(await page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe('light');
  const dimensoes = await page.evaluate(() => ({ url: location.pathname, documento: document.documentElement.scrollWidth, viewport: innerWidth,
    excedentes: [...document.querySelectorAll('body *')].filter(e => e.getBoundingClientRect().right > innerWidth && e.getBoundingClientRect().width > 0)
      .slice(0, 12).map(e => ({ tag: e.tagName, classe: String(e.className), direita: e.getBoundingClientRect().right })) }));
  expect(dimensoes.documento, JSON.stringify(dimensoes)).toBeLessThanOrEqual(dimensoes.viewport);
  const falhas = await page.evaluate(() => {
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 1;
    const ctx = canvas.getContext('2d', { willReadFrequently: true })!;
    const rgba = (cor: string) => {
      // O navegador também retorna color(srgb ...) para color-mix; resolver pela própria engine.
      ctx.clearRect(0, 0, 1, 1);
      ctx.fillStyle = cor;
      ctx.fillRect(0, 0, 1, 1);
      const n = ctx.getImageData(0, 0, 1, 1).data;
      return [n[0], n[1], n[2], n[3] / 255];
    };
    const sobre = (a: number[], b: number[]) => [0, 1, 2].map(i => a[i] * a[3] + b[i] * (1 - a[3]));
    const luz = (c: number[]) => c.slice(0, 3).map(n => n / 255).map(n => n <= .04045 ? n / 12.92 : ((n + .055) / 1.055) ** 2.4)
      .reduce((s, n, i) => s + n * [.2126, .7152, .0722][i], 0);
    const problemas: { texto: string; classe: string; contraste: number; minimo: number }[] = [];
    for (const el of document.querySelectorAll<HTMLElement>('body *')) {
      const estilo = getComputedStyle(el);
      if (!el.checkVisibility({ checkVisibilityCSS: true, checkOpacity: true })) continue;
      const texto = [...el.childNodes].filter(n => n.nodeType === Node.TEXT_NODE).map(n => n.textContent).join('').trim();
      if (!texto) continue;
      const ancestrais: HTMLElement[] = [];
      for (let p: HTMLElement | null = el; p; p = p.parentElement) ancestrais.unshift(p);
      // Gradientes e opacidade de grupo precisam de comparação visual, sem fingir medição exata.
      if (ancestrais.some(p => getComputedStyle(p).backgroundImage !== 'none' || Number(getComputedStyle(p).opacity) !== 1)) continue;
      let fundo = [255, 255, 255];
      for (const p of ancestrais) fundo = sobre(rgba(getComputedStyle(p).backgroundColor), fundo);
      const tinta = sobre(rgba(estilo.color), fundo);
      const [l1, l2] = [luz(tinta), luz(fundo)].sort((a, b) => b - a);
      const contraste = (l1 + .05) / (l2 + .05);
      const tamanho = parseFloat(estilo.fontSize);
      const minimo = tamanho >= 24 || (tamanho >= 18.66 && Number(estilo.fontWeight) >= 700) ? 3 : 4.5;
      if (contraste + .01 < minimo) problemas.push({ texto: texto.slice(0, 90), classe: el.className, contraste, minimo });
    }
    return problemas;
  });
  expect(falhas, JSON.stringify(falhas, null, 2)).toEqual([]);
}

test('público: tema claro, texto AA e foco com preferência clara ou escura', async ({ page }, info) => {
  const manifesto = await (await page.request.get('/board.webmanifest')).json();
  expect(manifesto.background_color).toBe('#f7f8fa');
  expect(manifesto.theme_color).toBe('#f7f8fa');
  for (const width of [1440, 390]) for (const colorScheme of ['light', 'dark'] as const) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await page.emulateMedia({ colorScheme });
    for (const url of ['/', '/login', '/catalogo', '/aderir', '/definir-senha', '/comprar']) {
      await page.goto(url);
      await expect(page.locator('h1').first()).toBeVisible();
      await expect(page.locator('meta[name="color-scheme"]')).toHaveAttribute('content', 'light');
      await conferirTema(page);
      await page.screenshot({ path: info.outputPath(`${url === '/' ? 'landing' : url.slice(1)}-${width}-${colorScheme}.png`), fullPage: true });
    }
    await page.goto('/login');
    await page.getByLabel('Login', { exact: true }).focus();
    await page.keyboard.press('Tab');
    await expect(page.getByLabel('Senha', { exact: true })).toBeFocused();
    const foco = await page.getByLabel('Senha', { exact: true }).evaluate(e => {
      const s = getComputedStyle(e); return { estilo: s.outlineStyle, largura: parseFloat(s.outlineWidth) };
    });
    expect(foco.estilo).toBe('solid'); expect(foco.largura).toBeGreaterThanOrEqual(2);
    await page.screenshot({ path: info.outputPath(`login-foco-${width}-${colorScheme}.png`), fullPage: true });
    await page.getByLabel('Login', { exact: true }).fill('nao-existe.demo');
    await page.getByLabel('Senha', { exact: true }).fill('senha-ficticia-invalida');
    await page.getByRole('button', { name: 'Entrar', exact: true }).click();
    await expect(page.getByRole('alert')).toBeVisible();
    await conferirTema(page);
    await page.screenshot({ path: info.outputPath(`login-erro-${width}-${colorScheme}.png`), fullPage: true });
  }
});

test('conta, projetos e Matrix conservam tema claro e leitura dos estados nas duas preferências', async ({ page, request }, info) => {
  const c = await prepararMatrix(request);
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), c.sessao);
  for (const width of [1440, 390]) for (const colorScheme of ['light', 'dark'] as const) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await page.emulateMedia({ colorScheme });
    await page.goto('/conta');
    await expect(page.getByRole('heading', { name: 'Sua conta' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Gravar', exact: true })).toBeDisabled();
    await conferirTema(page);
    await page.screenshot({ path: info.outputPath(`conta-disabled-${width}-${colorScheme}.png`), fullPage: true });
    await page.goto('/projetos?slug=matrix&visao=impactos');
    await expect(page.getByRole('heading', { name: 'Matriz deste projeto' })).toBeVisible();
    await conferirTema(page);
    await page.screenshot({ path: info.outputPath(`projetos-matrix-${width}-${colorScheme}.png`), fullPage: true });
    if (width === 390) await page.getByRole('combobox', { name: 'Componente', exact: true }).selectOption('componente-1');
    await page.getByRole('button', { name: 'Avaliação × Page · imp-008' }).filter({ visible: true }).click();
    if (width === 390) await page.getByRole('button', { name: 'Abrir editor completo →' }).click();
    await expect(page.getByRole('dialog').last()).toBeVisible();
    await conferirTema(page);
    await page.screenshot({ path: info.outputPath(`matrix-dialogo-${width}-${colorScheme}.png`), fullPage: false });
    await page.keyboard.press('Escape');
    await page.keyboard.press('Escape');
    await page.goto('/board?aba=board');
    await expect(page.locator('.br-main')).toBeVisible();
    await conferirTema(page);
    await page.screenshot({ path: info.outputPath(`board-${width}-${colorScheme}.png`), fullPage: true });
  }
});

test('admin: painel, catálogo, usuários e empresas mantêm texto AA no tema claro', async ({ page, request }, info) => {
  await garantirSenhaDefinitiva(request);
  const entrada = await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } });
  expect(entrada.ok()).toBeTruthy();
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), await entrada.json());
  for (const width of [1440, 390]) for (const colorScheme of ['light', 'dark'] as const) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await page.emulateMedia({ colorScheme });
    for (const url of ['/dashboard', '/usuarios', '/empresas', '/catalogo']) {
      await page.goto(url);
      await expect(page.locator('h1').first()).toBeVisible();
      await expect(page.getByText('Carregando…', { exact: true })).toHaveCount(0);
      await conferirTema(page);
      await page.screenshot({ path: info.outputPath(`${url.slice(1)}-${width}-${colorScheme}.png`), fullPage: true });
    }
  }
});

test('Design com versões, acesso e comparação permanece legível e cabe no celular', async ({ page, request }, info) => {
  const c = await prepararMatrix(request);
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), c.sessao);
  await page.goto('/conta');
  await expect(page.getByRole('heading', { name: 'Sua conta' })).toBeVisible();
  const imagem = await page.screenshot();
  for (const numero of [1, 2, 3]) {
    const r = await request.post(`${API}/api/design/versoes`, { headers: c.headers, multipart: {
      repositorio: 'https://example.test/demo/tema', tela: 'conta', nome: 'Conta de demonstração', numero: String(numero),
      tipo: numero === 2 ? 'proposta' : 'captura', origem: numero === 2 ? 'proposta' : 'web', tema: 'light',
      alvo: 'http://localhost/conta', branch: 'feature/demo/tema-com-branch-longo', commit: 'a'.repeat(40),
      autorTipo: 'agente', runner: 'codex', ...(numero > 1 ? { base: String(numero - 1) } : {}),
      imagem: { name: 'conta.png', mimeType: 'image/png', buffer: imagem },
    } });
    expect(r.ok(), await r.text()).toBeTruthy();
  }
  for (const width of [1440, 390]) for (const colorScheme of ['light', 'dark'] as const) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await page.emulateMedia({ colorScheme });
    await page.goto('/empresa/conhecimento?aba=design');
    const comparacao = page.getByRole('region', { name: 'Versões de Conta de demonstração' });
    await expect(comparacao.getByRole('article')).toHaveCount(3);
    for (const modo of ['Lado a lado', 'Uma', 'Sobrepor']) {
      await page.getByRole('group', { name: 'Modo de comparação' }).getByRole('button', { name: modo, exact: true }).click();
      await conferirTema(page);
      await page.screenshot({ path: info.outputPath(`design-${modo.replaceAll(' ', '-')}-${width}-${colorScheme}.png`), fullPage: true });
    }
  }
});
