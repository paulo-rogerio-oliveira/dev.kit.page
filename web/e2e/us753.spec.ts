import { expect, test } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { writeFileSync, unlinkSync } from 'node:fs';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

test('gestor consulta os assinantes reais da skill por teclado e em largura estreita', async ({ page, request }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await garantirSenhaDefinitiva(request);
  const admin = await request.post(`${API}/api/auth/login`, { data: { login: 'admin', senha: NOVA_SENHA } });
  expect(admin.ok()).toBeTruthy();
  const { token: tokenAdmin } = await admin.json();
  const adminHeaders = { Authorization: `Bearer ${tokenAdmin}` };
  const criada = await request.post(`${API}/api/empresas`, { headers: adminHeaders, data: { nome: 'Empresa Demonstração 753', plano: 'Empresarial', assentos: 5 } });
  expect(criada.ok()).toBeTruthy();
  const empresa = await criada.json();
  const marca = Date.now();
  const login = `gestor.demo.${marca}`;
  const convite = await request.post(`${API}/api/empresas/${empresa.id}/gestores`, { headers: adminHeaders, data: { login } });
  expect(convite.ok()).toBeTruthy();
  const { senhaInicial } = await convite.json();
  const inicial = await request.post(`${API}/api/auth/login`, { data: { login, senha: senhaInicial } });
  expect(inicial.ok()).toBeTruthy();
  const { token: tokenInicial } = await inicial.json();
  const troca = await request.post(`${API}/api/auth/trocar-senha`, { headers: { Authorization: `Bearer ${tokenInicial}` }, data: { senhaAtual: senhaInicial, novaSenha: NOVA_SENHA } });
  expect(troca.ok()).toBeTruthy();
  const sessao = await troca.json();
  const headers = { Authorization: `Bearer ${sessao.token}` };
  async function publicar(nome: string) {
    const resposta = await request.post(`${API}/api/catalogo/empresa`, { headers, data: { tipo: 'skill', nome, descricao: 'Skill de demonstração', conteudo: '{}' } });
    expect(resposta.ok()).toBeTruthy();
    return (await resposta.json()).id as number;
  }
  const primeira = await publicar('gerar-testes-demo');
  const segunda = await publicar('revisar-alteracoes-demo');
  const vazia = await publicar('skill-sem-assinantes');
  for (let i = 1; i <= 3; i++) {
    const usuarioLogin = `pessoa.demo.${marca}.${i}`;
    const usuario = await request.post(`${API}/api/usuarios`, { headers: adminHeaders, data: { login: usuarioLogin, nome: `Pessoa Demonstração ${i}`, papel: 'dev', empresaId: empresa.id } });
    expect(usuario.ok()).toBeTruthy();
    const { senhaTemporaria } = await usuario.json();
    const entrada = await request.post(`${API}/api/auth/login`, { data: { login: usuarioLogin, senha: senhaTemporaria } });
    expect(entrada.ok()).toBeTruthy();
    const { token } = await entrada.json();
    const senha = await request.post(`${API}/api/auth/trocar-senha`, { headers: { Authorization: `Bearer ${token}` }, data: { senhaAtual: senhaTemporaria, novaSenha: NOVA_SENHA } });
    expect(senha.ok()).toBeTruthy();
    const autenticado = await senha.json();
    const devHeaders = { Authorization: `Bearer ${autenticado.token}` };
    expect((await request.post(`${API}/api/catalogo/empresa/${segunda}/assinatura`, { headers: devHeaders })).ok()).toBeTruthy();
    if (i < 3) expect((await request.post(`${API}/api/catalogo/empresa/${primeira}/assinatura`, { headers: devHeaders })).ok()).toBeTruthy();
  }
  await page.goto('/login');
  await page.getByLabel('Login').fill(login);
  await page.getByLabel('Senha').fill(NOVA_SENHA);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForURL(/\/dashboard$/);
  await page.getByRole('link', { name: 'Catálogo da empresa', exact: true }).click();
  const botao = page.getByRole('button', { name: 'Ver assinantes de revisar-alteracoes-demo' });
  await botao.focus();
  await page.keyboard.press('Enter');
  const painel = page.getByRole('region', { name: 'revisar-alteracoes-demo' });
  await expect(painel.getByText('Pessoa Demonstração 3')).toBeVisible();
  await expect(painel.getByText('3 usuários com assinatura registrada nesta empresa')).toBeVisible();
  await expect(painel.getByRole('heading')).toBeFocused();
  await expect(painel.locator('time').first()).toContainText('UTC');
  await page.screenshot({ path: 'test-results/capturas/assinantes-com-dados.png', fullPage: true });

  // A captura do design abre outro navegador. Esta página temporária só autentica na base descartável.
  const projetoDeDesign = process.env.DEVKIT_E2E_DESIGN_PROJECT;
  if (projetoDeDesign) {
    const auxiliar = 'dist/captura-753.html';
    writeFileSync(auxiliar, `<style>html,body,iframe{margin:0;width:100%;height:100%;border:0;display:block}</style><script>sessionStorage.setItem('devkitpage.sessao', ${JSON.stringify(JSON.stringify(sessao))});</script><iframe title="Catálogo da empresa" src="/empresa/catalogo" onload="const doc=this.contentDocument;const abrir=()=>{const b=doc.querySelector('[aria-label=&quot;Ver assinantes de revisar-alteracoes-demo&quot;]');if(b){b.click();return true}return false};if(!abrir()){const o=new MutationObserver(()=>{if(abrir())o.disconnect()});o.observe(doc,{subtree:true,childList:true})}"></iframe>`);
    try {
      const args = ['design', '--projeto', projetoDeDesign, '--capturar', '--tela', 'empresa-catalogo', '--estado', 'implementada-753', '--url', new URL('/captura-753.html', page.url()).href, '--largura', '1440', '--altura', '1050', '--tema', 'dark', '--branch', 'feature/dev/projeto-invation-item-5', '--json'];
      // O iframe hospeda a aplicação real, sem reconstruir a tela para a captura.
      execFileSync('devcli', args, { encoding: 'utf8', timeout: 60_000, windowsHide: true });
      console.log(execFileSync('devcli', [...args, '--aplicar'], { encoding: 'utf8', timeout: 60_000, windowsHide: true }));
    } finally { unlinkSync(auxiliar); }
  }

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(painel.getByText('Pessoa Demonstração 3')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
  await page.screenshot({ path: 'test-results/capturas/assinantes-estreito.png', fullPage: true });
  await painel.getByRole('button', { name: 'Fechar' }).click();
  await expect(botao).toBeFocused();
  await page.getByRole('button', { name: 'Ver assinantes de skill-sem-assinantes' }).click();
  await expect(page.getByText('Nenhum assinante registrado para esta skill.')).toBeVisible();
  await page.screenshot({ path: 'test-results/capturas/assinantes-vazio.png', fullPage: true });
  await page.route(`**/api/catalogo/empresa/${vazia}/assinantes?**`, rota => rota.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ detail: 'Consulta indisponível.' }) }));
  await page.getByRole('button', { name: 'Fechar' }).click();
  await page.getByRole('button', { name: 'Ver assinantes de skill-sem-assinantes' }).click();
  await expect(page.getByRole('alert')).toContainText('Consulta indisponível.');
  await page.screenshot({ path: 'test-results/capturas/assinantes-erro.png', fullPage: true });
  await page.unroute(`**/api/catalogo/empresa/${vazia}/assinantes?**`);
  await page.getByRole('button', { name: 'Tentar novamente' }).click();
  await expect(page.getByText('Nenhum assinante registrado para esta skill.')).toBeVisible();
});
