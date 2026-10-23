import type { SituacaoDoItem, TipoDeItem } from '../../api/tipos';
import type { VarianteDoSelo } from '../../componentes/Selo';

/**
 * O nome de cada tipo de item (a vitrine, o catálogo da empresa, a aba Catálogos). O tipo `plan` é a Rule do app (o
 * antigo Tech Plan): o valor do contrato fica `plan`, só o rótulo mudou.
 */
export const NOME_DO_TIPO: Record<TipoDeItem, string> = { skill: 'Skill', plan: 'Rule', fluxo: 'Fluxo' };

/** O rótulo de cada tipo no plural (os chips de filtro da vitrine e da aba Catálogos). */
export const NOME_DO_TIPO_NO_PLURAL: Record<TipoDeItem, string> = { skill: 'Skills', plan: 'Rules', fluxo: 'Fluxos' };

/**
 * O rótulo de uma categoria (a tecnologia da vitrine, a categoria da aba Catálogos). O manifesto agrupa os plans em
 * "Tech Plans" — valor do contrato com o app (o mesmo `esperado.json` nos dois repositórios) —, e a tela mostra "Rules".
 */
export const rotuloDaCategoria = (categoria: string) => (categoria === 'Tech Plans' ? 'Rules' : categoria);

/** A situação de um item editável em relação à última release, com a cor do selo. */
export const SITUACAO_DO_ITEM: Record<SituacaoDoItem, { rotulo: string; variante: VarianteDoSelo }> = {
  novo: { rotulo: 'Novo', variante: 'info' },
  alterado: { rotulo: 'Alterado', variante: 'aviso' },
  publicado: { rotulo: 'Publicado', variante: 'sucesso' },
  'retirada-pendente': { rotulo: 'Retirada pendente', variante: 'aviso' },
  retirado: { rotulo: 'Retirado', variante: 'neutro' },
};
