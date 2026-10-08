/**
 * O trace de uma exceção não classificada (US #381) — o texto que o dev.kit mandou JÁ sanitizado
 * (sem caminhos, e-mails nem URLs), num bloco pré-formatado com rolagem. Focável pelo teclado, para
 * quem navega sem mouse conseguir rolar um trace longo.
 */
export function TraceDoErro({ trace }: { trace: string }) {
  return (
    <pre className="trace" tabIndex={0} aria-label="Trace da exceção">
      {trace || 'Sem trace guardado: as ocorrências deste grupo já passaram da retenção.'}
    </pre>
  );
}
