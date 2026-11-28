import { existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import {
  AMOSTRA_DO_PROJETO, BENEFICIOS, BOARD_WEB, CABECALHOS, CATALOGO, COMPARATIVO, CONCEITOS, CONTEXTO, EMPRESAS, FABRICA, HERO, INTEGRACOES, MENU, PASSOS,
  PERGUNTAS, PLANOS, PLATAFORMA, PRODUTOS_COMPARADOS, RECURSOS, RODAPE, ROTULO_DA_DISPONIBILIDADE, rotuloDaSecao, SECOES, SEGURANCA, tituloDaJanela,
} from './landing';
import { MIDIAS } from '../../midia/roteiro.mjs';

// Relativo a este arquivo (src/conteudo), e não à pasta de onde o Vitest foi chamado.
const PUBLICO = join(__dirname, '..', '..', 'public');
const arquivo = (url: string) => join(PUBLICO, ...url.split('/').filter(Boolean));

/** Todo o conteúdo da página, para as recusas: o que a landing não pode dizer. */
const CONTEUDO = [
  HERO, AMOSTRA_DO_PROJETO, PLATAFORMA, BOARD_WEB, CABECALHOS, BENEFICIOS, PASSOS, RECURSOS, CONCEITOS, FABRICA, CATALOGO, CONTEXTO, COMPARATIVO, INTEGRACOES,
  SEGURANCA, EMPRESAS, PLANOS, PERGUNTAS,
];

/** Os textos soltos (as folhas string) de um conteúdo, para conferir frase a frase. */
function textos(valor: unknown): string[] {
  if (typeof valor === 'string') return [valor];
  if (Array.isArray(valor)) return valor.flatMap(textos);
  if (valor && typeof valor === 'object') return Object.values(valor).flatMap(textos);
  return [];
}

describe('conteúdo da landing', () => {
  it('as seções seguem a proposta ajustes-2-landing v1: o hero, os recursos do dev.kit.page e o Board web antes do como funciona', () => {
    expect(SECOES.map((s) => s.id)).toEqual([
      'inicio', 'plataforma', 'board-web', 'como-funciona', 'recursos', 'beneficios', 'conceitos', 'fabrica', 'catalogo', 'comparativo', 'integracoes',
      'seguranca', 'empresas', 'planos', 'contato', 'faq', 'comecar',
    ]);
    // O menu e o rodapé só apontam para seções que existem.
    for (const id of [...MENU, ...RODAPE]) expect(SECOES.map((s) => s.id)).toContain(id);
    // O topo da proposta: Como funciona, Recursos, Catálogo e Planos (o Entrar e o Pedir demonstração ficam ao lado).
    expect(MENU.map(rotuloDaSecao)).toEqual(['Como funciona', 'Recursos', 'Catálogo', 'Planos']);
    expect(RODAPE.map(rotuloDaSecao)).toEqual(['Plataforma', 'Board web', 'Conceitos', 'Fábrica agêntica', 'Comparativo', 'Integrações', 'Segurança', 'Empresas', 'Perguntas']);
  });

  it('nenhuma seção fica sem caminho: o que saiu do menu do topo está no rodapé, sem repetir, na ordem da página', () => {
    const comLink = new Set<string>([...MENU, ...RODAPE]);
    // Só ficam fora: o início (a marca leva a ele), o por que (logo depois dos recursos), o contato (os CTAs de demonstração) e o CTA final.
    expect(SECOES.map((s) => s.id).filter((id) => !comLink.has(id))).toEqual(['inicio', 'beneficios', 'contato', 'comecar']);
    expect(comLink.size).toBe(MENU.length + RODAPE.length);
    const posicao = (id: string) => SECOES.findIndex((s) => s.id === id);
    expect([...RODAPE].sort((a, b) => posicao(a) - posicao(b))).toEqual([...RODAPE]);
  });

  it('o hero é o da proposta: o rótulo, "agentes de código." em destaque, as quatro peças no texto e o download para Windows', () => {
    expect(HERO.rotulo).toBe('Seu trabalho, do plano à entrega');
    expect(HERO.titulo).toBe('Planeje, acompanhe e entregue com agentes de código.');
    expect(HERO.titulo.endsWith(HERO.destaque)).toBe(true);
    expect(HERO.destaque).toBe('agentes de código.');
    for (const peca of ['desktop', 'serviço', 'devcli', 'Board web']) expect(HERO.texto).toContain(peca);
    expect(HERO.texto).toContain('Você acompanha projetos, Design e entregas em um só lugar.');
    expect(HERO.baixar).toBe('Baixar o dev.kit para Windows');
  });

  it('a amostra do hero é o cartão de projetos da proposta, marcado como demonstração', () => {
    expect(AMOSTRA_DO_PROJETO).toMatchObject({
      janela: 'dev.kit · projetos', etiqueta: 'Demonstração', titulo: 'Do pedido à entrega', texto: 'Planejamento em ondas, propostas visuais e impacto por item.',
    });
    expect(AMOSTRA_DO_PROJETO.etapas.map((e) => [e.texto, e.papel])).toEqual([
      ['Planejar e conferir propostas', 'Planejamento'],
      ['Implementar no branch do item', 'Agente'],
      ['Comparar proposta e recaptura', 'Avaliação'],
    ]);
    expect(AMOSTRA_DO_PROJETO.nota).toBe('Você mantém as decisões de aprovação e publicação.');
  });

  it('os recursos do dev.kit.page: os três cartões da proposta, cada link com um destino que existe, e as quatro peças', () => {
    expect(PLATAFORMA.rotulo).toBe('Recursos do dev.kit.page');
    expect(PLATAFORMA.titulo).toBe('O projeto continua com você, onde estiver.');
    expect(PLATAFORMA.cartoes.map((c) => [c.titulo, c.link.rotulo, c.link.destino])).toEqual([
      ['Projetos e Board web', 'Conhecer os recursos', '#board-web'],
      ['Design e avaliação', 'Como funciona o Design', '#recurso-design'],
      ['Catálogo e contexto', 'Explorar o catálogo', '/catalogo'],
    ]);
    const ancoras = [...SECOES.map((s) => s.id), ...RECURSOS.map((r) => `recurso-${r.id}`)];
    for (const { link } of PLATAFORMA.cartoes) if (link.destino.startsWith('#')) expect(ancoras, link.destino).toContain(link.destino.slice(1));
    expect(PLATAFORMA.pecas.itens.map((p) => [p.id, p.nome])).toEqual([
      ['desktop', 'App desktop'], ['servico', 'Serviço do Windows'], ['devcli', 'devcli'], ['board-web', 'Board web'],
    ]);
    for (const p of PLATAFORMA.pecas.itens) expect(p.onde.length > 0 && p.texto.length > 60, p.nome).toBe(true);
  });

  it('o Board web diz o que cada recurso exige: login, a fila liberada pelo administrador ou o navegador', () => {
    expect(BOARD_WEB.recursos.map((r) => [r.titulo, r.requisito, r.rota])).toEqual([
      ['O retrato de cada máquina', 'login', '/board'],
      ['Decisões com as evidências', 'login', '/board'],
      ['Projetos em ondas', 'login', '/projetos'],
      ['Design compartilhado', 'login', '/design'],
      ['Conversa remota', 'fila', '/board'],
      ['Ligar e reiniciar o dev.kit', 'fila', '/board'],
      ['Avisos no dispositivo', 'navegador', '/board'],
    ]);
    expect(BOARD_WEB.requisitos).toEqual({ login: 'Com login', fila: 'Com a fila liberada', navegador: 'Depende do navegador' });
    const condicoes = BOARD_WEB.condicoes.itens.join(' ');
    expect(condicoes).toContain('mesma conta do dev.kit');
    expect(condicoes).toContain('criada quando o administrador do dev.kit libera a provisão para a sua conta');
    expect(condicoes).toContain('Os avisos dependem do navegador');
    // (boards-estados r3) Aprovar e disparar viram pedido pela fila, com os portões repetidos; a frase antiga não volta.
    expect(condicoes).toContain('Aprovar o plano e disparar um projeto viram pedido à máquina, pela fila do controle remoto: o dev.kit repete os portões antes de executar.');
    expect(condicoes).not.toContain('continuam no dev.kit da máquina');
    // A aprovação remota só com o snapshot: o retrato sem ele deixa a aprovação no dev.kit.
    expect(BOARD_WEB.recursos[1].texto).toContain('Sem o snapshot, a aprovação fica no dev.kit.');
    expect(BOARD_WEB.ctas).toEqual({
      entrar: { rotulo: 'Entrar no Board web', destino: '/board' },
      planos: { rotulo: 'Ver os planos', destino: '#planos' },
    });
  });

  it('nenhum texto oferece a conversa ou os comandos remotos sem dizer que dependem da fila liberada', () => {
    const remoto = /conversa remota|comandos remotos|\blig(?:a|ar)\b[^.]*\bdev\.kit/i;
    // Fora dos cartões do Board web, a frase que promete o remoto cita a fila na mesma frase.
    const frases = textos([...CONTEUDO.filter((c) => c !== BOARD_WEB), { ...BOARD_WEB, recursos: [] }]).flatMap((t) => t.split(/(?<=\.)\s/));
    const citam = frases.filter((f) => remoto.test(f));
    expect(citam.length).toBeGreaterThanOrEqual(4);
    for (const frase of citam) expect(frase, frase).toMatch(/\bfila\b/);
    // Nos cartões, o recurso remoto leva a etiqueta da fila — e só ele.
    for (const r of BOARD_WEB.recursos) expect(r.requisito === 'fila', r.titulo).toBe(remoto.test(`${r.titulo}. ${r.texto}`));
  });

  it('o contexto diz para quem ele existe: a conta de uma empresa ou da assinatura individual, no site', () => {
    expect(CONTEXTO.texto).toContain('Com a conta de uma empresa ou da assinatura individual');
    expect(CONTEXTO.itens.map((i) => i.titulo)).toEqual(['Três níveis, o mais específico vale', 'Sugestões dos agentes', 'Repositórios e convenções']);
    expect(CONTEXTO.itens[0].texto).toContain('num tema em conflito vale o seu');
  });

  it('os planos são o teste grátis, o individual e o de empresas, sem preço inventado na página (US #425)', () => {
    expect(PLANOS.map((p) => p.id)).toEqual(['teste', 'individual', 'empresas']);
    expect(PLANOS[0].resumo).toContain('14 dias');
    expect(JSON.stringify(PLANOS)).not.toMatch(/R\$/);
    expect(CATALOGO.itens).toHaveLength(3);
  });

  it('nenhum texto da página explica o dashboard nem fala em métricas (US #381)', () => {
    const tudo = JSON.stringify(CONTEUDO);

    expect(tudo).not.toMatch(/dashboard/i);
    expect(tudo).not.toMatch(/m[ée]tricas?/i);
    expect(BENEFICIOS.map((b) => b.titulo)).not.toContain('Uso e qualidade medidos');
  });

  it('o código-fonte fica na máquina, e o que sai é o que a pessoa pede: o push, o artefato ou as capturas do Design', () => {
    const codigo = SEGURANCA.find((s) => s.titulo === 'Seu código fica com você')!.texto;
    expect(codigo).toContain('só sai pelo push do branch, quando a regra do fluxo permite');
    expect(codigo).not.toMatch(/nada sai/i);
    expect(PERGUNTAS.find((p) => p.pergunta === 'Onde o código roda?')!.resposta).toContain('um artefato gerado pelo Board ou as capturas do Design');
    expect(SEGURANCA.map((s) => s.titulo)).toEqual(expect.arrayContaining(['O Board web recebe só o retrato', 'Decisões auditadas']));
  });

  it('o fluxo é contado nas sete etapas da proposta, e o recurso Fluxos explica avaliadores, gatilhos e impasse', () => {
    expect(PASSOS.map((p) => p.titulo)).toEqual(['Clona', 'Implementa', 'Compila', 'Testa', 'Avalia', 'Commit e push', 'Pull Request']);
    const fluxos = RECURSOS.find((r) => r.id === 'fluxos')!;
    const texto = [fluxos.texto, ...fluxos.marcadores].join(' ');
    for (const termo of ['ao enviar', 'ao revisar', 'ao cumprir o objetivo', 'ao aprovar', 'teto de rodadas', 'impasse', 'nota mínima', 'Exporte e importe'])
      expect(texto).toContain(termo);
  });

  it('o Design conta a avaliação: as propostas fixadas no plano e a recaptura comparada com a proposta', () => {
    const design = RECURSOS.find((r) => r.id === 'design')!;
    expect(design.titulo).toBe('Design: da captura real à proposta e à avaliação');
    expect(design.texto).toContain('as propostas ficam fixadas no planejamento de cada item');
    expect(design.marcadores).toContain('Depois da implementação, a recaptura é comparada com a proposta na avaliação');
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

  it('os recursos da proposta (US #467) com os da versão 5 (Orquestrador, provedor cognitivo e Design), numerados 01–07, com aba, marcadores, moldura e legenda', () => {
    expect(RECURSOS.map((r) => r.id)).toEqual(['orquestrador', 'agente', 'provedor', 'design', 'fluxos', 'replicacao', 'board']);
    expect(RECURSOS.map((r) => r.numero)).toEqual(['01', '02', '03', '04', '05', '06', '07']);
    expect(RECURSOS.map((r) => r.aba)).toEqual(['Orquestrador', 'O agente sobre a task', 'Provedor cognitivo', 'Design', 'Fluxos com avaliadores', 'Cherry-pick', 'Horas']);
    expect(RECURSOS.filter((r) => r.novo).map((r) => r.id)).toEqual(['orquestrador', 'provedor', 'design']);
    expect(RECURSOS.map((r) => tituloDaJanela(r.janela))).toEqual([
      'dev.kit - Orquestrador', 'dev.kit - Agente', 'dev.kit - Tarefa avulsa', 'dev.kit - Orquestrador · Design', 'dev.kit - Atividades', 'dev.kit - Cherry-pick', 'dev.kit - Board',
    ]);
    for (const recurso of RECURSOS) {
      expect(recurso.marcadores.length).toBeGreaterThanOrEqual(2);
      expect(recurso.legenda.trim().length).toBeGreaterThan(10);
      // Cada recurso se mexe: o vídeo gravado pelo roteiro ou o GIF pronto do produto (o do Orquestrador).
      expect(Boolean(recurso.midia.video) || /\.gif$/.test(recurso.midia.captura ?? '')).toBe(true);
      expect(recurso.midia.poster).toMatch(/\.jpg$/);
      expect(recurso.midia.alt.trim().length).toBeGreaterThan(20);
    }
  });

  it('o Orquestrador usa o GIF das Novidades da versão 5, e o provedor cognitivo e o Design, vídeos novos com a captura do app v5', () => {
    const porId = (id: string) => RECURSOS.find((r) => r.id === id)!.midia;
    expect(porId('orquestrador')).toMatchObject({ captura: '/midia/orquestrador.gif', poster: '/midia/orquestrador.jpg' });
    expect(porId('orquestrador').video).toBeUndefined();
    expect(porId('provedor')).toMatchObject({ video: { webm: '/midia/codex.webm', mp4: '/midia/codex.mp4' }, captura: '/capturas/tela-codex.png' });
    expect(porId('design')).toMatchObject({ video: { webm: '/midia/design.webm', mp4: '/midia/design.mp4' }, captura: '/capturas/tela-design.png' });
  });

  it('os conceitos usam os nomes do produto: Rules no lugar de Tech Plans, e a escolha do provedor cognitivo sem destacar um só', () => {
    const tudo = JSON.stringify(CONTEUDO);
    expect(tudo).not.toMatch(/tech ?plans?/i);
    expect(tudo).not.toMatch(/Codex como agente/);
    expect(CATALOGO.titulo).toBe('Catálogo de skills, Rules e fluxos');
    // Os provedores citados são só os que o produto suporta, e cada lugar que os lista traz todos.
    const provedores = ['Claude Code', 'Codex', 'Kiro', 'Kimi', 'GLM'];
    const listas = [
      RECURSOS.find((r) => r.id === 'provedor')!.texto,
      RECURSOS.find((r) => r.id === 'agente')!.marcadores.join(' '),
      INTEGRACOES.find((i) => i.nome === 'Provedores cognitivos')!.texto,
      PERGUNTAS.find((p) => p.pergunta === 'Quais provedores de IA ele usa?')!.resposta,
    ];
    for (const lista of listas) for (const provedor of provedores) expect(lista, lista).toContain(provedor);
  });

  it('o hero não tem vídeo: a amostra é o cartão de projetos em HTML', () => {
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
    for (const lista of [BENEFICIOS, PASSOS, SEGURANCA, CONTEXTO.itens, PLATAFORMA.cartoes]) {
      expect(lista.length).toBeGreaterThanOrEqual(3);
      for (const item of lista) expect(item.titulo && item.texto).toBeTruthy();
    }
    expect(PASSOS).toHaveLength(7);
    expect(INTEGRACOES.map((i) => i.nome)).toEqual(expect.arrayContaining(['Azure DevOps', 'GitHub']));
    for (const p of PERGUNTAS) expect(p.pergunta.endsWith('?') && p.resposta.length > 0).toBe(true);
  });
});
