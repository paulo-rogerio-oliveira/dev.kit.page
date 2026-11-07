import { useSyncExternalStore } from 'react';

const CONSULTA = '(prefers-reduced-motion: reduce)';

function assinar(avisar: () => void): () => void {
  if (typeof window.matchMedia !== 'function') return () => {};
  const lista = window.matchMedia(CONSULTA);
  // O Safari antes do 14 (e dublês de teste) só têm o addListener legado na MediaQueryList.
  if (typeof lista.addEventListener !== 'function') {
    lista.addListener?.(avisar);
    return () => lista.removeListener?.(avisar);
  }
  lista.addEventListener('change', avisar);
  return () => lista.removeEventListener('change', avisar);
}

const agora = () => typeof window.matchMedia === 'function' && window.matchMedia(CONSULTA).matches;

/** Verdadeiro quando o sistema pede menos movimento — a mídia mostra só o poster. Acompanha a troca ao vivo. */
export function usePrefereMenosMovimento(): boolean {
  return useSyncExternalStore(assinar, agora, () => false);
}
