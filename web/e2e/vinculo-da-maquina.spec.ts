import { expect, test, type APIRequestContext } from '@playwright/test';
import { generateKeyPairSync, randomUUID, sign, type KeyObject } from 'node:crypto';
import { garantirSenhaDefinitiva } from './admin';
import { API, CODIGO_DE_REGISTRO, NOVA_SENHA } from './ambiente';

// O client credentials da máquina (remote-control-v3 item 9) de ponta a ponta, sobre a API real: o "app" vincula a máquina
// com a chave pública de um par ECDSA P-256; o "serviço" troca uma asserção ES256 pelo token da máquina e publica o heartbeat
// com o app fechado; o Board mostra o vínculo e o Desvincular derruba o token na hora.

interface Anuncio { clientId: string; audiencia: string; rotaDoToken: string; escopo: string }

const b64url = (dados: Buffer | string) => Buffer.from(dados).toString('base64url');

/** A asserção do private_key_jwt, como o serviço do dev.kit a monta: curta e com um jti novo. */
function assercao(chave: KeyObject, anuncio: Anuncio) {
  const agora = Math.floor(Date.now() / 1000);
  const cabecalho = b64url(JSON.stringify({ alg: 'ES256', typ: 'JWT' }));
  const corpo = b64url(JSON.stringify({ iss: anuncio.clientId, sub: anuncio.clientId, aud: anuncio.audiencia, iat: agora, exp: agora + 60, jti: randomUUID() }));
  const assinatura = sign('sha256', Buffer.from(`${cabecalho}.${corpo}`), { key: chave, dsaEncoding: 'ieee-p1363' });
  return `${cabecalho}.${corpo}.${b64url(assinatura)}`;
}

async function pedirToken(request: APIRequestContext, chave: KeyObject, anuncio: Anuncio) {
  return request.post(`${API}/${anuncio.rotaDoToken}`, {
    form: {
      grant_type: 'client_credentials',
      client_assertion_type: 'urn:ietf:params:oauth:client-assertion-type:jwt-bearer',
      client_assertion: assercao(chave, anuncio),
    },
  });
}

test('O serviço vinculado publica com o token da máquina e o Desvincular do Board o derruba', async ({ page, request }) => {
  await garantirSenhaDefinitiva(request);
  const admin = (await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json()).token as string;
  const criado = await request.post(`${API}/api/usuarios`, {
    headers: { Authorization: `Bearer ${admin}` }, data: { login: `vinculo-demo-${Date.now()}`, nome: 'Pessoa Demonstração Vínculo', papel: 'dev' },
  });
  const { usuario: { login }, senhaTemporaria } = await criado.json();
  const temporaria = await (await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaTemporaria } })).json();
  const sessao = await (await request.post(`${API}/api/auth/trocar-senha`, {
    headers: { Authorization: `Bearer ${temporaria.token}` }, data: { senhaAtual: senhaTemporaria, novaSenha: NOVA_SENHA },
  })).json();

  // O "app" logado: registra a máquina e a vincula com a chave pública (a privada fica com ele).
  const maquinaId = `notebook-item9-${Date.now()}`;
  const registro = await (await request.post(`${API}/api/maquinas/registrar`, {
    data: { maquinaId, versaoDevKit: '125', codigoRegistro: CODIGO_DE_REGISTRO },
  })).json();
  const { privateKey, publicKey } = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const vinculo = await request.post(`${API}/api/auth/cliente-da-maquina`, {
    headers: { Authorization: `Bearer ${sessao.token}`, 'X-Machine-Key': registro.chave },
    data: { chavePublica: publicKey.export({ type: 'spki', format: 'der' }).toString('base64') },
  });
  expect(vinculo.ok()).toBeTruthy();
  const anuncio = (await vinculo.json()) as Anuncio;
  expect(anuncio).toMatchObject({ clientId: maquinaId, escopo: 'remoto', rotaDoToken: 'api/auth/token' });

  // O "serviço", com o app fechado: o token da máquina, e o heartbeat e o retrato com ele.
  const token = await pedirToken(request, privateKey, anuncio);
  expect(token.ok()).toBeTruthy();
  const daMaquina = { Authorization: `Bearer ${(await token.json()).access_token}` };
  const heartbeat = { contrato: 1, maquinaId, nome: 'Notebook', versaoDevKit: '125', origem: 'servico', appAberto: false, estadoDoAgente: 'parado', turnoEmAndamento: false };
  expect((await request.post(`${API}/api/remoto/heartbeat`, { headers: daMaquina, data: heartbeat })).ok()).toBeTruthy();
  expect((await request.put(`${API}/api/remoto/retrato`, {
    headers: daMaquina,
    data: { contrato: 1, maquinaId, geradoEm: new Date().toISOString(), pendencias: [], projetos: [], fluxos: [{ id: 'item-9', nome: 'Item 9 · client credentials', etapa: '', situacao: 'rodando' }] },
  })).ok()).toBeTruthy();
  // Fora do remoto, o token da máquina não entra.
  expect((await request.get(`${API}/api/remoto/maquinas`, { headers: daMaquina })).status()).toBe(401);

  await page.setViewportSize({ width: 1440, height: 920 });
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), sessao);
  await page.goto('/board');
  await expect(page.getByText('Item 9 · client credentials')).toBeVisible({ timeout: 25_000 });
  await expect(page.getByText(/serviço ativo · dev\.kit fechado/i).first()).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/item9-01-board-servico-com-token-da-maquina.png' });

  // O vínculo no painel do Board, nos dois temas e no celular.
  const painel = page.getByRole('complementary', { name: 'Board' });
  const cartao = painel.getByRole('region', { name: 'Vínculo do serviço' });
  await expect(cartao).toContainText('Serviço vinculado');
  await cartao.screenshot({ path: 'test-results/capturas/item9-02-cartao-do-vinculo.png' });
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.screenshot({ path: 'test-results/capturas/item9-03-board-vinculado-escuro.png' });
  await page.emulateMedia({ colorScheme: 'light' });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole('tab', { name: 'Board', exact: true }).click();
  await expect(cartao).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await page.screenshot({ path: 'test-results/capturas/item9-04-board-vinculado-celular.png' });
  await page.setViewportSize({ width: 1440, height: 920 });

  // Desvincular pede a confirmação; Cancelar não grava nada.
  await cartao.getByRole('button', { name: 'Desvincular', exact: true }).click();
  await expect(cartao).toContainText('O heartbeat e a fila desta máquina param');
  await cartao.screenshot({ path: 'test-results/capturas/item9-05-confirmar-desvincular.png' });
  await cartao.getByRole('button', { name: 'Cancelar', exact: true }).click();
  await expect(cartao.getByRole('button', { name: 'Desvincular', exact: true })).toBeVisible();
  expect((await request.post(`${API}/api/remoto/heartbeat`, { headers: daMaquina, data: heartbeat })).ok()).toBeTruthy();

  await cartao.getByRole('button', { name: 'Desvincular', exact: true }).click();
  await cartao.getByRole('button', { name: 'Desvincular o serviço', exact: true }).click();
  await expect(cartao).toContainText('Serviço sem vínculo');
  await expect(page.locator('.br-aviso').filter({ hasText: 'Serviço desvinculado' })).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/item9-06-board-desvinculado.png' });

  // O recurso funcionando: o token já emitido e o próximo pedido são recusados na hora.
  expect((await request.post(`${API}/api/remoto/heartbeat`, { headers: daMaquina, data: heartbeat })).status()).toBe(401);
  const recusa = await pedirToken(request, privateKey, anuncio);
  expect(recusa.status()).toBe(401);
  expect((await recusa.json()).error).toBe('invalid_client');
});
