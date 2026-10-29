import { expect, test } from '@playwright/test';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { garantirSenhaDefinitiva } from './admin';
import { API, NOVA_SENHA } from './ambiente';

test('importa pasta pela interface, publica, recupera a release e apresenta falha do GitHub', async ({ page, request }) => {
  const fixtures = join(process.cwd(), '../api/tests/DevKitPage.Core.Tests/Fixtures/catalogo-v2');
  const manifesto = JSON.parse(await readFile(join(fixtures, 'manifesto.json'), 'utf8'));
  const skill = manifesto.itens.find((i: { tipo: string }) => i.tipo === 'skill');
  const pasta = await mkdtemp(join(tmpdir(), 'catalogo-us738-'));
  const slug = `local-${Date.now()}`;
  try {
    await mkdir(join(pasta, 'skills'));
    await writeFile(join(pasta, 'manifesto.json'), JSON.stringify({ ...manifesto, itens: [skill], retirados: [] }));
    await writeFile(join(pasta, skill.arquivo), await readFile(join(fixtures, 'base', skill.arquivo)));
    await garantirSenhaDefinitiva(request);
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/login');
    await page.getByLabel('Login').fill('admin');
    await page.getByLabel('Senha').fill(NOVA_SENHA);
    await page.getByRole('button', { name: 'Entrar' }).click();
    await page.waitForURL(/\/dashboard$/);
    await page.goto('/catalogo?aba=catalogos');
    const formulario = page.getByRole('form', { name: 'Novo catálogo' });
    await formulario.getByRole('button', { name: 'Pasta local' }).click();
    await formulario.locator('input[type=file]').setInputFiles(pasta);
    await formulario.getByLabel('Slug').fill(slug);
    await formulario.getByLabel('Nome', { exact: true }).fill('Catálogo local de demonstração');
    await formulario.getByRole('button', { name: 'Importar pasta local' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Importado da pasta local' })).toContainText('1 itens');
    await expect(page.getByRole('row').filter({ hasText: slug })).toContainText('Pasta local');
    await page.screenshot({ path: 'test-results/capturas/738-local-importado.png', fullPage: true });
    await page.getByRole('button', { name: 'Publicar Catálogo local de demonstração' }).click();
    const publicacao = page.getByRole('region', { name: 'Publicar Catálogo local de demonstração' });
    await expect(publicacao.getByRole('button', { name: 'Confirmar publicação' })).toBeEnabled();
    await publicacao.getByRole('button', { name: 'Confirmar publicação' }).click();
    await expect(page.getByRole('article', { name: `Release atual ${slug}-v1` })).toBeVisible();
    await page.screenshot({ path: 'test-results/capturas/738-release-publicada.png', fullPage: true });
    await page.goto('/catalogo?aba=catalogos');
    await expect(page.getByRole('button', { name: 'Publicar Catálogo local de demonstração' })).toBeDisabled();
    await formulario.getByRole('button', { name: 'GitHub' }).click();
    await formulario.getByLabel('Repositório').fill('http://localhost/interno');
    await formulario.getByLabel('Slug').fill('github-invalido');
    await formulario.getByLabel('Nome', { exact: true }).fill('Exemplo GitHub');
    const resposta = page.waitForResponse((r) => r.url() === `${API}/api/catalogos/importar` && r.status() === 400);
    await formulario.getByRole('button', { name: 'Importar do GitHub' }).click();
    await resposta;
    await expect(formulario.getByRole('alert')).toContainText('repositório público');
    await page.screenshot({ path: 'test-results/capturas/738-github-validacao.png', fullPage: true });
  } finally {
    await rm(pasta, { recursive: true, force: true });
  }
});
