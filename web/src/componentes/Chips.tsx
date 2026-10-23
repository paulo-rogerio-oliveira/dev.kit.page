export interface Chip<T extends string> {
  valor: T;
  rotulo: string;
  /** A contagem ao lado do rótulo ("Todos 58"); ausente, não aparece. */
  contagem?: number;
}

/**
 * Os chips de filtro (US #461 — Todos/Skills/Rules/Fluxos): um grupo de botões em que só um vale, com a
 * contagem de cada um. O `aria-pressed` diz ao leitor de tela qual está escolhido.
 */
export function Chips<T extends string>({ rotulo, opcoes, valor, aoMudar }: {
  rotulo: string;
  opcoes: Chip<T>[];
  valor: T;
  aoMudar: (valor: T) => void;
}) {
  return (
    <div className="chips" role="group" aria-label={rotulo}>
      {opcoes.map((o) => (
        <button key={o.valor} className="chip" type="button" aria-pressed={o.valor === valor} onClick={() => aoMudar(o.valor)}>
          {o.rotulo}
          {o.contagem !== undefined && <> <span className="chip-contagem">{o.contagem}</span></>}
        </button>
      ))}
    </div>
  );
}
