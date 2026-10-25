import { expect, test, type APIRequestContext, type Browser } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/** O gestor da empresa com a senha definitiva; devolve o token dele. */
async function gestorDefinitivo(request: APIRequestContext, token: string, empresa: number, login: string) {
  const convite = await request.post(`${API}/api/empresas/${empresa}/gestores`, { data: { login }, headers: { Authorization: `Bearer ${token}` } });
  expect(convite.ok()).toBeTruthy();
  const { senhaInicial } = (await convite.json()) as { senhaInicial: string };
  const inicial = (await (await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaInicial } })).json()) as { token: string };
  expect((await request.post(`${API}/api/auth/trocar-senha`, {
    data: { senhaAtual: senhaInicial, novaSenha: NOVA_SENHA }, headers: { Authorization: `Bearer ${inicial.token}` },
  })).ok()).toBeTruthy();
  return ((await (await request.post(`${API}/api/auth/login`, { data: { login, senha: NOVA_SENHA } })).json()) as { token: string }).token;
}

/**
 * A tela de demonstração que o "dev.kit" capturou: um checkout desenhado em HTML e fotografado pelo próprio navegador —
 * o PNG nasce no teste, e nenhum entra no repositório. `destaque` é a proposta (o botão de pagar em vermelho).
 */
async function telaDeDemonstracao(browser: Browser, destaque: boolean): Promise<Buffer> {
  const pagina = await browser.newPage({ viewport: { width: 720, height: 450 } });
  await pagina.setContent(`<body style="margin:0;font-family:sans-serif;background:#f4f4f6">
    <div style="height:40px;background:#2a2a30"></div>
    <div style="margin:28px 32px"><h2 style="margin:0 0 16px;color:#222">Checkout</h2>
      <div style="background:#fff;border:1px solid #ddd;border-radius:8px;padding:20px;color:#555">Pedido #1042 · 3 itens · R$ 189,90</div>
      <button style="margin-top:24px;float:right;padding:14px 48px;border:0;border-radius:6px;font-size:16px;color:#fff;background:${destaque ? '#e2261c' : '#9a9aa2'}">Pagar</button>
    </div></body>`);
  const png = await pagina.screenshot();
  await pagina.close();
  return png;
}

/**
 * A US #669 no navegador, com as capturas da entrega: o que o dev.kit enviou ao design compartilhado (a captura, a
 * proposta e a recaptura de uma tela, de dois branches do MESMO repositório) e o gestor comparando lado a lado em
 * /empresa/conhecimento?aba=design, até abrir o link estável que a PR cita.
 */
test('o gestor compara captura, proposta e recaptura de uma tela e abre o link estável', async ({ page, request, browser }) => {
  await page.setViewportSize({ width: 1440, height: 900 });

  // 1. A empresa, o gestor e o que o dev.kit teria enviado (multipart: os metadados e o PNG).
  await garantirSenhaDefinitiva(request);
  const { token } = (await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json()) as { token: string };
  const empresa = (await (await request.post(`${API}/api/empresas`, {
    data: { nome: `Empresa US669 ${Date.now()}`, plano: 'Empresarial', assentos: 3 }, headers: { Authorization: `Bearer ${token}` },
  })).json()) as { id: number };
  const gestor = `gestor.us669.${Date.now()}`;
  const doGestor = { Authorization: `Bearer ${await gestorDefinitivo(request, token, empresa.id, gestor)}` };
  const antes = await telaDeDemonstracao(browser, false);
  const proposta = await telaDeDemonstracao(browser, true);
  const enviar = async (numero: number, tipo: string, png: Buffer, branch: string, commit: string, base?: number, repositorio = 'https://github.com/acme/portal') => {
    const resposta = await request.post(`${API}/api/design/versoes`, {
      headers: doGestor,
      multipart: {
        repositorio, tela: 'checkout', nome: 'Checkout', numero: String(numero), tipo, origem: tipo === 'proposta' ? 'proposta' : 'web',
        alvo: 'http://localhost:5173/checkout', tema: 'light', branch, commit, autorTipo: 'agente', runner: 'claude',
        descricao: tipo === 'proposta' ? 'Botão de pagar em destaque.' : '', ...(base ? { base: String(base) } : {}),
        imagem: { name: 'checkout.png', mimeType: 'image/png', buffer: png },
      },
    });
    expect(resposta.status()).toBe(201);
    return (await resposta.json()) as { projeto: number };
  };
  await enviar(1, 'captura', antes, 'feature/dev/semana4', '81c0e2d');
  // O mesmo repositório escrito como o ssh do git: cai no MESMO projeto (o branch é atributo da versão).
  await enviar(2, 'proposta', proposta, 'feature/dev/semana5', '3f9c2a1', 1, 'git@github.com:acme/portal.git');
  const { projeto } = await enviar(3, 'captura', proposta, 'feature/dev/semana5', '9d41b07', 2);

  // 2. O gestor entra e abre a aba Design: o repositório, as telas e o uso do teto do plano.
  await page.goto('/login');
  await page.getByLabel('Login').fill(gestor);
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/(dashboard|conta)$/);
  await page.goto('/empresa/conhecimento?aba=design');
  await expect(page.getByLabel('Repositório')).toHaveValue('github.com/acme/portal');
  await expect(page.getByText(/3 de 500 versões/)).toBeVisible();

  // 3. A comparação lado a lado: antes, a proposta e a recaptura, com as imagens baixadas pelo link estável.
  const comparacao = page.getByRole('region', { name: 'Versões de Checkout' });
  await expect(comparacao.getByRole('article')).toHaveCount(3);
  await expect(comparacao.getByRole('img', { name: 'v003 captura (720x450)' })).toBeVisible();
  await expect(comparacao.getByText(`/design/${projeto}/checkout/v2`)).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/90-design-lado-a-lado.png', fullPage: true });

  // 4. O filtro de branch e o modo Sobrepor.
  await page.getByLabel('Branch').selectOption('feature/dev/semana5');
  await expect(comparacao.getByRole('article')).toHaveCount(2);
  await page.getByLabel('Branch').selectOption('');
  await page.getByRole('button', { name: 'Sobrepor' }).click();
  await expect(page.getByLabel('Opacidade da versão de cima')).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/91-design-sobrepor.png', fullPage: true });

  // 5. O link estável (o que a PR da semana cita) abre a página da versão.
  await page.getByRole('button', { name: 'Lado a lado' }).click();
  await page.goto(`/design/${projeto}/checkout/v2`);
  await expect(page.getByRole('heading', { name: 'Checkout · v002' })).toBeVisible();
  await expect(page.getByRole('img', { name: 'v002 proposta (720x450)' })).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/92-design-link-estavel.png', fullPage: true });
});
