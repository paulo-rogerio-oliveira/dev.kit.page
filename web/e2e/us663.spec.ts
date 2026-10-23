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
  page.on('dialog', (dialogo) => void dialogo.accept()); // as confirmações de promover e descartar
  await page.goto(`${WEB}/login`);
  await page.getByLabel('Login').fill(login);
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/(dashboard|conta)$/);
  return page;
}

interface Convencao { id: number; nivel: string; chave: string; valor: string; origem?: string; versao?: number }

/**
 * A US #663 de ponta a ponta no site, com as capturas da entrega: o AGENTE do dev (o mesmo POST que o
 * `devcli contexto --sugerir --aplicar` faz, com o token do dev) registra sugestões na camada do usuário e na da empresa;
 * o site as mostra como SUGESTÃO, ao lado da regra; o segredo é recusado (422); o gestor PROMOVE a da empresa a regra e
 * descarta outra; a rota antiga continua só com regras, e a com ?origem=todas traz a promovida como regra — que é o que o
 * dev.kit põe em "Regras do contexto (obrigatórias)" no próximo prompt.
 */
test('o agente sugere → o site mostra a sugestão → o gestor promove → vale como regra na leitura do dev.kit', async ({ browser, request }) => {
  // 1. A empresa, o gestor e o dev.
  await garantirSenhaDefinitiva(request);
  const admin = { Authorization: `Bearer ${((await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json()) as { token: string }).token}` };
  const sufixo = Date.now();
  const nomeDaEmpresa = `Empresa US663 ${sufixo}`;
  const empresa = (await (await request.post(`${API}/api/empresas`, { data: { nome: nomeDaEmpresa, plano: 'Empresarial', assentos: 3 }, headers: admin })).json()) as { id: number };
  const gestor = `gestor.us663.${sufixo}`;
  const convite = (await (await request.post(`${API}/api/empresas/${empresa.id}/gestores`, { data: { login: gestor }, headers: admin })).json()) as { senhaInicial: string };
  const doGestor = { Authorization: `Bearer ${await definitiva(request, gestor, convite.senhaInicial)}` };
  const dev = `dev.us663.${sufixo}`;
  const criado = (await (await request.post(`${API}/api/usuarios`, { data: { login: dev, nome: 'Dev US663', papel: 'dev', empresaId: empresa.id }, headers: admin })).json()) as { senhaTemporaria: string };
  const doDev = { Authorization: `Bearer ${await definitiva(request, dev, criado.senhaTemporaria)}` };

  // 2. As REGRAS de antes: o gestor grava o stack da empresa, o dev o estilo dele.
  expect((await request.put(`${API}/api/empresa/convencoes`, { data: { nivel: 'empresa', chave: 'contexto.stack', valor: '.NET 10 na API.' }, headers: doGestor })).ok()).toBeTruthy();
  expect((await request.put(`${API}/api/empresa/convencoes`, { data: { nivel: 'usuario', chave: 'contexto.estilo', valor: 'Respostas curtas.' }, headers: doDev })).ok()).toBeTruthy();

  // 3. O AGENTE do dev, ao concluir a task, registra o que aprendeu como SUGESTÃO (o devcli contexto --sugerir).
  const sugerir = (camada: string, tema: string, texto: string) =>
    request.post(`${API}/api/empresa/contexto/sugestoes`, { data: { camada, tema, texto, origem: 'usuario' }, headers: doDev });
  const testes = await sugerir('empresa', 'testes', 'Rode o lint e os testes do que mudou antes do PR.');
  expect(testes.ok()).toBeTruthy();
  expect(((await testes.json()) as Convencao).origem).toBe('agente'); // o corpo não escolhe a origem
  expect((await sugerir('empresa', 'stack', '.NET 8 na API.')).ok()).toBeTruthy(); // um tema que já tem regra
  expect((await sugerir('usuario', 'encoding', 'Os .bas do VB6 são Windows-1252: nunca converta para UTF-8.')).ok()).toBeTruthy();
  // O segredo é recusado, sem ecoar o valor.
  const comSegredo = await sugerir('usuario', 'acesso', 'Clone com ghp_abcdefghijklmnopqrstuvwxyz0123456789');
  expect(comSegredo.status()).toBe(422);
  expect(await comSegredo.text()).not.toContain('ghp_');

  // 4. O dev vê em /conta a regra dele e, à parte, a sugestão do agente dele.
  const paginaDoDev = await entrar(browser, dev);
  const meuContexto = paginaDoDev.getByRole('region', { name: 'Meu contexto' });
  await expect(meuContexto.getByRole('list', { name: 'Seu contexto', exact: true }).getByText('Respostas curtas.')).toBeVisible();
  await expect(meuContexto.getByRole('list', { name: 'Seu contexto — sugestões do agente' }).getByText(/Windows-1252/)).toBeVisible();
  await paginaDoDev.screenshot({ path: 'test-results/capturas/80-meu-contexto-regra-e-sugestao.png', fullPage: true });

  // 5. O gestor, na aba Contexto: a regra e as sugestões da empresa separadas; promove a do lint e descarta a do stack.
  const paginaDoGestor = await entrar(browser, gestor);
  await paginaDoGestor.goto(`${WEB}/empresa/conhecimento?aba=contexto`);
  const daEmpresa = paginaDoGestor.getByRole('region', { name: 'Empresa', exact: true });
  const sugestoesDaEmpresa = daEmpresa.getByRole('list', { name: 'Contexto da empresa — sugestões do agente' });
  await expect(sugestoesDaEmpresa.getByText('Rode o lint e os testes do que mudou antes do PR.')).toBeVisible();
  await expect(sugestoesDaEmpresa.getByText('Este tema já tem regra: a regra prevalece.')).toBeVisible();
  await paginaDoGestor.screenshot({ path: 'test-results/capturas/81-contexto-da-empresa-sugestoes.png', fullPage: true });

  await sugestoesDaEmpresa.getByRole('button', { name: 'Promover a regra a sugestão testes' }).click();
  await expect(paginaDoGestor.getByText('Sugestão testes promovida a regra.')).toBeVisible();
  await daEmpresa.getByRole('list', { name: 'Contexto da empresa — sugestões do agente' }).getByRole('button', { name: 'Descartar a sugestão stack' }).click();
  await expect(paginaDoGestor.getByText('Sugestão stack descartada.')).toBeVisible();
  await expect(daEmpresa.getByRole('list', { name: 'Contexto da empresa', exact: true }).getByText('Rode o lint e os testes do que mudou antes do PR.')).toBeVisible();
  await paginaDoGestor.screenshot({ path: 'test-results/capturas/82-contexto-da-empresa-promovida.png', fullPage: true });

  // 6. O admin geral vê as duas partes no painel da empresa.
  const paginaDoAdmin = await entrar(browser, 'admin');
  await paginaDoAdmin.goto(`${WEB}/empresas`);
  await paginaDoAdmin.getByRole('button', { name: `Contexto de ${nomeDaEmpresa}` }).click();
  const painel = paginaDoAdmin.getByRole('complementary', { name: `Contexto de ${nomeDaEmpresa}` });
  await expect(painel.getByRole('heading', { name: 'Sugestão (do agente)' }).first()).toBeVisible();
  await paginaDoAdmin.screenshot({ path: 'test-results/capturas/83-contexto-do-admin-regra-e-sugestao.png', fullPage: true });

  // 7. O que o dev.kit lê. A rota ANTIGA (o dev.kit instalado): só regras, e a promovida já é uma delas.
  const antiga = (await (await request.get(`${API}/api/empresa/convencoes`, { headers: doDev })).json()) as Convencao[];
  expect(antiga.map((c) => `${c.nivel}:${c.chave}`).sort()).toEqual(['empresa:contexto.stack', 'empresa:contexto.testes', 'usuario:contexto.estilo']);
  // Com ?origem=todas (o dev.kit desta US): as regras e a sugestão que sobrou, cada uma com a origem.
  const todas = (await (await request.get(`${API}/api/empresa/convencoes?origem=todas`, { headers: doDev })).json()) as Convencao[];
  expect(todas.find((c) => c.chave === 'contexto.testes')).toMatchObject({ nivel: 'empresa', origem: 'usuario' });
  expect(todas.filter((c) => c.origem === 'agente').map((c) => c.chave)).toEqual(['contexto.encoding']);
  expect(todas.find((c) => c.chave === 'contexto.stack' && c.nivel === 'empresa')!.valor).toBe('.NET 10 na API.'); // a sugestão não tocou a regra
});
