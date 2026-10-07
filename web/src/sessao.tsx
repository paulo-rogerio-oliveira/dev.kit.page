import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import type { LoginResponse, Papel } from './api/tipos';

/** Onde o token fica: a aba do navegador (fecha a aba, acaba a sessão). */
export const CHAVE_DA_SESSAO = 'devkitpage.sessao';

/** Por que a sessão acabou — a tela de login explica. */
export type MotivoDaSaida = 'expirou' | 'saiu' | null;

interface ContextoDaSessao {
  sessao: LoginResponse | null;
  motivo: MotivoDaSaida;
  entrar: (resposta: LoginResponse) => void;
  sair: (motivo?: MotivoDaSaida) => void;
}

const Contexto = createContext<ContextoDaSessao | null>(null);

/**
 * O papel da sessão (US #381): o que a API devolveu no login. Uma sessão gravada antes do papel
 * existir (sem o campo) era do admin — o único usuário de então.
 */
export function papelDa(sessao: LoginResponse): Papel {
  return sessao.papel ?? (sessao.ehAdmin ? 'admin' : 'gestor');
}

/** A sessão gravada, se ainda não venceu. */
export function sessaoGravada(agora = Date.now()): LoginResponse | null {
  try {
    const texto = sessionStorage.getItem(CHAVE_DA_SESSAO);
    if (!texto) return null;
    const sessao = JSON.parse(texto) as LoginResponse;
    return new Date(sessao.expiraEm).getTime() > agora ? sessao : null;
  } catch {
    return null;
  }
}

export function SessaoProvider({ children }: { children: ReactNode }) {
  // Uma sessão gravada e VENCIDA vira "expirou" já na abertura: o login diz por que pediu a senha.
  const [sessao, setSessao] = useState<LoginResponse | null>(() => sessaoGravada());
  const [motivo, setMotivo] = useState<MotivoDaSaida>(() =>
    sessaoGravada() === null && sessionStorage.getItem(CHAVE_DA_SESSAO) ? 'expirou' : null);

  const entrar = useCallback((resposta: LoginResponse) => {
    sessionStorage.setItem(CHAVE_DA_SESSAO, JSON.stringify(resposta));
    setMotivo(null);
    setSessao(resposta);
  }, []);

  const sair = useCallback((novoMotivo: MotivoDaSaida = 'saiu') => {
    sessionStorage.removeItem(CHAVE_DA_SESSAO);
    setMotivo(novoMotivo);
    setSessao(null);
  }, []);

  // O token vence no relógio: a sessão acaba na hora, e não no próximo clique.
  useEffect(() => {
    if (!sessao) return;
    const restante = new Date(sessao.expiraEm).getTime() - Date.now();
    const relogio = window.setTimeout(() => sair('expirou'), Math.max(0, restante));
    return () => window.clearTimeout(relogio);
  }, [sessao, sair]);

  const valor = useMemo(() => ({ sessao, motivo, entrar, sair }), [sessao, motivo, entrar, sair]);
  return <Contexto.Provider value={valor}>{children}</Contexto.Provider>;
}

export function useSessao(): ContextoDaSessao {
  const contexto = useContext(Contexto);
  if (!contexto) throw new Error('useSessao fora do SessaoProvider');
  return contexto;
}
