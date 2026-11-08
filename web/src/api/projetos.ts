export interface ItemDoProjeto {
  numero: number; titulo: string; descricao: string; fluxo: string;
  repositorios: string[]; componentes: string[]; dependencias: number[];
  onda: number; workItemId: number; situacao: string; tipo: string;
  prioridade: number; horas: number; branch: string; atualizadoEm?: string;
}

export interface Projeto {
  id: number; slug: string; nome: string; nivel: string; time: string;
  repositorios: string[]; branch: string; semanaConvertida: boolean;
  definicao: string; anexos: string[]; autonomia: boolean; paralelismo: number;
  destinoDaPr: string; situacao: string; versao: number; autor: string;
  atualizadoEm: string; itens: ItemDoProjeto[]; podeApagar: boolean;
  featureId: number; epicId: number; iteracao: string; board: string;
  branchesDasTasks?: string[];
}

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
export interface AcaoNoProjetoRemoto { acao: 'artefato'; projeto: string; repositorio?: string; sha?: string }
