import { chromium, expect, test as base } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { mkdtemp } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

// A instalação é vedada pelo Edge em contexto anônimo. Usa um perfil NOVO e temporário,
// sem reaproveitar sessão/perfil do desenvolvedor, para conferir o recurso de verdade.
const test = base.extend({
  context: async ({ baseURL }, usar) => {
    const perfil = await mkdtemp(join(tmpdir(), 'devkit-board-pwa-'));
    const contexto = await chromium.launchPersistentContext(perfil, {
      channel: process.env.DEVKIT_E2E_BROWSER_CHANNEL ?? 'msedge', headless: true, baseURL,
    });
    try { await usar(contexto); } finally { await contexto.close(); }
  },
});

test('Board real: leitura, temas, celular, sessão, atualização e PWA sem cache privado', async ({ page, request, context }) => {
  await garantirSenhaDefinitiva(request);
  const resposta = await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } });
  expect(resposta.ok()).toBeTruthy();
  const sessao = await resposta.json();
  const headers = { Authorization: `Bearer ${sessao.token}` };
  const retrato = JSON.parse(readFileSync(new URL('../../api/tests/DevKitPage.Api.Tests/ContratoDoRemoto/retrato.json', import.meta.url), 'utf8'));
  const heartbeat = { versao: 'v1', maquinaId: retrato.maquinaId, apelido: 'Notebook', versaoDevKit: '1.61.0', devKitAberto: true, agente: 'ativo', turnosEmAndamento: 1 };
  expect((await request.put(`${API}/api/remoto/v1/heartbeat`, { headers, data: heartbeat })).status()).toBe(200);
  retrato.geradoEm = new Date().toISOString();
  expect((await request.put(`${API}/api/remoto/v1/retrato`, { headers, data: retrato })).status()).toBe(200);

  await page.goto('/board');
  await expect(page).toHaveURL(/\/login$/);
  await page.getByLabel('Login').fill('admin');
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/board$/);
  await expect(page.getByText('Integrar o item 20 no projeto?')).toBeVisible();
  const escritas: string[] = [];
  page.on('request', r => { if (r.url().includes('/api/remoto/') && r.method() !== 'GET') escritas.push(r.method()); });
  for (const tema of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme: tema });
    await page.setViewportSize({ width: 1440, height: 920 });
    await expect(page.getByRole('heading', { name: 'Chat indisponível nesta fase' })).toBeVisible();
    await expect(page.getByRole('progressbar', { name: 'Andamento de projeto', exact: true })).toHaveJSProperty('value', 20);
    await expect(page.getByRole('button', { name: 'Decidir', exact: true })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Enviar mensagem indisponível' })).toBeDisabled();
    await expect(page.getByRole('link', { name: /Projetos|Fluxos|Infra/ })).toHaveCount(0);
    await page.screenshot({ path: `test-results/capturas/board-navegador-${tema}.png` });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.getByRole('tab', { name: 'Board', exact: true }).click();
    await expect(page.getByText('Integrar o item 20 no projeto?')).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: `test-results/capturas/board-celular-board-${tema}.png` });
    await page.getByRole('tab', { name: 'Chat', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Chat indisponível nesta fase' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Mensagem ao Orquestrador' })).toBeDisabled();
    await page.screenshot({ path: `test-results/capturas/board-celular-chat-${tema}.png` });
  }

  // Uma alteração publicada pela API precisa chegar à tela sem recarregar a página.
  retrato.projetos[0].integrados = 19;
  retrato.geradoEm = new Date().toISOString();
  expect((await request.put(`${API}/api/remoto/v1/retrato`, { headers, data: retrato })).status()).toBe(200);
  await page.getByRole('tab', { name: 'Board', exact: true }).click();
  await expect(page.getByText('19 de 21')).toBeVisible({ timeout: 25_000 });
  expect(escritas).toEqual([]);

  const manifesto = await (await request.get(new URL('/board.webmanifest', page.url()).href)).json();
  expect(manifesto).toMatchObject({ start_url: '/board', scope: '/board', display: 'standalone' });
  for (const icone of manifesto.icons) {
    const arquivo = await request.get(new URL(icone.src, page.url()).href);
    expect(arquivo.status()).toBe(200);
    const png = await arquivo.body();
    const lado = Number(icone.sizes.split('x')[0]);
    expect(png.readUInt32BE(16)).toBe(lado);
    expect(png.readUInt32BE(20)).toBe(lado);
  }
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.reload();
  await expect(page.getByText('19 de 21')).toBeVisible();
  expect(await page.evaluate(() => navigator.serviceWorker.controller?.scriptURL)).toContain('/board-sw.js');
  const cdp = await context.newCDPSession(page);
  const installabilidade = await cdp.send('Page.getInstallabilityErrors');
  expect(installabilidade.installabilityErrors).toEqual([]);
  expect(await page.evaluate(() => caches.keys())).toEqual([]);
  expect(await page.evaluate(() => Object.keys(localStorage))).not.toContain('devkitpage.sessao');
  await context.setOffline(true);
  await page.reload();
  await expect(page.getByRole('heading', { name: 'O Board precisa de internet' })).toBeVisible();
  await context.setOffline(false);
  await page.getByRole('link', { name: 'Tentar novamente' }).click();
  await expect(page.getByText('19 de 21')).toBeVisible();
  await page.evaluate(() => sessionStorage.clear());
  await page.reload();
  await expect(page).toHaveURL(/\/login$/);
});
