import { existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import {
  AMOSTRA_DO_BOARD, BENEFICIOS, CABECALHOS, CATALOGO, COMPARATIVO, EMPRESAS, HERO, INTEGRACOES, MENU, PASSOS, PERGUNTAS, PLANOS, PRODUTOS_COMPARADOS,
  RECURSOS, RODAPE, ROTULO_DA_DISPONIBILIDADE, rotuloDaSecao, SECOES, SEGURANCA, tituloDaJanela,
} from './landing';
import { MIDIAS } from '../../midia/roteiro.mjs';

// Relativo a este arquivo (src/conteudo), e não à pasta de onde o Vitest foi chamado.
const PUBLICO = join(__dirname, '..', '..', 'public');
const arquivo = (url: string) => join(PUBLICO, ...url.split('/').filter(Boolean));

describe('conteúdo da landing', () => {
  it('as seções seguem a proposta da home (US #467): recursos logo depois do como funciona, e o por que depois deles', () => {
    expect(SECOES.map((s) => s.id)).toEqual([
      'inicio', 'como-funciona', 'recursos', 'beneficios', 'catalogo', 'comparativo', 'integracoes', 'seguranca', 'empresas', 'planos', 'contato', 'faq', 'comecar',
    ]);
    // O menu e o rodapé só apontam para seções que existem.
    for (const id of [...MENU, ...RODAPE]) expect(SECOES.map((s) => s.id)).toContain(id);
    expect(MENU.map(rotuloDaSecao)).toEqual(['Como funciona', 'Recursos', 'Catálogo', 'Integrações', 'Segurança', 'Planos', 'Perguntas']);
  });

  it('os planos são o teste grátis, o individual e o de empresas, sem preço inventado na página (US #425)', () => {
    expect(PLANOS.map((p) => p.id)).toEqual(['teste', 'individual', 'empresas']);
    expect(PLANOS[0].resumo).toContain('14 dias');
    expect(JSON.stringify(PLANOS)).not.toMatch(/R\$/);
    expect(CATALOGO.itens).toHaveLength(3);
  });

  it('o hero destaca "Pull Request" no título e a amostra do Board traz a nota aceita', () => {
    expect(HERO.titulo.endsWith(HERO.destaque)).toBe(true);
    expect(HERO.selo).toBe('Para times no Azure DevOps');
    expect(AMOSTRA_DO_BOARD.nota).toEqual({ valor: 'Nota 92/100', texto: 'Aceito: acima da nota mínima' });
  });

  it('nenhum texto da página explica o dashboard nem fala em métricas (US #381)', () => {
    const textos = JSON.stringify([HERO, AMOSTRA_DO_BOARD, CABECALHOS, BENEFICIOS, PASSOS, RECURSOS, CATALOGO, COMPARATIVO, INTEGRACOES, SEGURANCA, EMPRESAS, PLANOS, PERGUNTAS]);

    expect(textos).not.toMatch(/dashboard/i);
    expect(textos).not.toMatch(/m[ée]tricas?/i);
    expect(BENEFICIOS.map((b) => b.titulo)).not.toContain('Uso e qualidade medidos');
  });

  it('o fluxo é contado nas sete etapas da proposta, e o recurso Fluxos explica avaliadores, gatilhos e impasse', () => {
    expect(PASSOS.map((p) => p.titulo)).toEqual(['Clona', 'Implementa', 'Compila', 'Testa', 'Avalia', 'Commit e push', 'Pull Request']);
    const fluxos = RECURSOS.find((r) => r.id === 'fluxos')!;
    const texto = [fluxos.texto, ...fluxos.marcadores].join(' ');
    for (const termo of ['ao enviar', 'ao revisar', 'ao cumprir o objetivo', 'ao aprovar', 'teto de rodadas', 'impasse', 'nota mínima', 'Exporte e importe'])
      expect(texto).toContain(termo);
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

  it('os quatro recursos da proposta (US #467), numerados 01–04, com aba, marcadores, moldura e legenda', () => {
    expect(RECURSOS.map((r) => r.id)).toEqual(['agente', 'fluxos', 'replicacao', 'board']);
    expect(RECURSOS.map((r) => r.numero)).toEqual(['01', '02', '03', '04']);
    expect(RECURSOS.map((r) => r.aba)).toEqual(['O agente sobre a task', 'Fluxos com avaliadores', 'Cherry-pick', 'Horas']);
    expect(RECURSOS.map((r) => tituloDaJanela(r.janela))).toEqual(['dev.kit - Agente', 'dev.kit - Atividades', 'dev.kit - Cherry-pick', 'dev.kit - Board']);
    expect(AMOSTRA_DO_BOARD.titulo).toBe('dev.kit - Board');
    for (const recurso of RECURSOS) {
      expect(recurso.marcadores.length).toBeGreaterThanOrEqual(2);
      expect(recurso.legenda.trim().length).toBeGreaterThan(10);
      expect(recurso.midia.video).toBeDefined();
      expect(recurso.midia.poster).toMatch(/\.jpg$/);
      expect(recurso.midia.alt.trim().length).toBeGreaterThan(20);
    }
  });

  it('o como funciona não tem mais o vídeo do hero (a proposta não o tem)', () => {
    expect(HERO).not.toHaveProperty('midia');
  });

  it('toda mídia referenciada existe em public (vídeos, posters e capturas)', () => {
    for (const { midia } of RECURSOS) {
      const urls = [midia.poster, midia.video!.webm, midia.video!.mp4, ...(midia.captura ? [midia.captura] : [])];
      for (const url of urls) expect(existsSync(arquivo(url)), url).toBe(true);
    }
  });

  it('o roteiro grava só as mídias que a home usa, e public/midia não guarda sobra', () => {
    const usadas = RECURSOS.map((r) => r.midia.poster.replace('/midia/', '').replace('.jpg', ''));
    expect(MIDIAS.map((m) => m.nome)).toEqual(usadas);
    const arquivos = readdirSync(join(PUBLICO, 'midia')).map((f) => f.replace(/\.(mp4|webm|jpg)$/, ''));
    expect([...new Set(arquivos)].sort()).toEqual([...usadas].sort());
  });

  it('as mídias regravadas não têm legenda por cima do vídeo: a legenda é a da página, abaixo da moldura', () => {
    for (const midia of MIDIAS)
      for (const slide of midia.slides) {
        expect(slide.legenda).toBeUndefined();
        for (const passo of slide.passos ?? []) expect(passo.legenda).toBeUndefined();
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
