import { useEffect, useRef, type ReactNode } from 'react';

/** Dialog nativo: foco contido, Escape e retorno ao acionador também no celular. */
export function DialogoMatrix({ titulo, contexto, fechar, children, rodape, rotulo = 'MATRIX · REVISÃO DA DEFINIÇÃO', classe = '' }: {
  titulo: string; contexto?: ReactNode; fechar: () => void; children: ReactNode; rodape?: ReactNode; rotulo?: string; classe?: string;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const anterior = document.activeElement as HTMLElement | null;
    const overflowAnterior = document.documentElement.style.overflow;
    document.documentElement.style.overflow = 'hidden';
    ref.current?.showModal();
    return () => { document.documentElement.style.overflow = overflowAnterior; anterior?.focus(); };
  }, []);
  return <dialog className={`mx-dialog mx ${classe}`} ref={ref} aria-label={titulo} onCancel={e => { e.preventDefault(); fechar(); }}>
    <header className="mx-dialog-top"><div><small>{rotulo}</small><h2>{titulo}</h2>{contexto && <div className="mx-dialog-context mx-chips">{contexto}</div>}</div>
      <button type="button" onClick={fechar} aria-label="Fechar painel">×</button></header>
    <div className="mx-dialog-body">{children}</div>
    {rodape && <footer className="mx-dialog-footer">{rodape}</footer>}
  </dialog>;
}
