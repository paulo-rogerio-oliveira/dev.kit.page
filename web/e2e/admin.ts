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
