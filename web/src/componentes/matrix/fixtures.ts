import type { DocumentoMatrix, RevisaoMatrix } from '../../api/matrix';

export const documentoDeTeste = (): DocumentoMatrix => ({
  contrato: 1, aplicacao: 'demo', tipo: 'matriz', id: 'matrix', projeto: 'matrix',
  matriz: { revisaoDoMapa: 1, componentes: [{ id: 'editor', nome: 'Editor', orientacao: 'Interface' }],
    layers: [{ id: 'page', nome: 'Page', orientacao: '' }, { id: 'api', nome: 'API', orientacao: '' }],
    celulas: [
      { id: 'imp-page', componente: 'editor', layer: 'page', obrigacao: 'alterar', aplicavel: true,
        justificativa: 'Editar no celular', origem: 'mapa r1', responsaveis: [{ id: 'item-4', item: 4 }], criterio: 'Validar em 390 px', evidenciaEsperada: 'Captura real' },
      { id: 'imp-api', componente: 'editor', layer: 'api', obrigacao: 'preservar', aplicavel: true,
        justificativa: 'Preservar contrato', origem: 'mapa r1', responsaveis: [{ id: 'item-2', item: 2 }], criterio: 'API compatível', evidenciaEsperada: 'Teste da API' },
    ] },
});
export const revisaoDeTeste = (documento = documentoDeTeste(), versao = 1): RevisaoMatrix => ({
  documento, versao, autor: 'demonstracao', em: '2026-11-10T10:00:00Z',
});
