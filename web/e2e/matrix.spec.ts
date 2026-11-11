import { expect, test } from '@playwright/test';
import { prepararMatrix } from './matrix-cenario';
import { API } from './ambiente';
import type { RevisaoMatrix } from '../src/api/matrix';

test('Matrix: persistência, concorrência real, mapa e recorte nas duas larguras', async ({ page, request }) => {
  const c = await prepararMatrix(request);
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), c.sessao);
  let versao = 4;
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await page.goto('/projetos?slug=matrix&visao=impactos');
    await expect(page.getByRole('heading', { name: 'Matriz deste projeto' })).toBeVisible();
    const dimensoes = await page.evaluate(() => ({ viewport: innerWidth, documento: document.documentElement.scrollWidth,
      excedentes: [...document.querySelectorAll('body *')].filter(e => e.getBoundingClientRect().right > innerWidth + 1 && e.getBoundingClientRect().width > 0)
        .slice(0, 10).map(e => ({ tag: e.tagName, classe: String(e.className), direita: e.getBoundingClientRect().right })) }));
    expect(dimensoes.documento, JSON.stringify(dimensoes)).toBeLessThanOrEqual(dimensoes.viewport);
    await page.getByRole('button', { name: 'Avaliação × Page · imp-008' }).filter({ visible: true }).click();
    const d = page.getByRole('dialog').last();
    await expect(d).toBeVisible();
    await d.getByRole('textbox', { name: 'Como validar', exact: true }).fill(`Critério real ${width}`);
    // Outro cliente usa o mesmo contrato CAS da aplicação desktop, sem mock de transporte.
    const remoto = structuredClone(c.matriz);
    remoto.matriz!.celulas.find(x => x.id === 'imp-0-2')!.criterio = `Desktop concorrente ${width}`;
    await c.gravar(remoto, versao++);
    await d.getByRole('button', { name: 'Revisar e salvar →' }).click();
    await expect(d.getByRole('heading', { name: 'Existe uma revisão mais recente' })).toBeVisible();
    await d.getByRole('button', { name: 'Reaplicar e revisar' }).click();
    await d.getByRole('button', { name: 'Revisar e salvar →' }).click();
    await expect(d.getByText(/Tasks afetadas: item-4/)).toBeVisible();
    await d.getByRole('button', { name: `Salvar r${versao + 1} e distribuir` }).click(); versao++;
    await expect(page.getByText(`Revisão r${versao} salva e disponível às tasks afetadas. Histórico anterior preservado.`)).toBeVisible();
    const h = await (await request.get(`${API}/api/matrix/demo/matriz/matrix`, { headers: c.headers })).json() as RevisaoMatrix[];
    expect(h.at(-1)!.documento.matriz!.celulas.find(x => x.id === 'imp-008')!.criterio).toBe(`Critério real ${width}`);
    expect(h.at(-1)!.documento.matriz!.celulas.find(x => x.id === 'imp-0-2')!.criterio).toBe(`Desktop concorrente ${width}`);
    c.matriz = h.at(-1)!.documento;
    await page.reload();
    await page.getByRole('button', { name: 'Mapa da aplicação ↗' }).click();
    await expect(page.getByRole('heading', { name: 'Componentes e relações de impacto' })).toBeVisible();
    await page.getByRole('button', { name: 'Histórico', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Histórico e autoria' }).first()).toBeVisible();
  }
  for (const colorScheme of ['light', 'dark'] as const) for (const width of [1440, 390]) {
    await page.emulateMedia({ colorScheme }); await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await page.goto('/board?aba=board');
    await page.getByRole('button', { name: 'Impactos', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Impactos da task · recorte' })).toBeVisible();
    await page.getByRole('button', { name: 'Avaliação × Page · imp-008' }).filter({ visible: true }).click();
    const cell = page.getByRole('dialog').last();
    await expect(cell.getByRole('textbox', { name: 'Como validar', exact: true })).toBeEnabled();
    await cell.getByRole('button', { name: 'Fechar', exact: true }).click();
    await page.getByRole('button', { name: 'Avaliação × API · imp-1-2' }).filter({ visible: true }).click();
    await expect(page.getByRole('dialog').last().getByRole('textbox', { name: 'Como validar', exact: true })).toBeDisabled();
    await page.keyboard.press('Escape'); await page.keyboard.press('Escape');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  }
  // O papel vem da API real: o leitor consulta a mesma revisão e recebe 403 ao tentar gravar.
  const recusa = await request.put(`${API}/api/matrix/demo/matriz/matrix?aplicar=true`, {
    headers: { Authorization: `Bearer ${c.leitor.token}` }, data: { versaoEsperada: versao, documento: c.matriz },
  });
  expect(recusa.status()).toBe(403);
  const paginaLeitor = await page.context().browser()!.newPage();
  await paginaLeitor.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), c.leitor);
  for (const width of [1440, 390]) {
    await paginaLeitor.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await paginaLeitor.goto(new URL('/projetos?slug=matrix&visao=impactos', page.url()).href);
    await expect(paginaLeitor.getByText('Somente leitura · edição exige editor ou dono.')).toBeVisible();
    await paginaLeitor.getByRole('button', { name: 'Avaliação × Page · imp-008' }).filter({ visible: true }).click();
    await expect(paginaLeitor.getByRole('dialog').last().getByRole('textbox', { name: 'Como validar', exact: true })).toBeDisabled();
    await paginaLeitor.keyboard.press('Escape');
  }
  await paginaLeitor.close();
});
