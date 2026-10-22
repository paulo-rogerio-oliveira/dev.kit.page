// O ROTEIRO das mídias da landing (US #283) — o mesmo de docs/landing-conteudo.md. Cada mídia é uma
// sequência de quadros ("slides"): uma TELA do dev.kit (as capturas de public/capturas, geradas pelo
// modo --screenshots do git.kit com dados de DEMONSTRAÇÃO) com os passos destacados. As coordenadas dos
// destaques são em pixels da captura.
//
// Desde a US #467 (a proposta da home) são só as QUATRO mídias dos recursos, e SEM legenda sobreposta: a
// legenda de cada uma é a do recurso em src/conteudo/landing.ts, que a página mostra abaixo da moldura de
// janela. O vídeo fica só com a tela do app, o destaque e o cursor — o que a proposta chama de "captura
// atual do app em tamanho maior, sem a legenda por cima".

/** As capturas e a área útil de cada uma (sem a margem branca que o gerador deixa). */
export const TELAS = {
  agente: { arquivo: 'tela-agente.png', largura: 884, altura: 660 },
  atividades: { arquivo: 'tela-atividades.png', largura: 1024, altura: 680 },
  cherryPick: { arquivo: 'tela-cherry-pick.png', largura: 955, altura: 680 },
  // A face do Board com o "Precisa de você" e o card de Horas (a tela-board-precisa-de-voce do gerador de capturas).
  board: { arquivo: 'tela-board.png', largura: 1024, altura: 720 },
  // A versão 5 (as capturas escuras do projeto de design do git.kit @ feature/dev/semana4, dados de demonstração):
  // a pré-execução com o seletor do agente (executar-com-agente v001) e o painel Design do Orquestrador
  // (orquestrador-design v001). O Orquestrador em si usa o GIF das Novidades, pronto.
  codex: { arquivo: 'tela-codex.png', largura: 1902, altura: 1038 },
  design: { arquivo: 'tela-design.png', largura: 1262, altura: 1058 },
};

const tela = (nome, duracao, passos) => ({ tipo: 'tela', tela: TELAS[nome], duracao, passos });

/**
 * As mídias: nome do arquivo (public/midia/NOME.mp4|webm|jpg) e os slides. O poster é o quadro do
 * instante zero — por isso todo slide inicial já começa com o primeiro destaque visível. Cada passo
 * diz, em comentário, o que a câmera mostra.
 */
export const MIDIAS = [
  {
    nome: 'agente',
    slides: [tela('agente', 12, [
      { em: 0, destaque: [508, 138, 250, 42], zoom: 1.5 }, // o pedido de comando: permitir sempre ou executar uma vez
      { em: 4, destaque: [45, 410, 512, 44], zoom: 1.5 }, // a suíte verde
      { em: 8, destaque: [24, 252, 730, 62], zoom: 1.4 }, // o commit com Ab#1234 para aprovar
    ])],
  },
  {
    nome: 'codex',
    slides: [tela('codex', 12, [
      { em: 0, destaque: [105, 785, 650, 55], zoom: 1.7 }, // o modelo da task: o agente que roda (Claude Code ou Codex)
      { em: 4, destaque: [1466, 188, 388, 24], zoom: 2 }, // o modelo no resumo da pré-execução
      { em: 8, destaque: [1467, 321, 386, 40], zoom: 2 }, // executar com o agente escolhido
    ])],
  },
  {
    nome: 'design',
    slides: [tela('design', 12, [
      { em: 0, destaque: [12, 236, 294, 145], zoom: 1.3 }, // as telas do projeto: captura real e proposta
      { em: 4, destaque: [336, 296, 296, 110], zoom: 1.6 }, // a galeria: a captura e a proposta lado a lado
      { em: 8, destaque: [235, 598, 760, 56], zoom: 1.35 }, // o agente gravou as propostas pedidas no chat
    ])],
  },
  {
    nome: 'fluxos',
    slides: [tela('atividades', 12, [
      { em: 0, destaque: [78, 63, 682, 30], zoom: 1 }, // as tasks do fluxo, cada uma no seu estado
      { em: 4, destaque: [72, 114, 936, 72], zoom: 1.35 }, // o turno em andamento
      { em: 8, destaque: [72, 198, 936, 178], zoom: 1.35 }, // aguardando você: aprovar e seguir para o push
    ])],
  },
  {
    nome: 'replicacao',
    slides: [tela('cherryPick', 12, [
      { em: 0, destaque: [12, 136, 935, 94], zoom: 1.25 }, // os branches de origem e destino
      { em: 4, destaque: [20, 375, 925, 70], zoom: 1.45 }, // os commits marcados
      { em: 8, destaque: [757, 638, 198, 34], zoom: 1.6 }, // replicar numa cópia temporária
    ])],
  },
  {
    nome: 'board',
    slides: [tela('board', 12, [
      { em: 0, destaque: [606, 493, 400, 141], zoom: 1.5 }, // as horas de hoje, lançadas
      { em: 4, destaque: [719, 509, 238, 30], zoom: 1.8 }, // hoje, ontem, semanal e mensal
      { em: 8, destaque: [70, 70, 520, 258], zoom: 1.25 }, // os work items trabalhados
    ])],
  },
];
