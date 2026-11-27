export interface ReferenciaDeProposta { projeto: string; tela: string; versao: number; hash: string }
export interface DesignDoItem { alteraTela: boolean; revisao: number; propostas: ReferenciaDeProposta[] }
export interface ModeloDoProjeto { agente: string; modelo: string }

export interface ResumoDoProjeto {
  id: number; slug: string; nome: string; nivel: string; time: string; situacao: string; versao: number;
  atualizadoEm: string; papel: string; totalDeItens: number; itensConcluidos: number;
  itensEmExecucao: number; itensComPergunta: number; itensFalhos: number; progresso: number; temPendencias: boolean;
}
export interface EdicaoDoItem {
  versaoEsperada: number; titulo: string; descricao: string; fluxo: string; agente: string; modelo: string;
  dependencias: number[]; componentes: string[]; prioridade: number;
}

export interface ItemDoProjeto {
  numero: number; titulo: string; descricao: string; fluxo: string;
  repositorios: string[]; componentes: string[]; dependencias: number[];
  onda: number; workItemId: number; situacao: string; tipo: string;
  prioridade: number; horas: number; branch: string; atualizadoEm?: string;
  design?: DesignDoItem | null;
  agente?: string | null; modelo?: string | null;
}

export interface PoliticaDoProjeto {
  slug: string; politica: string; revisao: number; fluxoDaFinalizacao: string;
  adotadaEm: string; motivo: string; identidadeDoFluxo: string; revisaoDoFluxo: string;
  modeloDaFinalizacao?: ModeloDoProjeto | null;
}

export interface Projeto {
  id: number; slug: string; nome: string; nivel: string; time: string;
  repositorios: string[]; branch: string; semanaConvertida: boolean;
  definicao: string; anexos: string[]; autonomia: boolean; paralelismo: number;
  destinoDaPr: string; situacao: string; versao: number; autor: string;
  atualizadoEm: string; itens: ItemDoProjeto[]; podeApagar: boolean;
  featureId: number; epicId: number; iteracao: string; board: string;
  branchesDasTasks?: string[];
  baseDoBranch?: string | null; fluxoPadrao?: string | null;
  agente?: string | null; modelo?: string | null; modelos?: ModeloDoProjeto[] | null; politicaDeFinalizacao?: PoliticaDoProjeto | null;
  alteradoEm?: string | null; papel?: string;
  /** Projecao operacional; itens conserva o cadastro historico. */
  itensDoGrafo?: number[] | null; pendenciaDoGrafo?: string | null;
  skillDoPacote?: string | null; skillsDoPacote?: OpcaoDeSkillDoPacote[] | null;
  configuracaoDoPacote?: 'Debug' | 'Release' | null;
}

/** A tela recebe somente metadados; conteúdo, comando e argumentos ficam no host. */
export interface OpcaoDeSkillDoPacote { nome: string; tipo: 'somente-comando' | 'com-parametros' }
export interface MetadadosDoProdutor { nome: string; tipo: 'skill' | 'principal' | 'todos-os-executaveis' | 'previa' | 'nao-aplicavel' }
export interface PrevisaoRemotaDoPacote { repositorio: string; produtor: MetadadosDoProdutor; motivo: string; aplicavel: boolean; formato: string }
export interface ProdutorDoPacote { repositorio: string; formato: string; aplicavel: boolean; motivo: string; produtor?: string; falhou?: boolean }
export interface OrigensDoPacote { tipo: 'origens-artefato'; projeto: string; resultados: ProdutorDoPacote[]; comandoId?: string }
export interface ResultadoDoPacote {
  id: number; projetoId: number; tipo: string; repositorio: string; sha: string; situacao: string; link: string;
  validoAte: string | null; hash: string; tamanho: number; ambiente: string; detalhe: string; criadoEm: string; idRemoto: number;
  estado: string; ciclo?: { estado: string; etapa: string; branch: string; pedidoId: string; configuracaoDoPacote?: 'Debug' | 'Release' | null } | null;
  produtor?: MetadadosDoProdutor | null;
}
export interface PedidoDoPacote { id: string; projetoId: number; criadoEm: string; estado: string; resultados: ResultadoDoPacote[] }

/**
 * (controle-v4, item 2) Um artefato do projeto (Contracts/V1/ArtefatosV1.cs): o pacote .NET para baixar (o link assinado vence
 * em `expiraEm`) ou o preview da página estática. Nunca é produção.
 */
export interface Artefato {
  id: number; projetoId: number; tipo: 'pacote' | 'preview'; repositorio: string; sha: string; link: string;
  expiraEm: string | null; hash: string; tamanho: number; ambiente: string; autor: string; criadoEm: string; retidoAte?: string | null;
}

/** Os últimos artefatos de um projeto, para o card do Board (`GET /api/projetos/artefatos`). */
export interface ArtefatosDoProjeto { projetoId: number; slug: string; nome: string; artefatos: Artefato[] }

/** A ação no projeto pelo site: o texto do comando `projeto` com `acao` (sem ela, o texto é o Novo projeto). */
export interface AcaoNoProjetoRemoto { acao: 'artefato'; projeto: string; repositorio?: string; sha?: string; formato?: 'pacote' | 'preview'; retomar?: number; conferir?: boolean }

export interface OrigemDoArtefato {
  tipo: 'origem-artefato'; projeto: string; repositorio: string; branch: string; sha: string; formato: string;
}
