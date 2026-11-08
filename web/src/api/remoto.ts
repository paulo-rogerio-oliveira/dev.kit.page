/** Contrato de leitura da fase 1, espelhado de Contracts/V1/RemotoV1.cs. */
export interface MaquinaRemota {
  maquinaId: string;
  nome: string;
  versaoDevKit: string;
  origem: string;
  appAberto: boolean;
  estadoDoAgente: string;
  turnoEmAndamento: boolean;
  conectada: boolean;
  ultimoHeartbeatEm: string | null;
  retratoGeradoEm: string | null;
  /** O serviço da máquina tem o client credentials ativo (item 9 do remote-control-v3); ausente na API antiga. */
  vinculada?: boolean;
}

export interface RetratoDaMaquina {
  maquina: MaquinaRemota;
  recebidoEm: string;
  retrato: {
    contrato: number;
    maquinaId: string;
    geradoEm: string;
    projetos: Array<{
      slug: string; nome: string; situacao: string; ondaAtual: number;
      itens: Array<{ numero: number; titulo: string; situacao: string; onda: number }> | null;
    }> | null;
    fluxos: Array<{
      id: string; nome: string; etapa: string; situacao: string;
      projeto?: string | null; item?: number | null; agente?: string | null; desdeEm?: string | null;
    }> | null;
    pendencias: PendenciaRemota[] | null;
  };
}

export interface PendenciaRemota {
  id: string; tipo: string; titulo: string; pergunta?: string | null; opcoes?: string[] | null;
  jaTentei?: string | null; projeto?: string | null; item?: number | null; fluxo?: string | null;
  snapshot?: SnapshotDaDecisao | null;
}
export interface SnapshotDaDecisao {
  origem: string; destino: string; sha: string;
  notas: Array<{ avaliador: string; nota: number; minima: number }>;
  testesAprovados: number; testesFalhos: number; regressaoConcluida: boolean;
  arquivos: number; commits: number; alteracoes: string; capturas: Array<{ titulo: string; url: string }>;
}
export interface RespostaDaDecisao {
  id: number; maquinaId: string; pendenciaId: string; snapshotId: string; acao: string; opcao: string | null;
  origem: string | null; destino: string | null; sha: string | null; em: string; segundoFator: boolean; estado: string;
}
/** `dispensada` é campo novo (correções do board remoto): o card fechado no site, só com `?dispensadas=true`. */
export interface DecisaoPendente { pendencia: PendenciaRemota; snapshotId: string; bloqueio: string | null; resposta: RespostaDaDecisao | null; dispensada?: boolean }
export interface ResponderDecisao { snapshotId: string; acao: 'responder' | 'aprovar' | 'ajuste'; opcao?: string; codigo?: string }
/** A dispensa do card (Contracts/V1/DecisoesV1.cs): só no site, a task da máquina não muda. */
export interface DispensaDaPendencia { maquinaId: string; pendenciaId: string; dispensada: boolean; em: string }

/** Conversa v1: o estado é confirmado pela API, nunca inferido do heartbeat. */
export interface ComandoRemoto {
  id: string; maquinaId: string; tipo: string; texto: string; dispositivo: string;
  estado: 'pendente' | 'na-fila' | 'entregue' | 'respondido' | 'concluido' | 'recusado' | 'expirado' | 'falhou';
  criadoEm: string; expiraEm: string; entregueEm: string | null; concluidoEm: string | null; erro: string;
}
/** (remote-control-v3, item 4) Os comandos de criação pelo site: o formulário vai em JSON no texto. */
export type TipoDeCriacao = 'projeto' | 'design';
/** O formulário do Novo projeto (Contracts/V1/CriacaoRemotaV1.cs): os repositórios pelo nome do catálogo da máquina. */
export interface NovoProjetoRemoto { nome: string; repositorios: string[]; definicao?: string; nivel?: 'usuario' | 'time' | 'empresa' }
/** O formulário do Novo design: o repositório pelo nome do catálogo. */
export interface NovoDesignRemoto { repositorio: string }

/**
 * O envio: a mensagem ao Orquestrador, o Ligar/Reiniciar que o serviço da máquina executa (item 8), o pedido do catálogo dos
 * atalhos do chat (o handshake, remote-control-v3) ou a criação de projeto e de projeto de design (item 4).
 */
export interface ComandoNovo {
  id: string; maquinaId: string; tipo: 'mensagem' | 'ligar' | 'reiniciar' | 'catalogo' | TipoDeCriacao; texto: string; dispositivo: 'celular' | 'navegador';
  /** O usuário confirmou reiniciar com o turno em andamento (sem isto, a API responde 409 `turno-em-andamento`). */
  confirmarReinicio?: boolean;
  /** (correções do board remoto) O fluxo do catálogo escolhido no `/`, o agente `cli:modelo` e os parâmetros dele. */
  fluxo?: number; agente?: string; parametros?: Record<string, string>;
}
/** Uma imagem da fala: o link da API de design, ou o aviso do porquê ela não está no site. */
export interface AnexoDaFala { titulo: string; url?: string | null; aviso?: string | null }
export interface RespostaRemota {
  id: string; maquinaId: string; comandoId: string | null; tipo: string; texto: string; geradaEm: string; recebidaEm: string;
}
/** A fala do chat do dev.kit (o recurso `falas-do-desktop`): quem falou e onde a fala nasceu. */
export interface FalaRemota {
  id: string; maquinaId: string; autor: 'dev' | 'orquestrador' | string; origem: 'desktop' | 'remoto' | string; texto: string;
  comandoId: string | null; ditaEm: string; recebidaEm: string;
  /** Campo novo: as imagens citadas na fala (a versão de design gravada). */
  anexos?: AnexoDaFala[] | null;
}
export interface ConversaRemota {
  /** `falas` é campo novo do contrato v1: a API antiga não o manda. */
  comandos: ComandoRemoto[]; respostas: RespostaRemota[]; ultimoEvento: number; falas?: FalaRemota[] | null;
}
/** O catálogo da máquina (contrato seção 5, Contracts/V1/CatalogoRemotoV1.cs): o que os atalhos / # @ do chat oferecem. */
export interface CatalogoRemoto {
  contrato: number; maquinaId: string; geradoEm: string; comandoId: string | null;
  workItems: Array<{ id: number; titulo: string; tipo?: string | null; estado?: string | null }> | null;
  repositorios: Array<{ nome: string; branchPadrao?: string | null }> | null;
  /** `id`, `parametros` e `agentePadrao` são campos novos (correções do board remoto): o dev.kit antigo não os manda. */
  fluxos: Array<FluxoDoCatalogo> | null;
  projetos: Array<{ slug: string; nome: string; situacao?: string | null }> | null;
  /** `repositorio` é campo novo (item 4) e `miniatura` (o link da última versão no site), das correções do board remoto. */
  designs: Array<{ nome: string; telas: number; repositorio?: string | null; miniatura?: string | null }> | null;
  /** Campos novos: os agentes `cli` ou `cli:modelo` da máquina e os membros do time (o parâmetro de usuário). */
  agentes?: string[] | null;
  membros?: string[] | null;
}
export interface FluxoDoCatalogo {
  nome: string; descricao?: string | null; id?: number | null; parametros?: ParametroDoFluxo[] | null; agentePadrao?: string | null;
}
/** Um parâmetro do fluxo: o `{NOME}`, o tipo do campo, as opções, a marca de usuário do board, se é obrigatório e o padrão. */
export interface ParametroDoFluxo {
  nome: string; rotulo?: string | null; tipo: 'texto' | 'lista' | 'sim-nao' | string; opcoes?: string[] | null; usuario?: boolean;
  obrigatorio?: boolean; padrao?: string | null;
}
/** O último catálogo recebido da máquina, com a data em que a API o recebeu (vale também com o dev.kit fechado). */
export interface CatalogoDaMaquina { maquinaId: string; recebidoEm: string; catalogo: CatalogoRemoto }
export interface EventoDaConversa {
  id: number; tipo: string; maquinaId: string; comando: ComandoRemoto | null; resposta: RespostaRemota | null; em: string;
  fala?: FalaRemota | null;
}
