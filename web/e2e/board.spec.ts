import { chromium, expect, test } from '@playwright/test';
import { mkdtemp } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

test('Board consulta o retrato real, atualiza e mantém os comandos bloqueados nos dois temas e tamanhos', async ({ page, request, context }) => {
  await garantirSenhaDefinitiva(request);
  const entrada = await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } });
  expect(entrada.ok()).toBeTruthy();
  const { token } = await entrada.json();
  const usuario = await request.post(`${API}/api/usuarios`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { login: `board-demo-${Date.now()}`, nome: 'Pessoa Demonstração Board', papel: 'dev' },
  });
  expect(usuario.ok()).toBeTruthy();
  const { usuario: { login }, senhaTemporaria } = await usuario.json();
  const temporaria = await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaTemporaria } });
  expect(temporaria.ok()).toBeTruthy();
  const inicial = await temporaria.json();
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    headers: { Authorization: `Bearer ${inicial.token}` }, data: { senhaAtual: senhaTemporaria, novaSenha: NOVA_SENHA },
  });
  expect(troca.ok()).toBeTruthy();
  const sessao = await troca.json();
  const headers = { Authorization: `Bearer ${sessao.token}` };
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), sessao);
  await page.goto('/board');
  await expect(page.getByText(/Nenhuma máquina cadastrada/)).toBeVisible();

  const heartbeat = { contrato: 1, maquinaId: 'notebook-demo', nome: 'Notebook', versaoDevKit: '79', origem: 'servico', appAberto: true, estadoDoAgente: 'ativo', turnoEmAndamento: false };
  expect((await request.post(`${API}/api/remoto/heartbeat`, { headers, data: heartbeat })).ok()).toBeTruthy();
  await page.reload();
  await expect(page.getByText(/Aguardando o primeiro retrato/)).toBeVisible();
  const retrato = {
    contrato: 1, maquinaId: heartbeat.maquinaId, geradoEm: new Date().toISOString(),
    pendencias: [{ id: 'integrar-20', tipo: 'aprovacao', titulo: 'Integrar o item 20 no projeto?', jaTentei: 'Nota 100 · regressão sem falhas' }],
    projetos: [
      { slug: 'projeto', nome: 'projeto', situacao: 'aprovado', ondaAtual: 10,
        itens: Array.from({ length: 21 }, (_, i) => ({ numero: i + 1, titulo: `Item ${i + 1}`, situacao: i === 19 ? 'rodando' : 'integrado', onda: 10 })) },
      { slug: 'bugs', nome: 'bugs', situacao: 'concluido', ondaAtual: 1,
        itens: Array.from({ length: 4 }, (_, i) => ({ numero: i + 1, titulo: `Correção ${i + 1}`, situacao: 'integrado', onda: 1 })) },
    ],
    fluxos: [
      { id: 'layout-20', nome: 'Item 20 · layout do plano', etapa: '', situacao: 'rodando' },
      { id: 'avaliador-20', nome: 'Avaliador · item 20', etapa: '', situacao: 'fila' },
    ],
  };
  expect((await request.put(`${API}/api/remoto/retrato`, { headers, data: retrato })).ok()).toBeTruthy();
  // Sem recarregar: comprova o polling do produto sobre a API real.
  await expect(page.getByText('Integrar o item 20 no projeto?')).toBeVisible({ timeout: 25_000 });
  await expect(page.getByRole('progressbar', { name: 'Progresso de projeto', exact: true })).toHaveAttribute('aria-valuenow', '20');

  const escritas: string[] = [];
  page.on('request', r => { if (r.url().includes('/api/') && r.method() !== 'GET') escritas.push(r.method()); });
  for (const colorScheme of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme });
    for (const width of [1440, 390, 320]) {
      await page.setViewportSize({ width, height: width > 700 ? 920 : 844 });
      await expect(page.getByText('Integrar o item 20 no projeto?')).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
      await expect(page.getByRole('button', { name: 'Decidir', exact: true })).toBeDisabled();
      if (width < 700) {
        await expect(page.getByRole('tablist').getByRole('tab')).toHaveCount(2);
        await page.getByRole('tab', { name: 'Chat', exact: true }).click();
        await page.keyboard.press('ArrowRight');
        await expect(page.getByRole('tab', { name: 'Board', exact: true })).toHaveAttribute('aria-selected', 'true');
        await page.keyboard.press('ArrowLeft');
        await expect(page.getByRole('tab', { name: 'Chat', exact: true })).toHaveAttribute('aria-selected', 'true');
      }
      await expect(page.getByLabel('Mensagem ao Orquestrador')).toBeDisabled();
      await expect(page.getByText(/será liberado na fase 2/)).toBeVisible();
      await expect(page.getByRole('button', { name: 'Enviar mensagem' })).toBeDisabled();
      if (width < 700) await page.getByRole('tab', { name: 'Board', exact: true }).click();
    }
  }
  expect(escritas).toEqual([]);

  // A lista e o retrato não passam pelo cache do service worker.
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.reload();
  await expect(page.getByText('Integrar o item 20 no projeto?')).toBeVisible();
  const manifesto = await page.evaluate(async () => {
    const link = document.querySelector<HTMLLinkElement>('link[rel="manifest"]')!;
    return (await fetch(link.href)).json();
  });
  expect(manifesto.start_url).toBe('/board');
  // O contexto padrão do Playwright é anônimo: nele o Edge recusa instalar (in-incognito).
  // A conferência usa um perfil persistente NOVO, sem tocar no navegador do desenvolvedor.
  const perfil = await mkdtemp(join(tmpdir(), 'board-pwa-e2e-'));
  const persistente = await chromium.launchPersistentContext(perfil, { channel: process.env.DEVKIT_E2E_BROWSER_CHANNEL });
  try {
    await persistente.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), sessao);
    const pwa = await persistente.newPage();
    await pwa.goto(page.url());
    await expect(pwa.getByText('Integrar o item 20 no projeto?')).toBeVisible();
    await pwa.evaluate(() => navigator.serviceWorker.ready);
    const cdp = await persistente.newCDPSession(pwa);
    await cdp.send('Page.enable');
    await expect.poll(async () => (await cdp.send('Page.getInstallabilityErrors')).installabilityErrors).toEqual([]);
    console.log('PWA: Page.getInstallabilityErrors retornou zero erros em perfil persistente temporário.');
  } finally { await persistente.close(); }
  expect(await page.evaluate(async () => (await caches.keys()).length)).toBe(0);
  await context.setOffline(true);
  for (const colorScheme of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme });
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Board sem conexão' })).toBeVisible();
    await expect(page.getByText('Integrar o item 20 no projeto?')).toHaveCount(0);
    expect(await page.evaluate(() => getComputedStyle(document.documentElement).colorScheme)).toBe(colorScheme);
  }
  await context.setOffline(false);
  await page.getByRole('link', { name: 'Tentar novamente' }).click();
  await expect(page.getByText('Integrar o item 20 no projeto?')).toBeVisible();

  // Limite aceito pelo contrato: uma máquina com nome sem espaços de 200 caracteres.
  const nomeLongo = 'X'.repeat(200);
  expect((await request.post(`${API}/api/remoto/heartbeat`, { headers, data: { ...heartbeat, nome: nomeLongo } })).ok()).toBeTruthy();
  await page.reload();
  await expect(page.getByTitle(nomeLongo, { exact: true })).toBeVisible();
  for (const width of [320, 390, 1440]) {
    await page.setViewportSize({ width, height: 844 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
    await expect(page.getByTitle(nomeLongo, { exact: true })).toBeVisible();
    const cabecalho = await page.locator('.br-top').boundingBox();
    const estado = await page.locator('.br-host').boundingBox();
    expect(estado!.y + estado!.height).toBeLessThanOrEqual(cabecalho!.y + cabecalho!.height);
  }

  expect((await request.post(`${API}/api/remoto/heartbeat`, { headers, data: { ...heartbeat, maquinaId: 'estacao-demo', nome: 'Estação' } })).ok()).toBeTruthy();
  // A lista seleciona a máquina mais recente ao reabrir; ela precisa do próprio retrato.
  expect((await request.put(`${API}/api/remoto/retrato`, { headers, data: { ...retrato, maquinaId: 'estacao-demo' } })).ok()).toBeTruthy();
  await page.reload();
  await expect(page.getByLabel('Máquina')).toBeVisible();
  for (const width of [320, 390, 1440]) {
    await page.setViewportSize({ width, height: 844 });
    const cabecalho = await page.locator('.br-top').boundingBox();
    const seletor = await page.getByLabel('Máquina').boundingBox();
    expect(seletor!.y).toBeGreaterThanOrEqual(cabecalho!.y);
    expect(seletor!.y + seletor!.height).toBeLessThanOrEqual(cabecalho!.y + cabecalho!.height);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  }

  // Falha de transporte, recuperação e posterior encerramento da sessão.
  await page.route('**/api/remoto/maquinas', route => route.abort('failed'));
  await page.reload();
  await expect(page.getByRole('alert')).toContainText('Não foi possível atualizar o Board');
  await page.unroute('**/api/remoto/maquinas');
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  await expect(page.getByText('Integrar o item 20 no projeto?')).toBeVisible();
  await page.route('**/api/remoto/maquinas', route => route.fulfill({ status: 401 }));
  await page.reload();
  await expect(page).toHaveURL(/\/login$/);
});
