import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test } from '@playwright/test';
import type { RetratoDaMaquina } from '../src/api/remoto';
import { API } from './ambiente';
import { prepararMatrix } from './matrix-cenario';

test('Matrix e conversa da mesma task preservam alvo e ações independentes no Board e celular', async ({ page, request }) => {
  const c = await prepararMatrix(request);
  const leitura = await request.get(`${API}/api/remoto/maquinas/${c.maquina}/retrato`, { headers: c.headers });
  expect(leitura.ok()).toBeTruthy();
  const foto = await leitura.json() as RetratoDaMaquina;
  const execucao = foto.retrato!.fluxos!.find(f => f.id === c.task)!;
  execucao.conversavel = true;
  expect((await request.put(`${API}/api/remoto/retrato`, { headers: c.headers, data: foto.retrato })).ok()).toBeTruthy();
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), c.sessao);
  const mensagens: unknown[] = [];
  page.on('request', r => {
    if (r.method() === 'POST' && r.url().endsWith('/api/remoto/comandos')) {
      const pedido = r.postDataJSON() as { tipo: string };
      if (pedido.tipo === 'mensagem') mensagens.push(pedido);
    }
  });
  const capturas = fileURLToPath(new URL('../../../apoio-capturas-fechamento-matrix/', import.meta.url));
  mkdirSync(capturas, { recursive: true });
  for (const colorScheme of ['light', 'dark'] as const) for (const width of [1440, 390]) {
    await page.emulateMedia({ colorScheme });
    await page.setViewportSize({ width, height: width === 390 ? 844 : 980 });
    await page.goto('/board?aba=board');
    const board = page.getByRole('complementary', { name: 'Board' });
    const conversar = board.getByRole('button', { name: 'Conversar com a task Item 4 · Editor Page da matriz' });
    await expect(conversar).toBeVisible();
    await expect(conversar.locator('button')).toHaveCount(0);
    await expect(board.getByRole('button', { name: 'Impactos', exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
    await page.screenshot({ path: resolve(capturas, `board-${colorScheme}-${width}.png`), fullPage: true });

    await board.getByRole('button', { name: 'Impactos', exact: true }).click();
    const impactos = page.getByRole('dialog', { name: 'Impactos da task · Item 4' });
    await expect(impactos.getByRole('heading', { name: 'Impactos da task · recorte' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Conversa da task Item 4 · Editor Page da matriz' })).toHaveCount(0);
    await page.screenshot({ path: resolve(capturas, `impactos-${colorScheme}-${width}.png`), fullPage: true });
    await impactos.getByRole('button', { name: 'Fechar painel', exact: true }).click();

    await conversar.click();
    await expect(page.getByRole('region', { name: 'Conversa da task Item 4 · Editor Page da matriz' })).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Mensagem à task' })).toBeEditable();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await page.screenshot({ path: resolve(capturas, `conversa-${colorScheme}-${width}.png`), fullPage: true });
    expect(mensagens).toEqual([]);
  }
});
