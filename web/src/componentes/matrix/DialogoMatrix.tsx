import { useEffect, useRef, type ReactNode } from 'react';

/** Dialog nativo: foco contido, Escape e retorno ao acionador também no celular. */
export function DialogoMatrix({ titulo, contexto, fechar, children, rodape }: {
  titulo: string; contexto?: string; fechar: () => void; children: ReactNode; rodape?: ReactNode;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const anterior = document.activeElement as HTMLElement | null;
    const overflowAnterior = document.documentElement.style.overflow;
    document.documentElement.style.overflow = 'hidden';
    ref.current?.showModal();
    return () => { document.documentElement.style.overflow = overflowAnterior; anterior?.focus(); };
  }, []);
  return <dialog className="mx-dialog mx" ref={ref} aria-label={titulo} onCancel={e => { e.preventDefault(); fechar(); }}>
    <header className="mx-dialog-top"><div><small>MATRIX · REVISÃO DA DEFINIÇÃO</small><h2>{titulo}</h2>{contexto && <p className="mx-dialog-context">{contexto}</p>}</div>
      <button type="button" onClick={fechar} aria-label="Fechar painel">×</button></header>
    <div className="mx-dialog-body">{children}</div>
    {rodape && <footer className="mx-dialog-footer">{rodape}</footer>}
  </dialog>;
}
