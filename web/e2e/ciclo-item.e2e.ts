import { expect, test, type APIRequestContext, type Page, type Route } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';
import type { Projeto } from '../src/api/projetos';

const pasta = fileURLToPath(new URL(`../../../entrega/capturas-board-validacao/${Date.now()}/`, import.meta.url));

async function preparar(request: APIRequestContext) {
  await garantirSenhaDefinitiva(request);
  const admin = await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json();
  const headers = { Authorization: `Bearer ${admin.token}` };
  const empresa = await request.post(`${API}/api/empresas`, { headers, data: { nome: 'Equipe fictícia · ciclo humano', plano: 'Empresarial', assentos: 5 } });
  expect(empresa.ok()).toBeTruthy();
  const { id: empresaId } = await empresa.json();
  async function usuario(nome: string) {
    const criado = await request.post(`${API}/api/usuarios`, { headers,
      data: { login: `${nome}-ciclo-${Date.now()}`, nome: `Demonstração ${nome}`, papel: 'dev', empresaId } });
    expect(criado.ok()).toBeTruthy();
    const conta = await criado.json();
    const inicial = await (await request.post(`${API}/api/auth/login`, { data: { login: conta.usuario.login, senha: conta.senhaTemporaria } })).json();
    const troca = await request.post(`${API}/api/auth/trocar-senha`, { headers: { Authorization: `Bearer ${inicial.token}` },
      data: { senhaAtual: conta.senhaTemporaria, novaSenha: NOVA_SENHA } });
    expect(troca.ok()).toBeTruthy();
    return { sessao: await troca.json(), usuario: conta.usuario };
  }
  return { dono: await usuario('dono'), leitor: await usuario('leitor') };
}

async function capturar(page: Page, nome: string) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.screenshot({ path: `${pasta}/${nome}.png`, fullPage: true });
}

test('decisão humana real: dono/editor/leitor, versão, confirmação, recibo e teclado em1280/390', async ({ page, request, browser }) => {
  mkdirSync(pasta, { recursive: true });
  const contas = await preparar(request);
  const headers = { Authorization: `Bearer ${contas.dono.sessao.token}`, 'X-DevKit-Ciclo-Item': '1' };
  const leitorHeaders = { Authorization: `Bearer ${contas.leitor.sessao.token}`, 'X-DevKit-Ciclo-Item': '1' };
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), contas.dono.sessao);

  for (const largura of [1280, 390]) {
    await page.setViewportSize({ width: largura, height: largura === 390 ? 980 : 1100 });
    const slug = `ciclo-${largura}`;
    const envio = { slug, nome: `Ciclo humano · demonstração ${largura}`, nivel: 'usuario', time: '',
      repositorios: ['https://example.test/demo/aplicacao'], branch: `feature/demo/${slug}`, semanaConvertida: false,
      definicao: 'Somente dados fictícios; preservar trabalho parcial.', anexos: ['referencia/board/v009/hash', 'referencia/board/v010/hash'],
      autonomia: true, paralelismo: 2, destinoDaPr: 'main', situacao: 'aprovado', versao: null,
      itens: [1, 2, 3].map(numero => ({ numero, titulo: ['Integração anterior', 'Trabalho parcial', 'Continuidade'][numero - 1],
        descricao: 'Trabalho e evidências de demonstração.', fluxo: 'tarefa avulsa', repositorios: ['https://example.test/demo/aplicacao'],
        componentes: [], dependencias: numero > 1 ? [numero - 1] : [], onda: numero, workItemId: 0,
        situacao: numero === 1 ? 'integrado' : numero === 2 ? 'rodando' : 'planejado', branch: `feature/demo/${slug}-item-${numero}` })) };
    const criada = await request.put(`${API}/api/projetos`, { headers, data: envio });
    expect(criada.status()).toBe(201);
    const criado = await criada.json() as Projeto;
    const rota = `${API}/api/projetos/${criado.id}`;
    async function reler() {
      const r = await request.get(rota, { headers }); expect(r.ok()).toBeTruthy(); return await r.json() as Projeto;
    }
    await page.goto(`/projetos?slug=${slug}`);
    await page.getByRole('button', { name: '2 · Trabalho parcial', exact: true }).click();
    const detalhe = page.getByRole('region', { name: 'Detalhe do item 2', exact: true });
    const cancelar = detalhe.getByRole('button', { name: 'Cancelar item', exact: true });
    await expect(cancelar).toBeEnabled();
    await expect(page.getByRole('navigation', { name: 'Visões do projeto' }).getByRole('button')).toHaveCount(4);
    await capturar(page, `${largura}-acoes`);
    await cancelar.click();
    let modal = page.getByRole('dialog');
    await expect(modal.getByText('Conferindo a decisão…')).toHaveCount(0);
    await expect(modal.getByLabel('Motivo da decisão')).toBeFocused();
    expect(await modal.evaluate(d => d.matches(':modal'))).toBe(true);
    await expect(modal.getByRole('button', { name: 'Cancelar item', exact: true })).toBeDisabled();
    await modal.getByLabel('Motivo da decisão').fill('Revisar sem aplicar a decisão.');
    for (let n = 0; n < 7; n++) {
      await page.keyboard.press('Tab');
      expect(await modal.evaluate(d => d.contains(document.activeElement))).toBe(true);
    }
    for (let n = 0; n < 7; n++) {
      await page.keyboard.press('Shift+Tab');
      expect(await modal.evaluate(d => d.contains(document.activeElement))).toBe(true);
    }
    await capturar(page, `${largura}-cancelar`);
    await page.keyboard.press('Escape');
    await expect(modal).toHaveCount(0);
    await expect(cancelar).toBeFocused();
    expect((await reler()).itens[1].decisaoHumana).toBeFalsy();

    await detalhe.getByRole('button', { name: 'Concluir manualmente', exact: true }).click();
    modal = page.getByRole('dialog');
    const motivo = 'Preservar a entrega parcial e continuar o restante.';
    await modal.getByLabel('Motivo da decisão').fill(motivo);
    await expect(modal.getByRole('button', { name: 'Concluir manualmente', exact: true })).toBeEnabled();
    // Concorrência real: outra gravação muda a versão, sem mock de resposta nem banco compartilhado.
    const concorrente = await request.put(rota, { headers, data: { ...envio, versao: criado.versao, definicao: 'Atualização concorrente isolada' } });
    expect(concorrente.ok()).toBeTruthy();
    const verConcorrente = (await concorrente.json() as Projeto).versao;
    await modal.getByRole('button', { name: 'Concluir manualmente', exact: true }).click();
    await expect(modal.getByRole('alert')).toContainText('O projeto mudou');
    await expect(modal.getByLabel('Motivo da decisão')).toHaveValue(motivo);
    await expect(modal.getByRole('button', { name: 'Reler e revisar' })).toBeEnabled();
    expect((await reler()).itens[1].decisaoHumana).toBeFalsy();
    await capturar(page, `${largura}-conflito`);
    await modal.getByRole('button', { name: 'Reler e revisar' }).click();
    await expect(modal.getByRole('button', { name: 'Concluir manualmente', exact: true })).toBeEnabled();
    await modal.getByRole('button', { name: 'Concluir manualmente', exact: true }).click();
    await expect(modal).toHaveCount(0);
    let atual = await reler();
    expect(atual.itens[1].situacao).toBe('concluido-manual');
    expect(atual.itens[1].decisaoHumana).toMatchObject({ motivo, versaoEsperada: String(verConcorrente), paradaPendente: true,
      confirmacao: `${slug}/2/concluir-manualmente/remota:${verConcorrente}` });
    expect(atual.itens[0].situacao).toBe('integrado');
    expect(atual.itens[1].branch).toBe(envio.itens[1].branch);
    expect(atual.anexos).toEqual(envio.anexos);
    await page.reload();
    await page.getByRole('button', { name: '2 · Trabalho parcial', exact: true }).click();
    await expect(page.getByRole('region', { name: 'Detalhe do item 2', exact: true }).getByText('Concluído pelo dev', { exact: true })).toBeVisible();
    await expect(page.getByText('1 Integrado', { exact: true })).toBeVisible();
    await capturar(page, `${largura}-recibo-reaberto`);

    const concessao = await request.post(`${rota}/acessos`, { headers, data: { usuario: contas.leitor.usuario.login, papel: 'leitor' } });
    expect(concessao.ok()).toBeTruthy();
    const acessos = await concessao.json();
    const contexto = await browser.newContext({ baseURL: new URL(page.url()).origin,
      viewport: { width: largura, height: largura === 390 ? 980 : 1100 } });
    const leitor = await contexto.newPage();
    await leitor.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), contas.leitor.sessao);
    await leitor.goto(`/projetos?slug=${slug}`);
    await leitor.getByRole('button', { name: '3 · Continuidade', exact: true }).click();
    const dele = leitor.getByRole('region', { name: 'Detalhe do item 3', exact: true });
    await expect(dele.getByText('Somente dono ou editor pode decidir em um projeto aberto.')).toBeVisible();
    await expect(dele.getByRole('button', { name: 'Cancelar item', exact: true })).toBeDisabled();
    const negado = await request.post(`${rota}/itens/3/decisao`, { headers: leitorHeaders,
      data: { pedido: crypto.randomUUID(), acao: 'cancelar-item', versaoEsperada: atual.versao } });
    expect(negado.status()).toBe(403);
    await capturar(leitor, `${largura}-leitor`);
    const idLeitor = acessos.acessos.find((a: { usuario: { login: string } }) => a.usuario.login === contas.leitor.usuario.login).usuario.id;
    expect((await request.put(`${rota}/acessos/${idLeitor}`, { headers, data: { papel: 'editor' } })).ok()).toBeTruthy();
    await leitor.reload();
    await leitor.getByRole('button', { name: '3 · Continuidade', exact: true }).click();
    await expect(dele.getByRole('button', { name: 'Cancelar item', exact: true })).toBeEnabled();
    await dele.getByRole('button', { name: 'Cancelar item', exact: true }).click();
    const cancelamento = leitor.getByRole('dialog');
    await cancelamento.getByLabel('Motivo da decisão').fill('Escopo encerrado pelo editor de demonstração.');
    await expect(cancelamento.getByRole('button', { name: 'Cancelar item', exact: true })).toBeEnabled();
    // A API real recusa a credencial da tentativa; leitura continua autenticada, sem auto-replay.
    const recusar = async (route: Route) => {
      if (route.request().postDataJSON()?.aplicar) await route.continue({ headers: { ...route.request().headers(), authorization: 'Bearer credencial-ficticia-invalida' } });
      else await route.continue();
    };
    await leitor.route('**/itens/3/decisao', recusar);
    await cancelamento.getByRole('button', { name: 'Cancelar item', exact: true }).click();
    await expect(cancelamento.getByRole('alert')).toContainText('401');
    await expect(cancelamento.getByLabel('Motivo da decisão')).toHaveValue('Escopo encerrado pelo editor de demonstração.');
    await capturar(leitor, `${largura}-sessao-recusada`);
    await leitor.unroute('**/itens/3/decisao', recusar);
    await expect(cancelamento.getByRole('button', { name: 'Reler e revisar' })).toBeEnabled();
    await cancelamento.getByRole('button', { name: 'Reler e revisar' }).click();
    await expect(cancelamento.getByRole('button', { name: 'Cancelar item', exact: true })).toBeEnabled();
    await cancelamento.getByRole('button', { name: 'Cancelar item', exact: true }).click();
    await expect(cancelamento).toHaveCount(0);
    atual = await reler();
    expect(atual.itens[2].situacao).toBe('cancelado');
    expect(atual.itens[1].situacao).toBe('concluido-manual');
    expect(atual.itens[0].situacao).toBe('integrado');
    await contexto.close();
  }
});
