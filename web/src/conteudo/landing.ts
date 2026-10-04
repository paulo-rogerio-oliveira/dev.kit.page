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

/** Um recurso do dev.kit: o texto vem do README e do manual do git.kit. */
export interface Recurso {
  id: string;
  titulo: string;
  texto: string;
  midia: Midia;
}

export interface Beneficio {
  titulo: string;
  texto: string;
}

/** Um passo do "como funciona" — do work item à Pull Request. */
export interface Passo {
  titulo: string;
  texto: string;
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

/** As seções, NA ORDEM da página (critério 1 da US #283) — a landing e o teste usam esta lista. */
export const SECOES: SecaoDaLanding[] = [
  { id: 'inicio', rotulo: 'Início' },
  { id: 'beneficios', rotulo: 'Benefícios' },
  { id: 'como-funciona', rotulo: 'Como funciona' },
  { id: 'recursos', rotulo: 'Recursos' },
  { id: 'integracoes', rotulo: 'Integrações' },
  { id: 'seguranca', rotulo: 'Segurança' },
  { id: 'contato', rotulo: 'Contato' },
  { id: 'faq', rotulo: 'Perguntas' },
  { id: 'comecar', rotulo: 'Começar' },
];

export const HERO = {
  titulo: 'O agente de IA que leva a sua task do work item à Pull Request',
  texto:
    'O dev.kit põe um agente para trabalhar nas tasks do Azure DevOps com as regras do seu time: clona os repositórios do work item, implementa, compila, testa, passa por avaliadores com nota mínima e entrega o commit, o push e a PR — enquanto você acompanha e aprova.',
  midia: midia(
    'hero',
    'Demonstração do dev.kit: um work item do board vira uma task, o agente trabalha nos repositórios, o avaliador dá a nota e o trabalho segue para o commit e a Pull Request.',
    '/capturas/tela-agente.png',
  ),
};

export const BENEFICIOS: Beneficio[] = [
  {
    titulo: 'Da task à PR sem trocar de janela',
    texto:
      'Work item, repositórios, branch, compilação, testes, commit e push no mesmo lugar. O agente faz o trabalho repetitivo; você decide o que entra.',
  },
  {
    titulo: 'Qualidade com nota mínima',
    texto:
      'Cada entrega passa por avaliadores que dão nota de 0 a 100. O objetivo só é aceito com a nota mínima de cada um, dada depois da última alteração.',
  },
  {
    titulo: 'Horas lançadas sozinhas',
    texto:
      'As horas do dia vão para os work items trabalhados, rateadas pelos turnos do agente e divididas entre as tasks filhas — sem planilha no fim do dia.',
  },
  {
    titulo: 'Uso e qualidade medidos',
    texto:
      'O dashboard mostra, por máquina, turnos, falhas por causa, nota média dos avaliadores e retrabalho por objetivo cumprido — com o envio ligado só por opção.',
  },
];

export const PASSOS: Passo[] = [
  {
    titulo: 'Escolha o work item',
    texto: 'Uma US ou um Bug da iteração (ou uma tarefa avulsa), os repositórios e o que precisa ser feito.',
  },
  {
    titulo: 'O agente trabalha nos repositórios',
    texto:
      'O dev.kit clona cada repositório no branch da task, grava as regras e as skills do time e roda a CLI do agente, turno a turno.',
  },
  {
    titulo: 'O avaliador confere',
    texto: 'Um agente avaliador dá a nota. Abaixo da mínima, o que ele apontou volta para o agente corrigir.',
  },
  {
    titulo: 'Commit, push e Pull Request',
    texto: 'Com a nota na mínima, o trabalho segue para o commit, o push e a PR — e para a sua aprovação.',
  },
];

export const RECURSOS: Recurso[] = [
  {
    id: 'agente',
    titulo: 'O agente sobre a sua task',
    texto:
      'Escolha a US ou o Bug da iteração (ou uma tarefa avulsa), marque os repositórios e descreva o que precisa ser feito. O dev.kit clona cada repositório lado a lado, cria o branch, grava as regras, as skills e o conhecimento comum na pasta da task e roda a CLI do agente — claude, kiro, kimi ou glm — turno a turno, até o commit, o push e a Pull Request.',
    midia: midia('agente', 'O agente pede para executar a compilação, roda a suíte de testes e propõe a mensagem de commit para aprovação.', '/capturas/tela-agente.png'),
  },
  {
    id: 'fluxos',
    titulo: 'Fluxos com avaliadores e objetivo',
    texto:
      'Encadeie agentes num fluxo: um planeja, outro implementa e um avaliador dá nota ao trabalho. O objetivo só é aceito com a nota mínima de cada avaliador, dada depois da última alteração — e o fluxo segue sozinho para a aprovação.',
    midia: midia('fluxos', 'A aba Atividades com as tasks do fluxo em execução, aguardando interação e prontas para enviar.', '/capturas/tela-atividades.png'),
  },
  {
    id: 'replicacao',
    titulo: 'Cherry-pick e replicação de branch',
    texto:
      'Replique commits entre branches numa cópia temporária, sem trocar o branch do seu projeto: por cherry-pick ou por integração de diff. Conflito que o git não resolve abre o TortoiseGitMerge arquivo por arquivo.',
    midia: midia('replicacao', 'A tela de cherry-pick: o repositório, os branches de origem e destino e os commits marcados para replicar.', '/capturas/tela-cherry-pick.png'),
  },
  {
    id: 'agendamento',
    titulo: 'Agendamento sem ninguém na frente da tela',
    texto:
      'Agende uma task por cron: o serviço do Windows roda o mesmo pipeline às três da manhã, com as permissões do agente liberadas e os comandos pedidos executados automaticamente.',
    midia: midia('agendamento', 'Os agendamentos do board, com a expressão cron, a próxima execução e o botão Executar agora.', '/capturas/tela-agendamentos.png'),
  },
  {
    id: 'depurador',
    titulo: 'Depurador sem tela',
    texto:
      'O agente colhe evidência antes de teorizar: roda o alvo sob o depurador do dev.kit, para no ponto combinado e devolve a pilha e os valores de cada quadro — ou você depura na aba Depuração, com os mesmos pontos de parada.',
    midia: midia('depurador', 'O agente roda devcli depurar: o alvo para no ponto combinado e a saída mostra a pilha e os valores das variáveis.'),
  },
  {
    id: 'devcli',
    titulo: 'devcli: o dev.kit nas mãos do agente',
    texto:
      'O lado do board voltado ao agente: consulta e altera work items, comenta, anexa, planeja as tasks, lança as horas e mede o próprio uso. Usa o mesmo banco e as mesmas credenciais do app — o agente nunca vê o token — e o que grava simula por padrão.',
    midia: midia('devcli', 'O agente consulta um work item com devcli workitem e simula a mudança de estado antes de aplicar.', '/capturas/tela-principal.png'),
  },
  {
    id: 'board',
    titulo: 'Board e horas',
    texto:
      'Os work items da iteração em cartões, o plano técnico, os repositórios e o branch da task num lugar só — e as horas do dia lançadas nos itens trabalhados, rateadas pelos turnos do agente.',
    midia: midia('board', 'O work item no board com os repositórios da task, o branch de cada um e as horas lançadas no dia.', '/capturas/tela-user-stories.png'),
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
    titulo: 'Telemetria opcional e anônima',
    texto: 'O envio de uso é opt-in, identificado por um GUID da máquina, sem caminhos, código, prompts nem nomes de cliente.',
  },
];

export const PERGUNTAS: PerguntaFrequente[] = [
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
    pergunta: 'O que acontece com os dados do pedido de demonstração?',
    resposta:
      'Usamos o nome e o e-mail só para o contato. O pedido é apagado em até 365 dias, ou antes, quando você pedir.',
  },
];
