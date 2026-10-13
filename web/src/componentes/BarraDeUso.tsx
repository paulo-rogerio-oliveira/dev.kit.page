/**
 * Quanto de um total está em uso (US #461) — os assentos da empresa: "N de M em uso" e a barra no trilho de
 * sempre (`.trilho`/`.barra`). Acima do total a barra para cheia, e o texto continua dizendo o número real.
 */
export function BarraDeUso({ usados, total, rotulo }: { usados: number; total: number; rotulo: string }) {
  const fracao = total > 0 ? Math.min(1, usados / total) : 0;
  return (
    <div className="barra-de-uso">
      <span>{usados} de {total} em uso</span>
      <span
        className="trilho"
        role="progressbar"
        aria-label={rotulo}
        aria-valuemin={0}
        aria-valuemax={total}
        aria-valuenow={Math.min(usados, total)}
      >
        <span className={`barra${fracao >= 1 ? ' barra-cheia' : ''}`} style={{ width: `${fracao * 100}%` }} />
      </span>
    </div>
  );
}
