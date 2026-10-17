import { expect, type APIRequestContext } from '@playwright/test';
import { API, NOVA_SENHA, SENHA_INICIAL } from './ambiente';

/**
 * O admin com a senha definitiva. Os cenários dividem a mesma base: se outro cenário já trocou a
 * senha, ela entra; se este roda sozinho, a troca obrigatória é feita aqui pela API.
 */
export async function garantirSenhaDefinitiva(request: APIRequestContext) {
  if ((await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).ok()) return;
  const inicial = await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: SENHA_INICIAL } });
  expect(inicial.ok()).toBeTruthy();
  const { token } = (await inicial.json()) as { token: string };
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    data: { senhaAtual: SENHA_INICIAL, novaSenha: NOVA_SENHA }, headers: { Authorization: `Bearer ${token}` },
  });
  expect(troca.ok()).toBeTruthy();
}

/** O token do admin, já com a senha definitiva. */
export async function tokenDoAdmin(request: APIRequestContext) {
  await garantirSenhaDefinitiva(request);
  return ((await (await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } })).json()) as { token: string }).token;
}

/** Faz a troca obrigatória da senha inicial de quem o admin criou e devolve o token com a senha definitiva. */
async function comSenhaDefinitiva(request: APIRequestContext, login: string, senhaInicial: string) {
  const inicial = (await (await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaInicial } })).json()) as { token: string };
  const troca = await request.post(`${API}/api/auth/trocar-senha`, {
    data: { senhaAtual: senhaInicial, novaSenha: NOVA_SENHA }, headers: { Authorization: `Bearer ${inicial.token}` },
  });
  expect(troca.ok()).toBeTruthy();
  return ((await (await request.post(`${API}/api/auth/login`, { data: { login, senha: NOVA_SENHA } })).json()) as { token: string }).token;
}

/** O gestor da empresa com a senha definitiva (o convite traz a inicial, de troca obrigatória); devolve o token dele. */
export async function gestorDefinitivo(request: APIRequestContext, token: string, empresa: number, login: string) {
  const convite = await request.post(`${API}/api/empresas/${empresa}/gestores`, { data: { login }, headers: { Authorization: `Bearer ${token}` } });
  expect(convite.ok()).toBeTruthy();
  const { senhaInicial } = (await convite.json()) as { senhaInicial: string };
  return comSenhaDefinitiva(request, login, senhaInicial);
}

/** Um dev da empresa criado pelo admin (US #405), com a senha definitiva; devolve o token dele. */
export async function devDefinitivo(request: APIRequestContext, token: string, empresa: number, login: string, nome: string) {
  const criacao = await request.post(`${API}/api/usuarios`, {
    data: { login, nome, papel: 'dev', empresaId: empresa }, headers: { Authorization: `Bearer ${token}` },
  });
  expect(criacao.ok()).toBeTruthy();
  const { senhaTemporaria } = (await criacao.json()) as { senhaTemporaria: string };
  return comSenhaDefinitiva(request, login, senhaTemporaria);
}
