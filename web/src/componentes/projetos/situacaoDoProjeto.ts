import type { EstadosDoWorkItem, EstadosNoBoard, ItemDoProjeto, Projeto, ResumoDoProjeto } from '../../api/projetos';

/**
 * (boards-estados, item 5) As regras puras do site para o projeto: a situação exibida, a ação de aprovar ou disparar que
 * cabe ao papel e à situação, a onda que espera o disparo no modo manual e o resumo do espelho no Azure Boards. Tudo vem
 * do contrato — situação, marca de disparo (`disparadoEm`), autonomia, itens e os campos do espelho —; nada é editado aqui.
 */

export interface SituacaoExibida { chave: string; rotulo: string }

const rotulos: Record<string, string> = {
  rascunho: 'Rascunho', aguardando: 'Aguardando aprovação', pausado: 'Pausado', fechando: 'Fechando', concluido: 'Concluído',
};

/** O aprovado se divide pela marca de disparo: "Aprovado · aguardando disparo" e "Em execução"; o resto é a situação do contrato. */
export function situacaoExibida(situacao: string, disparado: boolean): SituacaoExibida {
  if (situacao === 'aprovado')
    return disparado ? { chave: 'em-execucao', rotulo: 'Em execução' } : { chave: 'aguardando-disparo', rotulo: 'Aprovado · aguardando disparo' };
  return { chave: situacao, rotulo: rotulos[situacao] ?? situacao };
}

/**
 * O projeto já foi disparado: a marca do host ou, do dev.kit anterior à marca, algum item que saiu do planejado — a mesma
 * regra com que o host dá a marca, na migração, aos aprovados que já têm item iniciado.
 */
export const foiDisparado = (disparadoEm: string | null | undefined, algumIniciado: boolean) => !!disparadoEm || algumIniciado;

export const disparadoNoDetalhe = (p: Pick<Projeto, 'disparadoEm' | 'itens'>) =>
  foiDisparado(p.disparadoEm, p.itens.some(i => i.situacao !== 'planejado'));

export const situacaoDoDetalhe = (p: Projeto) => situacaoExibida(p.situacao, disparadoNoDetalhe(p));

export const situacaoDoResumo = (r: ResumoDoProjeto) => situacaoExibida(r.situacao,
  foiDisparado(r.disparadoEm, r.itensConcluidos + r.itensEmExecucao + r.itensComPergunta + r.itensFalhos > 0));

const terminais = new Set(['integrado', 'concluido-manual', 'cancelado']);

/**
 * Sem autonomia, cada onda depois da primeira espera o próprio disparo: é a primeira onda com item pendente, depois da
 * primeira do plano, com todos os itens ainda planejados. Nula com autonomia (o Orquestrador segue sozinho), antes do
 * disparo (o Disparar projeto abre a primeira) e com a onda já em andamento.
 */
export function ondaQueEspera(p: Projeto, itens: ItemDoProjeto[]): number | null {
  if (p.situacao !== 'aprovado' || p.autonomia || !itens.length || !disparadoNoDetalhe(p)) return null;
  const pendentes = itens.filter(i => !terminais.has(i.situacao));
  if (!pendentes.length) return null;
  const onda = Math.min(...pendentes.map(i => i.onda));
  return onda > Math.min(...itens.map(i => i.onda)) && itens.filter(i => i.onda === onda).every(i => i.situacao === 'planejado') ? onda : null;
}

export type AcaoRemota = 'aprovar' | 'disparar';
export interface AcaoDoProjeto { acao: AcaoRemota; rotulo: string }

/**
 * A ação do site: só para dono e editor (o leitor não vê ação). "Aprovar plano" no aguardando aprovação; "Disparar
 * projeto" no aprovado sem disparo; no modo manual, "Disparar onda N" quando a onda espera. Nas outras situações, nenhuma.
 */
export function acaoDoProjeto(p: Projeto, itens: ItemDoProjeto[]): AcaoDoProjeto | null {
  if (!['dono', 'editor'].includes(p.papel ?? '')) return null;
  if (p.situacao === 'aguardando') return { acao: 'aprovar', rotulo: 'Aprovar plano' };
  if (p.situacao !== 'aprovado') return null;
  if (!disparadoNoDetalhe(p)) return { acao: 'disparar', rotulo: 'Disparar projeto' };
  const onda = ondaQueEspera(p, itens);
  return onda === null ? null : { acao: 'disparar', rotulo: `Disparar onda ${onda}` };
}

const tiposDoPai: Record<string, string> = { epic: 'Epic', feature: 'Feature', requisito: 'Requisito' };
const tiposDosFilhos: Record<string, string> = { epic: 'Feature', feature: 'requisito ou Bug', requisito: 'Task' };

/** O nível do pai gravado no projeto; vazio, ausente ou desconhecido é a Feature (o comportamento de antes). */
export function nivelDoPai(nivelNoBoard: string | null | undefined) {
  const nivel = (nivelNoBoard ?? '').trim().toLowerCase();
  return nivel in tiposDoPai ? nivel : 'feature';
}

/** "A", "A e B", "A, B e C". */
export const listar = (nomes: string[]) => nomes.length < 2 ? nomes.join('') : `${nomes.slice(0, -1).join(', ')} e ${nomes.at(-1)}`;

export interface EspelhoNoResumo {
  /** O tipo do pai pelo nível: Epic, Feature ou Requisito. */
  tipoDoPai: string;
  /** O id do pai (o `featureId`, em qualquer nível); 0 enquanto o work item não existe. */
  id: number;
  /** O complemento: os tipos dos itens com o pai criado, ou quando o pai será criado ou vinculado. */
  detalhe: string;
}

/** O espelho do projeto no resumo, só leitura; nulo sem Azure Boards ("nao") ou sem a escolha ainda configurada. */
export function espelhoNoResumo(p: Projeto): EspelhoNoResumo | null {
  if (!p.board || p.board === 'nao') return null;
  const nivel = nivelDoPai(p.nivelNoBoard);
  const tipos = [...new Set(p.itens.map(i => i.tipo?.trim() ?? '').filter(t => t.length > 0))];
  const detalhe = p.featureId > 0 ? `itens ${tipos.length ? listar(tipos) : tiposDosFilhos[nivel]}`
    : p.board === 'vincular' ? 'vínculo pendente' : `${nivel === 'feature' ? 'criada' : 'criado'} na aprovação`;
  return { tipoDoPai: tiposDoPai[nivel], id: p.featureId, detalhe };
}

export type ConjuntoDoEspelho = 'pai' | 'itens';
export type MomentoDoEstado = 'inicial' | 'execucao' | 'finalizacao';
export interface EstadoNoEspelho { nome: string; configurado: boolean }

/** Os nomes de referência do padrão (D1): a finalização dos itens é a categoria Completed, a do pai, a Resolved. */
const padroes: Record<ConjuntoDoEspelho, Record<MomentoDoEstado, string>> = {
  pai: { inicial: 'New', execucao: 'Active', finalizacao: 'Resolved' },
  itens: { inicial: 'New', execucao: 'Active', finalizacao: 'Closed' },
};
const momentos: MomentoDoEstado[] = ['inicial', 'execucao', 'finalizacao'];

/** Os três estados de um conjunto: o nome configurado no dev.kit ou, vazio, o padrão pela categoria (indicado como tal). */
export function estadosDoConjunto(conjunto: ConjuntoDoEspelho, estados: EstadosNoBoard | null | undefined): EstadoNoEspelho[] {
  const configurados: EstadosDoWorkItem = estados?.[conjunto] ?? {};
  return momentos.map(momento => {
    const nome = configurados[momento]?.trim() ?? '';
    return nome ? { nome, configurado: true } : { nome: padroes[conjunto][momento], configurado: false };
  });
}

/** O estado configurado para a fase do item no espelho: o planejado no inicial, o integrado na finalização, o resto na execução. */
export function estadoDoItem(item: ItemDoProjeto, estados: EstadosNoBoard | null | undefined) {
  const [inicial, execucao, finalizacao] = estadosDoConjunto('itens', estados);
  return item.situacao === 'planejado' ? inicial.nome : item.situacao === 'integrado' ? finalizacao.nome : execucao.nome;
}

/** Os work items que a aprovação criou no board: os itens e, quando o projeto cria o pai, ele também. */
export function workItemsCriados(p: Projeto, itens: ItemDoProjeto[]) {
  return itens.filter(i => i.workItemId > 0).length + (p.board === 'criar' && p.featureId > 0 ? 1 : 0);
}
