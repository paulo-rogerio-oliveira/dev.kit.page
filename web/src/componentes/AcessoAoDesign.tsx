import type { ProjetoDeDesign } from '../api/tipos';
import { AcessoAoProjeto } from './AcessoAoProjeto';

/** Mantém a entrada do design sobre o painel de acesso compartilhado. */
export function AcessoAoDesign(props: { token: string; projeto: ProjetoDeDesign; tratar: (falha: unknown) => void }) {
  return <AcessoAoProjeto key={props.projeto.id} {...props} recurso="design" />;
}
