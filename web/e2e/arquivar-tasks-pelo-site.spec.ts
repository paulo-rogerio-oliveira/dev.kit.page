import { expect, test, type APIRequestContext } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/**
 * (controle-v4, item 3) O cartão de ARQUIVAR as tasks pedidas pelo Orquestrador e o DISPOSITIVO da decisão, de ponta a ponta
 * sobre a API real (SQLite temporário, dados fictícios): o retrato vai pelo mesmo PUT que o dev.kit faz, o dev autoriza com o
 * login confirmado no desktop (1440x900) e no celular (390x844) — sem o segundo fator desde a semana 7 —, e a caixa de saída
 * que a máquina lê devolve o dispositivo de cada decisão. As capturas vão para test-results/capturas-arquivar-tasks.
 */
const pasta = 'test-results/capturas-arquivar-tasks';

async function devComSessao(request: APIRequestContext) {
  await garantirSenhaDefinitiva(request);
  const admin = await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } });
  const { token } = await admin.json();
  const criado = await request.post(`${API}/api/usuarios`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { login: `arquivar-${Date.now()}`, nome: 'Pessoa Demonstração Arquivar', papel: 'dev' },
  });
  const { usuario: { login }, senhaTemporaria } = await criado.json();
  const temporaria = await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaTemporaria } });
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    headers: { Authorization: `Bearer ${(await temporaria.json()).token}` }, data: { senhaAtual: senhaTemporaria, novaSenha: NOVA_SENHA },
  });
  return troca.json();
}

/** O pedido do Orquestrador como o dev.kit o publica (RetratoRemoto.ArquivamentoAutorizavel). */
function cartao(id: string, tasks: string[]) {
  const comando = `devcli sessao --acao arquivar --alvo ${tasks.join(',')} --motivo "gatilhos antigos já integrados" --aplicar`;
  return {
    id, tipo: 'autorizacao', titulo: `Arquivar ${tasks.length} task(s) pedidas pelo Orquestrador`,
    pergunta: `O Orquestrador pede para arquivar: ${tasks.join(', ')}. As tasks saem dos Processos e continuam consultáveis `
      + '(a pasta não é apagada; devcli sessao --reabrir as traz de volta). Autorizar o arquivamento?',
    escopo: { ato: 'arquivar-tasks', alvo: 'Orquestrador', itens: tasks, comando },
  };
}

test('arquivar tasks pelo site: o cartão do Orquestrador, o login confirmado e o dispositivo na decisão', async ({ page, request }) => {
  test.setTimeout(120_000);
  const sessao = await devComSessao(request);
  const headers = { Authorization: `Bearer ${sessao.token}` };
  const maquinaId = `dev3-arquivar-${Date.now()}`;
  // A máquina anuncia o recurso das autorizações e publica os dois pedidos do Orquestrador.
  const batimento = await request.post(`${API}/api/remoto/heartbeat`, { headers, data: {
    contrato: 1, maquinaId, nome: 'dev3', versaoDevKit: '133', origem: 'servico', appAberto: true, estadoDoAgente: 'ativo', turnoEmAndamento: false,
  } });
  expect((await batimento.json()).recursos).toContain('decisoes-autorizar');
  expect((await request.put(`${API}/api/remoto/retrato`, { headers, data: {
    contrato: 1, maquinaId, geradoEm: new Date().toISOString(), projetos: [], fluxos: [],
    pendencias: [cartao('autorizacao-arquivar-gatilhos', ['#901', '#902']), cartao('autorizacao-arquivar-avaliador', ['t_207'])],
  } })).ok()).toBeTruthy();
  // (semana 7) Sem preparar autenticador: o dev deste teste nem tem TOTP, e a autorização sai com o login confirmado.
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), sessao);

  // 1. Desktop 1440x900: o cartão no Board, o escopo exato (as tasks e o comando) e a autorização com um clique.
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/board?aba=board');
  const painel = page.getByRole('complementary', { name: 'Board' });
  const gatilhos = painel.getByRole('region', { name: 'Arquivar 2 task(s) pedidas pelo Orquestrador' });
  await expect(gatilhos).toBeVisible({ timeout: 25_000 });
  await page.screenshot({ path: `${pasta}/01-desktop-cartao-arquivar-tasks.png` });
  await gatilhos.getByRole('button', { name: 'Autorizar' }).click();
  const escopo = page.getByLabel('Escopo da autorização');
  await expect(escopo).toContainText('Arquivar as tasks');
  await expect(escopo).toContainText('#901, #902');
  await expect(escopo).toContainText('devcli sessao --acao arquivar --alvo #901,#902');
  await expect(page.getByLabel('Código de 6 dígitos')).toHaveCount(0);
  await page.screenshot({ path: `${pasta}/02-desktop-escopo-sem-codigo.png` });
  await page.getByRole('button', { name: 'Autorizar este pedido' }).click();
  await expect(page.getByText(/login confirmado · pelo navegador/).first()).toBeVisible();
  await page.screenshot({ path: `${pasta}/03-desktop-autorizado-pelo-navegador.png` });

  // 2. Celular 390x844: o outro pedido, autorizado do celular.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/board?aba=board');
  const avaliador = page.getByRole('complementary', { name: 'Board' }).getByRole('region', { name: 'Arquivar 1 task(s) pedidas pelo Orquestrador' });
  await expect(avaliador).toBeVisible({ timeout: 25_000 });
  await page.screenshot({ path: `${pasta}/04-celular-cartao-arquivar-tasks.png`, fullPage: true });
  await avaliador.getByRole('button', { name: 'Autorizar' }).click();
  await expect(page.getByLabel('Escopo da autorização')).toContainText('t_207');
  await expect(page.getByLabel('Código de 6 dígitos')).toHaveCount(0);
  await page.screenshot({ path: `${pasta}/05-celular-escopo-sem-codigo.png`, fullPage: true });
  await page.getByRole('button', { name: 'Autorizar este pedido' }).click();
  await expect(page.getByText(/login confirmado · pelo celular/).first()).toBeVisible();
  await page.screenshot({ path: `${pasta}/06-celular-autorizado-pelo-celular.png`, fullPage: true });

  // 3. O que a máquina lê: a caixa de saída das decisões, com o dispositivo de cada uma (a mesma rota do dev.kit).
  const rota = `${API}/api/remoto/maquinas/${maquinaId}/decisoes/respostas`;
  const respostas = await (await request.get(rota, { headers })).json() as Array<{ pendenciaId: string; acao: string; segundoFator: boolean; dispositivo: string }>;
  expect(respostas.map(r => [r.pendenciaId, r.acao, r.segundoFator, r.dispositivo])).toEqual([
    ['autorizacao-arquivar-gatilhos', 'autorizar', false, 'navegador'],
    ['autorizacao-arquivar-avaliador', 'autorizar', false, 'celular'],
  ]);
  await page.setViewportSize({ width: 1440, height: 900 });
  // Cores fixas: com o navegador em tema escuro, o texto padrão sairia branco sobre o fundo claro.
  await page.setContent(`<meta charset="utf-8"><body style="font:15px/1.5 Segoe UI,sans-serif;margin:32px;background:#fff;color:#1b1f24">
    <h2>GET /api/remoto/maquinas/{máquina}/decisoes/respostas — a caixa de saída que o dev.kit consome</h2>
    <pre style="background:#f4f6f8;color:#1b1f24;padding:16px;border-radius:8px;white-space:pre-wrap">${JSON.stringify(respostas, null, 2).replace(/</g, '&lt;')}</pre></body>`);
  await page.screenshot({ path: `${pasta}/07-api-caixa-de-saida-com-dispositivo.png` });
});
