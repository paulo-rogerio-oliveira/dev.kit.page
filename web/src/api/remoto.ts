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
      projeto?: string | null; item?: number | null; agente?: string | null; desdeEm?: string | null; conversaId?: string | null;
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
export interface DecisaoPendente { pendencia: PendenciaRemota; snapshotId: string; bloqueio: string | null; resposta: RespostaDaDecisao | null }
export interface ResponderDecisao { snapshotId: string; acao: 'responder' | 'aprovar' | 'ajuste'; opcao?: string; codigo?: string }

/** Conversa v1: o estado é confirmado pela API, nunca inferido do heartbeat. */
export interface ComandoRemoto {
  conversaId?: string | null;
  id: string; maquinaId: string; tipo: string; texto: string; dispositivo: string;
  estado: 'pendente' | 'na-fila' | 'entregue' | 'respondido' | 'concluido' | 'recusado' | 'expirado' | 'falhou';
  criadoEm: string; expiraEm: string; entregueEm: string | null; concluidoEm: string | null; erro: string;
}
/**
 * O envio: a mensagem ao Orquestrador, o Ligar/Reiniciar que o serviço da máquina executa (item 8) ou o pedido do catálogo dos
 * atalhos do chat (o handshake, remote-control-v3).
 */
export interface ComandoNovo {
  id: string; maquinaId: string; tipo: 'mensagem' | 'ligar' | 'reiniciar' | 'catalogo' | 'conversa-task' | 'mensagem-task'; texto: string; dispositivo: 'celular' | 'navegador';
  conversaId?: string;
  /** O usuário confirmou reiniciar com o turno em andamento (sem isto, a API responde 409 `turno-em-andamento`). */
  confirmarReinicio?: boolean;
}
export interface RespostaRemota {
  conversaId?: string | null;
  id: string; maquinaId: string; comandoId: string | null; tipo: string; texto: string; geradaEm: string; recebidaEm: string;
}
/** A fala do chat do dev.kit (o recurso `falas-do-desktop`): quem falou e onde a fala nasceu. */
export interface FalaRemota {
  conversaId?: string | null;
  id: string; maquinaId: string; autor: 'dev' | 'orquestrador' | string; origem: 'desktop' | 'remoto' | string; texto: string;
  comandoId: string | null; ditaEm: string; recebidaEm: string;
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
  /** `agente` (US #867): o agente padrão do desenho, `cli:modelo`; ausente é o das Configurações da máquina. */
  fluxos: Array<{ nome: string; descricao?: string | null; agente?: string | null }> | null;
  projetos: Array<{ slug: string; nome: string; situacao?: string | null }> | null;
  designs: Array<{ nome: string; telas: number }> | null;
  /** US #867: os modelos que o dev escolhe DENTRO de um fluxo (campo novo: o dev.kit antigo não o manda). */
  modelos?: Array<{ valor: string; rotulo: string }> | null;
}
/** O último catálogo recebido da máquina, com a data em que a API o recebeu (vale também com o dev.kit fechado). */
export interface CatalogoDaMaquina { maquinaId: string; recebidoEm: string; catalogo: CatalogoRemoto }
export interface EventoDaConversa {
  id: number; tipo: string; maquinaId: string; comando: ComandoRemoto | null; resposta: RespostaRemota | null; em: string;
  fala?: FalaRemota | null;
}
