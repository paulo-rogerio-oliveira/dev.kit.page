import type { ReactNode } from 'react';

/**
 * Uma seção da landing: a âncora (o id, alvo do menu do topo), o título ligado por aria-labelledby,
 * o rótulo pequeno acima dele (US #405, "COMO FUNCIONA") e o subtítulo, ambos opcionais. O conteúdo
 * vem de fora.
 */
export function Secao({ id, titulo, rotulo, subtitulo, className, children }: {
  id: string;
  titulo: string;
  rotulo?: string;
  subtitulo?: string;
  className?: string;
  children: ReactNode;
}) {
  const idDoTitulo = `titulo-${id}`;
  return (
    <section id={id} className={`secao${className ? ` ${className}` : ''}`} aria-labelledby={idDoTitulo}>
      <header className="secao-cabecalho">
        {rotulo && <p className="secao-rotulo">{rotulo}</p>}
        <h2 id={idDoTitulo}>{titulo}</h2>
        {subtitulo && <p>{subtitulo}</p>}
      </header>
      {children}
    </section>
  );
}
