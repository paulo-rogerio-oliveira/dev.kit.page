import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test, type Page, type Route } from '@playwright/test';
import type { RevisaoMatrix } from '../src/api/matrix';
import { prepararMatrix } from './matrix-cenario';
import { API } from './ambiente';

async function abrirEditor(page: Page, largura: number) {
  if (largura === 390) await page.getByRole('combobox', { name: 'Componente', exact: true }).selectOption('componente-1');
  await page.getByRole('button', { name: 'Avaliação × Page · imp-008' }).filter({ visible: true }).click();
  if (largura === 390) await page.getByRole('button', { name: 'Abrir editor completo →' }).click();
  return page.getByRole('dialog').last();
}

test('Matrix: falha de rede, credencial recusada e retomada preservam o rascunho nas duas larguras', async ({ page, request }) => {
  const c = await prepararMatrix(request);
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), c.sessao);
  const capturas = fileURLToPath(new URL('../../../apoio-capturas-matrix-retomada/', import.meta.url));
  mkdirSync(capturas, { recursive: true });
  let versao = 4;
  async function historico() {
    const resposta = await request.get(`${API}/api/matrix/demo/matriz/matrix`, { headers: c.headers });
    expect(resposta.ok()).toBeTruthy();
    return await resposta.json() as RevisaoMatrix[];
  }
  for (const largura of [1440, 390]) {
    await page.setViewportSize({ width: largura, height: largura === 390 ? 980 : 1100 });
    await page.goto('/projetos?slug=matrix&visao=impactos');
    const editor = await abrirEditor(page, largura);
    const criterio = `Retomada com rascunho preservado em ${largura} px`;
    await editor.getByRole('textbox', { name: 'Como validar', exact: true }).fill(criterio);
    // Interrompe a rede do navegador, sem mock de resposta e sem desligar a API.
    await page.context().setOffline(true);
    try {
      await editor.getByRole('button', { name: 'Revisar e salvar →' }).click();
      await expect(editor.getByRole('alert').filter({ hasText: 'Sem conexão.' })).toBeVisible();
      await expect(editor.getByRole('textbox', { name: 'Como validar', exact: true })).toHaveValue(criterio);
      expect((await historico()).length).toBe(versao);
      await page.screenshot({ path: resolve(capturas, `rede-${largura}.png`), fullPage: true });
    } finally { await page.context().setOffline(false); }

    // Recarrega a página: a edição deve vir do rascunho persistido, não do estado React.
    await page.reload();
    const retomado = await abrirEditor(page, largura);
    await expect(retomado.getByRole('textbox', { name: 'Como validar', exact: true })).toHaveValue(criterio);
    await retomado.getByRole('button', { name: 'Revisar e salvar →' }).click();
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByText('Tasks afetadas: item-4', { exact: true })).toBeVisible();

    // A API real recebe credencial inválida apenas na tentativa de escrita.
    // Isto prova tratamento do 401 e retomada do salvamento; não simula login humano.
    const recusarCredencial = async (route: Route) => {
      if (route.request().method() === 'PUT' && route.request().url().includes('aplicar=true')) {
        await route.continue({ headers: { ...route.request().headers(), authorization: 'Bearer credencial-ficticia-invalida' } });
      } else await route.continue();
    };
    await page.route('**/api/matrix/**', recusarCredencial);
    try {
      await page.getByRole('button', { name: `Salvar r${versao + 1} e distribuir` }).click();
      await expect(page.getByRole('alert').filter({ hasText: 'Sessão expirada.' })).toBeVisible();
      expect((await historico()).length).toBe(versao);
      await page.screenshot({ path: resolve(capturas, `autenticacao-${largura}.png`), fullPage: true });
    } finally { await page.unroute('**/api/matrix/**', recusarCredencial); }

    await page.getByRole('button', { name: `Salvar r${versao + 1} e distribuir` }).click();
    versao++;
    await expect(page.getByText(`Revisão r${versao} salva e disponível às tasks afetadas. Histórico anterior preservado.`)).toBeVisible();
    const revisoes = await historico();
    expect(revisoes.length).toBe(versao);
    expect(revisoes.at(-1)!.documento.matriz!.celulas.find(x => x.id === 'imp-008')!.criterio).toBe(criterio);
    expect(revisoes[3].documento.matriz!.celulas.find(x => x.id === 'imp-008')!.criterio).toBe(c.matriz.matriz!.celulas.find(x => x.id === 'imp-008')!.criterio);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
    await page.screenshot({ path: resolve(capturas, `retomada-${largura}.png`), fullPage: true });
  }
  console.log(`Capturas reais de rede, autenticação e retomada: ${capturas}`);
});
