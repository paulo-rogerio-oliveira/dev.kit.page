import { expect, test } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/**
 * A vitrine do básico como o manifesto a publica (o e2e não tem o GitHub: a rota é respondida aqui, como no us461.spec.ts).
 * Os plans vêm na tecnologia "Tech Plans" — o valor do contrato com o app —, e a tela mostra "Rules".
 */
const VITRINE = {
  versao: 3, publicadaEm: '2026-10-22T12:00:00Z',
  grupos: [
    { tecnologia: 'Azure DevOps', itens: [{ tipo: 'skill', nome: 'lancar-horas', descricao: 'Lançar as horas do dia nos work items trabalhados.', tecnologia: 'Azure DevOps', versao: 1 }] },
    { tecnologia: 'Git', itens: [{ tipo: 'skill', nome: 'commitar-e-enviar', descricao: 'Commit e push do branch da task.', tecnologia: 'Git', versao: 1 }] },
    { tecnologia: 'Tech Plans', itens: [
      { tipo: 'plan', nome: 'build-minimo', descricao: 'Compilar só o projeto alterado e os que dependem dele.', tecnologia: 'Tech Plans', versao: 1 },
      { tipo: 'plan', nome: 'logs', descricao: 'O log mais atualizado de build e de testes na entrega.', tecnologia: 'Tech Plans', versao: 1 },
    ] },
    { tecnologia: 'Fluxos', itens: [{ tipo: 'fluxo', nome: 'tarefa-avulsa', descricao: 'Uma tarefa sem work item, com o avaliador.', tecnologia: 'Fluxos', versao: 1 }] },
  ],
};

/**
 * A revisão de conceitos: a landing fala em Rules e na escolha do provedor cognitivo (o Codex é um dos provedores), e o
 * catálogo — a vitrine pública e a aba Catálogos do admin — mostra o plan como Rule, sem mudar o contrato (`plan`).
 */
test('landing e catálogo com os nomes do produto: Rules no lugar de Tech Plans e a escolha do provedor cognitivo', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.route(`${API}/api/catalogo/base/vitrine`, (rota) => rota.fulfill({ json: VITRINE, headers: { 'Access-Control-Allow-Origin': '*' } }));

  // 1. A landing: o recurso 03 é o provedor cognitivo, e nenhum texto fala em Tech Plan.
  await page.goto('/');
  await expect(page.locator('body')).not.toContainText(/tech ?plans?/i);
  await page.getByRole('navigation', { name: 'Recursos em destaque' }).getByRole('link', { name: '03 Provedor cognitivo' }).click();
  const provedor = page.locator('#recurso-provedor');
  await expect(provedor.getByRole('heading', { name: 'Você escolhe o melhor provedor cognitivo' })).toBeVisible();
  for (const nome of ['Claude Code', 'Codex (OpenAI)', 'Kiro', 'Kimi', 'GLM']) await expect(provedor).toContainText(nome);
  await page.locator('#recursos').screenshot({ path: 'test-results/capturas/70-recursos-provedor-cognitivo.png' });
  await provedor.screenshot({ path: 'test-results/capturas/71-recurso-provedor-cognitivo.png' });
  await page.locator('#como-funciona').screenshot({ path: 'test-results/capturas/72-como-funciona.png' });
  await expect(page.locator('#conceito-rule')).toContainText('As Rules do catálogo somam os padrões do time');
  await page.locator('#conceitos').screenshot({ path: 'test-results/capturas/73-conceitos-rule.png' });
  await expect(page.locator('#catalogo')).toContainText('Catálogo de skills, Rules e fluxos');
  await page.locator('#catalogo').screenshot({ path: 'test-results/capturas/74-landing-catalogo.png' });
  await expect(page.locator('#integracoes')).toContainText('Provedores cognitivos');
  await page.locator('#integracoes').screenshot({ path: 'test-results/capturas/75-integracoes.png' });
  const faq = page.locator('#faq');
  await faq.getByText('Quais provedores de IA ele usa?').click();
  await faq.getByText('O que é o catálogo do dev.kit?').click();
  await expect(faq).toContainText('As skills, as Rules e os fluxos que o agente usa.');
  await faq.screenshot({ path: 'test-results/capturas/76-faq.png' });

  // 2. A vitrine pública: o chip "Rules", a categoria "Tech Plans" do manifesto exibida como "Rules" e o selo "Rule".
  await page.goto('/catalogo');
  await expect(page.getByText('Skills, Rules e fluxos que vêm com o dev.kit.', { exact: false })).toBeVisible();
  const rules = page.getByRole('region', { name: 'Rules' });
  await expect(rules.getByText('Rule', { exact: true })).toHaveCount(2);
  await expect(page.getByRole('button', { name: 'Rules 2' })).toBeVisible();
  // O título do grupo com espaço também dá nome à região (o id sem espaço no aria-labelledby).
  await expect(page.getByRole('region', { name: 'Azure DevOps' })).toBeVisible();
  await expect(page.locator('body')).not.toContainText(/tech ?plans?/i);
  await page.screenshot({ path: 'test-results/capturas/77-vitrine-rules.png', fullPage: true });
  await page.getByRole('button', { name: 'Rules 2' }).click();
  await page.screenshot({ path: 'test-results/capturas/78-vitrine-filtro-rules.png', fullPage: true });

  // 3. A aba Catálogos do admin: "Nova Rule", a conferência com a mensagem do app e o item salvo como Rule.
  await garantirSenhaDefinitiva(page.request);
  await page.goto('/login');
  await page.getByLabel('Login').fill('admin');
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/dashboard$/);
  await page.goto('/catalogo');
  await page.getByRole('tab', { name: 'Catálogos' }).click();
  const marca = Date.now();
  const novo = page.getByRole('form', { name: 'Novo catálogo' });
  await novo.getByLabel('Slug').fill(`rules-${marca}`);
  await novo.getByLabel('Nome', { exact: true }).fill(`Rules do time ${marca}`);
  await novo.getByLabel('Descrição').fill('Skills, Rules e fluxos do time.');
  await novo.getByRole('button', { name: 'Criar catálogo' }).click();

  await page.getByRole('button', { name: 'Nova Rule' }).click();
  const editor = page.getByRole('form', { name: 'Nova Rule' });
  await editor.getByRole('button', { name: 'Salvar rascunho' }).click();
  await expect(editor.getByRole('alert')).toContainText('A Rule está sem descrição (campo "description").');
  await page.screenshot({ path: 'test-results/capturas/79-catalogos-nova-rule-validacao.png', fullPage: true });
  // A chave explícita: a derivada da descrição segue o slug do app, que descarta letras acentuadas como o "õ".
  await editor.getByLabel('Chave').fill('padroes-de-arquitetura');
  await editor.getByLabel('Descrição').fill('Padrões de arquitetura');
  await editor.getByLabel('Conteúdo').fill('- Reaproveite os serviços que já existem.\n- Teste o que mudou, no menor escopo.');
  await editor.getByText('Ver o JSON que vai para o dev.kit').click();
  await expect(editor.getByLabel('JSON da Rule')).toContainText('"key": "padroes-de-arquitetura"');
  await page.screenshot({ path: 'test-results/capturas/80-catalogos-nova-rule.png', fullPage: true });
  await editor.getByRole('button', { name: 'Salvar rascunho' }).click();
  await expect(page.getByText('Rule padroes-de-arquitetura criada como rascunho', { exact: false })).toBeVisible();

  const itens = page.getByRole('region', { name: 'Itens' });
  const linha = itens.getByRole('row').filter({ hasText: 'padroes-de-arquitetura' });
  // A categoria derivada pela API é "Tech Plans" (o contrato); a tabela a mostra como "Rules".
  await expect(linha.getByRole('cell').nth(0)).toHaveText('Rule');
  await expect(linha.getByRole('cell').nth(2)).toHaveText('Rules');
  await expect(itens.getByRole('button', { name: 'Rules 1' })).toBeVisible();
  await expect(itens).not.toContainText(/tech ?plans?/i);
  await page.screenshot({ path: 'test-results/capturas/81-catalogos-itens-rule.png', fullPage: true });

  await linha.getByRole('button', { name: 'Editar padroes-de-arquitetura' }).click();
  await expect(page.getByRole('heading', { name: 'Editar Rule padroes-de-arquitetura' })).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/82-catalogos-editar-rule.png', fullPage: true });
});
