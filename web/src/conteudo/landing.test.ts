import { existsSync } from 'node:fs';
import { join } from 'node:path';
import {
  BENEFICIOS, COMPARATIVO, EMPRESAS, HERO, INTEGRACOES, PASSOS, PERGUNTAS, PRODUTOS_COMPARADOS, RECURSOS, ROTULO_DA_DISPONIBILIDADE, SECOES, SEGURANCA,
} from './landing';

// Relativo a este arquivo (src/conteudo), e não à pasta de onde o Vitest foi chamado.
const PUBLICO = join(__dirname, '..', '..', 'public');
const arquivo = (url: string) => join(PUBLICO, ...url.split('/').filter(Boolean));

describe('conteúdo da landing', () => {
  it('as seções seguem a ordem da US #283, com o comparativo e as empresas da US #381', () => {
    expect(SECOES.map((s) => s.id)).toEqual([
      'inicio', 'beneficios', 'como-funciona', 'recursos', 'comparativo', 'integracoes', 'seguranca', 'empresas', 'contato', 'faq', 'comecar',
    ]);
  });

  it('nenhum texto da página explica o dashboard nem fala em métricas (US #381)', () => {
    const textos = JSON.stringify([HERO, BENEFICIOS, PASSOS, RECURSOS, COMPARATIVO, INTEGRACOES, SEGURANCA, EMPRESAS, PERGUNTAS]);

    expect(textos).not.toMatch(/dashboard/i);
    expect(textos).not.toMatch(/m[ée]tricas?/i);
    expect(BENEFICIOS.map((b) => b.titulo)).not.toContain('Uso e qualidade medidos');
  });

  it('o fluxo é contado em etapas nomeadas, com avaliadores, gatilhos, objetivo, aprovação e fechamento', () => {
    expect(PASSOS.map((p) => p.titulo)).toEqual([
      'Work item', 'Executor', 'Avaliadores com nota mínima', 'Gatilhos entre agentes', 'Objetivo cumprido', 'Sua aprovação', 'Fechamento com a PR',
    ]);
    const fluxos = RECURSOS.find((r) => r.id === 'fluxos')!.texto;
    for (const termo of ['ao enviar', 'ao revisar', 'ao cumprir o objetivo', 'ao aprovar', 'teto de rodadas', 'impasse', 'parâmetros', 'Exporte e importe'])
      expect(fluxos).toContain(termo);
  });

  it('toda linha do comparativo tem valor para cada produto, e o dev.kit é a primeira coluna', () => {
    expect(PRODUTOS_COMPARADOS[0]).toEqual({ id: 'devkit', nome: 'dev.kit' });
    expect(COMPARATIVO.length).toBeGreaterThanOrEqual(8);
    for (const linha of COMPARATIVO) {
      for (const produto of PRODUTOS_COMPARADOS) expect(ROTULO_DA_DISPONIBILIDADE[linha.valores[produto.id]], `${linha.recurso} × ${produto.nome}`).toBeTruthy();
      expect(linha.valores.devkit).toBe('sim');
    }
  });

  it('a promessa de privacidade cita o trace sem caminhos e o consentimento da empresa', () => {
    const telemetria = SEGURANCA.find((s) => s.titulo.startsWith('Telemetria'))!.texto;

    expect(telemetria).toContain('sem caminhos, código, prompts nem nomes de cliente');
    expect(telemetria).toContain('trace de um erro, que sai sem caminhos, e-mails nem URLs');
    expect(telemetria).toContain('só com o seu consentimento');
    expect(EMPRESAS.itens.map((i) => i.titulo)).toEqual(expect.arrayContaining(['Consentimento do colaborador', 'Exportação auditada']));
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
    expect(PASSOS).toHaveLength(7);
    expect(INTEGRACOES.map((i) => i.nome)).toEqual(expect.arrayContaining(['Azure DevOps', 'GitHub']));
    for (const p of PERGUNTAS) expect(p.pergunta.endsWith('?') && p.resposta.length > 0).toBe(true);
  });
});
