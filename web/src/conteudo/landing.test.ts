import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { BENEFICIOS, HERO, INTEGRACOES, PASSOS, PERGUNTAS, RECURSOS, SECOES, SEGURANCA } from './landing';

// Relativo a este arquivo (src/conteudo), e não à pasta de onde o Vitest foi chamado.
const PUBLICO = join(__dirname, '..', '..', 'public');
const arquivo = (url: string) => join(PUBLICO, ...url.split('/').filter(Boolean));

describe('conteúdo da landing', () => {
  it('as seções seguem a ordem do critério 1 da US #283', () => {
    expect(SECOES.map((s) => s.id)).toEqual([
      'inicio', 'beneficios', 'como-funciona', 'recursos', 'integracoes', 'seguranca', 'contato', 'faq', 'comecar',
    ]);
  });

  it('os sete recursos principais, cada um com vídeo, poster e texto alternativo', () => {
    expect(RECURSOS.map((r) => r.id)).toEqual(['agente', 'fluxos', 'replicacao', 'agendamento', 'depurador', 'devcli', 'board']);
    for (const { midia } of [...RECURSOS, HERO]) {
      expect(midia.video).toBeDefined();
      expect(midia.poster).toMatch(/\.jpg$/);
      expect(midia.alt.trim().length).toBeGreaterThan(20);
    }
  });

  it('toda mídia referenciada existe em public (vídeos, posters e capturas)', () => {
    for (const { midia } of [...RECURSOS, HERO]) {
      const urls = [midia.poster, midia.video!.webm, midia.video!.mp4, ...(midia.captura ? [midia.captura] : [])];
      for (const url of urls) expect(existsSync(arquivo(url)), url).toBe(true);
    }
  });

  it('os blocos de texto não estão vazios', () => {
    for (const lista of [BENEFICIOS, PASSOS, SEGURANCA]) {
      expect(lista.length).toBeGreaterThanOrEqual(3);
      for (const item of lista) expect(item.titulo && item.texto).toBeTruthy();
    }
    expect(PASSOS).toHaveLength(4);
    expect(INTEGRACOES.map((i) => i.nome)).toEqual(expect.arrayContaining(['Azure DevOps', 'GitHub']));
    for (const p of PERGUNTAS) expect(p.pergunta.endsWith('?') && p.resposta.length > 0).toBe(true);
  });
});
