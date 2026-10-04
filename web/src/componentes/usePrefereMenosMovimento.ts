import { useSyncExternalStore } from 'react';

const CONSULTA = '(prefers-reduced-motion: reduce)';

function assinar(avisar: () => void): () => void {
  if (typeof window.matchMedia !== 'function') return () => {};
  const lista = window.matchMedia(CONSULTA);
  lista.addEventListener('change', avisar);
  return () => lista.removeEventListener('change', avisar);
}

const agora = () => typeof window.matchMedia === 'function' && window.matchMedia(CONSULTA).matches;

/** Verdadeiro quando o sistema pede menos movimento — a mídia mostra só o poster. Acompanha a troca ao vivo. */
export function usePrefereMenosMovimento(): boolean {
  return useSyncExternalStore(assinar, agora, () => false);
}
