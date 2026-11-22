import { createContext, useContext, useState } from 'react';

export interface RascunhoDeArtefato {
  repositorio: string;
  formato: '' | 'pacote' | 'preview';
  sha: string;
}

/** Somente escolhas de edição em memória; não guarda origem confirmada, recibo ou autorização. */
export const ContextoDosRascunhosDeArtefatos = createContext<Map<string, RascunhoDeArtefato> | null>(null);

export function useRascunhoDeArtefato(maquina: string, projeto: string, primeiroRepositorio: string) {
  const memoria = useContext(ContextoDosRascunhosDeArtefatos);
  const chave = JSON.stringify([maquina, projeto]);
  const ler = (): RascunhoDeArtefato => (maquina && memoria?.get(chave)) || { repositorio: primeiroRepositorio, formato: '', sha: '' };
  const [estado, setEstado] = useState(() => ({ memoria, chave, valor: ler() }));
  // Outro host/projeto ou outra sessão nunca recebe o estado local da identidade anterior.
  const valor = estado.memoria === memoria && estado.chave === chave ? estado.valor : ler();
  function alterar(alteracoes: Partial<RascunhoDeArtefato>) {
    const novo = { ...valor, ...alteracoes };
    if (maquina && memoria) memoria.set(chave, novo);
    setEstado({ memoria, chave, valor: novo });
  }
  return [valor, alterar] as const;
}
