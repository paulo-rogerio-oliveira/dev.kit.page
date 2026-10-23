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
 * Um recurso do dev.kit (US #467, a proposta da home): o número e o rótulo curto da aba (01–07), o texto com os
 * marcadores, a mídia numa moldura de janela do app ("dev.kit - Agente") e a legenda curta abaixo dela — a
 * legenda saiu de cima do vídeo e veio para a página. `novo` marca na aba o que chegou na versão 5.
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

/** O título da moldura de janela do app ("dev.kit - Agente"), o mesmo na amostra do hero e nos recursos. */
export const tituloDaJanela = (tela: string) => `dev.kit - ${tela}`;

/**
 * As seções, NA ORDEM da página — a landing e o teste usam esta lista. A ordem segue a proposta da home
 * (US #467): hero → como funciona (as sete etapas) → recursos (as quatro demonstrações) → por que o dev.kit
 * (os três cartões) → as seções que o menu, o rodapé e o "Quero uma demonstração" apontam (catálogo,
 * comparativo, integrações, segurança, empresas, planos, contato, FAQ) → o CTA escuro.
 */
export const SECOES: SecaoDaLanding[] = [
  { id: 'inicio', rotulo: 'Início' },
  { id: 'como-funciona', rotulo: 'Como funciona' },
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

/** As seções do menu do topo (a proposta da US #405, com o Catálogo e os Planos da US #425 e os Conceitos), na ordem. */
export const MENU = ['como-funciona', 'recursos', 'conceitos', 'catalogo', 'integracoes', 'seguranca', 'planos', 'faq'] as const;

/** As seções dos links do rodapé (a proposta da US #405, com os Conceitos e a Fábrica agêntica), na ordem. */
export const RODAPE = ['conceitos', 'fabrica', 'seguranca', 'comparativo', 'empresas', 'faq'] as const;

/** O rótulo de uma seção (o do menu e o do rodapé). */
export const rotuloDaSecao = (id: string) => SECOES.find((s) => s.id === id)?.rotulo ?? id;

export const HERO = {
  selo: 'Para times no Azure DevOps',
  titulo: 'O agente de IA que leva a sua task do work item à Pull Request',
  /** O trecho do título em destaque (a cor da marca). */
  destaque: 'Pull Request',
  texto:
    'O dev.kit clona os repositórios, implementa, compila e testa com as regras do seu time. Cada entrega passa por avaliadores com nota mínima antes do commit, do push e do PR. Você acompanha e aprova.',
};

/**
 * O "Board" do hero (US #405): uma amostra estática do que o dev.kit mostra — desde a versão 5, com a fala do
 * Orquestrador no topo (ele destravou uma task e só repassa o que precisa de você), o "Precisa de você" com o
 * que espera o dev, a nota do avaliador e as horas do dia. Dados de demonstração, como as mídias.
 */
export const AMOSTRA_DO_BOARD = {
  titulo: tituloDaJanela('Board'),
  orquestrador: { autor: 'Orquestrador', texto: 'Destravei a #509: respondi pela fila com o que o pedido.md já dizia.' },
  precisaDeVoce: [
    { titulo: 'US #393 — Agente: diretório de trabalho e multimídia', estado: 'pronto para revisão · PR aberta', acao: 'Aprovar' },
    { titulo: 'Fluxo “planejar e executar”', estado: 'aguardando interação', acao: 'Abrir' },
  ],
  avaliador: { titulo: 'Avaliador da entrega', notas: [93, 45, 92], gatilho: 'ao enviar' },
  nota: { valor: 'Nota 92/100', texto: 'Aceito: acima da nota mínima' },
  horas: { titulo: 'Horas de hoje', texto: '8h rateadas em 4 work items', partes: [40, 28, 16, 16] },
};

/** O cabeçalho de "Como funciona", de "Recursos" e de "Por que o dev.kit" (o rótulo pequeno acima do título). */
export const CABECALHOS = {
  comoFunciona: {
    rotulo: 'Como funciona',
    titulo: 'Do work item à Pull Request, em sete etapas',
    subtitulo: 'O agente faz o trabalho repetitivo, o Orquestrador acompanha e destrava. A decisão final é sua.',
  },
  recursos: {
    rotulo: 'Recursos',
    titulo: 'Cada recurso com a sua demonstração',
    subtitulo: 'Novo na versão 5: o Orquestrador, a escolha do provedor cognitivo e o Design.',
  },
  porQue: {
    rotulo: 'Por que o dev.kit',
    titulo: 'O trabalho repetitivo com o agente. As decisões com o time.',
  },
  ctaFinal: {
    titulo: 'Veja o dev.kit numa task do seu time',
    texto: 'Uma demonstração com um work item real, do clone à Pull Request.',
  },
  rodape: 'desenvolvido por Paulo Rogério',
};

/** Os três cartões de "Por que o dev.kit", cada um com o seu ícone. */
export const BENEFICIOS: (Beneficio & { icone: 'ramo' | 'nota' | 'relogio' })[] = [
  {
    icone: 'ramo',
    titulo: 'Da task à PR sem trocar de janela',
    texto:
      'Work item, repositórios, branch, compilação, testes, commit e push no mesmo lugar. O agente faz o trabalho repetitivo; você decide o que entra.',
  },
  {
    icone: 'nota',
    titulo: 'Qualidade com nota mínima',
    texto:
      'Cada entrega passa por avaliadores que dão nota de 0 a 100. O objetivo só é aceito com a nota mínima de cada um, dada depois da última alteração.',
  },
  {
    icone: 'relogio',
    titulo: 'Horas lançadas sozinhas',
    texto:
      'As horas do dia vão para os work items trabalhados, rateadas pelos turnos do agente e divididas entre as tasks filhas — sem planilha no fim do dia.',
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
 * de produção (os fluxos com gatilhos, avaliadores e árbitro) e, com as pessoas, em papéis de um mesmo time.
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
 * Os recursos da home (US #467, a proposta; a versão 5 trouxe o Orquestrador, a escolha do provedor cognitivo — o
 * Codex entrou como um dos provedores — e o Design): as abas 01–07
 * apontam para eles, e cada um traz a mídia do app atual sem legenda sobreposta — a legenda é a deste arquivo,
 * abaixo da moldura. O Orquestrador usa o GIF das Novidades da versão 5; os demais, os vídeos gravados por
 * web/midia/roteiro.mjs. Agendamento, depurador e devcli saíram da home: continuam no comparativo e nas integrações.
 */
export const RECURSOS: Recurso[] = [
  {
    id: 'orquestrador',
    numero: '01',
    aba: 'Orquestrador',
    novo: true,
    titulo: 'O Orquestrador acompanha todas as tasks',
    texto:
      'O chat no centro do Board vê todos os processos e agentes. Ele responde e destrava as perguntas deles com o que o pedido já diz, abre execuções e fluxos quando você pede e só repassa a você o que precisa de você.',
    marcadores: [
      'Responde pela fila o que o pedido.md já responde, sem parar a task',
      'Os pedidos de comando (Executar, Permitir sempre, Cancelar) aparecem ali mesmo',
      'Abre a execução ou o fluxo que você pede no chat',
    ],
    janela: 'Orquestrador',
    legenda: 'Destravou a task e só chamou você para o que é seu',
    midia: gif('orquestrador', 'O Orquestrador acompanha as tasks no Board, responde ao agente o que o pedido já diz e mostra o pedido de comando para executar ou permitir sempre.', 820, 560),
  },
  {
    id: 'agente',
    numero: '02',
    aba: 'O agente sobre a task',
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
    id: 'provedor',
    numero: '03',
    aba: 'Provedor cognitivo',
    novo: true,
    titulo: 'Você escolhe o melhor provedor cognitivo',
    texto:
      'O dev.kit não prende o time a um modelo: escolha o provedor e o modelo de cada papel — o agente que implementa, o avaliador que dá a nota e o Orquestrador. Claude Code, Codex (OpenAI), Kiro, Kimi e GLM estão no mesmo seletor.',
    marcadores: [
      'Escolhido na pré-execução da task e em cada agente do fluxo',
      'O dev.kit monta as regras próprias do ambiente de cada provedor: shell, sandbox e skills',
      'Novo na versão 5: o Codex (OpenAI) entrou entre os provedores',
    ],
    janela: 'Tarefa avulsa',
    legenda: 'O provedor e o modelo da task, no resumo antes de executar',
    // A mídia segue com o nome do arquivo gravado (codex): é a pré-execução com o seletor de modelo.
    midia: midia('codex', 'A pré-execução de uma tarefa avulsa: o vínculo, o repositório, o planejamento e o provedor escolhido no seletor de modelo, com o resumo e o botão Executar com o agente.', '/capturas/tela-codex.png'),
  },
  {
    id: 'design',
    numero: '04',
    aba: 'Design',
    novo: true,
    titulo: 'Design: da captura real à proposta e à implementação',
    texto:
      'Capture as telas reais da web e do desktop, peça a mudança no chat do Orquestrador e receba a proposta visual como versão nova, lado a lado com a original.',
    marcadores: [
      'Capturas reais versionadas por repositório e branch',
      'Propostas pedidas pelo chat do Orquestrador, com as telas selecionadas',
      'O fluxo Design vai da proposta à implementação e à recaptura',
    ],
    janela: 'Orquestrador · Design',
    legenda: 'A proposta aparece como versão nova, ao lado da captura',
    midia: midia('design', 'O painel Design do Orquestrador: as telas do projeto, a captura real e a proposta lado a lado, e o agente avisando que gravou as propostas pedidas.', '/capturas/tela-design.png'),
  },
  {
    id: 'fluxos',
    numero: '05',
    aba: 'Fluxos com avaliadores',
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
    id: 'replicacao',
    numero: '06',
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
    numero: '07',
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
  { nome: 'Azure DevOps', texto: 'Boards (work items, iterações, horas) e Repos.' },
  { nome: 'GitHub', texto: 'Clone, push e Pull Request pelo gh.' },
  { nome: 'Provedores cognitivos', texto: 'Claude Code, Codex (OpenAI), Kiro, Kimi e GLM: o melhor para cada agente, com as regras do ambiente de cada um.' },
  { nome: 'TortoiseGit', texto: 'Diffs e o merge de conflitos arquivo por arquivo.' },
  { nome: 'Serviço do Windows', texto: 'O agendador que roda as tasks no horário.' },
];

export const SEGURANCA: Beneficio[] = [
  {
    titulo: 'O agente nunca vê o token',
    texto: 'As credenciais do board e do GitHub ficam no dev.kit; o agente fala com eles pelo devcli, que aplica as mesmas regras da tela.',
  },
  {
    titulo: 'Gravar é explícito',
    texto: 'Todo comando que grava simula por padrão. Apagar o que não tem volta só com pedido explícito.',
  },
  {
    titulo: 'Seu código fica com você',
    texto: 'O agente trabalha em clones na sua máquina, no branch da task. Nada sai sem o seu commit e o seu push.',
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
 * A seção "Catálogo" (US #425): o catálogo básico versionado, que chega sozinho às máquinas, e o catálogo da
 * empresa. A lista completa, por tecnologia, é a página pública /catalogo.
 */
export const CATALOGO = {
  titulo: 'Catálogo de skills, Rules e fluxos',
  subtitulo: 'O dev.kit chega com o catálogo básico, e ele se atualiza sozinho.',
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
