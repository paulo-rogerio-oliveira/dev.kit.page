import { expect, test } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, CODIGO_DE_REGISTRO, NOVA_SENHA } from './ambiente';

const MAQUINA = 'e2e0maquina0000000000000000000405';

/** A última versão como a API a devolve da GitHub Release — a API do e2e não tem REPO_KEY, então o teste responde por ela. */
const VERSAO = {
  versao: 136, tag: 'v136', nome: 'dev.kit 136', publicadaEm: '2026-10-09T18:00:00Z', destaques: ['"Precisa de você" no Board'],
  tamanhoBytes: 52_428_800, sha256: 'ab'.repeat(32), urlDownload: '/api/versoes/136/download', loginObrigatorio: false,
};

/**
 * As telas da US #405 no navegador, com as capturas da entrega: a landing repaginada com o "Baixar o
 * dev.kit" mostrando a versão (e baixando com o token), e os cartões Impasses e Árbitro do painel
 * alimentados por um lote de telemetria DE VERDADE (os tipos novos pela ingestão da API).
 */
test('landing com o download da versão → lote com impasse e árbitro → cartões no painel', async ({ page, request }) => {
  // 1. A landing com a versão (as rotas de versão respondidas pelo teste, como o GitHub responderia pela API).
  // O CORS que a API daria (a web roda em outra porta): o preflight do Authorization e a exposição do nome do arquivo.
  const cors = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': 'Authorization, Accept', 'Access-Control-Expose-Headers': 'Content-Disposition' };
  const downloads: (string | null)[] = [];
  await page.route(`${API}/api/versoes/ultima`, (rota) => rota.fulfill({ json: VERSAO, headers: cors }));
  await page.route(`${API}/api/versoes/136/download`, async (rota) => {
    if (rota.request().method() === 'OPTIONS') return rota.fulfill({ status: 204, headers: cors });
    downloads.push(rota.request().headers().authorization ?? null);
    return rota.fulfill({
      body: Buffer.from([80, 75, 5, 6, ...new Array<number>(18).fill(0)]),
      headers: { ...cors, 'Content-Type': 'application/zip', 'Content-Disposition': 'attachment; filename=devkit-136.zip' },
    });
  });
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/');
  const baixar = page.getByRole('group', { name: 'Download do dev.kit' });
  await expect(baixar.getByRole('button', { name: 'Baixar o dev.kit 136' })).toBeVisible();
  await expect(baixar).toContainText('versão 136 · 09/10/2026 · 50,0 MB');
  await page.screenshot({ path: 'test-results/capturas/30-landing-nova.png', fullPage: true });
  await page.locator('#inicio').screenshot({ path: 'test-results/capturas/31-hero-baixar-versao.png' });

  // 2. Sem login, baixar leva ao login; com ele, o zip vem com o Bearer.
  await baixar.getByRole('button', { name: 'Baixar o dev.kit 136' }).click();
  await expect(page.getByText('Entre para baixar o dev.kit.')).toBeVisible();
  await garantirSenhaDefinitiva(request);
  await page.getByLabel('Login').fill('admin');
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  const arquivo = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Baixar o dev.kit 136' }).click();
  expect((await arquivo).suggestedFilename()).toBe('devkit-136.zip');
  expect(downloads).toHaveLength(1);
  expect(downloads[0]).toMatch(/^Bearer /);

  // 3. Uma máquina manda o impasse e o árbitro no contrato v1 — e a API os grava (nenhum ignorado).
  const registro = await request.post(`${API}/api/maquinas/registrar`, { data: { maquinaId: MAQUINA, versaoDevKit: '137', codigoRegistro: CODIGO_DE_REGISTRO } });
  const { chave } = (await registro.json()) as { chave: string };
  const em = new Date().toISOString();
  const evento = (id: string, tipo: string, valor: number, detalhe: string) => ({ eventId: id, tipo, sessaoId: 's405', quantidade: 1, valor, detalhe, em });
  const envio = await request.post(`${API}/api/telemetria/lote`, {
    headers: { 'X-Machine-Key': chave },
    data: {
      versao: 'v1', maquinaId: MAQUINA, versaoDevKit: '137', eventos: [
        evento('u405-i1', 'ImpasseDetectado', 35, 'mensagem-parada'),
        evento('u405-i2', 'ImpasseDetectado', 20, 'objetivo-parado'),
        evento('u405-i3', 'ImpasseResolvido', 8, 'arbitro-reagiu'),
        evento('u405-i4', 'ImpasseResolvido', 25, 'dev-falou'),
        evento('u405-r1', 'ArbitroAgiu', 1, 'cobrou|Arquivos alterados no turno'),
        evento('u405-r2', 'ArbitroAgiu', 1, 'cobrou|Arquivos alterados no turno'),
        evento('u405-r3', 'ArbitroAgiu', 2, 'cobrou|Idioma e fluxo'),
        evento('u405-r4', 'ArbitroAgiu', 2, 'corrigido|Arquivos alterados no turno'),
        evento('u405-r5', 'ArbitroAgiu', 3, 'corrigido|Idioma e fluxo'),
        evento('u405-r6', 'ArbitroAgiu', 4, 'escalou-ao-dev'),
      ],
    },
  });
  expect(envio.status()).toBe(202);
  expect(((await envio.json()) as { ignorados: number }).ignorados).toBe(0);

  // 4. Os cartões Impasses e Árbitro no painel (filtrados pela máquina: os números são os do lote).
  await page.goto('/dashboard');
  await page.getByRole('combobox').first().selectOption({ label: 'máquina e2e0maqu (dev.kit 137)' });
  const impasses = page.getByRole('article', { name: 'Impasses' });
  await expect(impasses.getByTestId('kpi-Impasses detectados')).toHaveText('2');
  await expect(impasses.getByTestId('kpi-Tempo médio parado')).toHaveText('27,5 min');
  await expect(impasses.getByTestId('kpi-Tempo médio até destravar')).toHaveText('16,5 min');
  const arbitro = page.getByRole('article', { name: 'Árbitro' });
  await expect(arbitro.getByTestId('kpi-Cobranças do árbitro')).toHaveText('3');
  await expect(arbitro.getByTestId('kpi-Taxa de correção')).toHaveText(/66,7\s?%/);
  await expect(arbitro.getByTestId('kpi-Escaladas ao dev')).toHaveText('1');
  await page.locator('.cartoes-do-fluxo').scrollIntoViewIfNeeded();
  await page.locator('.cartoes-do-fluxo').screenshot({ path: 'test-results/capturas/32-dashboard-impasses-arbitro.png' });
  await page.getByRole('region', { name: 'Qualidade de uso' }).screenshot({ path: 'test-results/capturas/33-dashboard-qualidade.png' });
});
