import { useState } from 'react';
import { mensagemDaFalha } from '../api/cliente';
import type { MaquinaRemota } from '../api/remoto';

/**
 * O vínculo do SERVIÇO da máquina (item 9 do remote-control-v3): com o client credentials ativo, o serviço publica e lê a
 * fila com o próprio token, também com o dev.kit fechado. O Desvincular pede confirmação e revoga o vínculo na hora; o
 * próximo login no dev.kit daquela máquina vincula de novo. Sem o campo (a API antiga), nada aparece.
 */
export function VinculoDoServico({ maquina, desvincular }: { maquina: MaquinaRemota; desvincular: () => Promise<void> }) {
  const [confirmando, setConfirmando] = useState(false);
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState('');
  if (maquina.vinculada === undefined) return null;

  if (!maquina.vinculada)
    return <section className="br-card br-vinculo br-vinculo-sem" aria-label="Vínculo do serviço">
      <b>Serviço sem vínculo</b>
      <p>Entre no dev.kit nesta máquina para o serviço acompanhar o controle remoto com o próprio token.</p>
    </section>;

  async function confirmar() {
    setEnviando(true); setErro('');
    try { await desvincular(); setConfirmando(false); }
    catch (falha) { setErro(`Não foi possível desvincular. ${mensagemDaFalha(falha)}`); }
    finally { setEnviando(false); }
  }

  return confirmando
    ? <section className="br-card br-vinculo br-vinculo-confirmar" aria-label="Vínculo do serviço">
      <b>Desvincular o serviço desta máquina?</b>
      <p>O heartbeat e a fila desta máquina param na hora. Entre de novo no dev.kit nela para vincular outra vez.</p>
      {erro && <p role="alert">{erro}</p>}
      <div className="br-row">
        <button className="br-btn br-perigo" disabled={enviando} onClick={() => void confirmar()}>Desvincular o serviço</button>
        <button className="br-btn" disabled={enviando} onClick={() => { setConfirmando(false); setErro(''); }}>Cancelar</button>
      </div>
    </section>
    : <section className="br-card br-vinculo" aria-label="Vínculo do serviço">
      <b><i aria-hidden="true" />Serviço vinculado</b>
      <p>O serviço desta máquina usa o próprio token, só para o controle remoto — também com o dev.kit fechado.</p>
      <button className="br-btn br-vinculo-acao" onClick={() => setConfirmando(true)}>Desvincular</button>
    </section>;
}
