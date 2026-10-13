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
 * Um recurso do dev.kit (US #467, a proposta da home): o número e o rótulo curto da aba (01–04), o texto com os
 * marcadores, a mídia numa moldura de janela do app ("dev.kit - Agente") e a legenda curta abaixo dela — a
 * legenda saiu de cima do vídeo e veio para a página.
 */
export interface Recurso {
  id: string;
  numero: string;
  aba: string;
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

/** As seções do menu do topo (a proposta da US #405, com o Catálogo e os Planos da US #425), na ordem. */
export const MENU = ['como-funciona', 'recursos', 'catalogo', 'integracoes', 'seguranca', 'planos', 'faq'] as const;

/** As seções dos links do rodapé (a proposta da US #405), na ordem. */
export const RODAPE = ['seguranca', 'comparativo', 'empresas', 'faq'] as const;

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
 * O "Board" do hero (US #405): uma amostra estática do que o dev.kit mostra — o "Precisa de você" com o
 * que espera o dev, a nota do avaliador e as horas do dia. Dados de demonstração, como as mídias.
 */
export const AMOSTRA_DO_BOARD = {
  titulo: tituloDaJanela('Board'),
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
    subtitulo: 'O agente faz o trabalho repetitivo. A decisão final é sua.',
  },
  recursos: {
    rotulo: 'Recursos',
    titulo: 'Cada recurso com a sua demonstração',
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
 * O fluxo em SETE ETAPAS NOMEADAS (US #405, a proposta visual): o que o agente faz em cada uma, do clone
 * à Pull Request. Os avaliadores, os gatilhos e o teto de rodadas são contados no recurso "Fluxos".
 */
export const PASSOS: Passo[] = [
  { titulo: 'Clona', texto: 'Os repositórios do work item, no branch certo.' },
  { titulo: 'Implementa', texto: 'Com as regras e os tech plans do seu time.' },
  { titulo: 'Compila', texto: 'Com o compilador do projeto.' },
  { titulo: 'Testa', texto: 'Roda os testes antes de seguir.' },
  { titulo: 'Avalia', texto: 'Avaliadores dão nota de 0 a 100; abaixo da mínima, refaz.' },
  { titulo: 'Commit e push', texto: 'Só o que é da task, com a mensagem do board.' },
  { titulo: 'Pull Request', texto: 'Você revisa e aprova.' },
];

/**
 * Os QUATRO recursos da home (US #467, a proposta): as abas 01–04 apontam para eles, e cada um traz a mídia
 * gravada do app atual (web/midia/roteiro.mjs) sem legenda sobreposta — a legenda é a deste arquivo, abaixo
 * da moldura. Agendamento, depurador e devcli saíram da home: continuam no comparativo e nas integrações.
 */
export const RECURSOS: Recurso[] = [
  {
    id: 'agente',
    numero: '01',
    aba: 'O agente sobre a task',
    titulo: 'O agente sobre a sua task',
    texto:
      'Escolha a US, o Bug ou uma tarefa avulsa, marque os repositórios e descreva o que precisa ser feito. O agente trabalha turno a turno até o commit, o push e a Pull Request.',
    marcadores: [
      'Clona cada repositório lado a lado e cria o branch',
      'Grava as regras, as skills e o conhecimento comum na pasta da task',
      'Roda a CLI que você escolher: Claude, Kiro, Kimi ou GLM',
    ],
    janela: 'Agente',
    legenda: 'Propõe o commit com Ab#1234 — e você aprova',
    midia: midia('agente', 'O agente pede para executar a compilação, roda a suíte de testes e propõe a mensagem de commit para aprovação.', '/capturas/tela-agente.png'),
  },
  {
    id: 'fluxos',
    numero: '02',
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
    numero: '03',
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
    numero: '04',
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
  { nome: 'Claude Code', texto: 'A CLI claude como agente da task.' },
  { nome: 'Kiro, Kimi e GLM', texto: 'As outras CLIs de agente, escolhidas por task.' },
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
  titulo: 'Catálogo de skills, Tech Plans e fluxos',
  subtitulo: 'O dev.kit chega com o catálogo básico, e ele se atualiza sozinho.',
  itens: [
    {
      titulo: 'Atualiza sozinho',
      texto: 'Uma skill corrigida chega a todas as máquinas: o dev.kit confere o catálogo ao abrir e uma vez por dia. O que você editou fica do seu jeito, com o aviso de versão nova.',
    },
    {
      titulo: 'O catálogo da sua empresa',
      texto: 'Publique as skills, os Tech Plans e os fluxos do time; cada pessoa assina o que usa, e assinar um fluxo traz as skills e os planos dele.',
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
      'As skills, os Tech Plans e os fluxos que o agente usa. O básico vem com o dev.kit e se atualiza sozinho; a sua empresa pode publicar os dela para o time assinar.',
  },
  {
    pergunta: 'Preciso trocar o meu processo no Azure DevOps?',
    resposta: 'Não. O dev.kit lê os work items, as iterações e os campos do processo que você já usa, e respeita os campos obrigatórios dele.',
  },
  {
    pergunta: 'Quais agentes de IA ele usa?',
    resposta: 'As CLIs claude, kiro, kimi e glm. Cada task escolhe a sua; a conta e o plano do agente são os seus.',
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
