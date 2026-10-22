import type { ReactNode } from 'react';

/**
 * As cores do selo: o estado neutro, o bom (ativo, atual), o que pede atenção, a informação, o papel do usuário e o
 * vermelho da marca (o gestor, o defeito — a paleta das propostas da versão 5).
 */
export type VarianteDoSelo = 'neutro' | 'sucesso' | 'aviso' | 'info' | 'papel' | 'marca';

/**
 * O selo (badge) das tabelas e cartões (US #461): o plano, o papel, a situação, o tipo do item. `ponto` põe a
 * bolinha antes do texto (o "● Ativo" da lista de usuários).
 */
export function Selo({ variante = 'neutro', ponto = false, children }: { variante?: VarianteDoSelo; ponto?: boolean; children: ReactNode }) {
  return (
    <span className={`insignia insignia-${variante}`}>
      {ponto && <span className="insignia-ponto" aria-hidden="true" />}
      {children}
    </span>
  );
}
