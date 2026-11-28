import { expect, test } from '@playwright/test';
import { API } from './ambiente';
import type { Projeto } from '../src/api/projetos';

test('Ondas: navegação, foco, edição persistida e conflito real em 1440 e 390', async ({ page, request }) => {
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: width === 390 ? 980 : 1100 });
    const consultas: string[] = [];
    const observar = (r: { url(): string }) => { const url = new URL(r.url()); if (url.pathname.startsWith('/api/projetos')) consultas.push(url.pathname); };
    page.on('request', observar);
    await page.goto('/__demo?estado=plano');
    await expect(page.getByRole('heading', { name: 'Definição do projeto' })).toBeVisible();
    expect(consultas.filter(r => r === '/api/projetos')).toHaveLength(0);
    expect(consultas.filter(r => r === '/api/projetos/resumos')).toHaveLength(1);
    expect(consultas.filter(r => /^\/api\/projetos\/\d+$/.test(r))).toHaveLength(1);
    await expect(page.getByRole('button', { name: 'Disparar projeto', exact: true })).toBeDisabled();
    await expect(page.getByText(/Projeto sem matriz definida/)).toBeVisible();
    await page.getByRole('button', { name: 'Ondas', exact: true }).click();
    await expect(page.getByRole('heading', { name: '4 · Desktop: Ondas e modal' })).toBeVisible();
    // (boards-estados, item 5) Aprovado e ainda não disparado: nenhuma onda está em andamento, então nenhuma é a atual (o
    // destaque volta com o disparo, conferido no fim deste laço).
    await expect(page.getByRole('region', { name: 'Onda 1', exact: true })).not.toHaveAttribute('aria-current');
    await expect(page.getByRole('region', { name: 'Onda 2', exact: true })).not.toHaveAttribute('aria-current');
    expect(await page.getByRole('region', { name: 'Onda 1', exact: true }).evaluate(e => getComputedStyle(e).borderColor))
      .toBe(await page.getByRole('region', { name: 'Onda 2', exact: true }).evaluate(e => getComputedStyle(e).borderColor));
    const card = page.getByRole('button', { name: 'Editar item 4', exact: true });
    await card.click();
    let modal = page.getByRole('dialog', { name: 'Editar item 4' });
    await expect(modal.getByLabel('Título', { exact: true })).toBeFocused();
    for (const nome of ['Fluxo', 'Agente e modelo', 'Prioridade']) {
      await expect(modal.getByRole('combobox', { name: nome, exact: true })).toBeVisible();
      expect(await modal.getByRole('combobox', { name: nome, exact: true }).evaluate(e => getComputedStyle(e).backgroundImage)).not.toBe('none');
    }
    if (width === 390) {
      expect((await modal.boundingBox())!.width).toBe(370);
      expect((await modal.locator('form').boundingBox())!.width).toBe(332);
    }
    await page.keyboard.press('Shift+Tab');
    await expect(modal.getByRole('button', { name: 'Fechar editor' })).toBeFocused();
    await page.keyboard.press('Shift+Tab');
    await expect(modal.getByRole('button', { name: 'Salvar item' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(modal).toHaveCount(0);
    await expect(card).toBeFocused();
    await card.click();
    modal = page.getByRole('dialog', { name: 'Editar item 4' });
    await modal.getByRole('combobox', { name: 'Fluxo', exact: true }).selectOption('13');
    await expect(modal.getByRole('combobox', { name: 'Fluxo', exact: true })).toHaveValue('13');
    await modal.getByRole('combobox', { name: 'Fluxo', exact: true }).selectOption('14');
    await modal.getByRole('combobox', { name: 'Agente e modelo', exact: true }).selectOption('\n');
    await expect(modal.getByRole('combobox', { name: 'Agente e modelo', exact: true })).toHaveValue('\n');
    await modal.getByRole('combobox', { name: 'Agente e modelo', exact: true }).selectOption('codex\ngpt-6.1-sol');
    await modal.getByRole('combobox', { name: 'Prioridade', exact: true }).selectOption('2');
    await modal.getByLabel('Título', { exact: true }).fill(`Edição persistida ${width}`);
    const sessao = await page.evaluate(() => JSON.parse(sessionStorage.getItem('devkitpage.sessao')!).token as string);
    const headers = { Authorization: `Bearer ${sessao}` };
    const resumos = await (await request.get(`${API}/api/projetos/resumos`, { headers })).json() as { id: number; slug: string }[];
    const id = resumos.find(p => p.slug === 'fixes')!.id;
    const antes = await (await request.get(`${API}/api/projetos/${id}`, { headers })).json() as Projeto;
    const salvamento = page.waitForResponse(r => r.url().endsWith(`/api/projetos/${id}/itens/4`) && r.request().method() === 'PUT');
    await modal.getByRole('button', { name: 'Salvar item' }).click();
    expect((await salvamento).status()).toBe(200);
    await expect(page.getByRole('dialog')).toHaveCount(0);
    const depois = await (await request.get(`${API}/api/projetos/${id}`, { headers })).json() as Projeto;
    expect(depois.versao).toBe(antes.versao + 1);
    expect(depois.situacao).toBe('aguardando');
    expect(depois.itens.find(i => i.numero === 4)!.titulo).toBe(`Edição persistida ${width}`);
    expect(depois.itens.find(i => i.numero === 4)!.prioridade).toBe(2);
    expect(depois.itens.find(i => i.numero === 5)).toEqual(antes.itens.find(i => i.numero === 5));
    await page.reload();
    await expect(page.getByRole('heading', { name: `4 · Edição persistida ${width}` })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await page.getByRole('button', { name: 'Editar item 4', exact: true }).click();
    modal = page.getByRole('dialog');
    await modal.getByLabel('Título', { exact: true }).fill(`Rascunho conservado ${width}`);
    const outro = depois.itens.find(i => i.numero === 5)!;
    const concorrente = await request.put(`${API}/api/projetos/${id}/itens/5`, { headers, data: {
      versaoEsperada: depois.versao, titulo: `Outro cliente ${width}`, descricao: outro.descricao, fluxo: outro.fluxo,
      agente: outro.agente, modelo: outro.modelo, dependencias: outro.dependencias, componentes: outro.componentes, prioridade: outro.prioridade,
    } });
    expect(concorrente.status()).toBe(200);
    await modal.getByRole('button', { name: 'Salvar item' }).click();
    await expect(modal.getByRole('alert')).toContainText('O projeto mudou');
    await expect(modal.getByLabel('Título', { exact: true })).toHaveValue(`Rascunho conservado ${width}`);
    await modal.getByRole('button', { name: 'Reler versão atual' }).click();
    await expect(modal.getByRole('button', { name: 'Aplicar rascunho à versão relida' })).toBeVisible();
    await modal.getByRole('button', { name: 'Aplicar rascunho à versão relida' }).click();
    await modal.getByRole('button', { name: 'Salvar item' }).click();
    await expect(page.getByRole('heading', { name: `4 · Rascunho conservado ${width}` })).toBeVisible();
    const final = await (await request.get(`${API}/api/projetos/${id}`, { headers })).json() as Projeto;
    expect(final.itens.find(i => i.numero === 5)!.titulo).toBe(`Outro cliente ${width}`);
    await page.goto('/projetos?slug=fixes&nivel=usuario&extra=teste&visao=plano');
    await page.getByRole('button', { name: 'Ondas', exact: true }).click();
    await expect(page).toHaveURL(/slug=fixes&nivel=usuario&extra=teste&visao=ondas/);
    await page.reload();
    await expect(page.getByRole('button', { name: 'Ondas', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await page.getByRole('button', { name: 'Plano', exact: true }).click();
    await page.goBack();
    await expect(page.getByRole('button', { name: 'Ondas', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await page.goForward();
    await expect(page.getByRole('button', { name: 'Plano', exact: true })).toHaveAttribute('aria-pressed', 'true');
    // Disparado (a marca gravada e devolvida pela API real): em execução, e a onda atual volta a ser destacada, com a borda própria.
    await page.goto('/__demo?estado=disparado');
    const atual = page.getByRole('region', { name: 'Onda 1', exact: true });
    await expect(atual).toHaveAttribute('aria-current', 'step');
    await expect(page.getByText('Em execução', { exact: true })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Onda 2', exact: true })).not.toHaveAttribute('aria-current');
    expect(await atual.evaluate(e => getComputedStyle(e).borderColor))
      .not.toBe(await page.getByRole('region', { name: 'Onda 2', exact: true }).evaluate(e => getComputedStyle(e).borderColor));
    page.off('request', observar);
  }
});

test('Ondas: vazios, troca de detalhe em espera, erro e recuperação conservam contexto em 1440 e 390', async ({ page }) => {
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: width === 390 ? 980 : 1100 });
    await page.goto('/__demo?estado=sem-itens');
    await expect(page.getByText('Ainda sem itens no plano.', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Ondas', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await page.goto('/__demo?estado=sem-resultados');
    await expect(page.getByText('Nenhum item corresponde aos filtros.', { exact: true })).toBeVisible();
    await expect(page.getByLabel('Buscar item')).toHaveValue('Busca sem correspondência');
    await page.goto('/__demo?estado=detalhe-carregando');
    await expect(page.getByLabel('Projeto ativo').locator('option:checked')).toHaveText('runners');
    await expect(page.getByText('Carregando detalhe do projeto…', { exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: '4 · Desktop: Ondas e modal' })).toHaveCount(0);
    await page.getByRole('button', { name: /Atualizar/ }).click();
    await expect(page.getByRole('heading', { name: '4 · runners: Desktop: Ondas e modal' })).toBeVisible();
    await expect(page).toHaveURL(/visao=ondas&nivel=usuario&extra=contexto/);
    await page.goto('/__demo?estado=detalhe-erro');
    await expect(page.getByRole('alert')).toContainText('Detalhe temporariamente indisponível');
    await expect(page.getByRole('heading', { name: '4 · Desktop: Ondas e modal' })).toHaveCount(0);
    await page.getByRole('button', { name: 'Reler projeto', exact: true }).click();
    await expect(page.getByRole('heading', { name: '4 · runners: Desktop: Ondas e modal' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Ondas', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await expect(page).toHaveURL(/visao=ondas&nivel=usuario&extra=contexto/);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
  }
});

test('Entrada móvel abre um ou vários projetos sem id e inclui item com título repetido pela API', async ({ page, request }) => {
  await page.setViewportSize({ width: 390, height: 980 });
  for (const estado of ['entrada-unica', 'entrada-varios']) {
    await page.goto('/__demo?estado=' + estado);
    const abrir = page.getByRole('link', { name: /^Abrir projeto selecionado / });
    await expect(abrir).toBeVisible();
    const opcoes = await page.getByLabel('Projeto ativo').locator('option').count();
    if (estado === 'entrada-unica') expect(opcoes).toBe(1); else expect(opcoes).toBeGreaterThan(1);
    await expect(page).not.toHaveURL(/projeto=\d|slug=/);
    await abrir.click();
    await expect(page.getByRole('heading', { name: 'Definição do projeto' })).toBeVisible();
    await expect(page).toHaveURL(/projeto=\d/);
    await expect(page).toHaveURL(/nivel=usuario&extra=contexto/);
    await page.reload();
    await expect(page.getByRole('heading', { name: 'Definição do projeto' })).toBeVisible();
  }
  await page.goto('/__demo?estado=ondas');
  await page.getByRole('button', { name: '+ Item', exact: true }).click();
  const modal = page.getByRole('dialog', { name: 'Novo item' });
  await modal.getByLabel('Título', { exact: true }).fill('Desktop: Ondas e modal');
  await modal.getByLabel('Descrição', { exact: true }).fill('Novo trabalho, título repetido');
  await modal.getByRole('combobox', { name: 'Fluxo', exact: true }).selectOption('14');
  await modal.getByRole('combobox', { name: 'Agente e modelo', exact: true }).selectOption('codex\ngpt-6.1-sol');
  await modal.getByLabel('Componentes', { exact: true }).fill('web/novo-item');
  const salvamento = page.waitForResponse(r => /\/itens\/0$/.test(r.url()) && r.request().method() === 'PUT');
  await modal.getByRole('button', { name: 'Salvar item', exact: true }).click();
  const resposta = await salvamento;
  expect(resposta.status()).toBe(200);
  const salvo = await resposta.json() as Projeto;
  const novo = salvo.itens.find(i => i.descricao === 'Novo trabalho, título repetido')!;
  expect(novo.workItemId).toBe(0); expect(novo.branch).toBe(''); expect(novo.design ?? null).toBeNull();
  const token = await page.evaluate(() => JSON.parse(sessionStorage.getItem('devkitpage.sessao')!).token as string);
  const relido = await (await request.get(`${API}/api/projetos/${salvo.id}`, { headers: { Authorization: `Bearer ${token}` } })).json() as Projeto;
  expect(relido.itens.find(i => i.numero === novo.numero)).toEqual(novo);
  await expect(page.getByRole('heading', { name: `${novo.numero} · Desktop: Ondas e modal`, exact: true })).toBeVisible();
  await expect(page.getByText(/Item salvo\. O plano aguarda aprovação/)).toBeVisible();
});
