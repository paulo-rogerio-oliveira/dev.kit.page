import { useState } from 'react';
import type { EixoMatrix } from '../../api/matrix';
import { identificador } from './modelo';

export function EixosDaMatrix({ titulo, eixos, mudar, desabilitado }: {
  titulo: string; eixos: EixoMatrix[]; mudar: (e: EixoMatrix[]) => void; desabilitado: boolean;
}) {
  const [novo, setNovo] = useState({ id: '', nome: '', orientacao: '' });
  const [erro, setErro] = useState('');
  return <section className="mx-card"><h4>{titulo}</h4>
    {eixos.map(e => <div className="mx-eixo" key={e.id}>
      <code>{e.id}</code><label>Nome de {e.id}<input value={e.nome} disabled={desabilitado}
        onChange={v => mudar(eixos.map(x => x.id === e.id ? { ...x, nome: v.target.value } : x))} /></label>
      <label>Orientação de {e.id}<textarea value={e.orientacao} disabled={desabilitado}
        onChange={v => mudar(eixos.map(x => x.id === e.id ? { ...x, orientacao: v.target.value } : x))} /></label>
      <button disabled={desabilitado || eixos.length === 1} onClick={() => mudar(eixos.filter(x => x.id !== e.id))}>Desativar {e.nome}</button>
    </div>)}
    <fieldset disabled={desabilitado}><legend>Adicionar {titulo.toLowerCase()}</legend>
      <div className="mx-columns"><label>Identificador estável<input value={novo.id} onChange={e => setNovo({ ...novo, id: e.target.value })} /></label>
        <label>Nome<input value={novo.nome} onChange={e => setNovo({ ...novo, nome: e.target.value })} /></label></div>
      <label>Orientação<textarea value={novo.orientacao} onChange={e => setNovo({ ...novo, orientacao: e.target.value })} /></label>
      <button onClick={() => {
        if (!identificador(novo.id) || !novo.nome.trim() || eixos.some(x => x.id === novo.id)) { setErro('Informe identificador único e nome.'); return; }
        mudar([...eixos, novo]); setNovo({ id: '', nome: '', orientacao: '' }); setErro('');
      }}>+ Adicionar</button>
    </fieldset>{erro && <p role="alert">{erro}</p>}
    <p className="mx-muted">Desativar preserva o histórico. As células e relações atingidas aparecem na revisão antes da gravação.</p>
  </section>;
}
