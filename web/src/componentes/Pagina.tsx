import type { ReactNode } from 'react';

/**
 * O corpo de uma página logada (US #461): o título, o subtítulo — ao lado do título (`emLinha`, o "1 empresa · 10
 * assentos contratados") ou embaixo dele (o texto do catálogo) — e, à direita, a ação ou a pílula da página.
 * O `Topo` fica fora: ele é o mesmo em todas.
 */
export function Pagina({ titulo, subtitulo, acao, emLinha = false, children }: {
  titulo: string;
  subtitulo?: ReactNode;
  acao?: ReactNode;
  emLinha?: boolean;
  children: ReactNode;
}) {
  return (
    <main className="painel">
      <header className={`cabecalho-da-pagina${emLinha ? ' cabecalho-em-linha' : ''}`}>
        <div className="cabecalho-textos">
          <h1>{titulo}</h1>
          {subtitulo && <p className="subtitulo">{subtitulo}</p>}
        </div>
        {acao && <div className="cabecalho-acao">{acao}</div>}
      </header>
      {children}
    </main>
  );
}
