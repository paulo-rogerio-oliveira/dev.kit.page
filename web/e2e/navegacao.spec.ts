import { createHash } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { prepararMatrix } from './matrix-cenario';
import { API, NOVA_SENHA } from './ambiente';

async function semTransbordo(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const contraste = await page.evaluate(() => {
    const canvas = document.createElement('canvas'); canvas.width = canvas.height = 1;
    const ctx = canvas.getContext('2d', { willReadFrequently: true })!;
    const rgba = (cor: string) => {
      ctx.clearRect(0, 0, 1, 1); ctx.fillStyle = cor; ctx.fillRect(0, 0, 1, 1);
      const n = ctx.getImageData(0, 0, 1, 1).data; return [n[0], n[1], n[2], n[3] / 255];
    };
    const sobre = (a: number[], b: number[]) => [0, 1, 2].map(i => a[i] * a[3] + b[i] * (1 - a[3]));
    const luz = (c: number[]) => c.slice(0, 3).map(n => n / 255).map(n => n <= .04045 ? n / 12.92 : ((n + .055) / 1.055) ** 2.4)
      .reduce((s, n, i) => s + n * [.2126, .7152, .0722][i], 0);
    return [...document.querySelectorAll<HTMLElement>('.navegacao-link, .navegacao-localizacao a, .navegacao-menu-botao')]
      .filter(e => e.checkVisibility({ checkVisibilityCSS: true })).map(e => {
        const pais: HTMLElement[] = []; for (let p: HTMLElement | null = e; p; p = p.parentElement) pais.unshift(p);
        let fundo = [255, 255, 255]; for (const p of pais) fundo = sobre(rgba(getComputedStyle(p).backgroundColor), fundo);
        const texto = sobre(rgba(getComputedStyle(e).color), fundo);
        const [a, b] = [luz(texto), luz(fundo)].sort((x, y) => y - x);
        return { texto: e.textContent, contraste: (a + .05) / (b + .05) };
      }).filter(e => e.contraste < 4.5);
  });
  expect(contraste, 'Texto da navegação com contraste AA').toEqual([]);
}
async function menu(page: Page, largura: number) {
  if (largura === 390) await page.getByRole('button', { name: '☰ Menu' }).click();
  return page.getByRole('navigation', { name: 'Navegação principal' });
}

for (const largura of [1440, 390]) test(`URL direta, histórico, Design e teclado em ${largura}px`, async ({ page, request }) => {
  const c = await prepararMatrix(request);
  await page.setViewportSize({ width: largura, height: largura === 390 ? 844 : 900 });
  await page.emulateMedia({ colorScheme: 'dark' });
  // A imagem é apenas um dado fictício enviado à API temporária; as recapturas abaixo são do produto.
  await page.setContent('<html lang="pt-BR" style="color-scheme:light;background:white;color:black"><body><h1>Proposta fictícia para testar navegação</h1></body></html>');
  const png = await page.screenshot();
  const envio = await request.post(`${API}/api/design/versoes`, { headers: c.headers, multipart: {
    repositorio: 'https://example.test/demo/aplicacao', tela: 'navegacao', nome: 'Navegação de demonstração', numero: '1', tipo: 'proposta',
    origem: 'proposta', alvo: 'http://localhost/demo', tema: 'light', branch: 'feature/demo/navegacao', commit: 'a'.repeat(40),
    autorTipo: 'agente', runner: 'codex', imagem: { name: 'demo.png', mimeType: 'image/png', buffer: png },
  } });
  expect(envio.ok(), await envio.text()).toBeTruthy();
  const designs = await (await request.get(`${API}/api/design/projetos`, { headers: c.headers })).json();
  const design = designs[0], versao = design.telas[0].versoes[0];
  const chave = design.chave.replace(/[^a-zA-Z0-9_-]/g, '-').replace(/^-+|-+$/g, '') || 'projeto';
  const identidade = `${chave.slice(0, 48)}-${createHash('sha256').update(design.chave).digest('hex').slice(0, 16)}`;
  const proposta = { projeto: identidade, tela: 'navegacao', versao: 1, hash: versao.sha256 };
  const gravado = await request.put(`${API}/api/projetos/${c.projeto.id}`, { headers: c.headers, data: {
    ...c.projeto, situacao: 'aguardando', itens: c.projeto.itens.map((i: { numero: number }) => ({ ...i, design: { alteraTela: i.numero === 4, revisao: 1, propostas: i.numero === 4 ? [proposta] : [] } })),
  } });
  expect(gravado.ok(), await gravado.text()).toBeTruthy();
  // Login pelo formulário, sem reinjetar sessão durante refresh ou histórico.
  await page.goto('/login');
  await page.getByLabel('Login', { exact: true }).fill(c.sessao.login);
  await page.getByLabel('Senha', { exact: true }).fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Sua conta' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Gravar', exact: true })).toBeVisible();
  await expect(page.getByText(/Carregando/)).toHaveCount(0);
  await semTransbordo(page);
  await page.screenshot({ path: `test-results/navegacao-capturas/conta-${largura}.png`, fullPage: true });
  const principal = await menu(page, largura);
  await semTransbordo(page);
  await page.screenshot({ path: `test-results/navegacao-capturas/menu-aberto-${largura}.png`, fullPage: true });
  await expect(principal.getByRole('link', { name: 'Usuários', exact: true })).toHaveCount(0);
  await principal.getByRole('link', { name: 'Projetos', exact: true }).click();
  await page.getByRole('combobox', { name: 'Nível de compartilhamento' }).selectOption('usuario');
  await page.getByRole('combobox', { name: 'Situação do projeto' }).selectOption('aguardando');
  await expect(page.getByRole('link', { name: `Abrir projeto ${c.projeto.nome}`, exact: true })).toBeVisible();
  await semTransbordo(page);
  await page.screenshot({ path: `test-results/navegacao-capturas/projetos-lista-${largura}.png`, fullPage: true });
  await page.getByRole('link', { name: `Abrir projeto ${c.projeto.nome}`, exact: true }).click();
  await page.getByRole('button', { name: 'Propostas visuais', exact: true }).click();
  const contexto = page.url();
  await page.reload();
  await expect(page.getByRole('button', { name: 'Propostas visuais', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('link', { name: 'Ver proposta fixada · v1' }).click();
  await expect(page.getByRole('heading', { name: 'Navegação de demonstração · v001' })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Navegação de demonstração · v001' })).toBeVisible();
  await expect(page.getByRole('img', { name: /v001 proposta/ })).toBeVisible();
  await semTransbordo(page);
  await page.screenshot({ path: `test-results/navegacao-capturas/design-versao-${largura}.png`, fullPage: true });
  await page.getByRole('link', { name: '← Projetos', exact: true }).click();
  await expect(page).toHaveURL(contexto);
  await expect(page.getByRole('combobox', { name: 'Nível de compartilhamento' })).toHaveValue('usuario');
  await expect(page.getByRole('combobox', { name: 'Situação do projeto' })).toHaveValue('aguardando');
  await page.goBack();
  await expect(page.getByRole('heading', { name: 'Navegação de demonstração · v001' })).toBeVisible();
  await page.goForward();
  await expect(page).toHaveURL(contexto);
  await page.getByRole('link', { name: '← Projetos', exact: true }).click();
  await expect(page).toHaveURL(/\/projetos\?nivel=usuario&situacao=aguardando$/);
  const nav = await menu(page, largura);
  await nav.getByRole('link', { name: 'Design', exact: true }).click();
  await page.getByRole('button', { name: 'Uma', exact: true }).click();
  const urlDesign = page.url();
  await page.reload();
  await expect(page.getByRole('button', { name: 'Uma', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await page.getByRole('button', { name: 'Sobrepor', exact: true }).click();
  await page.goBack();
  await expect(page).toHaveURL(urlDesign);
  await expect(page.getByRole('button', { name: 'Uma', exact: true })).toHaveAttribute('aria-pressed', 'true');
  if (largura === 390) {
    const botao = page.getByRole('button', { name: '☰ Menu' });
    await botao.focus(); await page.keyboard.press('Enter');
    await expect(page.getByRole('navigation', { name: 'Navegação principal' }).getByRole('link', { name: 'Board', exact: true })).toBeFocused();
    await page.keyboard.press('Escape'); await expect(botao).toBeFocused();
    const foco = await botao.evaluate(e => ({ largura: parseFloat(getComputedStyle(e).outlineWidth), estilo: getComputedStyle(e).outlineStyle }));
    expect(foco.estilo).toBe('solid'); expect(foco.largura).toBeGreaterThanOrEqual(2);
    await expect(botao).toHaveAttribute('aria-expanded', 'false');
    await semTransbordo(page);
    await page.screenshot({ path: 'test-results/navegacao-capturas/menu-foco-390.png', fullPage: true });
  }
  await page.goto('/usuarios');
  await expect(page.getByText(/Acesso negado à seção solicitada/)).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Sua conta' })).toBeVisible();
  // A API real recusa o token fictício; o destino continua no histórico ao recarregar o login.
  await page.evaluate(() => {
    const sessao = JSON.parse(sessionStorage.getItem('devkitpage.sessao')!);
    sessionStorage.setItem('devkitpage.sessao', JSON.stringify({ ...sessao, token: 'token-invalido-demonstracao' }));
  });
  await page.goto(new URL(contexto).pathname + new URL(contexto).search);
  await expect(page.getByRole('heading', { name: 'Entrar', exact: true })).toBeVisible();
  await page.reload();
  await page.getByLabel('Login', { exact: true }).fill(c.sessao.login);
  await page.getByLabel('Senha', { exact: true }).fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(contexto);
});
