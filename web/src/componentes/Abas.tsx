import type { KeyboardEvent, ReactNode } from 'react';

export interface Aba {
  id: string;
  rotulo: string;
  /** A aba recém-chegada leva o selo "novo" ao lado do rótulo (a Design da US #669). */
  novo?: boolean;
}

/**
 * As abas de uma página (US #461 — Vitrine, Releases e Catálogos): o `tablist` do WAI-ARIA, com as setas e o
 * Home/End movendo entre as abas, e o painel da aba ativa. Quem decide a aba é a página (ela fica na URL), e não
 * o componente.
 */
export function Abas({ rotulo, abas, ativa, aoMudar, prefixo = 'aba', children }: {
  rotulo: string;
  abas: Aba[];
  ativa: string;
  aoMudar: (id: string) => void;
  prefixo?: string;
  children: ReactNode;
}) {
  function teclar(evento: KeyboardEvent<HTMLButtonElement>, indice: number) {
    const destino = { ArrowRight: indice + 1, ArrowLeft: indice - 1, Home: 0, End: abas.length - 1 }[evento.key];
    if (destino === undefined) return;
    evento.preventDefault();
    const aba = abas[(destino + abas.length) % abas.length];
    aoMudar(aba.id);
    document.getElementById(`${prefixo}-${aba.id}`)?.focus();
  }

  return (
    <>
      <div className="abas" role="tablist" aria-label={rotulo}>
        {abas.map((aba, indice) => (
          <button
            key={aba.id}
            id={`${prefixo}-${aba.id}`}
            className="aba"
            type="button"
            role="tab"
            aria-selected={aba.id === ativa}
            aria-controls={`${prefixo}-painel`}
            tabIndex={aba.id === ativa ? 0 : -1}
            onClick={() => aoMudar(aba.id)}
            onKeyDown={(e) => teclar(e, indice)}
          >
            {aba.rotulo}
            {aba.novo && <span className="recurso-novo" aria-hidden="true">novo</span>}
          </button>
        ))}
      </div>
      <div id={`${prefixo}-painel`} role="tabpanel" aria-labelledby={`${prefixo}-${ativa}`} className="painel-da-aba">
        {children}
      </div>
    </>
  );
}
