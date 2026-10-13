import { useEffect, useRef, useState } from 'react';
import { copiarTexto } from '../areaDeTransferencia';

/**
 * O botão de copiar ao lado de um valor (US #461): o código de adesão, o SHA-256 da release, a senha temporária.
 * O retorno fica ao lado por dois segundos, num `aria-live` sem papel de status — a página já usa o `status` para os
 * avisos dela, e dois deles confundiriam o leitor de tela.
 */
export function BotaoCopiar({ texto, rotulo }: { texto: string; rotulo: string }) {
  const [estado, setEstado] = useState<'ocioso' | 'copiado' | 'falhou'>('ocioso');
  const relogio = useRef<number | undefined>(undefined);

  useEffect(() => () => window.clearTimeout(relogio.current), []);

  async function copiar() {
    const deu = await copiarTexto(texto);
    setEstado(deu ? 'copiado' : 'falhou');
    window.clearTimeout(relogio.current);
    relogio.current = window.setTimeout(() => setEstado('ocioso'), 2000);
  }

  return (
    <span className="copiar">
      <button className="botao-icone" type="button" aria-label={rotulo} title={rotulo} onClick={() => void copiar()}>
        <svg width="14" height="14" viewBox="0 0 16 16" aria-hidden="true" focusable="false">
          <rect x="5" y="5" width="9" height="9" rx="1.5" fill="none" stroke="currentColor" strokeWidth="1.5" />
          <path d="M11 3.5V3a1 1 0 0 0-1-1H3a1 1 0 0 0-1 1v7a1 1 0 0 0 1 1h.5" fill="none" stroke="currentColor" strokeWidth="1.5" />
        </svg>
      </button>
      <span className="copiar-retorno" aria-live="polite">
        {estado === 'copiado' ? 'Copiado' : estado === 'falhou' ? 'O navegador não deixou copiar' : ''}
      </span>
    </span>
  );
}
