import type { ReactNode } from 'react';

/**
 * O cartão à direita da lista (US #461 — "Nova empresa", "Novo usuário", "Novo catálogo"): o título e o formulário.
 * A página o põe dentro de um `.com-lateral`, que vira uma coluna só na tela estreita.
 */
export function PainelLateral({ id, titulo, children }: { id: string; titulo: string; children: ReactNode }) {
  return (
    <aside className="cartao painel-lateral" aria-labelledby={id}>
      <h2 id={id}>{titulo}</h2>
      {children}
    </aside>
  );
}
