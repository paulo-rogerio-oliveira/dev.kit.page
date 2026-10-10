import { expect, test } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/**
 * A gestão de usuários de ponta a ponta (US #405): o admin cria um dev com a senha temporária, o dev
 * entra com ela, é obrigado a trocá-la, cai na página inicial (onde fica o "Baixar o dev.kit") e não entra
 * no painel; o admin vê a situação mudar na lista.
 */
test('admin cria usuário → o usuário entra com a senha temporária → troca a senha → não entra no painel', async ({ page, request }) => {
  const login = `dev.e2e.${Date.now()}`;
  const senhaDoDev = 'senhaDoDevE2e2026';

  // 1. O admin entra e abre Usuários pelo dashboard.
  await garantirSenhaDefinitiva(request);
  await page.goto('/login');
  await page.getByLabel('Login').fill('admin');
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.getByRole('link', { name: 'Usuários' }).click();
  await expect(page.getByRole('heading', { name: 'Novo usuário' })).toBeVisible();

  // 2. Cria o dev: a senha temporária aparece uma vez.
  const formulario = page.getByRole('form', { name: 'Novo usuário' });
  await formulario.getByLabel('Login').fill(login);
  await formulario.getByLabel('Nome').fill('Dev do E2E');
  await formulario.getByLabel('Papel').selectOption('dev');
  await formulario.getByRole('button', { name: 'Criar usuário' }).click();
  const aviso = page.locator('.senha-temporaria');
  await expect(aviso).toContainText(`Usuário ${login} criado.`);
  const temporaria = (await aviso.locator('code').textContent())!.trim();
  expect(temporaria.length).toBeGreaterThanOrEqual(10);
  const linha = page.getByRole('row', { name: new RegExp(login.replaceAll('.', '\\.')) });
  await expect(linha).toContainText('Troca de senha pendente');
  await page.screenshot({ path: 'test-results/capturas/20-usuarios-senha-temporaria.png', fullPage: true });

  // A lista que a API devolve não traz a senha.
  const { token } = (await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json()) as { token: string };
  const lista = await (await request.get(`${API}/api/usuarios`, { headers: { Authorization: `Bearer ${token}` } })).text();
  expect(lista).toContain(login);
  expect(lista).not.toContain(temporaria);

  // 3. O dev entra com a temporária e é obrigado a trocá-la.
  await page.getByRole('button', { name: 'Sair' }).click();
  await page.goto('/login');
  await page.getByLabel('Login').fill(login);
  await page.getByLabel('Senha').fill(temporaria);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByRole('heading', { name: 'Defina a sua senha' })).toBeVisible();
  await page.getByLabel('Senha atual').fill(temporaria);
  await page.getByLabel(/^Nova senha/).fill(senhaDoDev);
  await page.getByLabel('Confirme a nova senha').fill(senhaDoDev);
  await page.getByRole('button', { name: 'Trocar senha' }).click();

  // 4. O dev cai na página inicial (o download), e o painel o devolve para lá.
  await expect(page.getByRole('heading', { level: 1 })).toContainText('agente de IA');
  await expect(page.getByRole('group', { name: 'Download do dev.kit' })).toBeVisible();
  await page.goto('/dashboard');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('agente de IA');
  await expect(page.getByRole('heading', { name: 'Quantidade de uso' })).toHaveCount(0);

  // A API confirma: a senha antiga não entra mais, a nova entra sem troca pendente, e o painel é 403.
  expect((await request.post(`${API}/api/auth/login`, { data: { login, senha: temporaria } })).status()).toBe(401);
  const doDev = await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaDoDev } });
  const sessao = (await doDev.json()) as { token: string; deveTrocarSenha: boolean; papel: string };
  expect(sessao.papel).toBe('dev');
  expect(sessao.deveTrocarSenha).toBe(false);
  expect((await request.get(`${API}/api/dashboard/quantidade`, { headers: { Authorization: `Bearer ${sessao.token}` } })).status()).toBe(403);
});
