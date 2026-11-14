import { createHash } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { expect, test } from '@playwright/test';
import type { DocumentoMatrix } from '../src/api/matrix';
import { prepararMatrix } from './matrix-cenario';
import { API } from './ambiente';

test('Matrix: avaliação vinculada, obsolescência seletiva e transferência no desktop e celular', async ({ page, request }) => {
  const c = await prepararMatrix(request);
  const evento = 'gatilho:123', shas = { 'demo/aplicacao': 'a'.repeat(40) };
  const revisaoDaEntrega = JSON.stringify({ Conferencia: { Ok: true }, Repositorios: [{ Name: 'demo/aplicacao', Delivery: { Head: shas['demo/aplicacao'] } }] });
  const recibo: DocumentoMatrix = { contrato: 1, aplicacao: 'demo', tipo: 'evento',
    id: 'e-' + createHash('sha256').update(c.task + '\n' + evento + '\nmatrix').digest('hex').toUpperCase().slice(0, 48), projeto: 'matrix',
    evento: { evento, matriz: 'matrix', maquina: c.maquina, sessaoAvaliada: c.task, avaliador: 7,
      revisao: 4, revisaoEfetiva: 4, revisaoDoContexto: 4, responsaveis: ['item-4'],
      celulas: c.matriz.matriz!.celulas.filter(x => x.responsaveis.some(r => r.id === 'item-4')).map(x => x.id), shas, revisaoDaEntrega } };
  await c.gravar(recibo, 0);
  const registros = await request.get(`${API}/api/matrix/demo/registro/matrix`, { headers: c.headers });
  expect(registros.ok()).toBeTruthy();
  const versaoRegistro = (await registros.json()).length;
  await c.gravar({ contrato: 1, aplicacao: 'demo', tipo: 'registro', id: 'matrix', projeto: 'matrix', registro: {
    celula: 'imp-008', revisaoDaDefinicao: 4, responsavel: 'item-4', tipo: 'avaliacao', resultado: 'atendido',
    justificativa: 'Pedido e recorte do evento conferidos; cenário de demonstração fictício.', evidencias: ['log-cenario-matrix.txt'],
    shas, evento, avaliador: 7, sessaoAvaliada: c.task, revisaoEfetiva: 4, revisaoDoContexto: 4, revisaoDaEntrega,
  } }, versaoRegistro);
  await page.addInitScript(s => sessionStorage.setItem('devkitpage.sessao', JSON.stringify(s)), c.sessao);
  const capturas = fileURLToPath(new URL('../../../apoio-capturas-matrix-page-correcao/', import.meta.url));
  mkdirSync(capturas, { recursive: true });
  for (const estado of ['vigente', 'obsoleta', 'transferida']) {
    const obsoleta = estado === 'obsoleta';
    if (obsoleta) {
      c.matriz.matriz!.celulas.find(x => x.id === 'imp-008')!.criterio = 'Novo critério após a leitura do avaliador';
      await c.gravar(c.matriz, 4);
    }
    if (estado === 'transferida') {
      c.matriz.matriz!.celulas.find(x => x.id === 'imp-008')!.responsaveis = [{ id: 'item-3', item: 3 }];
      await c.gravar(c.matriz, 5);
      const novoEvento = 'gatilho:124';
      await c.gravar({ ...recibo,
        id: 'e-' + createHash('sha256').update(c.task + '\n' + novoEvento + '\nmatrix').digest('hex').toUpperCase().slice(0, 48),
        evento: { ...recibo.evento!, evento: novoEvento, revisao: 6, revisaoEfetiva: 6, revisaoDoContexto: 6,
          responsaveis: ['item-3'], celulas: c.matriz.matriz!.celulas.filter(x => x.responsaveis.some(r => r.id === 'item-3')).map(x => x.id) },
      }, 0);
      await c.gravar({ contrato: 1, aplicacao: 'demo', tipo: 'registro', id: 'matrix', projeto: 'matrix', registro: {
        celula: 'imp-008', revisaoDaDefinicao: 6, responsavel: 'item-3', tipo: 'avaliacao', resultado: 'atendido',
        justificativa: 'Novo dono conferiu a obrigação transferida; histórico do responsável anterior preservado.',
        evidencias: ['log-transferencia-matrix.txt'], shas, evento: novoEvento, avaliador: 7, sessaoAvaliada: c.task,
        revisaoEfetiva: 6, revisaoDoContexto: 6, revisaoDaEntrega,
      } }, versaoRegistro + 1);
    }
    for (const colorScheme of ['light', 'dark'] as const) for (const width of [1440, 390]) {
      await page.emulateMedia({ colorScheme });
      await page.setViewportSize({ width, height: width === 390 ? 980 : 1100 });
      await page.goto('/projetos?slug=matrix&visao=impactos');
      if (width === 390) await page.getByRole('combobox', { name: 'Componente', exact: true }).selectOption('componente-1');
      const botao = page.getByRole('button', { name: 'Avaliação × Page · imp-008' }).filter({ visible: true });
      await expect(botao).toContainText(obsoleta ? 'A avaliar' : 'Comprovado');
      await botao.click();
      if (width === 390) await page.getByRole('button', { name: 'Abrir editor completo →' }).click();
      const dialogo = page.getByRole('dialog').last();
      await dialogo.getByRole('button', { name: 'Avaliação', exact: true }).click();
      await expect(dialogo.getByText(/Evento gatilho:123 · avaliador 7/)).toBeVisible();
      await expect(dialogo.getByText(obsoleta ? /Atendido · Histórico · Obrigação ou atribuição.*revisão 5/ : 'Atendido · Vigente')).toBeVisible();
      if (estado === 'transferida') {
        await expect(dialogo.getByText(/Evento gatilho:124 · avaliador 7/)).toBeVisible();
        await expect(dialogo.getByText(/Atendido · Histórico · Obrigação ou atribuição.*revisão 5/)).toBeVisible();
      }
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
      await page.screenshot({ path: resolve(capturas, `page-avaliacao-${estado}-${width}-${colorScheme}.png`), fullPage: true });
    }
  }
  console.log(`Capturas reais da avaliação Matrix: ${capturas}`);
});
