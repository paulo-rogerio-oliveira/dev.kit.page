import { useState } from 'react';
import type { CelulaMatrix, DocumentoMatrix, MatrizMatrix, RevisaoMatrix } from '../../api/matrix';
import type { Projeto } from '../../api/projetos';
import { CelulaDaMatrix } from './CelulaDaMatrix';
import { EixosDaMatrix } from './EixosDaMatrix';
import { HistoricoDaMatrix, MapaDaMatrix } from './MapaDaMatrix';
import { ConflitoDaMatrix, FalhaDaMatrix, PreviaDaMatrix } from './RevisaoDaMatrix';
import { celulasAlteradas, lerDefinicao, registroVigente, ultimo, validarCelula } from './modelo';
import { useEdicaoMatrix } from './useEdicaoMatrix';
import './Matrix.css';

export interface AlvoDoEditorMatrix {
  documento: DocumentoMatrix; historico: RevisaoMatrix[]; mapa: RevisaoMatrix[]; registros: RevisaoMatrix[];
  recorte?: string[]; responsavel?: string;
}
export function EditorMatrix({ alvo, projeto, token, conta, editar, solicitar }: {
  alvo: AlvoDoEditorMatrix; projeto?: Projeto; token: string; conta: string; editar: boolean; solicitar?: () => void;
}) {
  const [historico, setHistorico] = useState(alvo.historico);
  const [mapas, setMapas] = useState(alvo.mapa);
  const [vista, setVista] = useState('matriz');
  const [celula, setCelula] = useState('');
  const [eixos, setEixos] = useState(false);
  const [filtro, setFiltro] = useState('');
  const [pendentes, setPendentes] = useState(false);
  const [aviso, setAviso] = useState('');
  const [erro, setErro] = useState('');
  const e = useEdicaoMatrix({ documento: alvo.documento, versao: ultimo(historico)?.versao ?? 0, token, conta,
    aoSalvar: r => { setHistorico(h => [...h, r]); setCelula(''); setAviso(`Revisão r${r.versao} salva e disponível às tasks afetadas. Histórico anterior preservado.`); } });
  const m = e.documento.matriz;
  const mapa = ultimo(mapas);
  const modificar = (matriz: MatrizMatrix) => e.mudar({ ...e.documento, matriz });
  const noRecorte = (c: CelulaMatrix) => !alvo.recorte || alvo.recorte.includes(c.id);
  const podeEditar = editar && !e.ocupado;
  const avaliacao = (c: CelulaMatrix) => ultimo(alvo.registros.filter(r => r.documento.registro?.celula === c.id && r.documento.registro.tipo === 'avaliacao' && registroVigente(r.documento.registro, historico)))?.documento.registro;
  const situacao = (c: CelulaMatrix) => !c.aplicavel ? 'Não se aplica, com motivo' : avaliacao(c)?.resultado === 'atendido' ? 'Comprovado' : avaliacao(c) ? 'A avaliar' : 'A implementar';
  const visivel = (c: CelulaMatrix) => (!filtro || c.responsaveis.some(r => r.id === filtro)) && (!pendentes || situacao(c) !== 'Comprovado');
  const responsaveis = [...new Map((m?.celulas ?? []).flatMap(c => c.responsaveis).map(r => [r.id, r])).values()];
  const itemNome = (id: string, item: number | null) => item ? `Item ${item} · ${projeto?.itens.find(i => i.numero === item)?.titulo ?? id}` : id;
  function novaCelula(componente: string, layer: string) {
    if (!m || !podeEditar || alvo.recorte) return;
    // Reintrodução do mesmo par usa o ID histórico, como exige matrix-v1.
    const antiga = historico.flatMap(r => r.documento.matriz?.celulas ?? []).find(c => c.componente === componente && c.layer === layer);
    const c: CelulaMatrix = antiga ?? { id: `imp-${crypto.randomUUID()}`, componente, layer, obrigacao: 'alterar', aplicavel: true,
      justificativa: '', origem: 'Edição manual no Page', responsaveis: [], criterio: '', evidenciaEsperada: '' };
    modificar({ ...m, celulas: [...m.celulas, c] }); setCelula(c.id);
  }
  const revisar = () => {
    const falha = m?.celulas.map(validarCelula).find(Boolean);
    setErro(falha || ''); if (!falha) void e.revisar();
  };
  const celulaVista = (c: CelulaMatrix) => <button className={`mx-cell ${!c.aplicavel ? 'mx-na' : avaliacao(c)?.resultado === 'atendido' ? 'mx-ok' : 'mx-todo'}`}
    onClick={() => setCelula(c.id)} aria-label={`${m?.componentes.find(x => x.id === c.componente)?.nome} × ${m?.layers.find(x => x.id === c.layer)?.nome} · ${c.id}`}>
    <span>● {situacao(c)}</span><strong>{lerDefinicao(c.justificativa).obrigacao || 'Definir obrigação'}</strong>
    <small>{c.responsaveis.map(r => itemNome(r.id, r.item).length > 24 && r.item ? `Item ${r.item}` : itemNome(r.id, r.item)).join(', ') || 'Sem responsável'}{!noRecorte(c) && ' · dependência em consulta'}</small></button>;

  return <section className="mx" aria-label={alvo.recorte ? 'Impactos da task' : 'Impactos do projeto'}>
    <header className="mx-heading"><div><h3>{alvo.recorte ? 'Impactos da task · recorte' : 'Matriz deste projeto'}</h3>
      <p>Defina o que cada task deve entregar e como isso será avaliado.</p></div><span className="mx-badge">r{e.versao} · {editar ? 'Editor' : 'Leitor'}</span></header>
    <div className="mx-app"><strong>Aplicação {mapa?.documento.mapa?.nome ?? e.documento.aplicacao} · demanda {projeto?.nome ?? e.documento.id}</strong>
      <small>Outra aplicação da equipe mantém seu próprio mapa e suas próprias obrigações.</small></div>
    <nav className="mx-tabs" aria-label="Visões de Impactos">
      {['matriz', ...(!alvo.recorte ? ['mapa'] : []), 'historico'].map(v => <button key={v} aria-pressed={vista === v} onClick={() => setVista(v)}>
        {v === 'matriz' ? 'Matriz do projeto' : v === 'mapa' ? 'Mapa da aplicação ↗' : 'Histórico'}</button>)}
    </nav>
    {aviso && <p role="status" className="mx-nota">{aviso}</p>}
    {!editar && <p role="status" className="mx-nota">Somente leitura · edição exige editor ou dono.</p>}
    <FalhaDaMatrix edicao={e} />
    {vista === 'mapa' && mapa && <MapaDaMatrix key={`${mapa.documento.aplicacao}-${mapa.versao}`} documento={mapa.documento} versao={mapa.versao} token={token} conta={conta} editar={editar}
      matriz={m ?? null} aoSalvar={r => setMapas(h => [...h, r])} preparar={nova => {
        const antigas = historico.flatMap(r => r.documento.matriz?.celulas ?? []);
        modificar({ ...nova, celulas: nova.celulas.map(c => ({ ...c, id: antigas.find(a => a.componente === c.componente && a.layer === c.layer)?.id ?? c.id })) });
        setVista('matriz'); setAviso('Prévia preparada. Defina responsáveis e revise cada nova célula antes de salvar.');
      }} />}
    {vista === 'historico' && <><HistoricoDaMatrix historico={historico} /><HistoricoDaMatrix historico={mapas} /></>}
    {vista === 'matriz' && (e.conflito ? <ConflitoDaMatrix edicao={e} /> : e.previa ? <PreviaDaMatrix edicao={e} /> : <>
      {!m ? <p>Nenhuma matriz preparada. Abra o mapa da aplicação para selecionar os impactos da demanda.</p> : <>
        <div className="mx-actions mx-filters"><label>Tasks<select value={filtro} onChange={v => setFiltro(v.target.value)}><option value="">Todas as tasks</option>
          {responsaveis.map(r => <option key={r.id} value={r.id}>{itemNome(r.id, r.item)}</option>)}</select></label>
          {alvo.responsavel && <button onClick={() => setFiltro(alvo.responsavel!)}>Minha task</button>}
          <label className="mx-check"><input type="checkbox" checked={pendentes} onChange={v => setPendentes(v.target.checked)} />Pendentes</label>
          <span className="mx-badge">Sem responsável {m.celulas.filter(c => !c.responsaveis.length).length}</span>
          {!alvo.recorte && <button disabled={!podeEditar} onClick={() => setEixos(!eixos)}>+ Componentes / Layers</button>}
          {solicitar && <button onClick={solicitar}>✦ Preencher com Orquestrador</button>}
        </div>
        <div className="mx-grade"><table><caption className="mx-sr">Componentes × layers · revisão {e.versao}</caption><thead><tr><th>Componente</th>{m.layers.map(l => <th scope="col" key={l.id}>{l.nome}</th>)}</tr></thead>
          <tbody>{m.componentes.map(c => <tr key={c.id}><th scope="row">{c.nome}<small title={c.orientacao}>{!alvo.recorte ? <button className="mx-orientacao" onClick={() => setVista('mapa')}>Ver orientação ↗</button> : 'Orientação de origem'}</small></th>{m.layers.map(l => {
            const cell = m.celulas.find(x => x.componente === c.id && x.layer === l.id);
            return <td key={l.id}>{cell ? visivel(cell) && celulaVista(cell) : <button className="mx-cell mx-empty" disabled={!podeEditar || !!alvo.recorte} onClick={() => novaCelula(c.id, l.id)}>○ Não analisada{!alvo.recorte && ' · definir'}</button>}</td>;
          })}</tr>)}</tbody></table></div>
        <div className="mx-lista">{m.componentes.map(c => <section className="mx-card" key={c.id}><h4>{c.nome}</h4><p className="mx-muted">{c.orientacao}</p>
          {m.layers.map(l => { const cell = m.celulas.find(x => x.componente === c.id && x.layer === l.id);
            return <div className="mx-mobile-cell" key={l.id}><strong>{l.nome}</strong>{cell ? visivel(cell) && celulaVista(cell) :
              <button disabled={!podeEditar || !!alvo.recorte} onClick={() => novaCelula(c.id, l.id)}>○ Não analisada · definir</button>}</div>;
          })}</section>)}</div>
        {!m.celulas.filter(visivel).length && <p role="status">Nenhuma célula neste filtro.</p>}
        <p className="mx-legenda">● Comprovado · ● A avaliar · ● A implementar · ○ Não analisada · Não se aplica, com motivo</p>
        <p className="mx-muted">{m.celulas.filter(c => c.aplicavel).length} aplicáveis · {m.componentes.length * m.layers.length - m.celulas.length} sem análise · revisão {e.versao}
          {alvo.recorte && ` · ${alvo.recorte.length} célula(s) atribuída(s); demais em consulta`}</p>
        {eixos && !alvo.recorte && <div className="mx-columns">{(['componentes', 'layers'] as const).map(campo => <EixosDaMatrix key={campo}
          titulo={campo === 'componentes' ? 'Componentes' : 'Layers'} eixos={m[campo]} desabilitado={!podeEditar}
          mudar={lista => modificar({ ...m, [campo]: lista, celulas: m.celulas.filter(c => lista.some(x => x.id === (campo === 'componentes' ? c.componente : c.layer))) })} />)}</div>}
        {erro && <p role="alert" className="mx-alerta">{erro}</p>}
        <div className="mx-actions">{e.alterado && <span>{celulasAlteradas(e.rascunho.base.matriz, m).length} célula(s) alterada(s) · rascunho preservado</span>}
          <button className="mx-primary" disabled={!podeEditar || !e.alterado} onClick={revisar}>Revisar alterações e salvar →</button></div>
      </>}
      <p className="mx-nota">Uma definição compartilhada, uma responsabilidade por célula. Cada task recebe seu recorte. Envio, PR, integração e publicação mantêm seus portões.</p>
    </>)}
    {celula && m?.celulas.some(c => c.id === celula) && <CelulaDaMatrix key={celula} id={celula} edicao={e} editar={editar} itens={projeto?.itens ?? []}
      registros={alvo.registros} historico={historico} fechar={() => setCelula('')} recorte={alvo.recorte} solicitar={solicitar} />}
  </section>;
}
