import type { SituacaoDoItem, TipoDeItem } from '../../api/tipos';
import type { VarianteDoSelo } from '../../componentes/Selo';

/** O nome de cada tipo de item (a vitrine, o catálogo da empresa, a aba Catálogos). */
export const NOME_DO_TIPO: Record<TipoDeItem, string> = { skill: 'Skill', plan: 'Tech Plan', fluxo: 'Fluxo' };

/** A situação de um item editável em relação à última release, com a cor do selo. */
export const SITUACAO_DO_ITEM: Record<SituacaoDoItem, { rotulo: string; variante: VarianteDoSelo }> = {
  novo: { rotulo: 'Novo', variante: 'info' },
  alterado: { rotulo: 'Alterado', variante: 'aviso' },
  publicado: { rotulo: 'Publicado', variante: 'sucesso' },
  'retirada-pendente': { rotulo: 'Retirada pendente', variante: 'aviso' },
  retirado: { rotulo: 'Retirado', variante: 'neutro' },
};
