/** Extensão opcional do retrato v1. Ausência não anuncia suporte; resultados não concedem acesso aos downloads. */
export interface ArtefatosLocais {
  contrato: number; operacoesDisponiveis: boolean; aviso: string; artefatos: ArtefatoLocal[];
}
export interface FonteDoArtefato {
  id: string; nome: string; branch: string; sha: string; branchBase: string | null; shaBase: string | null;
}
export interface AlvoDoArtefato { repositorio: string; nome: string; receita: string; framework: string | null; runtime: string | null;
  saida?: string | null; assembly?: string | null }
export interface DadosDoConjunto {
  politicaDePins: string;
  pins: Array<{ consumidor: string; papel: string; repositorio: string; declarado: string; capturado: string;
    arvoreDeclarada: string; arvoreCapturada: string; raiz: string; politica: string }>;
  exclusoes: Array<{ repositorio: string; caminho: string; motivo: string }>;
  opcoes: { repositorioPrincipal: string; formatoSolicitado: string;
    previewRemoto: { app: string; grupo: string; assinatura: string; tenant: string } } | null;
}
export interface ArtefatoLocal {
  id: number; situacao: string; diagnostico: string; criadoEm: string; terminadoEm: string | null;
  conjuntoHash: string | null; fontes: FonteDoArtefato[]; alvos: AlvoDoArtefato[]; hash: string; tamanho: number;
  comando: { id: number | null; recibo: string; inicio: string | null; fim: string | null;
    motivoTermino: string | null; exit: number | null; logParcial: string } | null;
  previews: PreviewLocal[];
  publicacao?: { idRemoto: number; tipo: 'pacote' | 'preview'; repositorio: string; sha: string; hash: string; tamanho: number } | null;
  pacote?: { repositorio: string; sha: string; versao: string; hash: string; tamanho: number;
    idRemoto: number; validoAte: string | null } | null;
  detalhes?: DadosDoConjunto | null;
}
export interface PreviewLocal {
  receita: string; aplicacao: string; ciclo: string; versao: number; pedido: string; situacao: string;
  nestaMaquina: boolean; url: string | null; diagnostico: string; atualizadoEm: string;
  prontidao: { front: boolean; api: boolean; dependencias: boolean; jornada: boolean };
  componentes: Array<{ id: string; repositorio: string; situacao: string; porta: number | null;
    prontoEm: string | null; diagnostico: string; logParcial: string }>;
  abertura?: { id: string; situacao: string; diagnostico: string; atualizadoEm: string } | null;
}
/** O alvo é o conjunto consultado no retrato da máquina; nunca uma URL/SHA escolhido no dispositivo remoto. */
export interface OperacaoDoPreviewRemoto {
  acao: 'preview-local'; projeto: string; artefato: number; receita: string; aplicacao: string;
  operacao: 'iniciar' | 'consultar' | 'abrir' | 'parar'; versao?: number; conjunto: string;
}
