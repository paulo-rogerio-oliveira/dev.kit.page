import type { HTMLAttributes, ReactNode } from 'react';

/**
 * A MOLDURA de janela do app (US #467): a barra com os três pontos e o título ("dev.kit - Agente") por cima do
 * conteúdo. É a mesma da amostra do Board no hero e das mídias dos recursos — antes a barra só existia, escrita
 * à mão, dentro da amostra. A variante `escura` é a dos recursos, cuja mídia é a captura do app no tema escuro.
 */
export function Janela({ titulo, escura = false, className, children, ...resto }: {
  titulo: string;
  escura?: boolean;
  className?: string;
  children: ReactNode;
} & Omit<HTMLAttributes<HTMLDivElement>, 'title'>) {
  const classes = ['janela', escura ? 'janela-escura' : '', className ?? ''].filter(Boolean).join(' ');
  return (
    <div className={classes} {...resto}>
      <div className="janela-barra" aria-hidden="true"><span /><span /><span /><em>{titulo}</em></div>
      {children}
    </div>
  );
}
