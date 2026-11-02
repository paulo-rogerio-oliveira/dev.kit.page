import { expect, test } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

test('o login de verdade aparece como o último login na lista do admin', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await garantirSenhaDefinitiva(request);
  const admin = await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } });
  expect(admin.ok()).toBeTruthy();
  const { token } = await admin.json();
  const login = `dev.login.${Date.now()}`;
  const criado = await request.post(`${API}/api/usuarios`, {
    headers: { Authorization: `Bearer ${token}` }, data: { login, nome: 'Dev do último login', papel: 'dev', empresaId: null },
  });
  expect(criado.ok()).toBeTruthy();
  const { senhaTemporaria } = await criado.json();

  await page.goto('/login');
  await page.getByLabel('Login').fill('admin');
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page).toHaveURL(/dashboard/);
  await page.goto('/usuarios');

  // Ainda não entrou: a lista diz "nunca".
  const linha = page.getByRole('row').filter({ hasText: login });
  await expect(page.getByRole('columnheader', { name: 'Último login' })).toBeVisible();
  await expect(linha.getByRole('cell', { name: 'nunca' })).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/ultimo-login-nunca.png' });

  // A mesma rota do site e do dev.kit: o login com a senha certa grava a data, e a lista a mostra sem mais nada.
  const entrou = await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaTemporaria } });
  expect(entrou.ok()).toBeTruthy();
  await page.reload();
  await expect(linha.getByRole('cell', { name: /^\d{2}\/\d{2}\/\d{4}/ })).toBeVisible();
  await expect(linha.getByRole('cell', { name: 'nunca' })).toHaveCount(0);
  await page.screenshot({ path: 'test-results/capturas/ultimo-login-data.png' });
});
