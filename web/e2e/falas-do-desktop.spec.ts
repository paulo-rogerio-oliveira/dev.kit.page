import { expect, test, type APIRequestContext } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/**
 * (remote-control-v3, item 8) A fala DIGITADA no chat do dev.kit desktop aparece no chat do /board. O dev.kit a manda pela
 * caixa de saída (CaixaDeSaidaRemota.EsvaziarAsync → POST /api/remoto/conversa/falas) com o corpo FalaDoDevKitV1; este
 * cenário faz o mesmo POST, com o mesmo corpo, contra a API de verdade, e confere o site compilado: a fala do dev e a
 * resposta do Orquestrador no histórico, e a fala nova chegando pelo SSE sem recarregar. O defeito era do site: a API
 * gravava a fala, e a tela descartava o campo `falas` e o evento `fala`.
 */
async function devComSessao(request: APIRequestContext) {
  await garantirSenhaDefinitiva(request);
  const admin = await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } });
  expect(admin.ok()).toBeTruthy();
  const { token } = await admin.json();
  const criado = await request.post(`${API}/api/usuarios`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { login: `falas-demo-${Date.now()}`, nome: 'Pessoa Demonstração Falas', papel: 'dev' },
  });
  expect(criado.ok()).toBeTruthy();
  const { usuario: { login }, senhaTemporaria } = await criado.json();
  const temporaria = await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaTemporaria } });
  expect(temporaria.ok()).toBeTruthy();
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    headers: { Authorization: `Bearer ${(await temporaria.json()).token}` }, data: { senhaAtual: senhaTemporaria, novaSenha: NOVA_SENHA },
  });
  expect(troca.ok()).toBeTruthy();
  return troca.json();
}

test('a fala digitada no desktop e a resposta do Orquestrador aparecem no chat do site, também ao vivo', async ({ page, request }, info) => {
  const sessao = await devComSessao(request);
  const headers = { Authorization: `Bearer ${sessao.token}` };
  const maquinaId = 'notebook-falas';
  // O heartbeat do serviço do dev.kit: a API anuncia o recurso que libera a publicação das falas.
  const heartbeat = await request.post(`${API}/api/remoto/heartbeat`, { headers, data: {
    contrato: 1, maquinaId, nome: 'Notebook', versaoDevKit: '116', origem: 'servico', appAberto: true, estadoDoAgente: 'ativo', turnoEmAndamento: false,
  } });
  expect(heartbeat.ok()).toBeTruthy();
  expect((await heartbeat.json()).recursos).toContain('falas-do-desktop');

  // O corpo exato que o dev.kit envia (FalaDoDevKitV1): ids fd-/fo- da JobPonteRemota, origem desktop, sem comandoId.
  const falar = (id: string, autor: 'dev' | 'orquestrador', texto: string, ditaEm: Date) =>
    request.post(`${API}/api/remoto/conversa/falas`, { headers, data: { contrato: 1, maquinaId, id, autor, origem: 'desktop', texto, ditaEm: ditaEm.toISOString() } });
  const id = crypto.randomUUID().replaceAll('-', '');
  expect((await falar(`fd-${id}`, 'dev', 'Como está o projeto remote-control-v3?', new Date(Date.now() - 60_000))).status()).toBe(201);
  expect((await falar(`fo-${id}-1`, 'orquestrador', 'A onda 1 está rodando: o item 8 corrige as falas do desktop.', new Date(Date.now() - 30_000))).status()).toBe(201);
  // O reenvio da caixa (a rede caiu depois do POST) é idempotente: não duplica no site.
  expect((await falar(`fd-${id}`, 'dev', 'Como está o projeto remote-control-v3?', new Date(Date.now() - 60_000))).status()).toBe(200);

  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), sessao);
  await page.setViewportSize({ width: 1440, height: 920 });
  await page.goto('/board?aba=chat');
  const conversa = page.getByRole('log', { name: 'Conversa com o Orquestrador' });
  const dev = conversa.getByRole('article').filter({ hasText: 'Como está o projeto remote-control-v3?' });
  const orquestrador = conversa.getByRole('article').filter({ hasText: 'A onda 1 está rodando' });
  await expect(dev).toHaveCount(1);
  await expect(dev).toContainText('Você · no dev.kit');
  await expect(orquestrador).toContainText('Orquestrador · dev.kit');
  await page.screenshot({ path: info.outputPath('falas-do-desktop-historico.png') });

  // Ao vivo: a fala nova chega pelo SSE, sem recarregar a página.
  const outra = crypto.randomUUID().replaceAll('-', '');
  expect((await falar(`fd-${outra}`, 'dev', 'Pause o item 9 até eu voltar.', new Date())).status()).toBe(201);
  await expect(conversa.getByRole('article').filter({ hasText: 'Pause o item 9 até eu voltar.' })).toBeVisible({ timeout: 20_000 });
  await expect(conversa.getByRole('article')).toHaveCount(3);
  await page.screenshot({ path: info.outputPath('falas-do-desktop-ao-vivo.png') });

  // No celular, a mesma conversa (sem transbordo).
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(conversa.getByText('Pause o item 9 até eu voltar.')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await page.screenshot({ path: info.outputPath('falas-do-desktop-celular.png') });
});
