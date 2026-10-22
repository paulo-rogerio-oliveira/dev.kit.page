import { existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import {
  AMOSTRA_DO_BOARD, BENEFICIOS, CABECALHOS, CATALOGO, COMPARATIVO, CONCEITOS, EMPRESAS, FABRICA, HERO, INTEGRACOES, MENU, PASSOS, PERGUNTAS, PLANOS,
  PRODUTOS_COMPARADOS, RECURSOS, RODAPE, ROTULO_DA_DISPONIBILIDADE, rotuloDaSecao, SECOES, SEGURANCA, tituloDaJanela,
} from './landing';
import { MIDIAS } from '../../midia/roteiro.mjs';

// Relativo a este arquivo (src/conteudo), e não à pasta de onde o Vitest foi chamado.
const PUBLICO = join(__dirname, '..', '..', 'public');
const arquivo = (url: string) => join(PUBLICO, ...url.split('/').filter(Boolean));

describe('conteúdo da landing', () => {
  it('as seções seguem a proposta da home (US #467): recursos logo depois do como funciona, e o por que depois deles', () => {
    expect(SECOES.map((s) => s.id)).toEqual([
      'inicio', 'como-funciona', 'recursos', 'beneficios', 'conceitos', 'fabrica', 'catalogo', 'comparativo', 'integracoes', 'seguranca', 'empresas',
      'planos', 'contato', 'faq', 'comecar',
    ]);
    // O menu e o rodapé só apontam para seções que existem.
    for (const id of [...MENU, ...RODAPE]) expect(SECOES.map((s) => s.id)).toContain(id);
    expect(MENU.map(rotuloDaSecao)).toEqual(['Como funciona', 'Recursos', 'Conceitos', 'Catálogo', 'Integrações', 'Segurança', 'Planos', 'Perguntas']);
    expect(RODAPE.map(rotuloDaSecao)).toEqual(['Conceitos', 'Fábrica agêntica', 'Segurança', 'Comparativo', 'Empresas', 'Perguntas']);
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
    const textos = JSON.stringify([
      HERO, AMOSTRA_DO_BOARD, CABECALHOS, BENEFICIOS, PASSOS, RECURSOS, CONCEITOS, FABRICA, CATALOGO, COMPARATIVO, INTEGRACOES, SEGURANCA, EMPRESAS, PLANOS, PERGUNTAS,
    ]);

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

  it('os recursos da proposta (US #467) com os da versão 5 (Orquestrador, Codex e Design), numerados 01–07, com aba, marcadores, moldura e legenda', () => {
    expect(RECURSOS.map((r) => r.id)).toEqual(['orquestrador', 'agente', 'codex', 'design', 'fluxos', 'replicacao', 'board']);
    expect(RECURSOS.map((r) => r.numero)).toEqual(['01', '02', '03', '04', '05', '06', '07']);
    expect(RECURSOS.map((r) => r.aba)).toEqual(['Orquestrador', 'O agente sobre a task', 'Codex como agente', 'Design', 'Fluxos com avaliadores', 'Cherry-pick', 'Horas']);
    expect(RECURSOS.filter((r) => r.novo).map((r) => r.id)).toEqual(['orquestrador', 'codex', 'design']);
    expect(RECURSOS.map((r) => tituloDaJanela(r.janela))).toEqual([
      'dev.kit - Orquestrador', 'dev.kit - Agente', 'dev.kit - Tarefa avulsa', 'dev.kit - Orquestrador · Design', 'dev.kit - Atividades', 'dev.kit - Cherry-pick', 'dev.kit - Board',
    ]);
    expect(AMOSTRA_DO_BOARD.titulo).toBe('dev.kit - Board');
    expect(AMOSTRA_DO_BOARD.orquestrador.autor).toBe('Orquestrador');
    for (const recurso of RECURSOS) {
      expect(recurso.marcadores.length).toBeGreaterThanOrEqual(2);
      expect(recurso.legenda.trim().length).toBeGreaterThan(10);
      // Cada recurso se mexe: o vídeo gravado pelo roteiro ou o GIF pronto do produto (o do Orquestrador).
      expect(Boolean(recurso.midia.video) || /\.gif$/.test(recurso.midia.captura ?? '')).toBe(true);
      expect(recurso.midia.poster).toMatch(/\.jpg$/);
      expect(recurso.midia.alt.trim().length).toBeGreaterThan(20);
    }
  });

  it('o Orquestrador usa o GIF das Novidades da versão 5, e o Codex e o Design, vídeos novos com a captura do app v5', () => {
    const porId = (id: string) => RECURSOS.find((r) => r.id === id)!.midia;
    expect(porId('orquestrador')).toMatchObject({ captura: '/midia/orquestrador.gif', poster: '/midia/orquestrador.jpg' });
    expect(porId('orquestrador').video).toBeUndefined();
    expect(porId('codex')).toMatchObject({ video: { webm: '/midia/codex.webm', mp4: '/midia/codex.mp4' }, captura: '/capturas/tela-codex.png' });
    expect(porId('design')).toMatchObject({ video: { webm: '/midia/design.webm', mp4: '/midia/design.mp4' }, captura: '/capturas/tela-design.png' });
  });

  it('o como funciona não tem mais o vídeo do hero (a proposta não o tem)', () => {
    expect(HERO).not.toHaveProperty('midia');
  });

  it('toda mídia referenciada existe em public (vídeos, posters e capturas)', () => {
    for (const { midia } of RECURSOS) {
      const urls = [midia.poster, ...(midia.video ? [midia.video.webm, midia.video.mp4] : []), ...(midia.captura ? [midia.captura] : [])];
      for (const url of urls) expect(existsSync(arquivo(url)), url).toBe(true);
    }
  });

  it('o roteiro grava só os vídeos que a home usa, e public/midia não guarda sobra', () => {
    const nome = (r: (typeof RECURSOS)[number]) => r.midia.poster.replace('/midia/', '').replace('.jpg', '');
    expect(MIDIAS.map((m) => m.nome)).toEqual(RECURSOS.filter((r) => r.midia.video).map(nome));
    const arquivos = readdirSync(join(PUBLICO, 'midia')).map((f) => f.replace(/\.(mp4|webm|jpg|gif)$/, ''));
    expect([...new Set(arquivos)].sort()).toEqual(RECURSOS.map(nome).sort());
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
