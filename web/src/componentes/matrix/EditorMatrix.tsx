import { useEffect, useState } from 'react';
import type { CelulaMatrix, DocumentoMatrix, MatrizMatrix, RevisaoMatrix } from '../../api/matrix';
import type { Projeto } from '../../api/projetos';
import { CelulaDaMatrix, RegistrosDaCelula } from './CelulaDaMatrix';
import { DialogoMatrix } from './DialogoMatrix';
import { EixosDaMatrix } from './EixosDaMatrix';
import { HistoricoDaMatrix, MapaDaMatrix } from './MapaDaMatrix';
import { ConflitoDaMatrix, FalhaDaMatrix, PreviaDaMatrix } from './RevisaoDaMatrix';
import { celulasAlteradas, lerDefinicao, registroVigente, ultimo, validarCelula } from './modelo';
import { useEdicaoMatrix } from './useEdicaoMatrix';
import './Matrix.css';

export interface AlvoDoEditorMatrix {
  documento: DocumentoMatrix; historico: RevisaoMatrix[]; mapa: RevisaoMatrix[]; registros: RevisaoMatrix[];
  recorte?: string[]; responsavel?: string;
  autores?: Record<string, string>;
  minhasTasks?: string[];
}
export function EditorMatrix({ alvo, projeto, token, conta, editar, solicitar, visao = 'matriz', aplicacoes, selecionarAplicacao, aoRevisar }: {
  alvo: AlvoDoEditorMatrix; projeto?: Projeto; token: string; conta: string; editar: boolean; solicitar?: (motivo?: string) => void;
  visao?: string; aplicacoes?: { id: string; nome: string }[]; selecionarAplicacao?: (id: string) => void;
  aoRevisar?: (ativa: boolean) => void;
}) {
  const [historico, setHistorico] = useState(alvo.historico);
  const [mapas, setMapas] = useState(alvo.mapa);
  const [vista, setVista] = useState(visao);
  useEffect(() => setVista(visao), [visao]);
  const [origemEmConsulta, setOrigemEmConsulta] = useState(false);
  const [celula, setCelula] = useState('');
  const [selecionada, setSelecionada] = useState('');
  const [componente, setComponente] = useState('');
  const [layer, setLayer] = useState('');
  const [eixos, setEixos] = useState<'componentes' | 'layers' | ''>('');
  const [filtro, setFiltro] = useState('');
  const [somenteMinhas, setSomenteMinhas] = useState(false);
  const [painelRevisao, setPainelRevisao] = useState(false);
  const [layerDaRevisao, setLayerDaRevisao] = useState('');
  const [pendentes, setPendentes] = useState(false);
  const [aviso, setAviso] = useState('');
  const [erro, setErro] = useState('');
  const e = useEdicaoMatrix({ documento: alvo.documento, versao: ultimo(historico)?.versao ?? 0, token, conta,
    aoSalvar: r => { setHistorico(h => [...h, r]); setCelula(''); setPainelRevisao(false); setAviso(`Revisão r${r.versao} salva e disponível às tasks afetadas. Histórico anterior preservado.`); } });
  useEffect(() => { if (e.previa) setPainelRevisao(true); }, [e.previa]);
  const emRevisao = !!e.previa || painelRevisao;
  useEffect(() => { aoRevisar?.(emRevisao && vista === 'matriz'); return () => aoRevisar?.(false); }, [aoRevisar, emRevisao, vista]);
  const m = e.documento.matriz;
  const layerEditada = m?.layers.find(l => l.id === layerDaRevisao) ?? m?.layers[0];
  const mapa = ultimo(mapas);
  const modificar = (matriz: MatrizMatrix) => e.mudar({ ...e.documento, matriz });
  const noRecorte = (c: CelulaMatrix) => !alvo.recorte || alvo.recorte.includes(c.id);
  const podeEditar = editar && !e.ocupado;
  const avaliacao = (c: CelulaMatrix) => ultimo(alvo.registros.filter(r => r.documento.registro?.celula === c.id && r.documento.registro.tipo === 'avaliacao' && registroVigente(r.documento.registro, historico)))?.documento.registro;
  const situacao = (c: CelulaMatrix) => !c.aplicavel ? 'Não se aplica, com motivo' : avaliacao(c)?.resultado === 'atendido' ? 'Comprovado' : avaliacao(c) ? 'A avaliar' : 'A implementar';
  const pendente = (c: CelulaMatrix) => c.aplicavel && situacao(c) !== 'Comprovado';
  const minhasTasks = alvo.responsavel ? [alvo.responsavel] : alvo.minhasTasks ?? [];
  const visivel = (c: CelulaMatrix) => (!filtro || c.responsaveis.some(r => r.id === filtro)) &&
    (!somenteMinhas || c.responsaveis.some(r => minhasTasks.includes(r.id))) && (!pendentes || pendente(c));
  const responsaveis = [...new Map((m?.celulas ?? []).flatMap(c => c.responsaveis).map(r => [r.id, r])).values()];
  const itemNome = (id: string, item: number | null) => item ? `Item ${item} · ${projeto?.itens.find(i => i.numero === item)?.titulo ?? id}` : id;
  const componenteAtual = componente || m?.componentes[0]?.id;
  const moveis = m?.celulas.filter(c => c.componente === componenteAtual && (!layer || c.layer === layer) && visivel(c)) ?? [];
  const selecionadaAtual = moveis.find(c => c.id === selecionada) ?? moveis[0];
  const selecionadaDaGrade = m?.celulas.find(c => c.id === selecionada && visivel(c)) ?? m?.celulas.find(visivel);
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
  const celulaVista = (c: CelulaMatrix, movel = false) => <button className={`mx-cell ${!c.aplicavel ? 'mx-na' : avaliacao(c)?.resultado === 'atendido' ? 'mx-ok' : 'mx-todo'}${movel && selecionadaAtual?.id === c.id ? ' mx-selecionada' : ''}`}
    onClick={() => { setSelecionada(c.id); if (!movel) setCelula(c.id); }} aria-label={`${m?.componentes.find(x => x.id === c.componente)?.nome} × ${m?.layers.find(x => x.id === c.layer)?.nome} · ${c.id}`}>
    <span>● {situacao(c)}</span><strong>{lerDefinicao(c.justificativa).obrigacao || 'Definir obrigação'}</strong>
    <small>{c.responsaveis.map(r => itemNome(r.id, r.item).length > 24 && r.item ? `Item ${r.item}` : itemNome(r.id, r.item)).join(', ') || 'Sem responsável'}{!noRecorte(c) && ' · dependência em consulta'}</small></button>;

  return <section className="mx" aria-label={alvo.recorte ? 'Impactos da task' : 'Impactos do projeto'}>
    {vista !== 'mapa' && !emRevisao && <><header className="mx-heading"><div><h3>{alvo.recorte ? 'Impactos da task · recorte' : 'Matriz deste projeto'}</h3>
      <p>Defina o que cada task deve entregar e como isso será avaliado.</p></div><div className="mx-actions">
      {!alvo.recorte && <><button disabled={!podeEditar} onClick={() => setEixos(eixos === 'componentes' ? '' : 'componentes')}>+ Componente</button>
        <button disabled={!podeEditar} onClick={() => setEixos(eixos === 'layers' ? '' : 'layers')}>Layers</button></>}
      {solicitar && <button onClick={() => solicitar()}>✦ Preencher com Orquestrador</button>}<span className="mx-badge">r{e.versao} · {editar ? 'Editor' : 'Leitor'}</span></div></header>
    <div className="mx-app"><strong>Aplicação {mapa?.documento.mapa?.nome ?? e.documento.aplicacao} · demanda {projeto?.nome ?? e.documento.id}</strong>
      <small>Outra aplicação da equipe mantém seu próprio mapa e suas próprias obrigações.</small></div>
    <nav className="mx-tabs" aria-label="Visões de Impactos">
      {['matriz', ...(!alvo.recorte ? ['mapa'] : []), 'historico'].map(v => <button key={v} aria-pressed={vista === v} onClick={() => setVista(v)}>
        {v === 'matriz' ? 'Matriz do projeto' : v === 'mapa' ? 'Mapa da aplicação ↗' : 'Histórico'}</button>)}
    </nav></>}
    {aviso && <p role="status" className="mx-nota">{aviso}</p>}
    {!editar && <div className="mx-nota"><p role="status">Somente leitura · edição exige editor ou dono.</p>{solicitar && <button onClick={() => solicitar('Solicitar acesso de editor à definição da Matrix')}>Solicitar acesso</button>}</div>}
    <FalhaDaMatrix edicao={e} solicitar={solicitar} />
    {vista === 'mapa' && mapa && <MapaDaMatrix key={`${mapa.documento.aplicacao}-${mapa.versao}`} documento={mapa.documento} versao={mapa.versao} token={token} conta={conta} editar={editar}
      matriz={m ?? null} aplicacoes={aplicacoes} selecionarAplicacao={selecionarAplicacao} voltar={() => setVista('matriz')} historico={() => setVista('historico')}
      aoSalvar={r => { setMapas(h => [...h, r]); setAviso(`Revisão r${r.versao} do mapa salva. As obrigações da demanda foram preservadas.`); }} preparar={nova => {
        const antigas = historico.flatMap(r => r.documento.matriz?.celulas ?? []);
        modificar({ ...nova, celulas: nova.celulas.map(c => ({ ...c, id: antigas.find(a => a.componente === c.componente && a.layer === c.layer)?.id ?? c.id })) });
        setVista('matriz'); setAviso('Prévia preparada. Defina responsáveis e revise cada nova célula antes de salvar.');
      }} />}
    {vista === 'historico' && <div className="mx-review-grid"><HistoricoDaMatrix historico={historico} autores={alvo.autores} registros={alvo.registros} /><HistoricoDaMatrix historico={mapas} autores={alvo.autores} /></div>}
    {vista === 'entregas' && <section><h3>Evidências e avaliações da demanda</h3>{!alvo.registros.length && <p>Ainda sem comprovação registrada.</p>}
      {[...new Set(alvo.registros.flatMap(r => r.documento.registro ? [r.documento.registro.celula] : []))].map(id => <article className="mx-card" key={id}><h4>{id}</h4>
        <RegistrosDaCelula tipo="implementacao" celula={id} registros={alvo.registros} historico={historico} autores={alvo.autores} />
        <RegistrosDaCelula tipo="avaliacao" celula={id} registros={alvo.registros} historico={historico} autores={alvo.autores} /></article>)}</section>}
    {vista === 'matriz' && (e.conflito ? <ConflitoDaMatrix edicao={e} /> : emRevisao ? <section className="mx-review"><header className="mx-heading"><div><h3>Revisar alterações antes de salvar</h3>
      <p>{e.previa ? `Matriz do projeto r${e.versao} → r${e.previa.revisao.versao}` : 'Alterações pendentes de nova revisão'} · nenhuma publicação ou integração.</p></div><span className="mx-badge">{editar ? 'Editor' : 'Leitor'}</span></header><div className="mx-review-grid">
      {e.previa ? <PreviaDaMatrix edicao={e} cabecalho={false} voltar={() => { e.voltar(); setPainelRevisao(false); }} /> :
        <section className="mx-card"><h3>Resumo da revisão</h3><p role="status">Eixos alterados. Confira novamente as células e tasks afetadas antes de salvar.</p>
          <button onClick={() => { setPainelRevisao(false); e.voltar(); }}>Voltar à edição</button><button className="mx-primary" disabled={!podeEditar} onClick={revisar}>Revisar eixos alterados</button></section>}
      <HistoricoDaMatrix historico={historico} autores={alvo.autores} registros={alvo.registros} celulas={e.previa?.mudancas.celulas} />
      <section className="mx-card"><h3>Editar componentes e layers</h3><p>Eixos configuráveis desta aplicação; nenhuma layer é fixa no modelo.</p>
        <div className="mx-chips">{m?.componentes.map(c => <span key={c.id}>{c.nome}</span>)}<button disabled={!podeEditar || !!alvo.recorte} onClick={() => setEixos('componentes')}>+ Componente</button></div>
        <div className="mx-chips">{m?.layers.map(l => <button key={l.id} aria-pressed={layerEditada?.id === l.id} onClick={() => setLayerDaRevisao(l.id)}>{l.nome}</button>)}<button disabled={!podeEditar || !!alvo.recorte} onClick={() => setEixos('layers')}>+ Layer</button></div>
        {m && layerEditada && <label>Nome da layer<input disabled={!podeEditar || !!alvo.recorte} value={layerEditada.nome} onChange={v => modificar({ ...m, layers: m.layers.map(l => l.id === layerEditada.id ? { ...l, nome: v.target.value } : l) })} /></label>}
        {m && eixos && <EixosDaMatrix titulo={eixos === 'componentes' ? 'Componentes' : 'Layers'} eixos={m[eixos]} desabilitado={!podeEditar || !!alvo.recorte}
          mudar={lista => modificar({ ...m, [eixos]: lista, celulas: m.celulas.filter(c => lista.some(x => x.id === (eixos === 'componentes' ? c.componente : c.layer))) })} />}
        {e.previa && <button disabled={!podeEditar || !!alvo.recorte} onClick={() => void e.revisar()}>Revisar eixos alterados</button>}<p>Desativar preserva o histórico e exige revisão das células e tasks afetadas.</p></section>
      <section className="mx-card"><h3>Definição ≠ comprovação</h3><p>Definição r{e.versao} · recorte {alvo.responsavel ?? 'do projeto'}.</p>
        <p>Implementador: atendimento e evidências. Avaliador: resultado e justificativa, revisão do recorte e SHAs conferidos.</p><p>Uma edição da definição não reescreve o resultado registrado.</p>
        <button onClick={() => setVista('entregas')}>Consultar comprovação</button></section></div></section> : <>
      {!m ? <p>Nenhuma matriz preparada. Abra o mapa da aplicação para selecionar os impactos da demanda.</p> : <>
        {alvo.recorte?.length === 0 && <section className="mx-card" aria-label="Recorte sem obrigações"><h3>Nenhuma obrigação atribuída a esta task</h3>
          <p>O recorte está vinculado e carregado. As obrigações das demais tasks permanecem em consulta.</p>
          <button onClick={() => { setFiltro(''); setSomenteMinhas(false); setPendentes(false); }}>Consultar dependências</button>
          {solicitar && <button onClick={() => solicitar('Solicitar revisão do recorte vinculado sem obrigações atribuídas')}>Pedir revisão do recorte</button>}</section>}
        <div className="mx-actions mx-filters"><label>Tasks<select value={filtro} onChange={v => { setFiltro(v.target.value); setSomenteMinhas(false); }}><option value="">Todas as tasks</option>
          {responsaveis.map(r => <option key={r.id} value={r.id}>{itemNome(r.id, r.item)}</option>)}</select></label>
          <button aria-pressed={somenteMinhas} onClick={() => {
            if (!minhasTasks.length) { setAviso('Nenhuma task desta matriz está vinculada às suas máquinas. Abra a task no Board para consultar seu recorte.'); return; }
            setFiltro(''); setSomenteMinhas(v => !v);
          }}>Minha task</button>
          <label className="mx-check"><input type="checkbox" checked={pendentes} onChange={v => setPendentes(v.target.checked)} />Pendentes {m.celulas.filter(c => noRecorte(c) && pendente(c)).length}</label>
          <span className="mx-badge">Sem responsável {m.celulas.filter(c => !c.responsaveis.length).length}</span>
        </div>
        <div className="mx-grade"><table><caption className="mx-sr">Componentes × layers · revisão {e.versao}</caption><thead><tr><th>Componente</th>{m.layers.map(l => <th scope="col" key={l.id}>{l.nome}</th>)}</tr></thead>
          <tbody>{m.componentes.map(c => <tr key={c.id}><th scope="row">{c.nome}<small title={c.orientacao}>{!alvo.recorte ? <button className="mx-orientacao" onClick={() => setVista('mapa')}>Ver orientação ↗</button> : 'Orientação de origem'}</small></th>{m.layers.map(l => {
            const cell = m.celulas.find(x => x.componente === c.id && x.layer === l.id);
            return <td key={l.id}>{cell ? visivel(cell) && celulaVista(cell) : <button className="mx-cell mx-empty" disabled={!podeEditar || !!alvo.recorte} onClick={() => novaCelula(c.id, l.id)}>○ Não analisada{!alvo.recorte && ' · definir'}</button>}</td>;
          })}</tr>)}</tbody></table></div>
        <div className="mx-lista"><div className="mx-actions"><label>Componente<select value={componenteAtual} onChange={v => setComponente(v.target.value)}>
          {m.componentes.map(c => <option key={c.id} value={c.id}>{c.nome}</option>)}</select></label>
          <label>Layer<select value={layer} onChange={v => setLayer(v.target.value)}><option value="">Todas as layers</option>{m.layers.map(l => <option key={l.id} value={l.id}>{l.nome}</option>)}</select></label></div>
          {m.layers.filter(l => !layer || l.id === layer).map(l => { const c = m.celulas.find(x => x.componente === componenteAtual && x.layer === l.id);
            return <div className="mx-mobile-cell mx-card" key={l.id}><strong>{l.nome}</strong>{c ? visivel(c) && celulaVista(c, true) :
              <button disabled={!podeEditar || !!alvo.recorte} onClick={() => novaCelula(componenteAtual!, l.id)}>○ Não analisada · definir</button>}</div>;
          })}
          {selecionadaAtual && <section className="mx-card mx-cell-summary"><small>EDITAR CÉLULA · {selecionadaAtual.id}</small>
            <h3>{m.componentes.find(c => c.id === selecionadaAtual.componente)?.nome} × {m.layers.find(l => l.id === selecionadaAtual.layer)?.nome}</h3>
            <p>{selecionadaAtual.responsaveis.map(r => itemNome(r.id, r.item)).join(', ') || 'Responsável a definir'}</p>
            <p className="mx-alerta">Esta edição vale somente para esta demanda. O mapa da aplicação permanece como referência.</p>
            <label>Obrigação neste projeto<span className="mx-value">{lerDefinicao(selecionadaAtual.justificativa).obrigacao}</span></label>
            <div className="mx-columns mx-pair"><label>Aplicabilidade<span className="mx-value">{selecionadaAtual.aplicavel ? 'Obrigatório' : 'Não se aplica'}</span></label>
              <label>Responsável<span className="mx-value">{selecionadaAtual.responsaveis.map(r => itemNome(r.id, r.item)).join(', ') || 'A definir'}</span></label></div>
            <label>Como validar<span className="mx-value">{selecionadaAtual.criterio}</span></label>
            <button className="mx-primary" onClick={() => setCelula(selecionadaAtual.id)}>Abrir editor completo →</button>
          </section>}
        </div>
        {!m.celulas.filter(visivel).length && <p role="status">Nenhuma célula neste filtro.</p>}
        <p className="mx-legenda">● Comprovado · ● A avaliar · ● A implementar · ○ Não analisada · Não se aplica, com motivo</p>
        <p className="mx-muted">{m.celulas.filter(c => c.aplicavel).length} aplicáveis · {m.componentes.length * m.layers.length - m.celulas.length} sem análise · revisão {e.versao}
          {alvo.recorte && ` · ${alvo.recorte.length} célula(s) atribuída(s); demais em consulta`}</p>
        {eixos && !alvo.recorte && <EixosDaMatrix titulo={eixos === 'componentes' ? 'Componentes' : 'Layers'} eixos={m[eixos]} desabilitado={!podeEditar}
          mudar={lista => modificar({ ...m, [eixos]: lista, celulas: m.celulas.filter(c => lista.some(x => x.id === (eixos === 'componentes' ? c.componente : c.layer))) })} />}
        {erro && <p role="alert" className="mx-alerta">{erro}</p>}
        <div className="mx-actions">{e.alterado && <span>{celulasAlteradas(e.rascunho.base.matriz, m).length} célula(s) alterada(s) · rascunho preservado</span>}
          {e.alterado && <button disabled={!podeEditar} onClick={revisar}>Revisar alterações e salvar →</button>}
          {selecionadaDaGrade && <button className="mx-primary" onClick={() => setCelula(selecionadaDaGrade.id)}>{podeEditar && noRecorte(selecionadaDaGrade) ? 'Editar' : 'Consultar'} {selecionadaDaGrade.id.toUpperCase()} →</button>}</div>
      </>}
      <p className="mx-nota">Uma definição compartilhada, uma responsabilidade por célula. Cada task recebe seu recorte. Envio, PR, integração e publicação mantêm seus portões.</p>
    </>)}
    {celula && m?.celulas.some(c => c.id === celula) && <CelulaDaMatrix key={celula} id={celula} edicao={e} editar={editar} itens={projeto?.itens ?? []}
      registros={alvo.registros} historico={historico} fechar={() => setCelula('')} recorte={alvo.recorte} solicitar={solicitar} aplicacao={mapa?.documento.mapa?.nome}
      autores={alvo.autores} consultarOrigem={() => { setCelula(''); if (alvo.recorte) setOrigemEmConsulta(true); else setVista('mapa'); }} />}
    {origemEmConsulta && mapa && <DialogoMatrix titulo="Mapa da aplicação · consulta da origem" fechar={() => setOrigemEmConsulta(false)}>
      <MapaDaMatrix documento={mapa.documento} versao={mapa.versao} token={token} conta={conta} editar={false} matriz={m ?? null} aoSalvar={() => {}} preparar={() => {}} />
    </DialogoMatrix>}
  </section>;
}
