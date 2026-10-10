import { expect, test, type APIRequestContext } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/** A vitrine do catálogo básico como a API a monta das releases catalogo-vN (o e2e não tem o GitHub: a rota é respondida aqui). */
const VITRINE = {
  versao: 3, publicadaEm: '2026-10-10T12:00:00Z',
  grupos: [
    { tecnologia: 'Geral', itens: [
      { tipo: 'skill', nome: 'commitar-e-enviar', descricao: 'Commit e push do branch da task.', tecnologia: 'Geral', versao: 2 },
      { tipo: 'plan', nome: 'geral', descricao: 'As regras gerais do agente.', tecnologia: 'Geral', versao: 1 },
      { tipo: 'fluxo', nome: 'planejar-e-executar', descricao: 'Planeja, implementa e avalia.', tecnologia: 'Geral', versao: 1 },
    ] },
    { tecnologia: '.NET', itens: [
      { tipo: 'skill', nome: 'testar-dotnet', descricao: 'Roda a suíte do projeto alterado.', tecnologia: '.NET', versao: 3 },
    ] },
  ],
};

/** O gestor da empresa com a senha definitiva (o convite traz a inicial, de troca obrigatória). */
async function gestorDefinitivo(request: APIRequestContext, token: string, empresa: number, login: string) {
  const convite = await request.post(`${API}/api/empresas/${empresa}/gestores`, { data: { login }, headers: { Authorization: `Bearer ${token}` } });
  expect(convite.ok()).toBeTruthy();
  const { senhaInicial } = (await convite.json()) as { senhaInicial: string };
  const inicial = (await (await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaInicial } })).json()) as { token: string };
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    data: { senhaAtual: senhaInicial, novaSenha: NOVA_SENHA }, headers: { Authorization: `Bearer ${inicial.token}` },
  });
  expect(troca.ok()).toBeTruthy();
}

/**
 * As telas da US #425 no navegador, com as capturas da entrega: o catálogo e os planos na landing, a vitrine pública,
 * a adesão por código, a definição de senha, a compra individual (com o gateway simulado) e o gestor administrando a
 * empresa dele (usuários, código, política de login e catálogo da empresa).
 */
test('catálogo e planos → vitrine → adesão → definir senha → compra → o gestor administra a empresa', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.route(`${API}/api/catalogo/base/vitrine`, (rota) =>
    rota.fulfill({ json: VITRINE, headers: { 'Access-Control-Allow-Origin': '*' } }));

  // 1. A landing com as seções novas.
  await page.goto('/');
  await page.locator('#catalogo').scrollIntoViewIfNeeded();
  await page.locator('#catalogo').screenshot({ path: 'test-results/capturas/40-landing-catalogo.png' });
  await page.locator('#planos').scrollIntoViewIfNeeded();
  await expect(page.locator('#planos')).toContainText('Testar grátis por 14 dias');
  await page.locator('#planos').screenshot({ path: 'test-results/capturas/41-landing-planos.png' });

  // 2. A vitrine pública do catálogo básico, agrupada por tecnologia.
  await page.goto('/catalogo');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Catálogo do dev.kit');
  await expect(page.getByText('testar-dotnet')).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/42-catalogo-vitrine.png', fullPage: true });

  // 3. O admin cria a empresa; o dev adere com o código e o e-mail (o link vai por e-mail — no e2e, para o log).
  await garantirSenhaDefinitiva(request);
  const { token } = (await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json()) as { token: string };
  const empresa = (await (await request.post(`${API}/api/empresas`, {
    data: { nome: `Empresa US425 ${Date.now()}`, plano: 'Empresarial', assentos: 3 }, headers: { Authorization: `Bearer ${token}` },
  })).json()) as { id: number; codigoDeAdesao: string };
  await page.goto('/aderir');
  await page.getByLabel('Código de adesão').fill(empresa.codigoDeAdesao);
  await page.getByLabel('E-mail').fill(`ana.${Date.now()}@empresa.com`);
  await page.getByLabel('Nome').fill('Ana');
  await page.getByRole('button', { name: 'Aderir' }).click();
  await expect(page.getByRole('status')).toContainText('link');
  await page.screenshot({ path: 'test-results/capturas/43-adesao-feita.png', fullPage: true });

  // 4. O link de definir a senha: o token inválido (ou já usado) é recusado com a mensagem da API.
  await page.goto('/definir-senha?token=token-que-nao-existe');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Defina a sua senha');
  await page.screenshot({ path: 'test-results/capturas/44-definir-senha.png', fullPage: true });

  // 5. A compra individual: sem gateway configurado, o checkout simulado conclui na página do dev.kit.page.
  await page.goto('/comprar');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Assinar o dev.kit');
  await page.screenshot({ path: 'test-results/capturas/45-comprar.png', fullPage: true });
  await page.getByLabel('E-mail').fill(`individual.${Date.now()}@exemplo.com`);
  await page.getByLabel('Nome').fill('Pessoa Individual');
  await page.getByRole('button', { name: 'Ir para o pagamento' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Compra concluída');
  await page.screenshot({ path: 'test-results/capturas/46-compra-concluida.png', fullPage: true });

  // 6. O gestor entra e administra a empresa dele: assentos, código, política de login e usuários.
  const gestor = `gestor.us425.${Date.now()}`;
  await gestorDefinitivo(request, token, empresa.id, gestor);
  await page.goto('/login');
  await page.getByLabel('Login').fill(gestor);
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/(dashboard|conta)$/);
  await page.goto('/empresa/usuarios');
  await expect(page.getByTestId('assentos')).toContainText('de 3 assentos ocupados');
  await expect(page.getByRole('row', { name: /Ana/ })).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/47-empresa-usuarios.png', fullPage: true });

  await page.goto('/empresa/catalogo');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Catálogo da empresa');
  await page.screenshot({ path: 'test-results/capturas/48-empresa-catalogo.png', fullPage: true });

  await page.goto('/conta');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Sua conta');
  await page.screenshot({ path: 'test-results/capturas/49-conta.png', fullPage: true });
});
