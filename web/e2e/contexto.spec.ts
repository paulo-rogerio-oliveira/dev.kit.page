import { expect, test, type Page } from '@playwright/test';
import { devDefinitivo, gestorDefinitivo, tokenDoAdmin } from './admin';
import { API, NOVA_SENHA } from './ambiente';

async function entrar(page: Page, login: string) {
  await page.goto('/login');
  await page.getByLabel('Login').fill(login);
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  // Depois do Sair, o login devolve à página de onde se saiu quando ela é permitida (o /contexto, ao dev).
  await page.waitForURL(/\/(dashboard|conta|contexto)$/);
}

/**
 * O contexto do dev.kit (US #556) de ponta a ponta, com as capturas da entrega: o dev.kit do dev declara a equipe (pedido
 * pendente) e o gestor aprova; um AGENTE anota uma sugestão com o token do dev (o que o devcli contexto --anotar faz); o
 * dev grava a instrução dele, vê as duas com os selos e promove a sugestão; o gestor edita o contexto da empresa; o admin
 * do board vê e altera tudo; e o composto que o dev.kit sincroniza (GET /api/contexto?equipe=) traz os três níveis.
 */
test('o contexto de usuário, equipe e empresa: instrução × sugestão, por perfil', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const marca = Date.now();

  // 1. A empresa, o gestor e a dev (Ana).
  const admin = await tokenDoAdmin(request);
  const doAdmin = { Authorization: `Bearer ${admin}` };
  const empresa = (await (await request.post(`${API}/api/empresas`, {
    data: { nome: `Empresa Contexto ${marca}`, plano: 'Empresarial', assentos: 3 }, headers: doAdmin,
  })).json()) as { id: number; nome: string };
  const gestor = `gestor.ctx.${marca}`;
  const doGestor = { Authorization: `Bearer ${await gestorDefinitivo(request, admin, empresa.id, gestor)}` };
  const ana = `ana.ctx.${marca}`;
  const daAna = { Authorization: `Bearer ${await devDefinitivo(request, admin, empresa.id, ana, 'Ana Contexto')}` };

  // 2. O dev.kit da Ana sincroniza com a Equipe "vendas": vira um pedido pendente, sem acesso.
  const pedido = (await (await request.get(`${API}/api/contexto?equipe=vendas`, { headers: daAna })).json()) as { participacao: string };
  expect(pedido.participacao).toBe('pendente');
  expect((await request.get(`${API}/api/contexto/equipes/vendas`, { headers: daAna })).status()).toBe(403);
  expect((await request.put(`${API}/api/contexto`, {
    data: { nivel: 'equipe', chave: 'contexto.revisao', valor: 'Dois revisores', equipe: 'Vendas' }, headers: doGestor,
  })).ok()).toBeTruthy();

  // 3. Um agente, ao fim de uma execução, anota uma SUGESTÃO com o token da Ana; e uma credencial é recusada (422).
  expect((await request.put(`${API}/api/contexto`, {
    data: { nivel: 'usuario', chave: 'contexto.testes', valor: 'Rode só a suíte do projeto alterado', origem: 'agente' }, headers: daAna,
  })).ok()).toBeTruthy();
  const segredo = await request.put(`${API}/api/contexto`, {
    data: { nivel: 'usuario', chave: 'nota.acesso', valor: 'token ghp_1234567890abcdefghijABCDEFGHIJ', origem: 'agente' }, headers: daAna,
  });
  expect(segredo.status()).toBe(422);
  expect(await segredo.text()).not.toContain('ghp_');

  // 4. O gestor entra, vê o pedido da Ana e aprova; grava o contexto da empresa.
  await entrar(page, gestor);
  await page.goto('/contexto');
  await page.getByRole('button', { name: 'Aprovar Ana Contexto em vendas' }).click();
  await expect(page.getByText('Ana Contexto aprovado na equipe vendas.')).toBeVisible();
  const formulario = page.getByRole('form', { name: 'Gravar no contexto' });
  await formulario.getByLabel('Nível').selectOption('empresa');
  await formulario.getByLabel('Chave').fill('contexto.stack');
  await formulario.getByLabel('Texto').fill('APIs em .NET 10 (Minimal API) e telas em React 19; testes com xUnit e Vitest.');
  await formulario.getByRole('button', { name: 'Gravar' }).click();
  await expect(page.getByText('contexto.stack gravado no nível empresa.')).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/70-contexto-gestor.png', fullPage: true });
  await page.getByRole('button', { name: 'Sair' }).click();

  // 5. A Ana entra: grava a instrução dela, vê a sugestão do agente ao lado (selos) e o contexto da equipe e da empresa.
  await entrar(page, ana);
  await page.goto('/contexto');
  const doForm = page.getByRole('form', { name: 'Gravar no contexto' });
  await doForm.getByLabel('Chave').fill('contexto.testes');
  await doForm.getByLabel('Texto').fill('Prefiro testes pequenos, um por regra.');
  await doForm.getByRole('button', { name: 'Gravar' }).click();
  await expect(page.getByText('contexto.testes gravado no nível usuário.')).toBeVisible();
  const sugestao = page.getByRole('row', { name: /Rode só a suíte do projeto alterado/ });
  await expect(sugestao).toContainText('Sugestão');
  await expect(page.getByRole('row', { name: /Prefiro testes pequenos/ })).toContainText('Instrução');
  await expect(page.getByText('Dois revisores')).toBeVisible();
  await expect(page.getByText(/APIs em \.NET 10/)).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/71-contexto-dev-instrucao-e-sugestao.png', fullPage: true });

  // 6. Ela promove a sugestão: vira a instrução da mesma chave.
  await sugestao.getByRole('button', { name: /^Promover/ }).click();
  await expect(page.getByText('Sugestão contexto.testes promovida a instrução.')).toBeVisible();
  await expect(page.getByRole('row', { name: /Rode só a suíte do projeto alterado/ })).toContainText('Instrução');
  await page.screenshot({ path: 'test-results/capturas/72-contexto-dev-sugestao-promovida.png', fullPage: true });

  // 7. O que o dev.kit da Ana sincroniza agora: empresa → equipe → usuário.
  const composto = (await (await request.get(`${API}/api/contexto?equipe=vendas`, { headers: daAna })).json()) as {
    participacao: string; itens: { nivel: string; origem: string }[];
  };
  expect(composto.participacao).toBe('aprovado');
  expect(composto.itens.map((i) => i.nivel)).toEqual(['empresa', 'equipe', 'usuario']);
  await page.getByRole('button', { name: 'Sair' }).click();

  // 8. O admin do board escolhe a empresa, vê tudo — inclusive o contexto pessoal da Ana — e altera.
  await entrar(page, 'admin');
  await page.goto('/admin/contexto');
  await page.getByLabel('Empresa').selectOption({ label: empresa.nome });
  await expect(page.getByText('Prefiro testes pequenos, um por regra.').or(page.getByText('Rode só a suíte do projeto alterado'))).toBeVisible();
  const doAdminForm = page.getByRole('form', { name: 'Gravar no contexto' });
  await doAdminForm.getByLabel('Chave').fill('contexto.seguranca');
  await doAdminForm.getByLabel('Texto').fill('Nenhum segredo em código: Key Vault.');
  await doAdminForm.getByRole('button', { name: 'Gravar' }).click();
  await expect(page.getByText('contexto.seguranca gravado no nível empresa.')).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/73-contexto-admin-do-board.png', fullPage: true });
});
