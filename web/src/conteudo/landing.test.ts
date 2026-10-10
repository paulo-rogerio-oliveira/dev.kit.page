import { existsSync } from 'node:fs';
import { join } from 'node:path';
import {
  AMOSTRA_DO_BOARD, BENEFICIOS, CABECALHOS, COMPARATIVO, EMPRESAS, HERO, INTEGRACOES, MENU, PASSOS, PERGUNTAS, PRODUTOS_COMPARADOS, RECURSOS, RODAPE,
  ROTULO_DA_DISPONIBILIDADE, rotuloDaSecao, SECOES, SEGURANCA,
} from './landing';

// Relativo a este arquivo (src/conteudo), e não à pasta de onde o Vitest foi chamado.
const PUBLICO = join(__dirname, '..', '..', 'public');
const arquivo = (url: string) => join(PUBLICO, ...url.split('/').filter(Boolean));

describe('conteúdo da landing', () => {
  it('as seções seguem a proposta da US #405 e mantêm as da US #283 e da US #381', () => {
    expect(SECOES.map((s) => s.id)).toEqual([
      'inicio', 'como-funciona', 'beneficios', 'recursos', 'comparativo', 'integracoes', 'seguranca', 'empresas', 'contato', 'faq', 'comecar',
    ]);
    // O menu e o rodapé só apontam para seções que existem.
    for (const id of [...MENU, ...RODAPE]) expect(SECOES.map((s) => s.id)).toContain(id);
    expect(MENU.map(rotuloDaSecao)).toEqual(['Como funciona', 'Recursos', 'Integrações', 'Segurança', 'Perguntas']);
  });

  it('o hero destaca "Pull Request" no título e a amostra do Board traz a nota aceita', () => {
    expect(HERO.titulo.endsWith(HERO.destaque)).toBe(true);
    expect(HERO.selo).toBe('Para times no Azure DevOps');
    expect(AMOSTRA_DO_BOARD.nota).toEqual({ valor: 'Nota 92/100', texto: 'Aceito: acima da nota mínima' });
  });

  it('nenhum texto da página explica o dashboard nem fala em métricas (US #381)', () => {
    const textos = JSON.stringify([HERO, AMOSTRA_DO_BOARD, CABECALHOS, BENEFICIOS, PASSOS, RECURSOS, COMPARATIVO, INTEGRACOES, SEGURANCA, EMPRESAS, PERGUNTAS]);

    expect(textos).not.toMatch(/dashboard/i);
    expect(textos).not.toMatch(/m[ée]tricas?/i);
    expect(BENEFICIOS.map((b) => b.titulo)).not.toContain('Uso e qualidade medidos');
  });

  it('o fluxo é contado nas sete etapas da proposta, e o recurso Fluxos explica avaliadores, gatilhos e impasse', () => {
    expect(PASSOS.map((p) => p.titulo)).toEqual(['Clona', 'Implementa', 'Compila', 'Testa', 'Avalia', 'Commit e push', 'Pull Request']);
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
