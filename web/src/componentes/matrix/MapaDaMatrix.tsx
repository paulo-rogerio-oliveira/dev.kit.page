import { useState } from 'react';
import type { DocumentoMatrix, MatrizMatrix, RelacaoMatrix, RevisaoMatrix } from '../../api/matrix';
import { EixosDaMatrix } from './EixosDaMatrix';
import { ConflitoDaMatrix, FalhaDaMatrix, PreviaDaMatrix, ResumoDoDocumento } from './RevisaoDaMatrix';
import { useEdicaoMatrix } from './useEdicaoMatrix';
import { ultimo } from './modelo';

export function MapaDaMatrix({ documento, versao, token, conta, editar, aoSalvar, preparar, matriz }: {
  documento: DocumentoMatrix; versao: number; token: string; conta: string; editar: boolean;
  aoSalvar: (r: RevisaoMatrix) => void; preparar: (m: MatrizMatrix) => void; matriz: MatrizMatrix | null;
}) {
  const e = useEdicaoMatrix({ documento, versao, token, conta, aoSalvar });
  const mapa = e.documento.mapa!;
  const [origem, setOrigem] = useState(mapa.componentes[0]?.id ?? '');
  const [relacao, setRelacao] = useState('');
  const [eixos, setEixos] = useState(false);
  const [selecionadas, setSelecionadas] = useState<string[]>([]);
  const [erro, setErro] = useState('');
  const r = mapa.relacoes.find(r => r.id === relacao);
  const bloqueado = !editar || e.ocupado || !!e.previa || !!e.conflito;
  const mudarRelacao = (p: Partial<RelacaoMatrix>) => e.mudar({ ...e.documento, mapa: {
    ...mapa, relacoes: mapa.relacoes.map(x => x.id === relacao ? { ...x, ...p } : x),
  } });
  const nome = (id: string) => mapa.componentes.find(x => x.id === id)?.nome ?? id;
  function prepararMatriz() {
    if (e.alterado || !versao) { setErro('Salve a revisão do mapa antes de preparar a matriz.'); return; }
    const usadas = mapa.relacoes.filter(r => selecionadas.includes(r.id));
    if (!usadas.length) { setErro('Selecione pelo menos uma relação e suas layers.'); return; }
    const celulas = [...(matriz?.celulas ?? [])];
    for (const r of usadas) for (const layer of r.layers) {
      if (celulas.some(c => c.componente === r.destino && c.layer === layer)) continue;
      celulas.push({ id: `imp-${crypto.randomUUID()}`, componente: r.destino, layer, obrigacao: 'verificar', aplicavel: true,
        justificativa: r.motivo, origem: `mapa r${versao} · ${r.id}`, responsaveis: [], criterio: r.verificacao, evidenciaEsperada: r.propagacao });
    }
    const adicionar = <T extends { id: string }>(antes: T[], novos: T[]) => [...antes, ...novos.filter(n => !antes.some(a => a.id === n.id))];
    preparar({ revisaoDoMapa: versao,
      componentes: adicionar(matriz?.componentes ?? [], mapa.componentes), layers: adicionar(matriz?.layers ?? [], mapa.layers), celulas });
  }
  return <section className="mx-map" aria-label="Mapa da aplicação"><h3>Componentes e relações de impacto</h3>
    <p>Veja o que uma alteração pode atingir dentro da própria aplicação.</p>
    <div className="mx-app"><strong>{mapa.nome}</strong><span>Mapa reutilizável desta aplicação · revisão {versao}</span></div>
    <p className="mx-nota">Editar este mapa não altera obrigações desta demanda nem de outra aplicação. Matrizes já geradas preservam sua versão até revisão explícita.</p>
    <FalhaDaMatrix edicao={e} />
    {e.conflito ? <ConflitoDaMatrix edicao={e} /> : e.previa ? <PreviaDaMatrix edicao={e} /> : <>
      <div className="mx-actions"><button disabled={!editar} onClick={() => setEixos(!eixos)}>Editar componentes e layers</button>
        <button disabled={bloqueado || mapa.componentes.length < 2 || !mapa.layers.length} onClick={() => {
          const id = `rel-${crypto.randomUUID()}`;
          e.mudar({ ...e.documento, mapa: { ...mapa, relacoes: [...mapa.relacoes, { id, origem, destino: mapa.componentes.find(c => c.id !== origem)!.id,
            layers: [mapa.layers[0].id], gatilho: '', motivo: '', propagacao: '', verificacao: '' }] } }); setRelacao(id);
        }}>+ Relação</button></div>
      {eixos && <div className="mx-columns">{(['componentes', 'layers'] as const).map(campo =>
        <EixosDaMatrix key={campo} titulo={campo === 'componentes' ? 'Componentes' : 'Layers'} eixos={mapa[campo]} desabilitado={bloqueado}
          mudar={lista => e.mudar({ ...e.documento, mapa: { ...mapa, [campo]: lista,
            relacoes: mapa.relacoes.filter(r => campo === 'componentes' ? lista.some(x => x.id === r.origem) && lista.some(x => x.id === r.destino) : r.layers.some(l => lista.some(x => x.id === l)))
              .map(r => campo === 'layers' ? { ...r, layers: r.layers.filter(l => lista.some(x => x.id === l)) } : r) } })} />)}</div>}
      <div className="mx-map-grid"><aside className="mx-card"><h4>Componentes · {mapa.nome}</h4>{mapa.componentes.map(c =>
        <button key={c.id} className="mx-componente" aria-pressed={c.id === origem} onClick={() => setOrigem(c.id)}><strong>{c.nome}</strong><small>{c.orientacao}</small></button>)}
        <p className="mx-nota">Outra aplicação da equipe possui seu próprio mapa de componentes.</p></aside>
        <div><section className="mx-card"><h4>Alterar este componente pode impactar…</h4>
          {mapa.relacoes.filter(r => r.destino === origem).map(r => <p className="mx-entrada" key={r.id}>Entrada · {nome(r.origem)} → {nome(origem)}<br />Gatilho: {r.gatilho}</p>)}
          <div className="mx-origem"><small>COMPONENTE DE ORIGEM</small><strong>{nome(origem)}</strong></div><p className="mx-seta" aria-hidden="true">↓</p>
          <div className="mx-destinos">{mapa.relacoes.filter(r => r.origem === origem).map(r => <div className="mx-card" key={r.id}>
            <button onClick={() => setRelacao(r.id)} aria-pressed={relacao === r.id}><strong>{nome(r.destino)}</strong></button>
            <div className="mx-chips">{r.layers.map(l => <span key={l}>{mapa.layers.find(x => x.id === l)?.nome}</span>)}</div>
            <p>{r.propagacao}</p><label className="mx-check"><input type="checkbox" checked={selecionadas.includes(r.id)}
              onChange={v => setSelecionadas(a => v.target.checked ? [...a, r.id] : a.filter(x => x !== r.id))} />Incluir na demanda</label>
          </div>)}</div>
          {!mapa.relacoes.some(r => r.origem === origem) && <p>Nenhuma relação de saída neste componente.</p>}
          <p className="mx-muted">Cada relação informa origem, gatilho, motivo e orientação.</p></section>
          <section className="mx-card"><h4>Levar os impactos para uma demanda</h4>
            <p>{selecionadas.length} relação(ões) selecionada(s) · {new Set(mapa.relacoes.filter(r => selecionadas.includes(r.id)).flatMap(r => r.layers.map(l => `${r.destino}:${l}`))).size} célula(s) propostas · responsáveis a definir.</p>
            <p className="mx-nota">Prévia local. Nenhuma matriz existente será sobrescrita. Revise obrigações, atribuições e justificativas antes de salvar.</p>
            <button className="mx-primary" disabled={bloqueado} onClick={prepararMatriz}>Preparar matriz do projeto →</button>
          </section></div></div>
      {r && <section className="mx-card"><h4>Relação selecionada · {nome(r.origem)} → {nome(r.destino)}</h4><fieldset disabled={bloqueado}>
        <div className="mx-columns"><label>Componente de origem<select value={r.origem} onChange={v => mudarRelacao({ origem: v.target.value })}>{mapa.componentes.map(c => <option key={c.id} value={c.id}>{c.nome}</option>)}</select></label>
          <label>Componente afetado<select value={r.destino} onChange={v => mudarRelacao({ destino: v.target.value })}>{mapa.componentes.map(c => <option key={c.id} value={c.id}>{c.nome}</option>)}</select></label></div>
        <label>Por que existe este impacto?<textarea value={r.motivo} onChange={v => mudarRelacao({ motivo: v.target.value })} /></label>
        <label>Propagar quando houver alteração em<textarea value={r.gatilho} onChange={v => mudarRelacao({ gatilho: v.target.value })} /></label>
        <fieldset><legend>Layers do componente afetado</legend><div className="mx-actions">{mapa.layers.map(l => <label className="mx-check" key={l.id}>
          <input type="checkbox" checked={r.layers.includes(l.id)} onChange={v => mudarRelacao({ layers: v.target.checked ? [...r.layers, l.id] : r.layers.filter(x => x !== l.id) })} />{l.nome}</label>)}</div></fieldset>
        <label>Orientação de impacto<textarea value={r.propagacao} onChange={v => mudarRelacao({ propagacao: v.target.value })} /></label>
        <label>Como verificar<textarea value={r.verificacao} onChange={v => mudarRelacao({ verificacao: v.target.value })} /></label>
        <button onClick={() => { e.mudar({ ...e.documento, mapa: { ...mapa, relacoes: mapa.relacoes.filter(x => x.id !== r.id) } }); setRelacao(''); }}>Desativar relação</button>
      </fieldset></section>}
      {erro && <p role="alert" className="mx-alerta">{erro}</p>}
      <div className="mx-actions"><button className="mx-primary" disabled={bloqueado || !e.alterado} onClick={() => void e.revisar()}>Revisar e salvar mapa</button></div>
    </>}
  </section>;
}

export function HistoricoDaMatrix({ historico }: { historico: RevisaoMatrix[] }) {
  const [comparar, setComparar] = useState<number | null>(null);
  return <section aria-label="Histórico da Matrix"><h3>Histórico e autoria</h3>
    {!historico.length && <p>Ainda sem revisões salvas.</p>}
    {historico.slice().reverse().map(r => <article className="mx-card" key={`${r.documento.tipo}-${r.versao}`}>
      <strong>r{r.versao} · {r.documento.tipo} · autor {r.autor}</strong><p>{new Date(r.em).toLocaleString('pt-BR')}</p>
      <p>Origem: {[...new Set(r.documento.matriz?.celulas.map(c => c.origem) ?? [])].join(' · ') || 'Mapa da aplicação'}</p>
      <button onClick={() => setComparar(comparar === r.versao ? null : r.versao)}>Comparar r{r.versao} com a atual</button>
      {comparar === r.versao && <div className="mx-columns"><div><h4>r{r.versao}</h4><ResumoDoDocumento documento={r.documento} /></div>
        <div><h4>Atual r{ultimo(historico)?.versao}</h4><ResumoDoDocumento documento={ultimo(historico)!.documento} /></div></div>}
    </article>)}
  </section>;
}
