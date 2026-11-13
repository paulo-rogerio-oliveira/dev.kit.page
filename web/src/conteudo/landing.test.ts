import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import {
  AMOSTRA_DO_BOARD, AMOSTRA_DO_CELULAR, BENEFICIOS, BRACOS, CABECALHOS, CATALOGO, COMPARATIVO, CONCEITOS, DEMONSTRACAO_DOS_BRACOS, EMPRESAS,
  EVIDENCIAS, FABRICA, HERO, INTEGRACOES, MENU, PASSOS, PERGUNTAS, PLACAR, PLANOS, PRODUTOS_COMPARADOS, RECURSOS, RODAPE,
  ROTULO_DA_DISPONIBILIDADE, rotuloDaSecao, SECOES, SEGURANCA, tituloDaJanela,
} from './landing';
import { MIDIAS } from '../../midia/roteiro.mjs';

// Relativo a este arquivo (src/conteudo), e não à pasta de onde o Vitest foi chamado.
const PUBLICO = join(__dirname, '..', '..', 'public');
const arquivo = (url: string) => join(PUBLICO, ...url.split('/').filter(Boolean));

/** Todo o texto que a página mostra — o das regras de linguagem (dashboard, métricas, Tech Plan). */
const textosDaPagina = () => JSON.stringify([
  HERO, BRACOS, DEMONSTRACAO_DOS_BRACOS, AMOSTRA_DO_BOARD, AMOSTRA_DO_CELULAR, CABECALHOS, BENEFICIOS, PLACAR, PASSOS, RECURSOS, CONCEITOS, FABRICA,
  CATALOGO, COMPARATIVO, INTEGRACOES, SEGURANCA, EMPRESAS, PLANOS, PERGUNTAS,
]);

describe('conteúdo da landing', () => {
  it('as seções da landing refeita: o hero, como funciona, os braços, o tour e o por quê, antes das que o menu e o rodapé apontam', () => {
    expect(SECOES.map((s) => s.id)).toEqual([
      'inicio', 'como-funciona', 'bracos', 'recursos', 'beneficios', 'conceitos', 'fabrica', 'catalogo', 'comparativo', 'integracoes', 'seguranca',
      'empresas', 'planos', 'contato', 'faq', 'comecar',
    ]);
    // O menu e o rodapé só apontam para seções que existem.
    for (const id of [...MENU, ...RODAPE]) expect(SECOES.map((s) => s.id)).toContain(id);
    expect(MENU.map(rotuloDaSecao)).toEqual(['Como funciona', 'Produto', 'Recursos', 'Catálogo', 'Segurança', 'Planos', 'Perguntas']);
    expect(RODAPE.map(rotuloDaSecao)).toEqual(['Produto', 'Conceitos', 'Fábrica agêntica', 'Integrações', 'Segurança', 'Comparativo', 'Empresas', 'Perguntas']);
  });

  it('a primeira dobra diz para quem é, o que faz e como: "Pull Request" em destaque, as Rules, a nota, o Orquestrador e o celular', () => {
    expect(HERO.titulo).toBe('Do work item à Pull Request, com um time de agentes de IA');
    expect(HERO.titulo).toContain(HERO.destaque);
    expect(HERO.destaque).toBe('Pull Request');
    expect(HERO.selo).toBe('Para times no Azure DevOps');
    for (const termo of ['Rules', 'nota de 0 a 100', 'Orquestrador', 'no desktop ou no celular']) expect(HERO.texto).toContain(termo);
  });

  it('os quatro braços do produto (desktop, serviço, Board e devcli), com o resumo curto da dobra e o detalhe da seção Produto', () => {
    expect(BRACOS.map((b) => b.id)).toEqual(['desktop', 'servico', 'board', 'devcli']);
    expect(BRACOS.map((b) => b.nome)).toEqual(['Desktop', 'Serviço', 'Board', 'devcli']);
    expect(BRACOS.filter((b) => b.novo).map((b) => b.id)).toEqual(['board']);
    for (const braco of BRACOS) {
      // O resumo é a linha da primeira dobra: curto, para os quatro caberem na tela do notebook.
      expect(braco.resumo.length, braco.resumo).toBeLessThanOrEqual(80);
      expect(braco.titulo.trim().length).toBeGreaterThan(10);
      expect(braco.marcadores.length).toBeGreaterThanOrEqual(3);
    }
    const porId = (id: string) => BRACOS.find((b) => b.id === id)!;
    const tudo = (id: string) => [porId(id).resumo, porId(id).texto, ...porId(id).marcadores].join(' ');
    expect(tudo('desktop')).toContain('clones na sua máquina');
    expect(tudo('servico')).toContain('app fechado');
    expect(tudo('servico')).toContain('/devkit executar');
    expect(tudo('board')).toContain('segundo fator');
    expect(tudo('board')).toContain('o texto do chat nunca aprova');
    expect(tudo('devcli')).toContain('Mais de 50 comandos');
    expect(tudo('devcli')).toContain('MCP');
    expect(tudo('devcli')).toContain('só o --aplicar grava');
  });

  it('a vitrine é coerente: a mínima separa as notas, o celular pede o segundo fator e a notificação é o texto fixo do push', () => {
    expect(AMOSTRA_DO_BOARD.titulo).toBe('dev.kit - Board');
    expect(AMOSTRA_DO_BOARD.orquestrador.autor).toBe('Orquestrador');
    expect(AMOSTRA_DO_BOARD.nota).toEqual({ valor: 'Nota 92/100', texto: 'Aceito: acima da nota mínima' });
    const { notas, minima } = AMOSTRA_DO_BOARD.avaliador;
    expect(notas.some((n) => n < minima) && notas.some((n) => n >= minima)).toBe(true);
    expect(AMOSTRA_DO_CELULAR.decisao.acao).toBe('Aprovar com segundo fator');
    expect(AMOSTRA_DO_CELULAR.aviso).toBe('Texto do chat não aprova');
    // A notificação da ilustração do Board é a que o service worker do Board mostra (texto fixo, sem dado nenhum).
    const trabalhador = readFileSync(join(PUBLICO, 'board-sw.js'), 'utf8');
    expect(trabalhador).toContain(DEMONSTRACAO_DOS_BRACOS.board.notificacao.titulo);
    expect(trabalhador).toContain(DEMONSTRACAO_DOS_BRACOS.board.notificacao.texto);
    expect(DEMONSTRACAO_DOS_BRACOS.desktop.abas).toContain(DEMONSTRACAO_DOS_BRACOS.desktop.ativa);
  });

  it('o tour tem as oito abas, numeradas 01–08, com o rótulo curto, os marcadores, a moldura e a legenda', () => {
    expect(RECURSOS.map((r) => r.id)).toEqual(['orquestrador', 'projetos', 'agente', 'fluxos', 'provedor', 'design', 'replicacao', 'board']);
    expect(RECURSOS.map((r) => r.numero)).toEqual(['01', '02', '03', '04', '05', '06', '07', '08']);
    expect(RECURSOS.map((r) => r.aba)).toEqual(['Orquestrador', 'Projetos', 'O agente', 'Fluxos', 'Provedores', 'Design', 'Cherry-pick', 'Horas']);
    expect(RECURSOS.filter((r) => r.novo).map((r) => r.id)).toEqual(['projetos']);
    expect(RECURSOS.map((r) => tituloDaJanela(r.janela))).toEqual([
      'dev.kit - Orquestrador', 'dev.kit - Projetos', 'dev.kit - Agente', 'dev.kit - Atividades', 'dev.kit - Tarefa avulsa', 'dev.kit - Orquestrador · Design',
      'dev.kit - Cherry-pick', 'dev.kit - Board',
    ]);
    for (const recurso of RECURSOS) {
      // As oito abas cabem numa linha da tela larga: o rótulo é curto, e o título do painel diz o resto.
      expect(recurso.aba.length, recurso.aba).toBeLessThanOrEqual(14);
      expect(recurso.marcadores.length).toBeGreaterThanOrEqual(2);
      expect(recurso.legenda.trim().length).toBeGreaterThan(10);
      expect(recurso.midia.poster).toMatch(/\.(jpg|png)$/);
      expect(recurso.midia.alt.trim().length).toBeGreaterThan(20);
    }
  });

  it('a mídia de cada recurso: o GIF do Orquestrador, a captura real do plano nos Projetos e os vídeos gravados nos demais', () => {
    const porId = (id: string) => RECURSOS.find((r) => r.id === id)!.midia;
    expect(porId('orquestrador')).toMatchObject({ captura: '/midia/orquestrador.gif', poster: '/midia/orquestrador.jpg' });
    expect(porId('orquestrador').video).toBeUndefined();
    expect(porId('projetos')).toMatchObject({ poster: '/capturas/tela-projetos.png', captura: '/capturas/tela-projetos.png', largura: 760, altura: 428 });
    expect(porId('projetos').video).toBeUndefined();
    expect(porId('provedor')).toMatchObject({ video: { webm: '/midia/codex.webm', mp4: '/midia/codex.mp4' }, captura: '/capturas/tela-codex.png' });
    expect(porId('design')).toMatchObject({ video: { webm: '/midia/design.webm', mp4: '/midia/design.mp4' }, captura: '/capturas/tela-design.png' });
    for (const id of ['agente', 'fluxos', 'provedor', 'design', 'replicacao', 'board']) expect(porId(id).video, id).toBeDefined();
  });

  it('nenhum texto da página explica o dashboard nem fala em métricas (US #381)', () => {
    const textos = textosDaPagina();
    expect(textos).not.toMatch(/dashboard/i);
    expect(textos).not.toMatch(/m[ée]tricas?/i);
  });

  it('o fluxo é contado nas sete etapas da proposta, e o recurso Fluxos explica avaliadores, gatilhos e impasse', () => {
    expect(PASSOS.map((p) => p.titulo)).toEqual(['Clona', 'Implementa', 'Compila', 'Testa', 'Avalia', 'Commit e push', 'Pull Request']);
    const fluxos = RECURSOS.find((r) => r.id === 'fluxos')!;
    const texto = [fluxos.texto, ...fluxos.marcadores].join(' ');
    for (const termo of ['ao enviar', 'ao revisar', 'ao cumprir o objetivo', 'ao aprovar', 'teto de rodadas', 'impasse', 'nota mínima', 'Exporte e importe'])
      expect(texto).toContain(termo);
  });

  it('o por quê em bento: sete cartões, os dois largos com a ilustração, e o placar tem nota acima e abaixo da mínima', () => {
    expect(BENEFICIOS).toHaveLength(7);
    expect(BENEFICIOS.filter((b) => b.largo).map((b) => b.extra)).toEqual(['placar', 'evidencias']);
    expect(BENEFICIOS.map((b) => b.titulo)).toEqual(expect.arrayContaining(['Qualidade com nota mínima', 'Da task à PR sem trocar de janela', 'Horas lançadas sozinhas']));
    expect(PLACAR.avaliadores.some((a) => a.nota < PLACAR.minima)).toBe(true);
    expect(PLACAR.avaliadores.some((a) => a.nota >= PLACAR.minima)).toBe(true);
    expect(EVIDENCIAS).toEqual(['pedido.md', 'resumo', 'logs', 'diffs', 'capturas']);
  });

  it('toda linha do comparativo tem valor para cada produto, e o dev.kit é a primeira coluna', () => {
    expect(PRODUTOS_COMPARADOS[0]).toEqual({ id: 'devkit', nome: 'dev.kit' });
    expect(COMPARATIVO.length).toBeGreaterThanOrEqual(8);
    for (const linha of COMPARATIVO) {
      for (const produto of PRODUTOS_COMPARADOS) expect(ROTULO_DA_DISPONIBILIDADE[linha.valores[produto.id]], `${linha.recurso} × ${produto.nome}`).toBeTruthy();
      expect(linha.valores.devkit).toBe('sim');
    }
  });

  it('a segurança traz o segundo fator, os segredos fora dos logs, os comandos destrutivos bloqueados e a promessa da telemetria', () => {
    const porTitulo = (titulo: string) => SEGURANCA.find((s) => s.titulo === titulo)!.texto;
    expect(porTitulo('Aprovação com segundo fator')).toContain('SHA que foi avaliado');
    expect(porTitulo('Segredos fora dos logs')).toContain('nunca aparece nos logs da entrega');
    expect(porTitulo('Gravar é explícito')).toContain('comandos destrutivos ficam sempre bloqueados');
    const telemetria = SEGURANCA.find((s) => s.titulo.startsWith('Telemetria'))!.texto;
    expect(telemetria).toContain('sem caminhos, código, prompts nem nomes de cliente');
    expect(telemetria).toContain('trace de um erro, que sai sem caminhos, e-mails nem URLs');
    expect(telemetria).toContain('só com o seu consentimento');
    expect(EMPRESAS.itens.map((i) => i.titulo)).toEqual(expect.arrayContaining(['Consentimento do colaborador', 'Exportação auditada']));
  });

  it('os planos são o teste grátis, o individual e o de empresas, sem preço inventado na página (US #425)', () => {
    expect(PLANOS.map((p) => p.id)).toEqual(['teste', 'individual', 'empresas']);
    expect(PLANOS[0].resumo).toContain('14 dias');
    expect(JSON.stringify(PLANOS)).not.toMatch(/R\$/);
  });

  it('o catálogo conta os fluxos prontos pela própria lista, que tem os doze básicos do git.kit', () => {
    expect(CATALOGO.titulo).toBe('Catálogo de skills, Rules e fluxos');
    expect(CATALOGO.itens).toHaveLength(3);
    expect(CATALOGO.fluxos).toHaveLength(12);
    expect(new Set(CATALOGO.fluxos).size).toBe(CATALOGO.fluxos.length);
    expect(CATALOGO.subtitulo).toContain(`${CATALOGO.fluxos.length} fluxos prontos`);
    expect(CATALOGO.fluxos).toEqual(expect.arrayContaining(['Fluxo simples', 'Tarefa avulsa', 'Planejar e executar', 'Tela', 'Design', 'Deploy no Azure', 'Fechamento do projeto']));
  });

  it('os nomes do produto: Rules no lugar de Tech Plans, e a escolha do provedor cognitivo sem destacar um só', () => {
    const textos = textosDaPagina();
    expect(textos).not.toMatch(/tech ?plans?/i);
    expect(textos).not.toMatch(/Codex como agente/);
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

  it('o FAQ responde sobre o celular, o app fechado e o devcli, além do teste grátis, da compra e do cancelamento', () => {
    const perguntas = PERGUNTAS.map((p) => p.pergunta);
    for (const pergunta of [
      'Consigo acompanhar e aprovar pelo celular?', 'O dev.kit precisa ficar aberto?', 'Os agentes podem operar o próprio dev.kit?',
      'Como funciona o teste grátis?', 'Como compro só para mim?', 'Como cancelo a assinatura?', 'O que é o catálogo do dev.kit?',
    ]) expect(perguntas).toContain(pergunta);
    expect(new Set(perguntas).size).toBe(perguntas.length);
  });

  it('o hero não tem vídeo: a vitrine é feita de HTML', () => {
    expect(HERO).not.toHaveProperty('midia');
  });

  it('toda mídia referenciada existe em public (vídeos, posters e capturas)', () => {
    for (const { midia } of RECURSOS) {
      const urls = [midia.poster, ...(midia.video ? [midia.video.webm, midia.video.mp4] : []), ...(midia.captura ? [midia.captura] : [])];
      for (const url of urls) expect(existsSync(arquivo(url)), url).toBe(true);
    }
  });

  it('o roteiro grava só os vídeos que a home usa, e public/midia não guarda sobra', () => {
    // A captura estática dos Projetos mora em public/capturas: só as mídias de public/midia entram nesta conta.
    const daPastaMidia = RECURSOS.filter((r) => r.midia.poster.startsWith('/midia/'));
    const nome = (r: (typeof RECURSOS)[number]) => r.midia.poster.replace('/midia/', '').replace('.jpg', '');
    // O conjunto, e não a ordem: a ordem do roteiro é a da gravação, e a do tour pode mudar sem regravar nada.
    expect(MIDIAS.map((m) => m.nome).sort()).toEqual(RECURSOS.filter((r) => r.midia.video).map(nome).sort());
    const arquivos = readdirSync(join(PUBLICO, 'midia')).map((f) => f.replace(/\.(mp4|webm|jpg|gif)$/, ''));
    expect([...new Set(arquivos)].sort()).toEqual(daPastaMidia.map(nome).sort());
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
    expect(CONCEITOS.itens).toHaveLength(3);
    expect(FABRICA.fabrica.estacoes).toHaveLength(6);
    expect(INTEGRACOES.map((i) => i.nome)).toEqual(expect.arrayContaining(['Azure DevOps', 'GitHub', 'MCP']));
    for (const p of PERGUNTAS) expect(p.pergunta.endsWith('?') && p.resposta.length > 0).toBe(true);
  });
});
