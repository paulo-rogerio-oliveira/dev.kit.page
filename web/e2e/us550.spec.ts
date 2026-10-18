import { expect, test, type APIRequestContext, type Browser, type Page } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA, PORTA_DA_WEB } from './ambiente';

const WEB = `http://localhost:${PORTA_DA_WEB}`;

/** Troca a senha inicial (ou temporária) pela definitiva e devolve o token dela. */
async function definitiva(request: APIRequestContext, login: string, senha: string) {
  const inicial = (await (await request.post(`${API}/api/auth/login`, { data: { login, senha } })).json()) as { token: string };
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    data: { senhaAtual: senha, novaSenha: NOVA_SENHA }, headers: { Authorization: `Bearer ${inicial.token}` },
  });
  expect(troca.ok()).toBeTruthy();
  return ((await troca.json()) as { token: string }).token;
}

/** Cada papel no seu navegador (contexto próprio): a sessão de um não passa para o outro. */
async function entrar(browser: Browser, login: string): Promise<Page> {
  const page = await (await browser.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
  await page.goto(`${WEB}/login`);
  await page.getByLabel('Login').fill(login);
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/(dashboard|conta)$/);
  return page;
}

/**
 * A US #550 no navegador, com as capturas da entrega: o dev grava o PRÓPRIO contexto em /conta, o gestor grava o da
 * empresa e o de uma equipe na aba Contexto, o admin geral vê os três níveis no painel da empresa e edita o do dev — e a
 * rota que o dev.kit lê (GET /api/empresa/convencoes?time=) traz a composição dos três níveis.
 */
test('dev grava o próprio contexto → gestor grava empresa e equipe → admin vê e edita os três níveis → o dev.kit lê a composição', async ({ browser, request }) => {
  // 1. A empresa, o gestor e o dev (pela gestão de usuários do admin).
  await garantirSenhaDefinitiva(request);
  const admin = { Authorization: `Bearer ${((await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json()) as { token: string }).token}` };
  const sufixo = Date.now();
  const nomeDaEmpresa = `Empresa US550 ${sufixo}`;
  const empresa = (await (await request.post(`${API}/api/empresas`, { data: { nome: nomeDaEmpresa, plano: 'Empresarial', assentos: 3 }, headers: admin })).json()) as { id: number };
  const gestor = `gestor.us550.${sufixo}`;
  const convite = (await (await request.post(`${API}/api/empresas/${empresa.id}/gestores`, { data: { login: gestor }, headers: admin })).json()) as { senhaInicial: string };
  await definitiva(request, gestor, convite.senhaInicial);
  const dev = `dev.us550.${sufixo}`;
  const criado = (await (await request.post(`${API}/api/usuarios`, { data: { login: dev, nome: 'Dev US550', papel: 'dev', empresaId: empresa.id }, headers: admin })).json()) as { senhaTemporaria: string };
  const doDev = { Authorization: `Bearer ${await definitiva(request, dev, criado.senhaTemporaria)}` };

  // 2. O dev grava o próprio contexto em /conta → Meu contexto.
  const paginaDoDev = await entrar(browser, dev);
  const meuContexto = paginaDoDev.getByRole('region', { name: 'Meu contexto' });
  await meuContexto.getByLabel('Tema', { exact: true }).fill('estilo');
  await meuContexto.getByLabel('Texto', { exact: true }).fill('Respostas curtas, com o porquê.\nSempre com os testes do que mudou.');
  await meuContexto.getByRole('button', { name: 'Gravar' }).click();
  await expect(meuContexto.getByText('Tema estilo gravado no seu contexto.')).toBeVisible();
  await paginaDoDev.screenshot({ path: 'test-results/capturas/70-meu-contexto.png', fullPage: true });

  // 3. O gestor grava o da empresa e o da equipe vendas na aba Contexto.
  const paginaDoGestor = await entrar(browser, gestor);
  await paginaDoGestor.goto(`${WEB}/empresa/conhecimento?aba=contexto`);
  const daEmpresa = paginaDoGestor.getByRole('region', { name: 'Empresa', exact: true });
  await daEmpresa.getByLabel('Tema', { exact: true }).fill('stack');
  await daEmpresa.getByLabel('Texto', { exact: true }).fill('.NET 10 na API e React 19 na web.');
  await daEmpresa.getByRole('button', { name: 'Gravar' }).click();
  await expect(paginaDoGestor.getByText('Tema stack gravado no contexto.')).toBeVisible();
  const daEquipe = paginaDoGestor.getByRole('region', { name: 'Equipe', exact: true });
  await daEquipe.getByLabel('Equipe', { exact: true }).fill('Vendas');
  await daEquipe.getByLabel('Tema', { exact: true }).fill('stack');
  await daEquipe.getByLabel('Texto', { exact: true }).fill('Angular 19 no front do time de vendas.');
  await daEquipe.getByRole('button', { name: 'Gravar' }).click();
  await expect(daEquipe.getByRole('list', { name: 'Contexto da equipe vendas' }).getByText('Angular 19 no front do time de vendas.')).toBeVisible();
  // O gestor lê o contexto do dev, sem editar.
  await expect(paginaDoGestor.getByRole('region', { name: 'Usuários', exact: true }).getByText('Sempre com os testes do que mudou.', { exact: false })).toBeVisible();
  await paginaDoGestor.screenshot({ path: 'test-results/capturas/71-contexto-do-gestor.png', fullPage: true });

  // 4. O admin geral abre o painel Contexto da empresa: os três níveis, e edita o do dev.
  const paginaDoAdmin = await entrar(browser, 'admin');
  await paginaDoAdmin.goto(`${WEB}/empresas`);
  await paginaDoAdmin.getByRole('button', { name: `Contexto de ${nomeDaEmpresa}` }).click();
  const painel = paginaDoAdmin.getByRole('complementary', { name: `Contexto de ${nomeDaEmpresa}` });
  await expect(painel.getByText('.NET 10 na API e React 19 na web.')).toBeVisible();
  const usuarios = painel.getByRole('region', { name: 'Usuários', exact: true });
  await usuarios.getByLabel('Usuário', { exact: true }).selectOption(dev);
  await usuarios.getByRole('button', { name: 'Editar o tema estilo' }).click();
  await usuarios.getByLabel('Texto', { exact: true }).fill('Respostas curtas, com o porquê (revisado pelo admin).');
  await usuarios.getByRole('button', { name: 'Gravar' }).click();
  await expect(painel.getByText('Tema estilo gravado.')).toBeVisible();
  await paginaDoAdmin.screenshot({ path: 'test-results/capturas/72-contexto-do-admin.png', fullPage: true });

  // 5. O que o dev.kit do dev lê: empresa → equipe → usuário, no contrato de sempre.
  const lido = (await (await request.get(`${API}/api/empresa/convencoes?time=vendas`, { headers: doDev })).json()) as { nivel: string; chave: string; valor: string }[];
  expect(lido.filter((c) => c.chave.startsWith('contexto.')).map((c) => `${c.nivel}:${c.chave}`)).toEqual(['empresa:contexto.stack', 'time:contexto.stack', 'usuario:contexto.estilo']);
  expect(lido.find((c) => c.nivel === 'usuario')!.valor).toBe('Respostas curtas, com o porquê (revisado pelo admin).');
});
