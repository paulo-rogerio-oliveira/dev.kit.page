import { expect, test, type APIRequestContext, type Browser, type Page } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA, PORTA_DA_WEB } from './ambiente';

const WEB = `http://localhost:${PORTA_DA_WEB}`;

async function definitiva(request: APIRequestContext, login: string, senha: string) {
  const inicial = await (await request.post(`${API}/api/auth/login`, { data: { login, senha } })).json() as { token: string };
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    data: { senhaAtual: senha, novaSenha: NOVA_SENHA }, headers: { Authorization: `Bearer ${inicial.token}` },
  });
  expect(troca.ok()).toBeTruthy();
  return ((await troca.json()) as { token: string }).token;
}

async function entrar(browser: Browser, login: string): Promise<Page> {
  const contexto = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const page = await contexto.newPage();
  await page.goto(`${WEB}/login`);
  await page.getByLabel('Login').fill(login);
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/(dashboard|conta)$/);
  await page.goto(`${WEB}/projetos`);
  return page;
}

async function capturar(page: Page, arquivo: string) {
  await expect(page.getByRole('article', { name: 'Detalhe do projeto' })).toBeVisible();
  await page.screenshot({ path: `test-results/capturas/${arquivo}.png`, fullPage: true });
}

test('projeto em ondas: dono concede, leitor só consulta, editor altera e remoção revoga acesso', async ({ browser, request }) => {
  await garantirSenhaDefinitiva(request);
  const sessao = await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json() as { token: string };
  const admin = { Authorization: `Bearer ${sessao.token}` };
  const sufixo = Date.now();
  const empresa = await (await request.post(`${API}/api/empresas`, {
    headers: admin, data: { nome: `Equipe Projeto ${sufixo}`, plano: 'Empresarial', assentos: 5 },
  })).json() as { id: number };
  const dono = `dono.projeto.${sufixo}`;
  const convite = await (await request.post(`${API}/api/empresas/${empresa.id}/gestores`, { headers: admin, data: { login: dono } })).json() as { senhaInicial: string };
  const doDono = { Authorization: `Bearer ${await definitiva(request, dono, convite.senhaInicial)}` };
  const dev = `bia.projeto.${sufixo}`;
  const usuario = await (await request.post(`${API}/api/usuarios`, {
    headers: admin, data: { login: dev, nome: 'Bia Lima', papel: 'dev', empresaId: empresa.id },
  })).json() as { senhaTemporaria: string };
  const daDev = { Authorization: `Bearer ${await definitiva(request, dev, usuario.senhaTemporaria)}` };
  const envio = {
    slug: 'controle-remoto-demo', nome: 'Controle remoto · demonstração', nivel: 'empresa', time: '',
    repositorios: ['https://github.com/acme/git.kit', 'https://github.com/acme/dev.kit.page'],
    branch: 'feature/dev/projeto-controle-remoto-demo', semanaConvertida: false, definicao: 'Operar o dev.kit pelo navegador.',
    anexos: [], autonomia: true, paralelismo: 2, destinoDaPr: 'feature/dev/semana7', situacao: 'aprovado', versao: null,
    itens: [
      { numero: 1, titulo: 'Autenticação', descricao: 'Identidade da sessão', fluxo: 'tarefa avulsa', repositorios: [], componentes: ['API'],
        dependencias: [], onda: 1, workItemId: 0, situacao: 'integrado', branch: 'feature/dev/projeto-controle-remoto-demo-item-1' },
      { numero: 2, titulo: 'Painel remoto', descricao: 'Acompanhar as execuções', fluxo: 'Tela', repositorios: [], componentes: ['Web'],
        dependencias: [1], onda: 2, workItemId: 0, situacao: 'planejado', branch: 'feature/dev/projeto-controle-remoto-demo-item-2' },
    ],
  };
  const criacao = await request.put(`${API}/api/projetos`, { headers: doDono, data: envio });
  expect(criacao.status()).toBe(201);
  const criado = await criacao.json() as { id: number; versao: number };
  const rota = `${API}/api/projetos/${criado.id}`;
  expect(await (await request.get(`${API}/api/projetos`, { headers: daDev })).json()).toEqual([]);
  expect((await request.get(rota, { headers: daDev })).status()).toBe(404);

  const paginaDono = await entrar(browser, dono);
  const painel = paginaDono.getByRole('region', { name: 'Acesso' });
  await expect(painel.getByText(/Seu papel:/)).toContainText('Dono');
  // Base reconstituída: a página real com somente o painel acrescentado oculto.
  const oculto = await paginaDono.addStyleTag({ content: '.design-acesso { display: none !important; }' });
  await capturar(paginaDono, '100-projeto-acesso-base-dark');
  await oculto.evaluate(estilo => estilo.parentNode?.removeChild(estilo));
  await capturar(paginaDono, '101-projeto-acesso-dono-dark');

  await painel.getByLabel('Usuário da empresa').selectOption(dev);
  await painel.getByRole('button', { name: 'Dar acesso' }).click();
  await expect(painel.getByLabel(`Papel de ${dev}`)).toHaveValue('leitor');
  await capturar(paginaDono, '102-projeto-acesso-concedido-dark');
  const paginaDev = await entrar(browser, dev);
  const dela = paginaDev.getByRole('region', { name: 'Acesso' });
  await expect(dela.getByText(/Seu papel:/)).toContainText('Leitor');
  await expect(dela.getByRole('form', { name: 'Dar acesso' })).toHaveCount(0);
  await capturar(paginaDev, '103-projeto-acesso-leitor-dark');
  const recusa = await request.put(rota, { headers: daDev, data: { ...envio, versao: criado.versao } });
  expect(recusa.status()).toBe(403);
  expect((await recusa.json() as { detail: string }).detail).toContain('editor ou dono');

  await painel.getByLabel(`Papel de ${dev}`).selectOption('editor');
  await expect(painel.getByText(`Bia Lima (${dev}) agora é editor do projeto.`)).toBeVisible();
  await capturar(paginaDono, '104-projeto-acesso-editor-concedido-dark');
  await paginaDev.reload();
  await expect(dela.getByText(/Seu papel:/)).toContainText('Editor');
  await capturar(paginaDev, '105-projeto-acesso-editor-dark');
  const gravacao = await request.put(rota, { headers: daDev, data: { ...envio, versao: criado.versao } });
  expect(gravacao.status()).toBe(200);

  paginaDono.once('dialog', dialogo => void dialogo.dismiss());
  await painel.getByRole('button', { name: `Remover ${dev}` }).click();
  await expect(painel.getByLabel(`Papel de ${dev}`)).toBeVisible();
  paginaDono.once('dialog', dialogo => void dialogo.accept());
  await painel.getByRole('button', { name: `Remover ${dev}` }).click();
  await expect(painel.getByText(`Bia Lima (${dev}) não tem mais acesso ao projeto.`)).toBeVisible();
  await capturar(paginaDono, '106-projeto-acesso-removido-dark');
  expect(await (await request.get(`${API}/api/projetos`, { headers: daDev })).json()).toEqual([]);
  expect((await request.get(rota, { headers: daDev })).status()).toBe(404);
  await paginaDev.reload();
  await expect(paginaDev.getByText(/Nenhum projeto sincronizado/)).toBeVisible();
  await paginaDev.screenshot({ path: 'test-results/capturas/107-projeto-acesso-sem-acesso-dark.png', fullPage: true });
  await paginaDono.context().close();
  await paginaDev.context().close();
});
