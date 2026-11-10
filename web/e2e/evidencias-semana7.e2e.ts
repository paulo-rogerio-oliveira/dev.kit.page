import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/**
 * (semana 7) As EVIDÊNCIAS ponta a ponta dos itens 2 e 4, no desktop (1440x900) e no celular (390x844), sobre a API real (SQLite
 * temporário, fila e provisão simuladas — playwright.evidencias-semana7.config.ts), com dados FICTÍCIOS:
 * - item 2: o aprovar e o autorizar SEM segundo fator — a revisão com o escopo, as evidências e o SHA, e a decisão registrada com o
 *   login confirmado e o dispositivo;
 * - item 4: a conversa com o agente de UMA task — o Executando com a task conversável, o chat dela com as últimas falas, a mensagem
 *   do dev indo à fila com o destino e a máquina entregando à task e respondendo (as mesmas rotas que o dev.kit usa).
 * As capturas vão para test-results/evidencias-semana7. Nenhuma senha real: o usuário de teste é fictício.
 */
// Ao lado do roteiro (web/test-results), qualquer que seja a pasta corrente de quem o roda.
const pasta = fileURLToPath(new URL('../test-results/evidencias-semana7', import.meta.url));
const LOGIN = 'usuario.ficticio.semana7@exemplo.test';
const MAQUINA = 'maquina-demo-semana7';
const DESKTOP = { width: 1440, height: 900 };
const CELULAR = { width: 390, height: 844 };
const TASK = 'exec-7a1c3e5f9b2d4c6e';
const minutos = (n: number) => new Date(Date.now() - n * 60_000).toISOString();

/** O usuário de teste numa empresa, com a fila do controle remoto pronta (o admin libera e o worker da provisão conclui). */
async function prepararUsuario(request: APIRequestContext) {
  await garantirSenhaDefinitiva(request);
  const admin = { Authorization: `Bearer ${(await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json()).token}` };
  const empresa = await (await request.post(`${API}/api/empresas`, { headers: admin, data: { nome: 'Empresa de demonstração da semana 7', plano: 'Empresarial', assentos: 5 } })).json() as { id: number };
  const criado = await request.post(`${API}/api/usuarios`, { headers: admin, data: { login: LOGIN, nome: 'Pessoa Demonstração', papel: 'dev', empresaId: empresa.id } });
  expect(criado.status(), await criado.text()).toBe(201);
  const { usuario, senhaTemporaria } = await criado.json() as { usuario: { id: number }; senhaTemporaria: string };
  expect((await request.put(`${API}/api/usuarios/${usuario.id}/recursos/provisao`, { headers: admin, data: { liberado: true } })).ok()).toBeTruthy();
  const temporaria = await (await request.post(`${API}/api/auth/login`, { data: { login: LOGIN, senha: senhaTemporaria } })).json() as { token: string };
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    headers: { Authorization: `Bearer ${temporaria.token}` }, data: { senhaAtual: senhaTemporaria, novaSenha: NOVA_SENHA },
  });
  expect(troca.ok()).toBeTruthy();
  const sessao = await troca.json() as { token: string };
  await expect.poll(async () => {
    const usuarios = await (await request.get(`${API}/api/usuarios`, { headers: admin })).json() as Array<{ login: string; fila?: { estado: string } | null }>;
    return usuarios.find(u => u.login === LOGIN)?.fila?.estado;
  }, { message: 'a fila do controle remoto do usuário (worker da provisão simulado)', timeout: 120_000, intervals: [1000] }).toBe('pronto');
  return sessao;
}

/** O que o dev.kit publica: o heartbeat com o recurso, o retrato (a task conversável, a aprovação e a autorização) e as falas da task. */
async function publicarMaquina(request: APIRequestContext, headers: Record<string, string>) {
  const batimento = await request.post(`${API}/api/remoto/heartbeat`, { headers, data: {
    contrato: 1, maquinaId: MAQUINA, nome: 'Máquina de demonstração', versaoDevKit: '137', origem: 'servico', appAberto: true, estadoDoAgente: 'ativo', turnoEmAndamento: false,
  } });
  expect((await batimento.json() as { recursos: string[] }).recursos).toContain('conversa-da-task');
  const snapshot = { origem: 'feature/dev/projeto-demo-item-4', destino: 'feature/dev/projeto-demo', sha: 'a1b2c3d4e5f60718293a4b5c6d7e8f9012345678',
    notas: [{ avaliador: 'Avaliador do item', nota: 94, minima: 90 }], testesAprovados: 212, testesFalhos: 0, regressaoConcluida: true, arquivos: 9, commits: 3,
    alteracoes: '3 commit(s) e 9 arquivo(s).', capturas: [] };
  expect((await request.put(`${API}/api/remoto/retrato`, { headers, data: {
    contrato: 1, maquinaId: MAQUINA, geradoEm: new Date().toISOString(), projetos: [],
    fluxos: [
      { id: TASK, nome: '#4 Conversar com o agente da task', etapa: 'Implementação', situacao: 'rodando', agente: 'claude:opus', desdeEm: minutos(40), conversavel: true },
      { id: 'exec-2b9d0f1e3a5c7b8d', nome: '#3 Comando delegado travado', etapa: 'Avaliação', situacao: 'rodando', agente: 'claude', desdeEm: minutos(90) },
    ],
    pendencias: [
      { id: 'aprovacao-item-4', tipo: 'aprovacao', titulo: 'Integrar o item 4 no projeto projeto-demo?', projeto: 'projeto-demo', item: 4, snapshot },
      { id: 'autorizacao-comando', tipo: 'autorizacao', titulo: '#4 Conversar com o agente da task', pergunta: 'Autorizar o dev.kit a executar o comando bloqueado UMA vez nesta task?',
        escopo: { ato: 'executar-comando', alvo: '#4 Conversar com o agente da task', comando: 'dotnet test tests/GitKit.Core.Tests/GitKit.Core.Tests.csproj' } },
    ],
  } })).ok()).toBeTruthy();
  const falar = (id: string, autor: string, texto: string, ditaEm: string, extra: Record<string, unknown> = {}) => request.post(`${API}/api/remoto/conversa/falas`, { headers, data: {
    contrato: 1, maquinaId: MAQUINA, id, autor, origem: 'desktop', texto, ditaEm, sessao: TASK, ...extra,
  } });
  expect((await falar('ft-1', 'dev', 'Comece pelo contrato da API e depois a tela do celular.', minutos(25))).status()).toBe(201);
  expect((await falar('ft-2', 'agente', 'Contrato feito (sessao opcional nas falas e nos comandos); agora a rota da conversa da task e os testes da API.', minutos(18))).status()).toBe(201);
  expect((await falar('ft-3', 'agente', 'Testes da API: 48 aprovados. Sigo com o chat da task no Board.', minutos(6))).status()).toBe(201);
}

/** A MÁQUINA atende a mensagem com destino à task: confirma o recebimento, entrega (o eco da fala do dev) e o agente responde. */
async function entregarATask(request: APIRequestContext, headers: Record<string, string>) {
  let comando: { id: string; texto: string; estado: string } | undefined;
  await expect.poll(async () => {
    const conversa = await (await request.get(`${API}/api/remoto/maquinas/${MAQUINA}/sessoes/${TASK}/conversa`, { headers })).json() as { comandos: Array<{ id: string; texto: string; estado: string }> };
    comando = conversa.comandos.find(c => ['pendente', 'na-fila'].includes(c.estado));
    return !!comando;
  }, { message: 'a mensagem do site na fila da máquina', timeout: 60_000, intervals: [500] }).toBeTruthy();
  const recebido = await request.post(`${API}/api/remoto/comandos/${comando!.id}/recebido`, { headers, data: { contrato: 1, maquinaId: MAQUINA } });
  expect((await recebido.json() as { executar: boolean }).executar).toBeTruthy();
  expect((await request.post(`${API}/api/remoto/respostas`, { headers, data: {
    contrato: 1, maquinaId: MAQUINA, id: `ok-${comando!.id}`, comandoId: comando!.id, tipo: 'concluido', texto: 'Entregue à task: a resposta do agente aparece nesta conversa.', geradaEm: new Date().toISOString(),
  } })).ok()).toBeTruthy();
  const falar = (id: string, autor: string, origem: string, texto: string, extra: Record<string, unknown> = {}) => request.post(`${API}/api/remoto/conversa/falas`, { headers, data: {
    contrato: 1, maquinaId: MAQUINA, id, autor, origem, texto, ditaEm: new Date().toISOString(), sessao: TASK, ...extra,
  } });
  expect((await falar('ft-4', 'dev', 'remoto', comando!.texto, { comandoId: comando!.id })).status()).toBe(201);
  expect((await falar('ft-5', 'agente', 'desktop', 'Certo: rodando só o Board.test.tsx — 34 aprovados. O chat da task está pronto para as capturas.')).status()).toBe(201);
}

async function capturar(page: Page, nome: string, paginaInteira = false) {
  await page.screenshot({ path: `${pasta}/${nome}.png`, fullPage: paginaInteira });
}

test('evidências da semana 7: aprovar e autorizar sem segundo fator (item 2) e a conversa com o agente de uma task (item 4)', async ({ page, request }) => {
  test.setTimeout(600_000);
  mkdirSync(pasta, { recursive: true });
  const sessao = await prepararUsuario(request);
  const headers = { Authorization: `Bearer ${sessao.token}` };
  await publicarMaquina(request, headers);
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), sessao);

  // ---- Item 2, desktop 1440x900: a revisão do aprovar e do autorizar, sem a etapa do código.
  await page.setViewportSize(DESKTOP);
  await page.goto('/board?aba=board');
  const painel = page.getByRole('complementary', { name: 'Board' });
  const aprovacao = painel.getByRole('region', { name: 'Integrar o item 4 no projeto projeto-demo?' });
  await expect(aprovacao).toBeVisible({ timeout: 25_000 });
  await aprovacao.getByRole('button', { name: 'Decidir' }).click();
  await expect(page.getByRole('button', { name: 'Aprovar este SHA' })).toBeEnabled();
  await expect(page.getByLabel('Código de 6 dígitos')).toHaveCount(0);
  await expect(page.getByText('Vale só para este SHA e este destino.')).toBeVisible();
  await expect(page.getByText(/Não autoriza PR, merge na semana nem fechamento do projeto/)).toBeVisible();
  await capturar(page, 'item2-depois-aprovar-1440x900');
  // A revisão ANTES de aprovar também precisa estar documentada no celular.
  await page.setViewportSize(CELULAR);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await expect(page.getByLabel('Código de 6 dígitos')).toHaveCount(0);
  await capturar(page, 'item2-depois-aprovar-390x844', true);
  await page.setViewportSize(DESKTOP);
  await page.getByRole('button', { name: 'Aprovar este SHA' }).click();
  await expect(page.getByText(/login confirmado · pelo navegador/).first()).toBeVisible();
  await capturar(page, 'item2-depois-aprovado-registrado-1440x900');
  await page.getByRole('button', { name: '‹ Voltar ao Board' }).click();
  await painel.getByRole('region', { name: '#4 Conversar com o agente da task' }).getByRole('button', { name: 'Autorizar' }).click();
  await expect(page.getByLabel('Escopo da autorização')).toContainText('dotnet test tests/GitKit.Core.Tests/GitKit.Core.Tests.csproj');
  await expect(page.getByLabel('Código de 6 dígitos')).toHaveCount(0);
  await capturar(page, 'item2-depois-autorizar-1440x900');
  await page.getByRole('button', { name: '‹ Voltar ao Board' }).click();

  // ---- Item 4, desktop 1440x900: o Executando com a task conversável e o chat DELA.
  await expect(painel.getByRole('button', { name: 'Conversar com a task #4 Conversar com o agente da task' })).toBeVisible();
  await expect(painel.getByRole('button', { name: 'Conversar com a task #3 Comando delegado travado' })).toHaveCount(0);
  await painel.getByRole('heading', { name: /Executando/ }).scrollIntoViewIfNeeded();
  await capturar(page, 'item4-executando-com-task-conversavel-1440x900');
  await painel.getByRole('button', { name: 'Conversar com a task #4 Conversar com o agente da task' }).click();
  const chat = page.getByRole('region', { name: 'Conversa da task #4 Conversar com o agente da task' });
  await expect(chat.getByText('Testes da API: 48 aprovados. Sigo com o chat da task no Board.')).toBeVisible({ timeout: 25_000 });
  await capturar(page, 'item4-chat-da-task-1440x900');
  await chat.getByLabel('Mensagem à task').fill('Rode só os testes do Board, por favor.');
  await chat.getByRole('button', { name: 'Enviar à task' }).click();
  await expect(chat.getByText('aguardando o dev.kit')).toBeVisible();
  await capturar(page, 'item4-mensagem-na-fila-1440x900');
  await entregarATask(request, headers);
  await expect(chat.getByText('entregue à task')).toBeVisible({ timeout: 30_000 });
  await expect(chat.getByText(/rodando só o Board.test.tsx/)).toBeVisible({ timeout: 30_000 });
  await capturar(page, 'item4-entregue-e-resposta-do-agente-1440x900');

  // ---- Celular 390x844: o mesmo, na aba Board e na aba Chat.
  await page.setViewportSize(CELULAR);
  await page.goto('/board?aba=board');
  const painelCelular = page.getByRole('complementary', { name: 'Board' });
  await expect(painelCelular.getByRole('region', { name: '#4 Conversar com o agente da task' })).toBeVisible({ timeout: 25_000 });
  await painelCelular.getByRole('region', { name: '#4 Conversar com o agente da task' }).getByRole('button', { name: 'Autorizar' }).click();
  await expect(page.getByLabel('Código de 6 dígitos')).toHaveCount(0);
  await capturar(page, 'item2-depois-autorizar-390x844', true);
  await page.getByRole('button', { name: 'Autorizar este pedido' }).click();
  await expect(page.getByText(/login confirmado · pelo celular/).first()).toBeVisible();
  await capturar(page, 'item2-depois-autorizado-pelo-celular-390x844', true);
  await page.getByRole('button', { name: '‹ Voltar ao Board' }).click();
  await painelCelular.getByRole('button', { name: 'Conversar com a task #4 Conversar com o agente da task' }).scrollIntoViewIfNeeded();
  await capturar(page, 'item4-executando-com-task-conversavel-390x844');
  await painelCelular.getByRole('button', { name: 'Conversar com a task #4 Conversar com o agente da task' }).click();
  const chatCelular = page.getByRole('region', { name: 'Conversa da task #4 Conversar com o agente da task' });
  await expect(chatCelular.getByText(/rodando só o Board.test.tsx/)).toBeVisible({ timeout: 25_000 });
  await capturar(page, 'item4-chat-da-task-390x844');

  // O aprovar no celular: o snapshot já foi aprovado no desktop, então a revisão mostra o registro (a decisão é única).
  await page.goto('/board?aba=board');
  await page.getByRole('complementary', { name: 'Board' }).getByRole('region', { name: 'Integrar o item 4 no projeto projeto-demo?' }).getByRole('button', { name: 'Ver detalhes' }).click();
  await capturar(page, 'item2-depois-aprovar-registrado-390x844', true);
});
