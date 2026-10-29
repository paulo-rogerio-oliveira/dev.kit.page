import { expect, test } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, CODIGO_DE_REGISTRO, NOVA_SENHA } from './ambiente';

test('Uso consulta a API vazia, apresenta telemetria e recupera uma falha', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await garantirSenhaDefinitiva(request);
  await page.goto('/login');
  await page.getByLabel('Login').fill('admin');
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByText(/Nenhuma máquina enviou telemetria ainda/)).toBeVisible();
  await expect(page.getByTestId('saude-Tasks paradas sem processo')).toHaveText('0');
  await page.screenshot({ path: 'test-results/capturas/uso-vazio.png' });

  const maquinaId = `uso-${Date.now()}`;
  const registro = await request.post(`${API}/api/maquinas/registrar`, {
    data: { maquinaId, versaoDevKit: '136', codigoRegistro: CODIGO_DE_REGISTRO },
  });
  expect(registro.ok()).toBeTruthy();
  const { chave } = await registro.json();
  const lote = await request.post(`${API}/api/telemetria/lote`, {
    headers: { 'X-Machine-Key': chave },
    data: { versao: 'v1', maquinaId, versaoDevKit: '136', eventos: [
      { eventId: `${maquinaId}-1`, tipo: 'SituacaoDaTask', sessaoId: 'uso-demo', quantidade: 1, detalhe: 'parada-sem-processo', em: new Date().toISOString() },
    ] },
  });
  expect(lote.status()).toBe(202);
  await page.reload();
  await expect(page.getByTestId('saude-Tasks paradas sem processo')).toHaveText(/1/);
  await expect(page.getByText(/Nenhuma máquina enviou telemetria ainda/)).toHaveCount(0);
  await page.getByRole('combobox', { name: 'Máquina', exact: true }).selectOption({ index: 1 });
  await expect(page.getByTestId('saude-Tasks paradas sem processo')).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/uso-com-dados.png' });

  // A resposta de falha é induzida; as consultas e a recuperação usam a API real.
  await page.route('**/api/dashboard/utilidade?**', (rota) => rota.fulfill({
    status: 503, contentType: 'application/problem+json', body: JSON.stringify({ detail: 'Consulta indisponível.' }),
  }));
  await page.getByLabel('Período').selectOption('7');
  await expect(page.getByRole('alert')).toContainText('Consulta indisponível.');
  await expect(page.getByRole('heading', { name: 'Saúde agora' })).toHaveCount(0);
  await page.screenshot({ path: 'test-results/capturas/uso-erro.png' });
  await page.unroute('**/api/dashboard/utilidade?**');
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  await expect(page.getByTestId('saude-Tasks paradas sem processo')).toHaveText(/1/);
  await expect(page.getByRole('alert')).toHaveCount(0);
  await page.screenshot({ path: 'test-results/capturas/uso-recuperado.png' });
});
