import { expect, test } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, CODIGO_DE_REGISTRO, NOVA_SENHA, SENHA_INICIAL } from './ambiente';
import { montarCenario687, tokenDoAdmin } from './cenario687.mjs';

/**
 * A US #687 no navegador, com as capturas da entrega — o admin orientado a utilidade, nas quatro telas das propostas
 * aprovadas: o painel de utilidade (v008), a licença por usuário (v005), a edição da empresa com os assentos (v009) e o
 * catálogo em lista + editor de artefato (v010), cada uma FUNCIONANDO sobre o cenário montado pela API.
 */
test('o admin vê a saúde, define a licença, edita a empresa e publica o artefato do catálogo', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await garantirSenhaDefinitiva(request);
  const token = await tokenDoAdmin(API, SENHA_INICIAL, NOVA_SENHA);
  const marca = Date.now().toString(36).slice(-5);
  await montarCenario687(API, token, CODIGO_DE_REGISTRO, marca);

  await page.goto('/login');
  await page.getByLabel('Login').fill('admin');
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/dashboard$/);

  // 1. O painel de utilidade: a saúde de agora, o resultado, os turnos por desfecho e onde as tasks travam.
  await expect(page.getByRole('heading', { name: 'Saúde agora' })).toBeVisible();
  await expect(page.getByTestId('saude-Tasks paradas sem processo')).toContainText('6');
  await expect(page.getByRole('group', { name: 'Fechamentos que falharam' })).toContainText('#669');
  await expect(page.getByRole('figure', { name: 'Turnos por dia, pelo desfecho' })).toBeVisible();
  await expect(page.getByRole('figure', { name: 'Onde as tasks travam' })).toContainText('Esperou processo em segundo plano que morreu');
  await page.screenshot({ path: 'test-results/capturas/87-admin-dashboard-utilidade.png' });
  // A ação do cartão recorta o log de eventos pelo caso.
  await page.getByRole('group', { name: 'Tasks paradas sem processo' }).getByRole('button', { name: 'Ver ›' }).click();
  await expect(page.getByRole('form', { name: 'Filtrar o log' }).getByLabel('Tipo')).toHaveValue('SituacaoDaTask');
  await expect(page.getByRole('cell', { name: 'parada-sem-processo' }).first()).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/88-admin-dashboard-log-recortado.png', fullPage: true });

  // 2. A licença por usuário: a coluna, o painel e a validade aplicada com o motivo.
  await page.goto('/usuarios');
  await page.getByRole('searchbox', { name: 'Buscar por nome ou login' }).fill(marca);
  const ana = page.getByRole('row').filter({ hasText: `ana.lima.${marca}` });
  await expect(ana).toContainText('vence em 3 dias');
  await expect(page.getByRole('row').filter({ hasText: `gestor.globex.${marca}` })).toContainText('Bloqueado: licença');
  await ana.getByRole('button', { name: 'Licença de Ana Lima' }).click();
  const painel = page.getByRole('complementary', { name: 'Licença de Ana Lima' });
  await expect(painel).toContainText('trial · vence em 3 dias');
  await painel.getByRole('button', { name: '+30 dias' }).click();
  await expect(painel.getByRole('status')).toContainText('33 dias a partir de hoje');
  await painel.getByLabel('Motivo').fill('piloto Acme estendido');
  await page.screenshot({ path: 'test-results/capturas/89-admin-usuarios-licenca.png' });
  await painel.getByRole('button', { name: 'Aplicar validade' }).click();
  await expect(page.getByText(/Licença de Ana Lima aplicada: 33 dias/)).toBeVisible();
  await expect(painel).toContainText('piloto Acme estendido');
  await page.screenshot({ path: 'test-results/capturas/90-admin-usuarios-licenca-aplicada.png' });

  // 3. A edição da empresa: os dados, o contrato e os assentos com a prévia.
  await page.goto('/empresas');
  await page.getByRole('button', { name: `Editar Acme Software ${marca}` }).click();
  const empresa = page.getByRole('complementary', { name: `Editar Acme Software ${marca}` });
  await expect(empresa.getByLabel('CNPJ')).toHaveValue('11.222.333/0001-81');
  for (let i = 0; i < 5; i++) await empresa.getByRole('button', { name: 'Mais um assento' }).click();
  await expect(empresa.getByRole('status')).toContainText('+5 assentos (25 → 30)');
  await page.screenshot({ path: 'test-results/capturas/91-admin-empresas-editar.png' });
  await empresa.getByRole('button', { name: 'Salvar alterações' }).click();
  await expect(page.getByText(`Empresa Acme Software ${marca} atualizada: 30 assentos.`)).toBeVisible();
  await expect(page.getByRole('row').filter({ hasText: `Acme Software ${marca}` })).toContainText('2 de 30 em uso');

  // 4. O catálogo em lista + editor: o rascunho da v2, as diferenças com a v1 e a publicação.
  await page.goto('/catalogo');
  await expect(page.getByRole('tab', { name: 'Itens' })).toHaveAttribute('aria-selected', 'true');
  await page.getByRole('region', { name: 'Itens do catálogo' }).getByRole('button', { name: /compilar-net-framework/ }).click();
  const editor = page.getByRole('region', { name: 'Editor de compilar-net-framework' });
  await expect(editor).toContainText('rascunho da v2 · não publicado');
  await expect(editor.getByLabel('Conteúdo (SKILL.md)')).toHaveValue(/-nodeReuse:false/);
  await expect(editor.locator('.editor-gutter .linha-alterada').first()).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/92-admin-catalogo-editor.png' });
  await editor.getByRole('tab', { name: 'Diferenças com a v1' }).click();
  await expect(editor.getByLabel('Diferenças com a versão publicada')).toContainText('+ 3. Use -m:1 e -nodeReuse:false');
  await page.screenshot({ path: 'test-results/capturas/93-admin-catalogo-diferencas.png' });
  page.once('dialog', (dialogo) => void dialogo.accept());
  await editor.getByRole('button', { name: 'Publicar v2' }).click();
  await expect(editor.getByText(/Publicada a catalogo-v\d+/)).toBeVisible();
  await expect(editor).not.toContainText('rascunho da v2');
  // Publicado, as diferenças passam a ser com a v2 — e não há mais linha nova.
  await expect(editor.getByRole('tab', { name: 'Diferenças com a v2' })).toBeVisible();
  await expect(editor.getByLabel('Diferenças com a versão publicada')).not.toContainText('+ 3. Use');
  await page.screenshot({ path: 'test-results/capturas/94-admin-catalogo-publicado.png' });
});
