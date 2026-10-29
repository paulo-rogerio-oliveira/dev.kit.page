import { expect, test } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, CODIGO_DE_REGISTRO, NOVA_SENHA } from './ambiente';

test('Uso consulta uma empresa vazia, apresenta telemetria e recupera uma falha', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await garantirSenhaDefinitiva(request);
  // Os cenários compartilham a base. Uma empresa própria mantém o vazio real sem apagar dados dos demais.
  const marca = `${Date.now()}-${test.info().retry}`;
  const admin = await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } });
  expect(admin.ok()).toBeTruthy();
  const { token } = await admin.json();
  const empresaCriada = await request.post(`${API}/api/empresas`, {
    headers: { Authorization: `Bearer ${token}` }, data: { nome: `Uso ${marca}`, plano: 'Empresarial', assentos: 2 },
  });
  expect(empresaCriada.ok()).toBeTruthy();
  const empresa = await empresaCriada.json();
  const login = `gestor.uso.${marca}`;
  const gestor = await request.post(`${API}/api/empresas/${empresa.id}/gestores`, {
    headers: { Authorization: `Bearer ${token}` }, data: { login },
  });
  expect(gestor.ok()).toBeTruthy();
  const { senhaInicial } = await gestor.json();
  const inicial = await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaInicial } });
  expect(inicial.ok()).toBeTruthy();
  const { token: tokenInicial } = await inicial.json();
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    headers: { Authorization: `Bearer ${tokenInicial}` }, data: { senhaAtual: senhaInicial, novaSenha: NOVA_SENHA },
  });
  expect(troca.ok()).toBeTruthy();
  await page.goto('/login');
  await page.getByLabel('Login').fill(login);
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByText(/Nenhum colaborador aderiu ainda/)).toBeVisible();
  await expect(page.getByTestId('saude-Tasks paradas sem processo')).toHaveText('0');
  await page.screenshot({ path: 'test-results/capturas/uso-vazio.png' });

  const maquinaId = `uso-${marca}`;
  const colaborador = `Colaborador US751 ${marca}`;
  const registro = await request.post(`${API}/api/maquinas/registrar`, {
    data: { maquinaId, versaoDevKit: '136', codigoRegistro: CODIGO_DE_REGISTRO, codigoEmpresa: empresa.codigoDeAdesao, colaborador },
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
  await expect(page.getByTestId('saude-Tasks paradas sem processo')).toHaveText(/^1\s*\+1 em 24 h$/);
  await expect(page.getByText(/Nenhum colaborador aderiu ainda/)).toHaveCount(0);
  await page.getByRole('combobox', { name: 'Colaborador', exact: true }).selectOption({ label: colaborador });
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
  await expect(page.getByTestId('saude-Tasks paradas sem processo')).toHaveText(/^1\s*\+1 em 24 h$/);
  await expect(page.getByRole('alert')).toHaveCount(0);
  await page.screenshot({ path: 'test-results/capturas/uso-recuperado.png' });
});
