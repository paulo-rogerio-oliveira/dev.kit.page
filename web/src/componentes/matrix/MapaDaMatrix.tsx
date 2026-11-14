import { useState } from 'react';
import type { DocumentoMatrix, MatrizMatrix, RelacaoMatrix, RevisaoMatrix } from '../../api/matrix';
import { EixosDaMatrix } from './EixosDaMatrix';
import { ConflitoDaMatrix, FalhaDaMatrix, PreviaDaMatrix, ResumoDoDocumento } from './RevisaoDaMatrix';
import { useEdicaoMatrix } from './useEdicaoMatrix';
import { celulasAlteradas, ultimo } from './modelo';

export function MapaDaMatrix({ documento, versao, token, conta, editar, aoSalvar, preparar, matriz, aplicacoes, selecionarAplicacao, voltar, historico }: {
  documento: DocumentoMatrix; versao: number; token: string; conta: string; editar: boolean;
  aoSalvar: (r: RevisaoMatrix) => void; preparar: (m: MatrizMatrix) => void; matriz: MatrizMatrix | null;
  aplicacoes?: { id: string; nome: string }[]; selecionarAplicacao?: (id: string) => void; voltar?: () => void; historico?: () => void;
}) {
  const e = useEdicaoMatrix({ documento, versao, token, conta, aoSalvar });
  const mapa = e.documento.mapa!;
  const [origem, setOrigem] = useState(mapa.componentes[0]?.id ?? '');
  const [relacao, setRelacao] = useState(mapa.relacoes.find(r => r.origem === mapa.componentes[0]?.id)?.id ?? '');
  const [eixos, setEixos] = useState<'componentes' | 'layers' | ''>('');
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
    <nav className="mx-tabs" aria-label="Visões do mapa"><button aria-pressed={true}>Componentes da aplicação</button>
      {voltar && <button onClick={voltar}>Matriz do projeto</button>}{historico && <button onClick={historico}>Histórico do mapa</button>}</nav>
    <div className="mx-card mx-map-context"><label>Aplicação<select value={documento.aplicacao} disabled={!selecionarAplicacao} onChange={v => selecionarAplicacao?.(v.target.value)}>
      {(aplicacoes?.length ? aplicacoes : [{ id: documento.aplicacao, nome: mapa.nome }]).map(a => <option value={a.id} key={a.id}>{a.nome}</option>)}</select></label>
      <span>Mapa reutilizável desta aplicação · revisão {versao}</span></div>
    <p className="mx-nota">Editar este mapa não altera obrigações desta demanda nem de outra aplicação. Matrizes já geradas preservam sua versão até revisão explícita.</p>
    <FalhaDaMatrix edicao={e} />
    {e.conflito ? <ConflitoDaMatrix edicao={e} /> : e.previa ? <PreviaDaMatrix edicao={e} /> : <>
      <div className="mx-actions"><button disabled={bloqueado} onClick={() => setEixos('componentes')}>+ Componente</button>
        <button disabled={bloqueado} onClick={() => setEixos(eixos === 'componentes' ? '' : 'componentes')}>Editar componente</button>
        <button disabled={bloqueado || mapa.componentes.length < 2 || !mapa.layers.length} onClick={() => {
          const id = `rel-${crypto.randomUUID()}`;
          e.mudar({ ...e.documento, mapa: { ...mapa, relacoes: [...mapa.relacoes, { id, origem, destino: mapa.componentes.find(c => c.id !== origem)!.id,
            layers: [mapa.layers[0].id], gatilho: '', motivo: '', propagacao: '', verificacao: '' }] } }); setRelacao(id);
        }}>+ Relação</button><button disabled={bloqueado} onClick={() => setEixos(eixos === 'layers' ? '' : 'layers')}>Layers</button></div>
      {eixos && <EixosDaMatrix titulo={eixos === 'componentes' ? 'Componentes' : 'Layers'} eixos={mapa[eixos]} desabilitado={bloqueado}
          mudar={lista => e.mudar({ ...e.documento, mapa: { ...mapa, [eixos]: lista,
            relacoes: mapa.relacoes.filter(r => eixos === 'componentes' ? lista.some(x => x.id === r.origem) && lista.some(x => x.id === r.destino) : r.layers.some(l => lista.some(x => x.id === l)))
              .map(r => eixos === 'layers' ? { ...r, layers: r.layers.filter(l => lista.some(x => x.id === l)) } : r) } })} />}
      <div className="mx-map-grid"><aside className="mx-card"><h4>Componentes · {mapa.nome}</h4>{mapa.componentes.map(c =>
        <button key={c.id} className="mx-componente" aria-pressed={c.id === origem} onClick={() => { setOrigem(c.id); setRelacao(mapa.relacoes.find(r => r.origem === c.id)?.id ?? ''); }}><strong>{c.nome}</strong><small>{c.orientacao}</small>
          <small>{mapa.relacoes.filter(r => r.destino === c.id).length} entrada(s) · {mapa.relacoes.filter(r => r.origem === c.id).length} saída(s)</small></button>)}
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
      {r && <section className="mx-card mx-relation"><small>RELAÇÃO SELECIONADA</small><h4>{nome(r.origem)} → {nome(r.destino)}</h4><fieldset disabled={bloqueado}>
        <div className="mx-columns"><label>Componente de origem<select value={r.origem} onChange={v => mudarRelacao({ origem: v.target.value })}>{mapa.componentes.map(c => <option key={c.id} value={c.id}>{c.nome}</option>)}</select></label>
          <label>Componente afetado<select value={r.destino} onChange={v => mudarRelacao({ destino: v.target.value })}>{mapa.componentes.map(c => <option key={c.id} value={c.id}>{c.nome}</option>)}</select></label></div>
        <label>Por que existe este impacto?<textarea value={r.motivo} onChange={v => mudarRelacao({ motivo: v.target.value })} /></label>
        <label>Propagar quando houver alteração em<textarea value={r.gatilho} onChange={v => mudarRelacao({ gatilho: v.target.value })} /></label>
        <fieldset><legend>Layers do componente afetado</legend><div className="mx-actions">{mapa.layers.map(l => <label className="mx-check" key={l.id}>
          <input type="checkbox" checked={r.layers.includes(l.id)} onChange={v => mudarRelacao({ layers: v.target.checked ? [...r.layers, l.id] : r.layers.filter(x => x !== l.id) })} />{l.nome}</label>)}</div></fieldset>
        <label>Orientação de impacto<textarea value={r.propagacao} onChange={v => mudarRelacao({ propagacao: v.target.value })} /></label>
        <label>Como verificar<textarea value={r.verificacao} onChange={v => mudarRelacao({ verificacao: v.target.value })} /></label>
        <p className="mx-nota">Mapa r{versao} → r{versao + 1}. Matrizes já geradas preservam sua versão até revisão explícita.</p>
        <div className="mx-actions"><button onClick={() => {
          const original = e.rascunho.base.mapa!.relacoes.find(x => x.id === r.id);
          e.mudar({ ...e.documento, mapa: { ...mapa, relacoes: original ? mapa.relacoes.map(x => x.id === r.id ? original : x) : mapa.relacoes.filter(x => x.id !== r.id) } });
          setRelacao('');
        }}>Cancelar relação</button><button className="mx-primary" disabled={!e.alterado} onClick={() => void e.revisar()}>Salvar relação</button></div>
        <button onClick={() => { e.mudar({ ...e.documento, mapa: { ...mapa, relacoes: mapa.relacoes.filter(x => x.id !== r.id) } }); setRelacao(''); }}>Desativar relação</button>
      </fieldset></section>}
      {erro && <p role="alert" className="mx-alerta">{erro}</p>}
      <div className="mx-actions"><button className="mx-primary" disabled={bloqueado || !e.alterado} onClick={() => void e.revisar()}>Revisar e salvar mapa</button></div>
    </>}
  </section>;
}

export function HistoricoDaMatrix({ historico, autores = {}, registros = [], celulas }: {
  historico: RevisaoMatrix[]; autores?: Record<string, string>; registros?: RevisaoMatrix[]; celulas?: string[];
}) {
  const [comparar, setComparar] = useState<number | null>(null);
  return <section aria-label="Histórico da Matrix"><h3>Histórico e autoria</h3>
    {!historico.length && <p>Ainda sem revisões salvas.</p>}
    <div className="mx-timeline">{historico.slice().reverse().map(r => {
      const anterior = historico.find(h => h.versao === r.versao - 1);
      const ids = celulasAlteradas(anterior?.documento.matriz, r.documento.matriz);
      const alteradas = [...(anterior?.documento.matriz?.celulas ?? []), ...(r.documento.matriz?.celulas ?? [])].filter(c => ids.includes(c.id));
      const tasks = [...new Set(alteradas.flatMap(c => c.responsaveis.map(t => t.item ? `Item ${t.item}` : t.id)))];
      const resultados = registros.filter(v => v.documento.registro?.revisaoDaDefinicao === r.versao && (!celulas || celulas.includes(v.documento.registro.celula)));
      return <article key={`${r.documento.tipo}-${r.versao}`}>
      <strong>r{r.versao} · {autores[r.autor] ?? (/^\d+$/.test(r.autor) ? `Usuário ${r.autor}` : r.autor)}</strong><p>{new Date(r.em).toLocaleString('pt-BR')}</p>
      <p>{r.documento.matriz ? `${ids.length} obrigação(ões) alterada(s) · ${tasks.join(', ') || 'nenhuma task afetada'}` : 'Mapa reutilizável da aplicação'}</p>
      {alteradas.length > 0 && <details><summary>Origem das alterações</summary>{[...new Set(alteradas.map(c => c.origem))].map(origem => <p key={origem}>{origem}</p>)}</details>}
      {resultados.slice(0, 2).map(v => <p key={v.versao}>
        {v.documento.registro!.tipo === 'avaliacao' ? 'Avaliação preservada' : 'Evidência registrada'}: {v.documento.registro!.resultado} · recorte r{r.versao} · {v.documento.registro!.celula}<br />
        {Object.entries(v.documento.registro!.shas).map(([repo, sha]) => <span key={repo} title={sha}>{repo} · SHA <code>{sha.slice(0, 12)}</code> </span>)}</p>)}
      {resultados.length > 2 && <details><summary>Ver {resultados.length} comprovações desta revisão</summary>{resultados.map(v => <p key={v.versao}>{v.documento.registro!.celula} · {v.documento.registro!.resultado}<br />
        {Object.entries(v.documento.registro!.shas).map(([repo, sha]) => <code key={repo}>{repo}: {sha} </code>)}</p>)}</details>}
      <button onClick={() => setComparar(comparar === r.versao ? null : r.versao)}>Comparar r{r.versao} com a atual</button>
      {comparar === r.versao && <div className="mx-columns"><div><h4>r{r.versao}</h4><ResumoDoDocumento documento={r.documento} /></div>
        <div><h4>Atual r{ultimo(historico)?.versao}</h4><ResumoDoDocumento documento={ultimo(historico)!.documento} /></div></div>}
    </article>; })}</div>
  </section>;
}
