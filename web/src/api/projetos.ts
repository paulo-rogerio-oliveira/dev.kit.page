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
