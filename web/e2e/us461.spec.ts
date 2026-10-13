import { expect, test, type APIRequestContext } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/** A vitrine do básico (o e2e não tem o GitHub: a rota é respondida aqui, como no us425.spec.ts). */
const VITRINE = {
  versao: 2, publicadaEm: '2026-10-10T12:00:00Z',
  grupos: [{ tecnologia: 'Geral', itens: [{ tipo: 'skill', nome: 'commitar-e-enviar', descricao: 'Commit e push do branch da task.', tecnologia: 'Geral', versao: 1 }] }],
};

/** O envelope devkit-fluxo que o `devcli fluxos --exportar` grava: a task e uma etapa que roda a skill do catálogo. */
function fluxoQueUsa(skill: string) {
  const no = (id: string, kind: string, title: string, skillName: string, x: number) => ({
    id, kind, title, caption: '', skillName, hookId: 0, plan: '', agentKind: '', agentModel: '', argument: '', isEvaluator: false,
    createIfMissing: false, opensTask: false, criterio: '', techPlanId: 0, x, y: 40,
  });
  return {
    formato: 'devkit-fluxo', versao: 1, nome: `entrega ${skill}`, descricao: 'Compila e entrega, pelo catálogo do e2e.',
    desenho: {
      nodes: [no('task', 'Task', 'Task', '', 32), no('e1', 'Step', skill, skill, 332)],
      links: [{ from: 'task', fromPort: 'next', to: 'e1', kind: 'Sequence' }],
      parameters: [], workItemOptional: true, repositories: '', schedules: [],
    },
    agentes: [],
    planos: [],
  };
}

/** Um dev da empresa com a senha definitiva — o token dele (a senha temporária exige a troca antes de qualquer rota). */
async function tokenDeDev(request: APIRequestContext, admin: string, empresaId: number, login: string): Promise<string> {
  const criado = await request.post(`${API}/api/usuarios`, {
    data: { login, nome: login, papel: 'dev', empresaId }, headers: { Authorization: `Bearer ${admin}` },
  });
  expect(criado.ok()).toBeTruthy();
  const { senhaTemporaria } = (await criado.json()) as { senhaTemporaria: string };
  const inicial = (await (await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaTemporaria } })).json()) as { token: string };
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    data: { senhaAtual: senhaTemporaria, novaSenha: NOVA_SENHA }, headers: { Authorization: `Bearer ${inicial.token}` },
  });
  expect(troca.ok()).toBeTruthy();
  return ((await troca.json()) as { token: string }).token;
}

/**
 * A aba Catálogos de ponta a ponta (US #461), com as capturas da entrega: o admin cria um catálogo, relaciona uma
 * empresa, cria uma skill e um fluxo que a usa, publica, confere a release e o manifesto na aba Releases — e a API
 * confirma que o dev da empresa relacionada recebe o catálogo em /api/catalogos/meus, e o de outra empresa não.
 */
test('admin cria catálogo → relaciona empresa → skill e fluxo → publica → release e manifesto → só a empresa recebe', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.route(`${API}/api/catalogo/base/vitrine`, (rota) => rota.fulfill({ json: VITRINE, headers: { 'Access-Control-Allow-Origin': '*' } }));

  const marca = Date.now();
  const slug = `e2e-${marca}`;
  const skill = `compilar-e2e-${marca}`;

  // 0. As duas empresas, pela API: a que vai receber o catálogo e a que não.
  await garantirSenhaDefinitiva(request);
  const { token } = (await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json()) as { token: string };
  const criarEmpresa = async (nome: string) => (await (await request.post(`${API}/api/empresas`, {
    data: { nome, plano: 'Empresarial', assentos: 3 }, headers: { Authorization: `Bearer ${token}` },
  })).json()) as { id: number; nome: string };
  const relacionada = await criarEmpresa(`Empresa Catálogo ${marca}`);
  const outra = await criarEmpresa(`Empresa Fora ${marca}`);

  // 1. O admin entra e abre a aba Catálogos.
  await page.goto('/login');
  await page.getByLabel('Login').fill('admin');
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/dashboard$/);
  await page.goto('/catalogo');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Catálogo do dev.kit');
  await page.screenshot({ path: 'test-results/capturas/60-catalogo-vitrine.png', fullPage: true });
  await page.getByRole('tab', { name: 'Catálogos' }).click();
  await expect(page.getByRole('region', { name: 'Catálogos cadastrados' })).toBeVisible();

  // 2. Cria o catálogo pelo painel lateral: o detalhe dele abre.
  const novo = page.getByRole('form', { name: 'Novo catálogo' });
  await novo.getByLabel('Slug').fill(slug);
  await novo.getByLabel('Nome', { exact: true }).fill(`Catálogo E2E ${marca}`);
  await novo.getByLabel('Descrição').fill('Criado pelo cenário ponta a ponta.');
  await novo.getByRole('button', { name: 'Criar catálogo' }).click();
  const empresas = page.getByRole('region', { name: 'Empresas' });
  await expect(empresas).toContainText('Nenhuma empresa relacionada');

  // 3. Relaciona a empresa.
  await empresas.getByLabel('Empresa a relacionar').selectOption(String(relacionada.id));
  await empresas.getByRole('button', { name: 'Relacionar' }).click();
  await expect(empresas.getByRole('button', { name: `Desrelacionar ${relacionada.nome}` })).toBeVisible();

  // 4. Cria a skill pelo formulário.
  await page.getByRole('button', { name: 'Nova skill' }).click();
  const editorDaSkill = page.getByRole('form', { name: 'Nova skill' });
  await editorDaSkill.getByLabel('Nome', { exact: true }).fill(skill);
  await editorDaSkill.getByLabel('Descrição').fill('Compila o projeto do e2e.');
  await editorDaSkill.getByLabel('Categoria').fill('E2E');
  await editorDaSkill.getByLabel('Conteúdo').fill('# Compilar\n\nRode o build do projeto e confira os avisos.');
  await page.screenshot({ path: 'test-results/capturas/61-catalogos-editor-skill.png', fullPage: true });
  await editorDaSkill.getByRole('button', { name: 'Salvar rascunho' }).click();
  await expect(page.getByRole('status').filter({ hasText: `${skill} criado como rascunho` })).toBeVisible();

  // 5. Cria o fluxo que usa a skill: o JSON exportado do dev.kit, com a prévia dos passos.
  await page.getByRole('button', { name: 'Novo fluxo' }).click();
  const editorDoFluxo = page.getByRole('form', { name: 'Novo fluxo' });
  await editorDoFluxo.getByLabel('JSON do fluxo').fill(JSON.stringify(fluxoQueUsa(skill), null, 2));
  await expect(editorDoFluxo.getByRole('list', { name: 'Prévia dos passos' })).toContainText(`e1 → Step → ${skill}`);
  await page.screenshot({ path: 'test-results/capturas/62-catalogos-editor-fluxo.png', fullPage: true });
  await editorDoFluxo.getByRole('button', { name: 'Salvar rascunho' }).click();
  await expect(page.getByRole('status').filter({ hasText: 'Fluxo' }).filter({ hasText: 'criado como rascunho' })).toBeVisible();
  const itens = page.getByRole('region', { name: 'Itens' });
  await expect(itens.getByRole('row')).toHaveCount(3);
  await page.screenshot({ path: 'test-results/capturas/63-catalogos-detalhe.png', fullPage: true });

  // 6. Publica: o relatório mostra as duas mudanças e nenhum problema.
  await itens.getByRole('button', { name: 'Publicar' }).click();
  const publicacao = page.getByRole('region', { name: `Publicar Catálogo E2E ${marca}` });
  await expect(publicacao.getByRole('list', { name: 'Mudanças' })).toContainText(`skill:${skill}`);
  await expect(publicacao.getByRole('list', { name: 'Problemas' })).toHaveCount(0);
  await page.screenshot({ path: 'test-results/capturas/64-catalogos-publicar.png', fullPage: true });
  await publicacao.getByRole('button', { name: 'Confirmar publicação' }).click();

  // 7. A aba Releases do catálogo: a v1 é a atual, e o manifesto traz a skill e o fluxo.
  await expect(page.getByRole('tab', { name: 'Releases' })).toHaveAttribute('aria-selected', 'true');
  const atual = page.getByRole('article', { name: `Release atual ${slug}-v1` });
  await expect(atual).toBeVisible();
  await expect(atual.getByText('Atual')).toBeVisible();
  await atual.getByRole('button', { name: 'Ver manifesto' }).click();
  const manifesto = page.getByRole('dialog', { name: `Manifesto da ${slug}-v1` });
  await expect(manifesto).toContainText(skill);
  await expect(manifesto).toContainText('fluxo');
  await page.screenshot({ path: 'test-results/capturas/65-catalogo-releases.png', fullPage: true });

  // 8. A API: o dev da empresa relacionada recebe o catálogo; o de outra empresa, só o básico.
  const doRelacionado = await tokenDeDev(request, token, relacionada.id, `dev.cat.${marca}@empresa.com`);
  const doOutro = await tokenDeDev(request, token, outra.id, `dev.fora.${marca}@empresa.com`);
  const meus = async (t: string) => {
    const resposta = await request.get(`${API}/api/catalogos/meus`, { headers: { Authorization: `Bearer ${t}` } });
    expect(resposta.ok()).toBeTruthy();
    return (await resposta.json()) as { slug: string; versao: number; basico: boolean }[];
  };
  const vistosPeloRelacionado = await meus(doRelacionado);
  expect(vistosPeloRelacionado.find((c) => c.slug === slug)).toMatchObject({ versao: 1, basico: false });
  expect(vistosPeloRelacionado.some((c) => c.basico)).toBeTruthy();
  expect((await meus(doOutro)).map((c) => c.slug)).not.toContain(slug);

  // 9. As telas de Empresas e Usuários no visual dos prints, com os dados do cenário (as capturas da entrega).
  await page.goto('/empresas');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Empresas');
  await expect(page.getByText(relacionada.nome)).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/66-empresas.png', fullPage: true });
  await page.goto('/usuarios');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Usuários');
  await expect(page.getByText(`dev.cat.${marca}@empresa.com`).first()).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/67-usuarios.png', fullPage: true });
});
