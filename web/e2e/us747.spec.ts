import { expect, test } from '@playwright/test';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

test('projetos sincronizados mostram plano, andamento e branches na lista e no detalhe', async ({ page, request }) => {
  await garantirSenhaDefinitiva(request);
  const login = await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } });
  expect(login.ok()).toBeTruthy();
  const sessao = await login.json() as { token: string };
  const headers = { Authorization: `Bearer ${sessao.token}` };
  const repositorios = ['https://github.com/exemplo/app-desktop', 'https://github.com/exemplo/app-web'];
  const envio = {
    slug: 'sincronia-demo', nome: 'Sincronia de demonstração', nivel: 'usuario', time: '', repositorios,
    branch: 'feature/dev/projeto-demo', semanaConvertida: false, definicao: 'Demonstração isolada de sincronização.',
    anexos: [], autonomia: true, paralelismo: 3, destinoDaPr: 'feature/dev/semana5', situacao: 'aprovado', versao: null,
    featureId: 0, epicId: 0, iteracao: '', board: 'nao', branchesDasTasks: [], atualizadoEm: new Date().toISOString(),
    itens: [
      { numero: 1, titulo: 'Contratos e rotas do servidor', descricao: '', fluxo: '6', repositorios: [repositorios[1]],
        componentes: ['API'], dependencias: [], onda: 1, workItemId: 0, situacao: 'integrado', tipo: '', prioridade: 1, horas: 4, branch: 'feature/dev/projeto-demo-item-1' },
      { numero: 2, titulo: 'Cliente desktop envia e baixa', descricao: '', fluxo: '6', repositorios: [repositorios[0]],
        componentes: ['Sincronia'], dependencias: [1], onda: 2, workItemId: 0, situacao: 'rodando', tipo: '', prioridade: 1, horas: 8, branch: 'feature/dev/projeto-demo-item-2' },
      { numero: 3, titulo: 'Botão Sincronizar nas abas', descricao: '', fluxo: '6', repositorios: [repositorios[0]],
        componentes: ['Telas'], dependencias: [2], onda: 3, workItemId: 0, situacao: 'planejado', tipo: '', prioridade: 2, horas: 4, branch: 'feature/dev/projeto-demo-item-3' },
    ],
  };
  expect((await request.put(`${API}/api/projetos`, { headers, data: envio })).status()).toBe(201);
  expect((await request.put(`${API}/api/projetos`, { headers, data: { ...envio, slug: 'outro', nome: 'Outro projeto' } })).status()).toBe(201);
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/login');
  await page.getByLabel('Login').fill('admin');
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/(dashboard|conta)$/);
  await page.goto('/projetos?nivel=usuario');
  await page.getByRole('button', { name: /^Sincronia de demonstração/ }).click();
  const detalhe = page.getByRole('article', { name: 'Detalhe do projeto' });
  await expect(detalhe.getByText('Onda 2 de 3')).toBeVisible();
  await expect(detalhe.getByText('feature/dev/projeto-demo-item-2')).toBeVisible();
  await expect(detalhe.getByText('1 Integrado')).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/us747-projetos.png', fullPage: true });
  await page.getByRole('button', { name: /^Outro projeto/ }).click();
  await expect(detalhe.getByRole('heading', { name: 'Outro projeto', exact: true })).toBeVisible();
  await expect(page.getByLabel('Nível de compartilhamento')).toHaveValue('usuario');
});
