import type { ReactNode } from 'react';

/**
 * Uma seção da landing: a âncora (o id, alvo do menu do topo), o título ligado por aria-labelledby
 * e o subtítulo opcional. O conteúdo vem de fora.
 */
export function Secao({ id, titulo, subtitulo, className, children }: {
  id: string;
  titulo: string;
  subtitulo?: string;
  className?: string;
  children: ReactNode;
}) {
  const idDoTitulo = `titulo-${id}`;
  return (
    <section id={id} className={`secao${className ? ` ${className}` : ''}`} aria-labelledby={idDoTitulo}>
      <header className="secao-cabecalho">
        <h2 id={idDoTitulo}>{titulo}</h2>
        {subtitulo && <p>{subtitulo}</p>}
      </header>
      {children}
    </section>
  );
}
