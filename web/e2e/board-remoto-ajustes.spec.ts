import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { readFileSync } from 'node:fs';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

/**
 * As correções do board remoto (07/11/2026) de ponta a ponta, sobre a API real (SQLite temporário, dados fictícios): o que o
 * dev.kit publica — o heartbeat, o retrato com as pendências, o catálogo com o fluxo, os agentes, os membros, os repositórios
 * de mesmo nome e a miniatura do design, e a fala com a imagem (o campo `anexos`) — vai pelos MESMOS POST/PUT que o dev.kit faz.
 * Só o PNG da versão de design é servido pelo navegador do teste (a rota da US #669 exige o projeto de design da empresa, fora
 * deste cenário): é a proposta chat-remoto v016 do painel Design. As capturas vão para test-results/capturas-board-remoto.
 */
const pasta = 'test-results/capturas-board-remoto';
const propostaV016 = process.env.DEVKIT_E2E_PROPOSTA ?? '';

async function devComSessao(request: APIRequestContext) {
  await garantirSenhaDefinitiva(request);
  const admin = await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } });
  const { token } = await admin.json();
  const criado = await request.post(`${API}/api/usuarios`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { login: `board-ajustes-${Date.now()}`, nome: 'Pessoa Demonstração Board', papel: 'dev' },
  });
  const { usuario: { login }, senhaTemporaria } = await criado.json();
  const temporaria = await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaTemporaria } });
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    headers: { Authorization: `Bearer ${(await temporaria.json()).token}` }, data: { senhaAtual: senhaTemporaria, novaSenha: NOVA_SENHA },
  });
  return troca.json();
}

const minutos = (n: number) => new Date(Date.now() - n * 60_000).toISOString();

async function publicarMaquina(request: APIRequestContext, headers: Record<string, string>, maquinaId: string) {
  expect((await request.post(`${API}/api/remoto/heartbeat`, { headers, data: {
    contrato: 1, maquinaId, nome: 'dev3', versaoDevKit: '130', origem: 'servico', appAberto: true, estadoDoAgente: 'ativo', turnoEmAndamento: false,
  } })).ok()).toBeTruthy();
  const snapshot = { origem: 'feature/dev/projeto-remote-control-v3-item-4', destino: 'feature/dev/projeto-remote-control-v3', sha: 'a1b2c3d4e5f60718293a4b5c6d7e8f9012345678',
    notas: [{ avaliador: 'Avaliador do item', nota: 94, minima: 90 }], testesAprovados: 212, testesFalhos: 0, regressaoConcluida: true, arquivos: 9, commits: 3,
    alteracoes: '3 commit(s) e 9 arquivo(s).', capturas: [{ titulo: 'chat-remoto · v016 proposta', url: '/api/design/dev-kit-page/chat-remoto/v16' }] };
  expect((await request.put(`${API}/api/remoto/retrato`, { headers, data: {
    contrato: 1, maquinaId, geradoEm: new Date().toISOString(),
    // (ajuste 8) O projeto com todos os itens integrados já sai "fechando"; o concluído e ele só aparecem com o filtro.
    projetos: [{ slug: 'remote-control-v3', nome: 'remote-control-v3', situacao: 'aprovado', ondaAtual: 3, itens: [{ numero: 4, titulo: 'Criação pelo site', situacao: 'rodando', onda: 2 }] },
      { slug: 'controle-remoto-v2', nome: 'controle-remoto-v2', situacao: 'fechando', ondaAtual: 2, itens: [{ numero: 1, titulo: 'Fila', situacao: 'integrado', onda: 1 }, { numero: 2, titulo: 'Chat', situacao: 'integrado', onda: 2 }] },
      { slug: 'board-v5', nome: 'board-v5', situacao: 'concluido', ondaAtual: 1, itens: [{ numero: 1, titulo: 'Board', situacao: 'integrado', onda: 1 }] }],
    fluxos: [{ id: 'item-8', nome: 'Item 8 · fechamento', etapa: 'Implementação', situacao: 'rodando', agente: 'claude' }],
    pendencias: [
      { id: 'aprovacao-item-4', tipo: 'aprovacao', titulo: 'Integrar o item 4 no projeto remote-control-v3?', projeto: 'remote-control-v3', item: 4, snapshot },
      { id: 'pergunta-item-8', tipo: 'pergunta', titulo: 'Disparo do devcli — O integrar e o Fechamento não podem prender o item', projeto: 'remote-control-v3', item: 8,
        pergunta: 'Seguir com o Fechamento?', opcoes: ['Seguir', 'Aguardar'] },
      // (ajuste 9) O comando bloqueado como autorização, com o escopo exato.
      { id: 'pendencia-comando', tipo: 'autorizacao', titulo: '#821 Correções do board remoto', pergunta: 'Autorizar o dev.kit a executar o comando bloqueado UMA vez nesta task?',
        escopo: { ato: 'executar-comando', alvo: '#821 Correções do board remoto', comando: 'dotnet test tests/GitKit.Core.Tests/GitKit.Core.Tests.csproj' } },
    ],
  } })).ok()).toBeTruthy();
  expect((await request.put(`${API}/api/remoto/catalogo`, { headers, data: {
    contrato: 1, maquinaId, geradoEm: new Date().toISOString(), comandoId: null,
    workItems: [{ id: 812, titulo: 'Handshake do chat remoto', tipo: 'User Story', estado: 'Active' }, { id: 815, titulo: 'Filtro de dia do Board', tipo: 'Bug', estado: 'Active' },
      { id: 821, titulo: 'Correções do board remoto', tipo: 'Task', estado: 'Active' }],
    repositorios: [{ nome: 'github.com/empresa-demo/dev.kit.page', branchPadrao: 'main' }, { nome: 'github.com/empresa-demo/git.kit', branchPadrao: 'main' },
      { nome: 'dev.azure.com/outra-org/git.kit' }, { nome: 'github.com/empresa-demo/portal' }],
    fluxos: [
      { nome: 'Implementar com revisor', descricao: 'Implementação avaliada e revisão do responsável', id: 7, agentePadrao: 'claude:opus', parametros: [
        { nome: 'REVISOR', rotulo: 'Revisor', tipo: 'texto', usuario: true, obrigatorio: true },
        { nome: 'AMBIENTE', tipo: 'lista', opcoes: ['homologação', 'produção'], obrigatorio: true, padrao: 'homologação' },
        { nome: 'NOTIFICAR', tipo: 'sim-nao', obrigatorio: false } ] },
      { nome: 'Tarefa avulsa', descricao: 'Implementação avaliada', id: 3 },
    ],
    projetos: [{ slug: 'remote-control-v3', nome: 'remote-control-v3', situacao: 'aprovado' }],
    designs: [{ nome: 'github.com/empresa-demo/dev.kit.page', telas: 4, repositorio: 'github.com/empresa-demo/dev.kit.page', miniatura: '/api/design/dev-kit-page/chat-remoto/v16' }],
    agentes: ['claude', 'claude:opus', 'claude:sonnet', 'kiro', 'kiro:auto', 'codex'],
    membros: ['Pessoa Revisora', 'Pessoa Dev', 'Pessoa Gestora'],
  } })).ok()).toBeTruthy();
  const falar = (id: string, autor: string, texto: string, ditaEm: string, anexos?: unknown) => request.post(`${API}/api/remoto/conversa/falas`, { headers, data: {
    contrato: 1, maquinaId, id, autor, origem: 'desktop', texto, ditaEm, ...(anexos ? { anexos } : {}),
  } });
  expect((await falar('fd-1', 'dev', 'Qual é a situação da onda 2 do remote-control-v3?', minutos(30))).status()).toBe(201);
  expect((await falar('fo-1-1', 'orquestrador', 'A onda 2 tem os itens 5, 9 e 10 integrados; o item 4 aguarda aprovação e o item 8 está em implementação.', minutos(29))).status()).toBe(201);
  expect((await falar('fd-2', 'dev', 'Gere uma proposta do layout do chat do Orquestrador na web.', minutos(12))).status()).toBe(201);
  expect((await falar('dv-1', 'orquestrador', 'Versão de design gravada: chat-remoto v016 (Proposta).', minutos(10), [
    { titulo: 'chat-remoto v016 · Proposta', url: '/api/design/dev-kit-page/chat-remoto/v16' },
    { titulo: 'chat-remoto v017 · Proposta', aviso: 'o dev.kit.page recusou a versão (o teto de versões do plano ou um dado inválido): ela fica só no dev.kit desta máquina.' },
  ])).status()).toBe(201);
}

/** A imagem da versão de design: servida pelo navegador do teste (o PNG da proposta v016), com o token conferido. */
async function servirDesign(page: Page) {
  const png = propostaV016 ? readFileSync(propostaV016) : Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=', 'base64');
  await page.route('**/api/design/dev-kit-page/**', async rota => {
    expect(rota.request().headers()['authorization']).toMatch(/^Bearer /);
    await rota.fulfill({ status: 200, contentType: 'image/png', body: png });
  });
}

test('correções do board remoto: layout v016, imagens com modal, fluxo com modelo e parâmetros, repositório pelo nome e dispensar', async ({ page, request }) => {
  const sessao = await devComSessao(request);
  const headers = { Authorization: `Bearer ${sessao.token}` };
  const maquinaId = `dev3-ajustes-${Date.now()}`;
  await publicarMaquina(request, headers, maquinaId);
  await servirDesign(page);
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), sessao);

  // 1. Desktop: o layout da proposta v016 — o selo e o Reiniciar no cabeçalho do Orquestrador, o topo só com a máquina.
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/board');
  const chat = page.getByRole('region', { name: 'Orquestrador' });
  await expect(chat.getByRole('button', { name: 'Reiniciar dev.kit' })).toBeVisible({ timeout: 25_000 });
  await expect(chat.getByText('Texto do chat não aprova')).toBeVisible();
  await expect(page.locator('.br-top button')).toHaveCount(0);
  await expect(page.locator('footer')).toHaveCount(0);
  const miniatura = chat.getByRole('button', { name: 'Ampliar chat-remoto v016 · Proposta' });
  await expect(miniatura.getByRole('img')).toBeVisible();
  await expect(chat.getByText(/chat-remoto v017 · Proposta: o dev.kit.page recusou/)).toBeVisible();
  await expect(chat.getByRole('button', { name: 'Atualizar catálogo do dev.kit' })).toHaveAttribute('title', /catálogo do dev.kit · atualizado agora há pouco/i);
  await page.screenshot({ path: `${pasta}/01-board-layout-v016-desktop.png` });

  await chat.getByRole('button', { name: 'Atalhos do chat' }).hover();
  await expect(page.getByRole('tooltip')).toBeVisible();
  await page.screenshot({ path: `${pasta}/02-dica-dos-atalhos.png` });

  // 2. A imagem da proposta abre no modal.
  await miniatura.click();
  const modal = page.getByRole('dialog', { name: 'chat-remoto v016 · Proposta' });
  await expect(modal.getByRole('img', { name: 'chat-remoto v016 · Proposta' })).toBeVisible();
  await page.screenshot({ path: `${pasta}/03-imagem-no-modal.png` });
  await modal.getByRole('button', { name: 'Fechar' }).click();

  // 3. O fluxo com o modelo (o agente padrão do fluxo) e os parâmetros; o obrigatório vazio segura o envio.
  await chat.getByRole('button', { name: 'Trocar fluxo' }).click();
  await page.getByRole('button', { name: /^Implementar com revisor/ }).click();
  await expect(chat.getByRole('button', { name: 'Modelo do fluxo' })).toContainText('claude:opus');
  await chat.getByLabel('Mensagem ao Orquestrador').fill('Implemente as correções do board remoto');
  await chat.getByLabel('Mensagem ao Orquestrador').press('Enter');
  await expect(chat.getByText('Preencha antes de enviar: Revisor.')).toBeVisible();
  await chat.getByRole('button', { name: 'Modelo do fluxo' }).click();
  await expect(page.getByRole('group', { name: 'Agentes da máquina' })).toBeVisible();
  await page.screenshot({ path: `${pasta}/04-fluxo-modelo-e-parametros.png` });
  await page.getByRole('group', { name: 'Agentes da máquina' }).getByRole('button', { name: 'kiro:auto' }).click();
  await chat.getByRole('button', { name: 'Parâmetro Revisor' }).click();
  await page.getByRole('textbox', { name: 'Revisor' }).fill('revis');
  await page.screenshot({ path: `${pasta}/05-parametro-usuario-com-busca.png` });
  await page.getByRole('group', { name: 'Membros do time' }).getByRole('button', { name: 'Pessoa Revisora' }).click();
  await expect(chat.getByRole('button', { name: 'Parâmetro Revisor' })).toContainText('Revisor: Pessoa Revisora');

  // 4. O repositório pelo nome; o dono só nos dois "git.kit".
  await chat.getByRole('button', { name: '@ Repositório' }).click();
  const repositorios = page.getByRole('group', { name: 'Do catálogo do dev.kit: repositório' });
  await expect(repositorios.getByRole('button')).toHaveText([/^dev\.kit\.page/, /^git\.kit empresa-demo/, /^git\.kit outra-org/, /^portal/]);
  await page.screenshot({ path: `${pasta}/06-repositorio-pelo-nome.png` });
  await repositorios.getByRole('button', { name: /^git\.kit empresa-demo/ }).click();
  await page.getByRole('button', { name: 'Aplicar contexto' }).click();
  await expect(chat.getByRole('button', { name: '@ Repositório' })).toHaveText('@ git.kit ▾');
  await page.screenshot({ path: `${pasta}/07-compositor-com-fluxo-e-repositorio.png` });

  // 5. O × do card: só no site, o filtro Dispensadas o traz de volta, e o card de Design com a miniatura.
  const painel = page.getByRole('complementary', { name: 'Board' });
  await painel.getByRole('button', { name: 'Dispensar Disparo do devcli — O integrar e o Fechamento não podem prender o item' }).click();
  await expect(painel.getByRole('heading', { name: 'Precisa de você · 2' })).toBeVisible();
  await expect(painel.getByRole('button', { name: 'Ampliar github.com/empresa-demo/dev.kit.page · última versão' }).getByRole('img')).toBeVisible();
  await page.screenshot({ path: `${pasta}/08-card-dispensado-e-miniatura-do-design.png` });
  await painel.getByRole('group', { name: 'Filtro das pendências' }).getByRole('button', { name: /Dispensadas/ }).click();
  await expect(painel.getByRole('button', { name: 'Restaurar' })).toBeVisible();
  await page.screenshot({ path: `${pasta}/09-filtro-dispensadas.png` });
  // A dispensa ficou na API: a lista padrão não a traz, e a marcada sim.
  const padrao = await (await request.get(`${API}/api/remoto/maquinas/${maquinaId}/decisoes`, { headers })).json();
  expect(padrao.map((d: { pendencia: { id: string } }) => d.pendencia.id)).toEqual(['aprovacao-item-4', 'pendencia-comando']);
  await painel.getByRole('button', { name: 'Restaurar' }).click();
  await painel.getByRole('group', { name: 'Filtro das pendências' }).getByRole('button', { name: 'Pendentes' }).click();
  await expect(painel.getByRole('heading', { name: 'Precisa de você · 3' })).toBeVisible();

  // 7. (ajuste 8) Os projetos: só o em andamento; o "Mostrar concluídos" traz o em fechamento e o concluído.
  await expect(painel.getByRole('link', { name: 'Abrir controle-remoto-v2' })).toHaveCount(0);
  await painel.getByRole('checkbox', { name: /Mostrar concluídos \(2\)/ }).check();
  await expect(painel.getByRole('link', { name: 'Abrir controle-remoto-v2' })).toContainText('em fechamento');
  await painel.getByRole('link', { name: 'Abrir controle-remoto-v2' }).scrollIntoViewIfNeeded();
  await page.screenshot({ path: `${pasta}/15-projetos-mostrar-concluidos.png` });
  await painel.getByRole('checkbox', { name: /Mostrar concluídos/ }).uncheck();

  // 8. (ajuste 9) A autorização do comando bloqueado: o escopo exato e o segundo fator, como o aprovar.
  await painel.getByRole('button', { name: 'Autorizar' }).click();
  await expect(page.getByLabel('Escopo da autorização')).toContainText('dotnet test tests/GitKit.Core.Tests/GitKit.Core.Tests.csproj');
  await page.getByRole('button', { name: 'Autorizar com segundo fator' }).click();
  await page.screenshot({ path: `${pasta}/16-autorizacao-com-segundo-fator.png` });
  await page.getByRole('button', { name: '‹ Voltar ao Board' }).click();

  // 9. (complemento do ajuste 5) A caixa começa com 3 linhas e cresce até 8, depois rola.
  const caixa = chat.getByLabel('Mensagem ao Orquestrador');
  const alturaInicial = (await caixa.boundingBox())!.height;
  await caixa.fill(Array.from({ length: 12 }, (_, i) => `Linha ${i + 1} do pedido ao Orquestrador`).join('\n'));
  const alturaCheia = (await caixa.boundingBox())!.height;
  expect(alturaCheia).toBeGreaterThan(alturaInicial * 2);
  expect(await caixa.evaluate(e => e.scrollHeight > e.clientHeight)).toBeTruthy();
  await page.screenshot({ path: `${pasta}/17-caixa-cresce-ate-8-linhas-desktop.png` });
  await caixa.fill('');

  // 6. Celular 390x844 (ajuste 6): as três listas preenchidas, o painel inteiro à vista, e o selo "decisões auditadas".
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/board?aba=chat');
  await expect(chat.getByText('Chat não aprova · decisões auditadas')).toBeVisible({ timeout: 25_000 });
  await expect(chat.getByRole('button', { name: 'Atualizar catálogo do dev.kit' })).toBeVisible();
  await expect(chat.getByRole('button', { name: 'Ampliar chat-remoto v016 · Proposta' }).getByRole('img')).toBeVisible();
  await page.screenshot({ path: `${pasta}/10-celular-chat.png` });
  for (const [botao, grupo, arquivo] of [
    ['Trocar fluxo', 'Do catálogo do dev.kit: fluxo, projeto ou design', '11-celular-lista-fluxos'],
    ['# Work item', 'Do catálogo do dev.kit: work item em andamento', '12-celular-lista-work-items'],
    ['@ Repositório', 'Do catálogo do dev.kit: repositório', '13-celular-lista-repositorios'],
  ] as const) {
    await chat.getByRole('button', { name: botao }).click();
    const lista = page.getByRole('group', { name: grupo });
    await expect(lista.getByRole('button').first()).toBeInViewport();
    expect(await lista.getByRole('button').count()).toBeGreaterThan(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
    await page.screenshot({ path: `${pasta}/${arquivo}.png` });
    await page.getByRole('button', { name: 'Cancelar' }).click();
  }
  // O teclado do celular não diz a tecla: o "/" digitado na caixa vazia abre a lista do mesmo jeito.
  await chat.getByLabel('Mensagem ao Orquestrador').fill('/');
  await expect(page.getByRole('group', { name: 'Do catálogo do dev.kit: fluxo, projeto ou design' })).toBeInViewport();
  await page.getByRole('button', { name: 'Cancelar' }).click();
  // A caixa do celular também cresce até 8 linhas, e as mensagens ficam com o resto da altura.
  await chat.getByLabel('Mensagem ao Orquestrador').fill(Array.from({ length: 10 }, (_, i) => `Linha ${i + 1}`).join('\n'));
  await page.screenshot({ path: `${pasta}/18-celular-caixa-cresce.png` });
  await chat.getByLabel('Mensagem ao Orquestrador').fill('');
  await page.goto('/board?aba=board');
  await expect(page.getByRole('complementary', { name: 'Board' }).getByRole('button', { name: /^Dispensar / }).first()).toBeVisible({ timeout: 25_000 });
  await page.screenshot({ path: `${pasta}/14-celular-board-com-fechar.png`, fullPage: true });
});
