import { expect, test } from '@playwright/test';
import { API, CODIGO_DE_REGISTRO, NOVA_SENHA, SENHA_INICIAL } from './ambiente';

const MAQUINA = 'e2e0maquina0000000000000000000001';

/** Um evento no contrato v1 — o mesmo formato que o dev.kit (TelemetrySyncService) envia. */
const evento = (id: string, tipo: string, valor: number | null = null, detalhe = '', quantidade = 1) => ({
  eventId: id, tipo, sessaoId: 'sessao-e2e', quantidade, valor, detalhe, em: new Date().toISOString(),
});

test('base nova → admin troca a senha → a máquina envia → os números aparecem no dashboard', async ({ page, request }) => {
  // 1. A landing abre sem login e leva ao login.
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toContainText('agente de IA');
  await page.screenshot({ path: 'test-results/capturas/01-landing.png', fullPage: true });
  await page.getByRole('link', { name: 'Entrar' }).first().click();

  // 2. O admin semeado entra com a senha inicial e é obrigado a trocá-la.
  await page.getByLabel('Login').fill('admin');
  await page.getByLabel('Senha').fill(SENHA_INICIAL);
  await page.screenshot({ path: 'test-results/capturas/02-login.png' });
  await page.getByRole('button', { name: 'Entrar' }).click();
  await expect(page.getByRole('heading', { name: 'Defina a sua senha' })).toBeVisible();
  await page.getByLabel('Senha atual').fill(SENHA_INICIAL);
  await page.getByLabel(/^Nova senha/).fill(NOVA_SENHA);
  await page.getByLabel('Confirme a nova senha').fill(NOVA_SENHA);
  await page.screenshot({ path: 'test-results/capturas/03-troca-de-senha.png' });
  await page.getByRole('button', { name: 'Trocar senha' }).click();

  // 3. O dashboard de uma base nova: nenhuma máquina ainda.
  await expect(page.getByRole('heading', { name: 'Quantidade de uso' })).toBeVisible();
  await expect(page.getByText(/Nenhuma máquina enviou telemetria ainda/)).toBeVisible();

  // 4. Uma máquina se registra com o código e envia um lote (e o reenvia: não pode dobrar).
  const registro = await request.post(`${API}/api/maquinas/registrar`, {
    data: { maquinaId: MAQUINA, versaoDevKit: '1.4.0', codigoRegistro: CODIGO_DE_REGISTRO },
  });
  expect(registro.ok()).toBeTruthy();
  const { chave } = (await registro.json()) as { chave: string };
  const lote = {
    versao: 'v1', maquinaId: MAQUINA, versaoDevKit: '1.4.0',
    eventos: [
      evento('e2e-1', 'SessaoIniciada', null, '1.4.0'),
      evento('e2e-2', 'TurnoExecutado', 30000, 'claude'),
      evento('e2e-3', 'TurnoExecutado', 10000, 'claude'),
      evento('e2e-4', 'TurnoExecutado', 20000, 'kiro'),
      evento('e2e-5', 'TurnoFalhou', null, 'limite-de-uso'),
      evento('e2e-6', 'FerramentaAcionada', null, 'Bash', 12),
      evento('e2e-7', 'ArquivoAlterado', null, '', 5),
      evento('e2e-8', 'ObjetivoAvaliado', 95),
      evento('e2e-9', 'ObjetivoCumprido'),
    ],
  };
  for (let vez = 0; vez < 2; vez++) {
    const envio = await request.post(`${API}/api/telemetria/lote`, { data: lote, headers: { 'X-Machine-Key': chave } });
    expect(envio.status()).toBe(202);
  }

  // 5. Os números no dashboard — os do lote, uma vez só.
  await page.reload();
  await expect(page.getByTestId('kpi-Sessões')).toHaveText('1');
  await expect(page.getByTestId('kpi-Turnos')).toHaveText('3');
  await expect(page.getByTestId('kpi-Ferramentas acionadas')).toHaveText('12');
  await expect(page.getByTestId('kpi-Arquivos alterados')).toHaveText('5');
  await expect(page.getByTestId('kpi-Taxa de falha de turno')).toHaveText(/33,3\s?%/);
  await expect(page.getByTestId('kpi-Duração média do turno')).toHaveText('20,0 s');
  await expect(page.getByTestId('kpi-Nota média dos avaliadores')).toHaveText('95,0');
  await expect(page.getByTestId('kpi-Objetivos cumpridos / recusados')).toHaveText('1 / 0');
  await expect(page.getByRole('option', { name: /máquina e2e0maqu/ })).toBeAttached();
  await expect(page.getByText(/9 eventos/)).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/04-dashboard.png', fullPage: true });

  // 6. O filtro pela máquina mantém os números (ela é a única).
  await page.getByLabel('Máquina').selectOption({ label: 'máquina e2e0maqu (dev.kit 1.4.0)' });
  await expect(page.getByTestId('kpi-Turnos')).toHaveText('3');
});
