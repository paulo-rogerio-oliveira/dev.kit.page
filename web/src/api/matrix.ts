/** Contrato matrix-v1, compartilhado com Contracts/V1/Matrix.cs e os clientes locais. */
export interface EixoMatrix { id: string; nome: string; orientacao: string }
export interface RelacaoMatrix {
  id: string; origem: string; destino: string; layers: string[];
  gatilho: string; motivo: string; propagacao: string; verificacao: string;
}
export interface MapaMatrix {
  nome: string; repositorios: string[]; componentes: EixoMatrix[]; layers: EixoMatrix[]; relacoes: RelacaoMatrix[];
}
export interface ResponsavelMatrix { id: string; item: number | null }
export interface CelulaMatrix {
  id: string; componente: string; layer: string; obrigacao: 'alterar' | 'preservar' | 'verificar';
  aplicavel: boolean; justificativa: string; origem: string; responsaveis: ResponsavelMatrix[];
  criterio: string; evidenciaEsperada: string;
}
export interface MatrizMatrix { revisaoDoMapa: number; componentes: EixoMatrix[]; layers: EixoMatrix[]; celulas: CelulaMatrix[] }
export interface RegistroMatrix {
  celula: string; revisaoDaDefinicao: number; responsavel: string; tipo: 'implementacao' | 'avaliacao';
  resultado: 'atendido' | 'parcial' | 'nao-atendido' | 'nao-aplicavel'; justificativa: string;
  evidencias: string[]; shas: Record<string, string>;
}
export interface VinculoMatrix { responsavel: string; sessao: string; item: number | null; maquina: string }
export interface DocumentoMatrix {
  contrato: number; aplicacao: string; tipo: 'mapa' | 'matriz' | 'registro' | 'vinculo'; id: string; projeto: string;
  mapa?: MapaMatrix | null; matriz?: MatrizMatrix | null; registro?: RegistroMatrix | null; vinculo?: VinculoMatrix | null;
}
export interface CabecalhoMatrix { aplicacao: string; tipo: DocumentoMatrix['tipo']; id: string; projeto: string; versao: number }
export interface RevisaoMatrix { versao: number; autor: string; em: string; documento: DocumentoMatrix }
export interface EdicaoMatrix { versaoEsperada: number; documento: DocumentoMatrix }
export interface ResultadoMatrix {
  aplicado: boolean; revisao: RevisaoMatrix; mudancas: { celulas: string[]; responsaveis: string[] };
}

export const rotaMatrix = (alvo: Pick<CabecalhoMatrix, 'aplicacao' | 'tipo' | 'id'>) =>
  `/api/matrix/${[alvo.aplicacao, alvo.tipo, alvo.id].map(encodeURIComponent).join('/')}`;
