// O conteúdo da landing, separado da apresentação: trocar um texto ou uma mídia é mexer aqui, e
// não nos componentes. A mensagem e o roteiro de cada mídia estão em docs/landing-conteudo.md; as
// mídias, em public/midia (geradas por `npm run midia` a partir de web/midia/roteiro.mjs).

/**
 * Uma mídia animada: o vídeo em dois formatos (WebM primeiro, MP4 como alternativa), o poster (o
 * primeiro quadro, mostrado antes do vídeo carregar e com prefers-reduced-motion) e a captura
 * estática que entra quando não há vídeo ou ele falha.
 */
export interface Midia {
  video?: { webm: string; mp4: string };
  poster: string;
  captura?: string;
  alt: string;
  largura: number;
  altura: number;
}

/**
 * Um recurso do tour (as abas 01–08 da seção Recursos): o número e o rótulo CURTO da aba (as oito cabem numa linha da
 * tela larga; o título do painel diz o resto), o texto com os marcadores,
 * a mídia numa moldura de janela do app ("dev.kit - Agente") e a legenda curta abaixo dela. `novo` marca na aba o que
 * chegou nas versões 6 e 7.
 */
export interface Recurso {
  id: string;
  numero: string;
  aba: string;
  novo?: boolean;
  titulo: string;
  texto: string;
  marcadores: string[];
  /** A tela do app mostrada na moldura (o título da janela: "dev.kit - {janela}"). */
  janela: string;
  legenda: string;
  midia: Midia;
}

export interface Beneficio {
  titulo: string;
  texto: string;
}

/** Os ícones de traço da landing (o componente Icone da Landing.tsx). */
export type NomeDoIcone = 'monitor' | 'servico' | 'celular' | 'terminal' | 'nota' | 'ramo' | 'relogio' | 'arbitro' | 'camadas' | 'livro' | 'entrega';

/**
 * Um BRAÇO do dev.kit. O produto tem quatro — o app desktop, o serviço do Windows, o Board do dev.kit.page (no site e
 * no celular) e o devcli —, e a primeira dobra mostra os quatro: o `resumo` é a linha de cada um ali; o título, o
 * texto e os marcadores são o detalhe da seção "Produto".
 */
export interface Braco {
  id: 'desktop' | 'servico' | 'board' | 'devcli';
  nome: string;
  icone: NomeDoIcone;
  novo?: boolean;
  resumo: string;
  titulo: string;
  texto: string;
  marcadores: string[];
}

/** Uma etapa nomeada do "como funciona" — do work item à Pull Request (US #381: o fluxo novo). */
export interface Passo {
  titulo: string;
  texto: string;
}

/** Um produto da tabela comparativa (US #381). O dev.kit é sempre a primeira coluna. */
export type ProdutoComparado = 'devkit' | 'copilot' | 'devin' | 'cursor';

/** O que a página pública do produto mostra sobre o recurso: sim, não ou em parte (com integração ou configuração extra). */
export type Disponibilidade = 'sim' | 'nao' | 'parcial';

/** Uma linha da tabela comparativa: o recurso e o valor de CADA produto (o teste confere que nenhum falta). */
export interface LinhaComparativa {
  recurso: string;
  valores: Record<ProdutoComparado, Disponibilidade>;
}

export interface Integracao {
  nome: string;
  texto: string;
}

export interface PerguntaFrequente {
  pergunta: string;
  resposta: string;
}

/**
 * Um conceito de "trabalhar com agentes" (agente, skill, rule): o que é, como o dev.kit o usa e um EXEMPLO curto,
 * no formato do arquivo em que ele mora — o exemplo é o que torna o conceito concreto.
 */
export interface Conceito {
  id: string;
  titulo: string;
  texto: string;
  marcadores: string[];
  exemplo: { arquivo: string; codigo: string };
}

/** Uma seção da landing: a âncora (id) e o rótulo do menu do topo. */
export interface SecaoDaLanding {
  id: string;
  rotulo: string;
}

const LARGURA = 1280;
const ALTURA = 720;

/** A mídia de um recurso pelo nome do arquivo em public/midia (ex.: agente.webm, agente.mp4, agente.jpg). */
function midia(nome: string, alt: string, captura?: string): Midia {
  return {
    video: { webm: `/midia/${nome}.webm`, mp4: `/midia/${nome}.mp4` },
    poster: `/midia/${nome}.jpg`,
    captura,
    alt,
    largura: LARGURA,
    altura: ALTURA,
  };
}

/**
 * Uma mídia que é um GIF pronto do produto (o das Novidades da versão 5): sem vídeo, o GIF entra como a captura
 * animada e o poster é o primeiro quadro dele — o que fica parado com prefers-reduced-motion.
 */
function gif(nome: string, alt: string, largura: number, altura: number): Midia {
  return { poster: `/midia/${nome}.jpg`, captura: `/midia/${nome}.gif`, alt, largura, altura };
}

/**
 * Uma mídia que é uma CAPTURA estática do app (public/capturas/tela-NOME.png), sem vídeo: a mesma imagem é o poster e
 * a captura. É a do recurso Projetos — a tela real do plano, com dados de demonstração, recortada em 16:9.
 */
function captura(nome: string, alt: string, largura: number, altura: number): Midia {
  return { poster: `/capturas/tela-${nome}.png`, captura: `/capturas/tela-${nome}.png`, alt, largura, altura };
}

/** O título da moldura de janela do app ("dev.kit - Agente"), o mesmo na vitrine do hero e nos recursos. */
export const tituloDaJanela = (tela: string) => `dev.kit - ${tela}`;

/**
 * As seções, NA ORDEM da página — a landing e o teste usam esta lista. A landing refeita (12/11/2026): o hero com os
 * quatro braços na primeira dobra → como funciona (as sete etapas) → produto (os quatro braços em detalhe) → recursos
 * (o tour em abas) → por que o dev.kit (o bento) → conceitos e a fábrica agêntica → as seções que o menu, o rodapé e o
 * "Quero uma demonstração" apontam (catálogo, comparativo, integrações, segurança, empresas, planos, contato, FAQ) →
 * o CTA escuro.
 */
export const SECOES: SecaoDaLanding[] = [
  { id: 'inicio', rotulo: 'Início' },
  { id: 'como-funciona', rotulo: 'Como funciona' },
  { id: 'bracos', rotulo: 'Produto' },
  { id: 'recursos', rotulo: 'Recursos' },
  { id: 'beneficios', rotulo: 'Por que o dev.kit' },
  { id: 'conceitos', rotulo: 'Conceitos' },
  { id: 'fabrica', rotulo: 'Fábrica agêntica' },
  { id: 'catalogo', rotulo: 'Catálogo' },
  { id: 'comparativo', rotulo: 'Comparativo' },
  { id: 'integracoes', rotulo: 'Integrações' },
  { id: 'seguranca', rotulo: 'Segurança' },
  { id: 'empresas', rotulo: 'Empresas' },
  { id: 'planos', rotulo: 'Planos' },
  { id: 'contato', rotulo: 'Contato' },
  { id: 'faq', rotulo: 'Perguntas' },
  { id: 'comecar', rotulo: 'Começar' },
];

/** As seções do menu do topo, na ordem. */
export const MENU = ['como-funciona', 'bracos', 'recursos', 'catalogo', 'seguranca', 'planos', 'faq'] as const;

/** As seções dos links do rodapé, na ordem. */
export const RODAPE = ['bracos', 'conceitos', 'fabrica', 'integracoes', 'seguranca', 'comparativo', 'empresas', 'faq'] as const;

/** O rótulo de uma seção (o do menu e o do rodapé). */
export const rotuloDaSecao = (id: string) => SECOES.find((s) => s.id === id)?.rotulo ?? id;

export const HERO = {
  selo: 'Para times no Azure DevOps',
  titulo: 'Do work item à Pull Request, com um time de agentes de IA',
  /** O trecho do título em destaque (a cor da marca, sem quebra de linha no meio). */
  destaque: 'Pull Request',
  texto:
    'O dev.kit clona os repositórios, implementa, compila e testa com as Rules do seu time. Avaliadores dão nota de 0 a 100, o Orquestrador acompanha cada task — e você só decide o que importa, no desktop ou no celular.',
};

/**
 * Os QUATRO braços do dev.kit (a regra do projeto: serviço, app desktop, Board e devcli). Os fatos vêm do git.kit (o
 * GitKit.Scheduler, o devcli e as Novidades das versões 6 e 7) e do dev.kit.page (o Board remoto, as decisões e o push).
 */
export const BRACOS: Braco[] = [
  {
    id: 'desktop',
    nome: 'Desktop',
    icone: 'monitor',
    resumo: 'O app do Windows, com o agente trabalhando em clones na sua máquina.',
    titulo: 'O app do Windows, onde o trabalho acontece',
    texto:
      'O Board mostra o que precisa de você, e cada task tem a sua janela: Agente, Git, Task, Entrega, Depuração e Terminal. Os fluxos, os Projetos e o Design ficam a um clique.',
    marcadores: [
      'O Orquestrador no centro do Board: responde aos agentes e abre execuções pelo chat',
      'A entrega de cada task com o pedido, o resumo, os logs, os diffs e as capturas',
      'O agente roda no provedor que você escolher, em clones no branch da task',
    ],
  },
  {
    id: 'servico',
    nome: 'Serviço',
    icone: 'servico',
    resumo: 'Agenda, vigia o board e liga o dev.kit, mesmo com o app fechado.',
    titulo: 'O serviço do Windows, que não para',
    texto:
      'Roda com a máquina ligada, mesmo sem o dev.kit aberto: é o relógio dos agendamentos e o elo da máquina com o site.',
    marcadores: [
      'Dispara os agendamentos no horário, e o job vai até o fim mesmo se o serviço reiniciar',
      'Vigia o board: o comentário /devkit executar num work item abre a task',
      'Liga e reinicia o dev.kit quando você pede pelo site, e mantém a máquina visível no Board',
    ],
  },
  {
    id: 'board',
    nome: 'Board',
    icone: 'celular',
    novo: true,
    resumo: 'No site e no celular: fale com o Orquestrador e aprove com segundo fator.',
    titulo: 'O Board no site e no celular',
    texto: 'Acompanhe e comande as suas máquinas pelo dev.kit.page — no navegador ou instalado como app no celular.',
    marcadores: [
      'A mesma conversa do Orquestrador do desktop, com o fluxo, o work item e o repositório',
      'Aprovação com segundo fator, presa ao SHA que foi avaliado; o texto do chat nunca aprova',
      'Notificação no celular quando uma decisão espera por você',
      'Projetos e projetos de design criados pelo site viram pedido na máquina',
    ],
  },
  {
    id: 'devcli',
    nome: 'devcli',
    icone: 'terminal',
    resumo: 'Mais de 50 comandos e servidor MCP: o agente opera o dev.kit como você.',
    titulo: 'O devcli: o dev.kit na linha de comando',
    texto:
      'As ações da tela têm o seu comando, e é por ele que os agentes operam o dev.kit — com a sua credencial e as regras da tela, sem nunca ver o token.',
    marcadores: [
      'Mais de 50 comandos: work items, horas, fluxos, projetos, entrega, design e o depurador',
      'Servidor MCP para o Claude Code, o VS Code (Copilot) e o Cursor',
      'O que grava simula por padrão: só o --aplicar grava',
    ],
  },
];

/** O que a entrega de uma task guarda (a pasta entrega/): a janela do desktop e o bento mostram a mesma lista. */
export const EVIDENCIAS = ['pedido.md', 'resumo', 'logs', 'diffs', 'capturas'];

/**
 * As ilustrações de cada braço na seção Produto (dados de demonstração, como a vitrine): as abas da janela da task, o
 * que o serviço fez, a notificação e a conversa do Board no celular e os comandos do devcli.
 */
export const DEMONSTRACAO_DOS_BRACOS = {
  desktop: { abas: ['Agente', 'Git', 'Task', 'Entrega', 'Depuração', 'Terminal'], ativa: 'Entrega' },
  servico: {
    estado: 'Serviço ativo · dev.kit fechado',
    linhas: [
      { quando: '22:00', texto: 'Agendamento: Compilação noturna' },
      { quando: '09:14', texto: '/devkit executar no #1234: task aberta' },
      { quando: '09:30', texto: 'Ligar dev.kit, pedido pelo site' },
    ],
  },
  board: {
    notificacao: { titulo: 'dev.kit · decisão pendente', texto: 'Há uma decisão pendente. Abra o dev.kit para conferir.' },
    conversa: [
      { autor: 'Você · celular', texto: 'Como está a onda 2 do portal-web?', sua: true },
      { autor: 'Orquestrador · dev.kit', texto: 'Os itens 5 e 9 integraram; o item 4 espera a sua aprovação.', sua: false },
    ],
  },
  devcli: {
    comandos: [
      { comando: 'devcli horas', nota: 'simula o rateio do dia' },
      { comando: 'devcli horas --aplicar', nota: 'grava no Azure Boards' },
      { comando: 'devcli mcp', nota: 'servidor MCP (stdio)' },
    ],
  },
};

/**
 * A vitrine do hero (desde a US #405, "Amostra do Board"): o Board do desktop — a fala do Orquestrador, o projeto em
 * ondas, o "Precisa de você" e a avaliação com a nota — com o Board do celular por cima. Dados de demonstração.
 */
export const AMOSTRA_DO_BOARD = {
  titulo: tituloDaJanela('Board'),
  orquestrador: { autor: 'Orquestrador', texto: 'Destravei a #509: respondi pela fila com o que o pedido.md já dizia.' },
  projeto: { nome: 'portal-web', onda: 'onda 2 de 3', ondas: ['integrada', 'rodando', 'proxima'] as const, detalhe: '3 itens em paralelo · 4 de 9 integrados' },
  precisaDeVoce: [
    { titulo: 'US #393 — Agente: diretório de trabalho e multimídia', estado: 'pronto para revisão · PR aberta', acao: 'Aprovar' },
    { titulo: 'Fluxo “planejar e executar”', estado: 'aguardando interação', acao: 'Abrir' },
  ],
  avaliador: { titulo: 'Avaliador da entrega', notas: [93, 45, 92], gatilho: 'ao enviar', minima: 90 },
  nota: { valor: 'Nota 92/100', texto: 'Aceito: acima da nota mínima' },
};

/** O Board do dev.kit.page no celular, na vitrine: a máquina, a decisão que pede o segundo fator e a fala do Orquestrador. */
export const AMOSTRA_DO_CELULAR = {
  maquina: 'Notebook · serviço ativo',
  pendencias: 'Precisa de você · 1',
  decisao: { pergunta: 'Integrar o item 4 no projeto portal-web?', detalhe: 'Nota 94 · regressão sem falhas', acao: 'Aprovar com segundo fator' },
  fala: { autor: 'Orquestrador · dev.kit', texto: 'Só falta a sua aprovação para a onda 3 começar.' },
  aviso: 'Texto do chat não aprova',
};

/** O cabeçalho das seções (o rótulo pequeno acima do título) e o texto do CTA final e do rodapé. */
export const CABECALHOS = {
  comoFunciona: {
    rotulo: 'Como funciona',
    titulo: 'Do work item à Pull Request, em sete etapas',
    subtitulo: 'O agente faz o trabalho repetitivo, o Orquestrador acompanha e destrava. A decisão final é sua.',
  },
  bracos: {
    rotulo: 'Produto',
    titulo: 'Um produto, quatro braços',
    subtitulo: 'O mesmo trabalho onde for mais prático: no app, pelo serviço do Windows, no navegador e no celular, ou na linha de comando.',
  },
  recursos: {
    rotulo: 'Recursos',
    titulo: 'Veja cada recurso funcionando',
    subtitulo: 'Telas do app com dados de demonstração. Novo: os Projetos, com o plano em ondas e os itens em paralelo.',
  },
  porQue: {
    rotulo: 'Por que o dev.kit',
    titulo: 'O trabalho repetitivo com os agentes. As decisões com o time.',
  },
  ctaFinal: {
    titulo: 'Veja o dev.kit numa task do seu time',
    texto: 'Uma demonstração com um work item real, do clone à Pull Request.',
  },
  rodape: 'desenvolvido por Paulo Rogério',
};

/** O placar do cartão "Qualidade com nota mínima": os avaliadores das Rules do catálogo e a nota de cada um. */
export const PLACAR = {
  minima: 90,
  avaliadores: [
    { nome: 'Arquitetura', nota: 93 },
    { nome: 'Testes', nota: 45 },
    { nome: 'Reuso', nota: 92 },
  ],
};

/**
 * "Por que o dev.kit", em bento: sete cartões, cada um com o seu ícone. Os `largo` ocupam duas colunas na tela larga, e
 * o `extra` é a ilustração do cartão (o placar dos avaliadores ou as evidências da entrega).
 */
export const BENEFICIOS: (Beneficio & { icone: NomeDoIcone; largo?: boolean; extra?: 'placar' | 'evidencias' })[] = [
  {
    icone: 'nota',
    largo: true,
    extra: 'placar',
    titulo: 'Qualidade com nota mínima',
    texto:
      'Cada entrega passa por avaliadores que dão nota de 0 a 100. O objetivo só é aceito com a nota mínima de cada um, dada depois da última alteração.',
  },
  {
    icone: 'ramo',
    titulo: 'Da task à PR sem trocar de janela',
    texto:
      'Work item, repositórios, branch, compilação, testes, commit e push no mesmo lugar. O agente faz o trabalho repetitivo; você decide o que entra.',
  },
  {
    icone: 'arbitro',
    titulo: 'Um árbitro que destrava',
    texto: 'A execução parada é retomada sozinha: no limite de uso, a task troca de agente e segue; o impasse chega a você com a causa.',
  },
  {
    icone: 'camadas',
    titulo: 'O melhor provedor para cada papel',
    texto: 'Um provedor para implementar, outro para avaliar, outro para o Orquestrador — sem prender o time a um modelo.',
  },
  {
    icone: 'relogio',
    titulo: 'Horas lançadas sozinhas',
    texto:
      'As horas do dia vão para os work items trabalhados, rateadas pelos turnos do agente e divididas entre as tasks filhas — sem planilha no fim do dia.',
  },
  {
    icone: 'livro',
    titulo: 'Do jeito do seu time',
    texto: 'As Rules dizem o que nunca muda e as skills ensinam como fazer: escritas uma vez, versionadas no catálogo e usadas por todo agente.',
  },
  {
    icone: 'entrega',
    largo: true,
    extra: 'evidencias',
    titulo: 'Evidência em cada entrega',
    texto:
      'O pedido, o resumo item a item, os logs de build e testes, os diffs do SHA enviado e as capturas da tela: tudo guardado na entrega de cada task, para você conferir antes de aprovar.',
  },
];

/**
 * A parte CONCEITUAL de trabalhar com agentes: o agente, a skill e a rule, cada um com um exemplo no formato em
 * que ele vive no dev.kit, e um exemplo de ponta a ponta de como os três se juntam numa task.
 */
export const CONCEITOS = {
  rotulo: 'Conceitos',
  titulo: 'Trabalhar com agentes: agente, skill e rule',
  subtitulo: 'Três peças simples. O agente faz; a skill ensina como; a rule diz o que nunca muda.',
  itens: [
    {
      id: 'agente',
      titulo: 'Agente',
      texto:
        'Um modelo de IA com ferramentas — ler e editar arquivos, rodar comandos, consultar o board — que trabalha em turnos sobre um pedido. Ele não adivinha o contexto: recebe o work item, o planejamento técnico e as Rules do time.',
      marcadores: [
        'Recebe um pedido claro: o work item e o planejamento técnico',
        'Age e verifica: implementa, compila e testa antes de dizer que terminou',
        'Chama você só quando precisa: decisão, permissão ou aprovação',
      ],
      exemplo: {
        arquivo: 'pedido.md',
        codigo: [
          'US #387 — Exportar o ROI em CSV',
          '',
          'Planejamento técnico:',
          '- rota GET /api/roi/exportar?formato=csv',
          '- reaproveitar ExportacaoCsv',
          '- teste de API cobrindo o filtro por máquina',
        ].join('\n'),
      },
    },
    {
      id: 'skill',
      titulo: 'Skill',
      texto:
        'Um procedimento reutilizável, escrito uma vez e usado por qualquer agente: o nome, quando usar e os passos. É como o time ensina "do nosso jeito" — compilar, testar, abrir o bug, lançar as horas.',
      marcadores: [
        'Um arquivo SKILL.md por procedimento, com a descrição que diz quando usar',
        'O agente escolhe a skill pelo assunto, ou o fluxo a manda rodar num roteiro',
        'Versionada no catálogo: a correção chega a todas as máquinas',
      ],
      exemplo: {
        arquivo: 'skills/compilar-angular/SKILL.md',
        codigo: [
          '---',
          'name: compilar-angular',
          'description: Compilar o projeto Angular após alterar arquivos .ts/.html/.scss',
          '---',
          '1. Rode `npx ng build` no projeto alterado.',
          '2. Erro de tipo: corrija e compile de novo.',
          '3. Relate o comando e o resultado.',
        ].join('\n'),
      },
    },
    {
      id: 'rule',
      titulo: 'Rule',
      texto:
        'Os padrões e os limites do time, que entram no prompt e valem em TODO turno, qualquer que seja o pedido. Onde a skill diz como fazer, a rule diz o limite: o que não se faz, o que sempre se relata, como tratar o encoding.',
      marcadores: [
        'Entram no arquivo de regras do branch (o CLAUDE.md, no Claude Code) e em toda conversa',
        'As Rules do catálogo somam os padrões do time: arquitetura, nomes, testes, o que entregar',
        'O árbitro confere cada resposta contra elas e cobra o que faltou',
      ],
      exemplo: {
        arquivo: 'CLAUDE.md',
        codigo: [
          '## Idioma e fluxo',
          '- NÃO faça commit nem push: o dev.kit cuida disso após a aprovação do desenvolvedor.',
          '',
          '## Encoding dos arquivos',
          '- Preserve o encoding original do arquivo (UTF-8 ou Windows-1252), sem converter.',
        ].join('\n'),
      },
    },
  ] satisfies Conceito[],
  /** Um exemplo de ponta a ponta: as três peças numa task real. */
  exemplo: {
    titulo: 'Um exemplo, de ponta a ponta',
    passos: [
      { titulo: 'O pedido', texto: 'O work item #387 chega com o planejamento técnico: exportar o ROI em CSV.' },
      { titulo: 'As rules', texto: 'Entram no primeiro turno: sem commit, encoding preservado, arquivos relatados.' },
      { titulo: 'As skills', texto: 'O agente implementa e roda compilar-projeto e testar-dotnet, do jeito do time.' },
      { titulo: 'A entrega', texto: 'O avaliador dá a nota; com a mínima, o resumo vai para você aprovar.' },
    ] satisfies Passo[],
  },
};

/**
 * A FÁBRICA AGÊNTICA e o TIME AGÊNTICO: como os agentes se organizam além de uma task — em estações de uma linha
 * de produção (os fluxos com gatilhos, avaliadores e árbitro, e os Projetos em ondas) e, com as pessoas, em papéis de
 * um mesmo time.
 */
export const FABRICA = {
  rotulo: 'Fábrica agêntica',
  titulo: 'Da fábrica agêntica ao time agêntico',
  subtitulo: 'Agentes organizados como uma linha de produção — e pessoas e agentes no mesmo time, cada um no seu papel.',
  fabrica: {
    titulo: 'Fábrica agêntica',
    texto:
      'Uma linha de produção de software em que cada estação é um agente com um papel, uma entrada e um critério de aceite. O trabalho passa de estação em estação por gatilhos, e só segue com a nota mínima. No dev.kit, a fábrica é um fluxo salvo: roda pelo Board, por agendamento ou por gatilho.',
    estacoes: [
      { titulo: 'Planejar', texto: 'Um agente lê o work item e escreve o planejamento técnico e as tasks filhas.' },
      { titulo: 'Implementar', texto: 'Outro agente executa o plano com as rules e as skills do time.' },
      { titulo: 'Verificar', texto: 'Compila e testa o que mudou, no menor escopo que cobre a alteração.' },
      { titulo: 'Avaliar', texto: 'Avaliadores dão nota de 0 a 100; abaixo da mínima, o trabalho volta.' },
      { titulo: 'Arbitrar', texto: 'O árbitro cobra a regra quebrada, destrava o impasse e escala a você.' },
      { titulo: 'Entregar', texto: 'Commit, push e Pull Request — para a pessoa revisar e aprovar.' },
    ] satisfies Passo[],
    marcadores: [
      'Projetos: o Orquestrador planeja em ondas e dispara os itens em paralelo, cada um no seu branch',
      'Medida de ponta a ponta: falhas por causa, notas, impasses e custo por objetivo',
      'Best-of-N: a mesma task em 2 a 4 agentes, e fica a execução de maior nota',
      'Cada estação é trocável: outro agente, outro provedor cognitivo, outra skill',
    ],
  },
  time: {
    titulo: 'Time agêntico',
    texto:
      'Pessoas e agentes no mesmo time, com papéis claros: o agente executa e se verifica, a pessoa define o pedido e decide o que entra. Ninguém revisa sozinho o próprio trabalho.',
    papeis: [
      { titulo: 'Desenvolvedor', texto: 'Escreve o pedido e o plano, responde quando é chamado e aprova a entrega.' },
      { titulo: 'Agente executor', texto: 'Implementa, compila e testa na task, e relata o que mudou.' },
      { titulo: 'Agente avaliador', texto: 'Dá a nota com um critério escrito. Não altera o código que avalia.' },
      { titulo: 'Árbitro', texto: 'Confere as rules a cada resposta e cobra a correção antes de seguir.' },
      { titulo: 'Agentes entre tasks', texto: 'Pedem e respondem uns aos outros pela fila de mensagens do dev.kit.' },
      { titulo: 'Gestor', texto: 'Acompanha uso, qualidade e custo do time — com o consentimento de cada pessoa.' },
    ] satisfies Beneficio[],
  },
};

/**
 * O fluxo em SETE ETAPAS NOMEADAS (US #405, a proposta visual): o que o agente faz em cada uma, do clone
 * à Pull Request. Os avaliadores, os gatilhos e o teto de rodadas são contados no recurso "Fluxos".
 */
export const PASSOS: Passo[] = [
  { titulo: 'Clona', texto: 'Os repositórios do work item, no branch certo.' },
  { titulo: 'Implementa', texto: 'Com o provedor cognitivo que você escolher, as Rules e as skills do seu time.' },
  { titulo: 'Compila', texto: 'Com o compilador do projeto.' },
  { titulo: 'Testa', texto: 'Roda os testes antes de seguir.' },
  { titulo: 'Avalia', texto: 'Avaliadores dão nota de 0 a 100; abaixo da mínima, refaz.' },
  { titulo: 'Commit e push', texto: 'Só o que é da task, com a mensagem do board.' },
  { titulo: 'Pull Request', texto: 'Você revisa e aprova: o Orquestrador só traz o que precisa de você.' },
];

/**
 * O tour dos recursos (as abas 01–08). O Orquestrador usa o GIF das Novidades da versão 5; os Projetos (versões 6 e
 * 7), a captura real do plano; os demais, os vídeos gravados por web/midia/roteiro.mjs. Agendamento, depurador e
 * devcli estão nos braços, no comparativo e nas integrações.
 */
export const RECURSOS: Recurso[] = [
  {
    id: 'orquestrador',
    numero: '01',
    aba: 'Orquestrador',
    titulo: 'O Orquestrador acompanha todas as tasks',
    texto:
      'O chat no centro do Board vê todos os processos e agentes. Ele responde e destrava as perguntas deles com o que o pedido já diz, abre execuções e fluxos quando você pede e só repassa a você o que precisa de você.',
    marcadores: [
      'Responde pela fila o que o pedido.md já responde, sem parar a task',
      'Os pedidos de comando (Executar, Permitir sempre, Cancelar) aparecem ali mesmo',
      'A mesma conversa no site e no celular, pelo Board do dev.kit.page',
    ],
    janela: 'Orquestrador',
    legenda: 'Destravou a task e só chamou você para o que é seu',
    midia: gif('orquestrador', 'O Orquestrador acompanha as tasks no Board, responde ao agente o que o pedido já diz e mostra o pedido de comando para executar ou permitir sempre.', 820, 560),
  },
  {
    id: 'projetos',
    numero: '02',
    aba: 'Projetos',
    novo: true,
    titulo: 'Projetos: o plano em ondas, os itens em paralelo',
    texto:
      'Descreva o que o time quer entregar: o Orquestrador quebra em itens, monta o plano em ondas e, com a sua aprovação, dispara os itens em paralelo — cada um no seu branch, com o fluxo e o agente dele.',
    marcadores: [
      'As ondas respeitam as dependências; até 8 itens rodam ao mesmo tempo',
      'Cada item é integrado pelo próprio dev.kit no branch do projeto',
      'Um fechamento único, com a PR do projeto para você revisar',
    ],
    janela: 'Projetos',
    legenda: 'O plano proposto pelo Orquestrador: 16 de 20 itens integrados, até 3 em paralelo',
    midia: captura('projetos', 'O plano de um projeto no dev.kit: 16 de 20 itens integrados, a onda 9 de 10 com até 3 itens em paralelo, o andamento de cada onda e os botões Pausar e Aprovar e disparar.', 760, 428),
  },
  {
    id: 'agente',
    numero: '03',
    aba: 'O agente',
    titulo: 'O agente sobre a sua task',
    texto:
      'Escolha a US, o Bug ou uma tarefa avulsa, marque os repositórios e descreva o que precisa ser feito. O agente trabalha turno a turno até o commit, o push e a Pull Request.',
    marcadores: [
      'Clona cada repositório lado a lado e cria o branch',
      'Grava as regras, as skills e o conhecimento comum na pasta da task',
      'Roda no provedor cognitivo que você escolher: Claude Code, Codex, Kiro, Kimi ou GLM',
    ],
    janela: 'Agente',
    legenda: 'Propõe o commit com Ab#1234 — e você aprova',
    midia: midia('agente', 'O agente pede para executar a compilação, roda a suíte de testes e propõe a mensagem de commit para aprovação.', '/capturas/tela-agente.png'),
  },
  {
    id: 'fluxos',
    numero: '04',
    aba: 'Fluxos',
    titulo: 'Fluxos com avaliadores, gatilhos e objetivo',
    texto:
      'Encadeie agentes num fluxo: um planeja, outro implementa e os avaliadores dão nota ao trabalho. Exporte e importe fluxos para levá-los a outro time.',
    marcadores: [
      'Gatilhos ligam as etapas: ao enviar, ao revisar, ao cumprir o objetivo, ao aprovar',
      'O objetivo só é aceito com a nota mínima de cada avaliador, dada depois da última alteração',
      'Um teto de rodadas para o fluxo em impasse e chama você',
    ],
    janela: 'Atividades',
    legenda: 'Com a nota mínima de cada avaliador, o trabalho segue para o push',
    midia: midia('fluxos', 'A aba Atividades com as tasks do fluxo em execução, aguardando interação e prontas para enviar.', '/capturas/tela-atividades.png'),
  },
  {
    id: 'provedor',
    numero: '05',
    aba: 'Provedores',
    titulo: 'Você escolhe o melhor provedor cognitivo',
    texto:
      'O dev.kit não prende o time a um modelo: escolha o provedor e o modelo de cada papel — o agente que implementa, o avaliador que dá a nota e o Orquestrador. Claude Code, Codex (OpenAI), Kiro, Kimi e GLM estão no mesmo seletor.',
    marcadores: [
      'Escolhido na pré-execução da task e em cada agente do fluxo',
      'O dev.kit monta as regras próprias do ambiente de cada provedor: shell, sandbox e skills',
      'No limite de uso, a task troca de agente e retoma sozinha',
    ],
    janela: 'Tarefa avulsa',
    legenda: 'O provedor e o modelo da task, no resumo antes de executar',
    // A mídia segue com o nome do arquivo gravado (codex): é a pré-execução com o seletor de modelo.
    midia: midia('codex', 'A pré-execução de uma tarefa avulsa: o vínculo, o repositório, o planejamento e o provedor escolhido no seletor de modelo, com o resumo e o botão Executar com o agente.', '/capturas/tela-codex.png'),
  },
  {
    id: 'design',
    numero: '06',
    aba: 'Design',
    titulo: 'Design: da captura real à proposta e à implementação',
    texto:
      'Capture as telas reais da web e do desktop, peça a mudança no chat do Orquestrador e receba a proposta visual como versão nova, lado a lado com a original.',
    marcadores: [
      'Capturas reais versionadas por repositório e branch',
      'Propostas pedidas pelo chat do Orquestrador, com as telas selecionadas',
      'As versões sobem ao dev.kit.page, com o link estável que a PR cita',
    ],
    janela: 'Orquestrador · Design',
    legenda: 'A proposta aparece como versão nova, ao lado da captura',
    midia: midia('design', 'O painel Design do Orquestrador: as telas do projeto, a captura real e a proposta lado a lado, e o agente avisando que gravou as propostas pedidas.', '/capturas/tela-design.png'),
  },
  {
    id: 'replicacao',
    numero: '07',
    aba: 'Cherry-pick',
    titulo: 'Cherry-pick e replicação de branch',
    texto: 'Replique commits entre branches numa cópia temporária, sem trocar o branch do seu projeto.',
    marcadores: [
      'Por cherry-pick ou por integração de diff',
      'Conflito que o git não resolve abre no TortoiseGitMerge, arquivo por arquivo',
    ],
    janela: 'Cherry-pick',
    legenda: 'Marque os commits a replicar',
    midia: midia('replicacao', 'A tela de cherry-pick: o repositório, os branches de origem e destino e os commits marcados para replicar.', '/capturas/tela-cherry-pick.png'),
  },
  {
    id: 'board',
    numero: '08',
    aba: 'Horas',
    titulo: 'Horas lançadas sozinhas',
    texto:
      'As horas do dia vão para os work items trabalhados, rateadas pelos turnos do agente e divididas entre as tasks filhas — sem planilha no fim do dia.',
    marcadores: ['Rateio por turnos do agente', 'Divisão entre as tasks filhas'],
    janela: 'Board',
    legenda: 'Horas de hoje, já rateadas',
    midia: midia('board', 'O Board com as horas de hoje lançadas, os períodos de hoje a mensal e os work items trabalhados que esperam por você.', '/capturas/tela-board.png'),
  },
];

export const INTEGRACOES: Integracao[] = [
  { nome: 'Azure DevOps', texto: 'Boards (work items, iterações, horas), Repos e Pipelines.' },
  { nome: 'GitHub', texto: 'Clone, push e Pull Request pelo gh, e o GitHub Actions de cada commit.' },
  { nome: 'Provedores cognitivos', texto: 'Claude Code, Codex (OpenAI), Kiro, Kimi e GLM: o melhor para cada agente, com as regras do ambiente de cada um.' },
  { nome: 'Azure', texto: 'Fluxos prontos criam e publicam no App Service e no Static Web Apps, em teste; produção só com a sua aprovação.' },
  { nome: 'MCP', texto: 'O devcli como servidor MCP no Claude Code, no VS Code (Copilot) e no Cursor.' },
  { nome: 'TortoiseGit', texto: 'Diffs e o merge de conflitos arquivo por arquivo.' },
];

export const SEGURANCA: Beneficio[] = [
  {
    titulo: 'O agente nunca vê o token',
    texto: 'As credenciais do board e do GitHub ficam no dev.kit; o agente fala com eles pelo devcli, que aplica as mesmas regras da tela.',
  },
  {
    titulo: 'Gravar é explícito',
    texto: 'Todo comando que grava simula por padrão. Apagar o que não tem volta só com pedido explícito, e os comandos destrutivos ficam sempre bloqueados.',
  },
  {
    titulo: 'Seu código fica com você',
    texto: 'O agente trabalha em clones na sua máquina, no branch da task, e o dev.kit não leva o seu código para servidor nenhum. Commit e push só quando o fluxo permite.',
  },
  {
    titulo: 'Aprovação com segundo fator',
    texto: 'Pelo site, aprovar ou autorizar pede o segundo fator e vale só para o SHA que foi avaliado. O texto do chat nunca aprova.',
  },
  {
    titulo: 'Segredos fora dos logs',
    texto: 'Variáveis e segredos ficam nas Configurações; o devcli os entrega ao comando que precisa deles, e o valor de um segredo nunca aparece nos logs da entrega.',
  },
  {
    titulo: 'Telemetria anônima, que você desliga',
    texto:
      'O envio de uso vem ligado e se desliga em Configurações → Telemetria de uso. É identificado por um GUID da máquina, sem caminhos, código, prompts nem nomes de cliente — inclusive o trace de um erro, que sai sem caminhos, e-mails nem URLs. Ao aderir a uma empresa, o nome que você informar é compartilhado com o gestor dela, e só com o seu consentimento.',
  },
];

/** Os produtos da tabela comparativa, na ordem das colunas. As fontes e a data estão em docs/landing-conteudo.md. */
export const PRODUTOS_COMPARADOS: { id: ProdutoComparado; nome: string }[] = [
  { id: 'devkit', nome: 'dev.kit' },
  { id: 'copilot', nome: 'Copilot coding agent + Azure Boards' },
  { id: 'devin', nome: 'Devin' },
  { id: 'cursor', nome: 'Cursor' },
];

/**
 * A tabela comparativa (US #381): os recursos que pesam para quem trabalha no Azure DevOps. Só o que a
 * página pública de cada produto mostra, sem preço — revisada a cada versão da landing.
 */
export const COMPARATIVO: LinhaComparativa[] = [
  { recurso: 'Azure Repos, sem precisar do código no GitHub', valores: { devkit: 'sim', copilot: 'nao', devin: 'parcial', cursor: 'sim' } },
  { recurso: 'Work item do Azure Boards como ponto de partida', valores: { devkit: 'sim', copilot: 'sim', devin: 'parcial', cursor: 'nao' } },
  { recurso: 'Avaliadores que dão nota ao trabalho', valores: { devkit: 'sim', copilot: 'nao', devin: 'nao', cursor: 'nao' } },
  { recurso: 'Nota mínima por avaliador para aceitar o objetivo', valores: { devkit: 'sim', copilot: 'nao', devin: 'nao', cursor: 'nao' } },
  { recurso: 'Gatilhos entre agentes num fluxo salvo', valores: { devkit: 'sim', copilot: 'nao', devin: 'parcial', cursor: 'nao' } },
  { recurso: 'Agendamento na sua máquina (cron, serviço do Windows)', valores: { devkit: 'sim', copilot: 'nao', devin: 'nao', cursor: 'nao' } },
  { recurso: 'Horas lançadas no work item', valores: { devkit: 'sim', copilot: 'nao', devin: 'nao', cursor: 'nao' } },
  { recurso: 'Cherry-pick e replicação de branch numa cópia temporária', valores: { devkit: 'sim', copilot: 'nao', devin: 'nao', cursor: 'nao' } },
  { recurso: 'Depurador que o agente usa sem tela', valores: { devkit: 'sim', copilot: 'nao', devin: 'nao', cursor: 'nao' } },
  { recurso: 'Roda na sua máquina, com o seu código', valores: { devkit: 'sim', copilot: 'nao', devin: 'nao', cursor: 'sim' } },
];

/** O texto de cada valor da tabela. */
export const ROTULO_DA_DISPONIBILIDADE: Record<Disponibilidade, string> = {
  sim: 'Sim',
  nao: 'Não',
  parcial: 'Em parte',
};

/**
 * "Para empresas" (US #381): a venda empresarial — o gestor vê e exporta o uso do time, e o
 * colaborador decide o que compartilha. O contato é o MESMO formulário de demonstração.
 */
export const EMPRESAS = {
  titulo: 'Para empresas',
  subtitulo: 'O uso do agente no time inteiro, com o consentimento de cada pessoa.',
  itens: [
    {
      titulo: 'O gestor acompanha o time',
      texto: 'Sessões, turnos, falhas, notas dos avaliadores e objetivos de cada colaborador que aderiu — só da sua empresa, nunca de outra.',
    },
    {
      titulo: 'Consentimento do colaborador',
      texto: 'Cada pessoa adere no próprio dev.kit, com o código da empresa e o aceite de um aviso claro do que o gestor passa a ver. Sem o aceite, a máquina segue anônima.',
    },
    {
      titulo: 'Exportação auditada',
      texto: 'Os dados saem em CSV ou JSON, por período e por colaborador, e cada exportação fica registrada: quem, quando e o quê.',
    },
    {
      titulo: 'Assentos e adesão simples',
      texto: 'O plano define os assentos; o código de adesão entra em Configurações, sem instalar nada além do dev.kit.',
    },
  ] satisfies Beneficio[],
};

/**
 * Os fluxos prontos do catálogo básico, na ordem em que um time costuma conhecê-los — os mesmos doze do
 * CatalogoDosFluxosBasicos do git.kit. O subtítulo do Catálogo conta por esta lista.
 */
const FLUXOS_DO_CATALOGO = [
  'Fluxo simples', 'Tarefa avulsa', 'Planejar e executar', 'Somente planejamento', 'Implementação de work item planejado', 'Tela', 'Design',
  'Criar aplicação', 'Publicar no Azure', 'Deploy no Azure', 'Distribuição .NET', 'Fechamento do projeto',
];

/**
 * A seção "Catálogo" (US #425): o catálogo básico versionado, que chega sozinho às máquinas, e o catálogo da
 * empresa. A lista completa, por tecnologia, é a página pública /catalogo.
 */
export const CATALOGO = {
  titulo: 'Catálogo de skills, Rules e fluxos',
  subtitulo: `Mais de 60 skills, 20 Rules e ${FLUXOS_DO_CATALOGO.length} fluxos prontos no catálogo básico — e ele se atualiza sozinho.`,
  itens: [
    {
      titulo: 'Atualiza sozinho',
      texto: 'Uma skill corrigida chega a todas as máquinas: o dev.kit confere o catálogo ao abrir e uma vez por dia. O que você editou fica do seu jeito, com o aviso de versão nova.',
    },
    {
      titulo: 'O catálogo da sua empresa',
      texto: 'Publique as skills, as Rules e os fluxos do time; cada pessoa assina o que usa, e assinar um fluxo traz as skills e as Rules dele.',
    },
    {
      titulo: 'Versionado e revisado',
      texto: 'Cada item tem versão e hash. O que muda no catálogo básico passa pela revisão do repositório antes de ser publicado.',
    },
  ] satisfies Beneficio[],
  fluxos: FLUXOS_DO_CATALOGO,
};

/** Um plano da seção "Planos" (US #425): o teste grátis, a assinatura individual e o plano para empresas. */
export interface Plano {
  id: 'teste' | 'individual' | 'empresas';
  nome: string;
  resumo: string;
  itens: string[];
}

/** Os planos, na ordem dos cartões. O preço é o do checkout do gateway — a página não o repete. */
export const PLANOS: Plano[] = [
  {
    id: 'teste',
    nome: 'Teste grátis',
    resumo: '14 dias, contados da primeira abertura. Sem cartão e sem login.',
    itens: ['Todos os recursos do dev.kit', 'Catálogo básico atualizado', 'Reinstalar não reinicia o prazo'],
  },
  {
    id: 'individual',
    nome: 'Individual',
    resumo: 'A assinatura para uma pessoa, renovada pelo login.',
    itens: ['Licença de 30 dias que se renova sozinha', 'Catálogo próprio: publique para você', 'Cancele quando quiser'],
  },
  {
    id: 'empresas',
    nome: 'Empresas',
    resumo: 'Assentos por usuário, para o time inteiro.',
    itens: ['Catálogo da empresa', 'Gestor administra os usuários e os assentos', 'Adesão por código e e-mail'],
  },
];

export const PERGUNTAS: PerguntaFrequente[] = [
  {
    pergunta: 'Preciso trocar o meu processo no Azure DevOps?',
    resposta: 'Não. O dev.kit lê os work items, as iterações e os campos do processo que você já usa, e respeita os campos obrigatórios dele.',
  },
  {
    pergunta: 'Quais provedores de IA ele usa?',
    resposta:
      'O que você escolher, papel a papel — o agente que implementa, o avaliador e o Orquestrador: Claude Code, Codex (OpenAI), Kiro, Kimi ou GLM. A conta e o plano de cada provedor são os seus.',
  },
  {
    pergunta: 'O agente faz commit e push sozinho?',
    resposta:
      'Só quando a regra do fluxo permite e com o trabalho compilando e os testes passando. Pull Request e aprovação ficam com o desenvolvedor.',
  },
  {
    pergunta: 'Onde o código roda?',
    resposta: 'Na sua máquina, em clones na pasta da task. O dev.kit não leva o seu código para servidor nenhum.',
  },
  {
    pergunta: 'Consigo acompanhar e aprovar pelo celular?',
    resposta:
      'Sim. O Board do dev.kit.page mostra as suas máquinas, a conversa com o Orquestrador e o que precisa de você; ele instala como app no celular e avisa por notificação. Aprovar pede o segundo fator, e o texto do chat nunca aprova.',
  },
  {
    pergunta: 'O dev.kit precisa ficar aberto?',
    resposta:
      'Não. O serviço do Windows dispara os agendamentos no horário, vigia os comentários /devkit executar e liga ou reinicia o dev.kit quando você pede pelo site. As tasks rodam no app, que o serviço abre quando precisa.',
  },
  {
    pergunta: 'Os agentes podem operar o próprio dev.kit?',
    resposta:
      'Sim, pelo devcli: mais de 50 comandos com a sua credencial e as regras da tela, e ele também sobe como servidor MCP no Claude Code, no VS Code (Copilot) e no Cursor. O que grava simula antes: só o --aplicar grava.',
  },
  {
    pergunta: 'Como funciona o teste grátis?',
    resposta:
      'Baixe o dev.kit sem cadastro: os 14 dias contam da primeira abertura e ficam registrados também no servidor, então reinstalar não reinicia o prazo.',
  },
  {
    pergunta: 'Como compro só para mim?',
    resposta:
      'Em Planos, escolha a assinatura individual. Depois do pagamento, chega um e-mail com o link para definir a senha; entre no dev.kit com o seu e-mail e a licença se renova sozinha.',
  },
  {
    pergunta: 'Como cancelo a assinatura?',
    resposta: 'Na sua conta, em "Gerenciar assinatura". O dev.kit segue funcionando até o fim da licença atual, e ela não renova mais.',
  },
  {
    pergunta: 'O que é o catálogo do dev.kit?',
    resposta:
      'As skills, as Rules e os fluxos que o agente usa. O básico vem com o dev.kit e se atualiza sozinho; a sua empresa pode publicar os dela para o time assinar.',
  },
  {
    pergunta: 'Como funciona o plano para empresas?',
    resposta:
      'A empresa recebe um código de adesão. Cada pessoa adere com o código e o e-mail, define a senha pelo link que chega e ocupa um assento — o gestor administra os usuários e bloqueia quem sair, liberando o assento. O uso só é compartilhado com o gestor com o aceite do aviso de coleta no dev.kit.',
  },
  {
    pergunta: 'O que acontece com os dados do pedido de demonstração?',
    resposta:
      'Usamos o nome e o e-mail só para o contato. O pedido é apagado em até 365 dias, ou antes, quando você pedir.',
  },
];
