import { expect, test } from '@playwright/test';
import { garantirSenhaDefinitiva, gestorDefinitivo } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/**
 * A US #491 no navegador, com as capturas da entrega: o que o dev.kit gravou na empresa (o catálogo de repositórios, as
 * convenções da empresa, do time e do usuário) auditado e editado pelo gestor em /empresa/conhecimento. Desde a US #556 as
 * convenções gravadas pelo dev.kit antigo (/api/empresa/convencoes) aparecem no contexto (/contexto).
 */
test('o gestor audita e edita o catálogo de repositórios e as convenções em seus níveis', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });

  // 1. A empresa e o gestor; o que o devcli teria gravado entra pela API, com o token do gestor.
  await garantirSenhaDefinitiva(request);
  const { token } = (await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json()) as { token: string };
  const empresa = (await (await request.post(`${API}/api/empresas`, {
    data: { nome: `Empresa US491 ${Date.now()}`, plano: 'Empresarial', assentos: 3 }, headers: { Authorization: `Bearer ${token}` },
  })).json()) as { id: number };
  const gestor = `gestor.us491.${Date.now()}`;
  const doGestor = { Authorization: `Bearer ${await gestorDefinitivo(request, token, empresa.id, gestor)}` };
  for (const data of [
    { origem: 'https://github.com/acme/vendas-api', descricao: 'API de vendas (.NET 8)' },
    { origem: 'https://github.com/acme/portal', descricao: 'Portal do cliente (Angular)' },
  ])
    expect((await request.post(`${API}/api/empresa/repositorios`, { data, headers: doGestor })).ok()).toBeTruthy();
  for (const data of [
    { nivel: 'empresa', chave: 'branch.feature', valor: 'feature/{equipe}/{id}', time: '' },
    { nivel: 'empresa', chave: 'branch.hotfix', valor: 'hotfix/{id}', time: '' },
    { nivel: 'time', chave: 'branch.feature', valor: 'vendas/{id}', time: 'vendas' },
    { nivel: 'usuario', chave: 'branch.bug', valor: 'bug/{id}', time: '' },
  ])
    expect((await request.put(`${API}/api/empresa/convencoes`, { data, headers: doGestor })).ok()).toBeTruthy();

  // 2. O gestor entra e audita o catálogo: caminho, descrição, autor e data.
  await page.goto('/login');
  await page.getByLabel('Login').fill(gestor);
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/(dashboard|conta)$/);
  await page.goto('/empresa/conhecimento');
  await expect(page.getByText('https://github.com/acme/vendas-api')).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/60-conhecimento-repositorios.png', fullPage: true });

  // 3. Corrige uma descrição pelo formulário.
  await page.getByRole('button', { name: 'Editar https://github.com/acme/vendas-api' }).click();
  const formulario = page.getByRole('form', { name: 'Descrever um repositório' });
  await formulario.getByLabel('Descrição').fill('API de vendas e pedidos (.NET 8)');
  await formulario.getByRole('button', { name: 'Gravar' }).click();
  await expect(page.getByText('https://github.com/acme/vendas-api gravado no catálogo da empresa.', { exact: true })).toBeVisible();
  await expect(page.getByText('API de vendas e pedidos (.NET 8)')).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/61-conhecimento-repositorio-editado.png', fullPage: true });

  // 4. As convenções nos três níveis — gravadas pela rota antiga — aparecem como instruções do contexto.
  await page.getByRole('link', { name: 'Contexto' }).click();
  await expect(page.getByText('vendas/{id}')).toBeVisible();
  await expect(page.getByText('bug/{id}')).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/62-conhecimento-convencoes.png', fullPage: true });
});
