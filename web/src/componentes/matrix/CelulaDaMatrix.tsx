import { useEffect, useState } from 'react';
import type { CelulaMatrix, RegistroMatrix, RevisaoMatrix } from '../../api/matrix';
import type { ItemDoProjeto } from '../../api/projetos';
import { DialogoMatrix } from './DialogoMatrix';
import { ConflitoDaMatrix, FalhaDaMatrix, PreviaDaMatrix } from './RevisaoDaMatrix';
import { celulasAlteradas, escreverDefinicao, exportarRascunho, lerDefinicao, registroVigente, validarCelula } from './modelo';
import type { EdicaoDaMatrix } from './useEdicaoMatrix';

const resultados: Record<RegistroMatrix['resultado'], string> = {
  atendido: 'Atendido', parcial: 'Parcial', 'nao-atendido': 'Não atendido', 'nao-aplicavel': 'Não aplicável',
};
export function RegistrosDaCelula({ tipo, celula, registros, historico, autores = {} }: {
  tipo: RegistroMatrix['tipo']; celula: string; registros: RevisaoMatrix[]; historico: RevisaoMatrix[]; autores?: Record<string, string>;
}) {
  const lista = registros.filter(r => r.documento.registro?.celula === celula && r.documento.registro.tipo === tipo);
  return <section><h3>{tipo === 'implementacao' ? 'Evidências do implementador' : 'Resultado do avaliador'}</h3>
    {!lista.length && <p>Ainda sem {tipo === 'implementacao' ? 'comprovação' : 'avaliação'} registrada.</p>}
    {lista.slice().reverse().map(r => {
      const v = r.documento.registro!, vigente = registroVigente(v, historico);
      return <article className="mx-card" key={`${r.documento.tipo}-${r.versao}`}>
        <strong>{resultados[v.resultado]} · {vigente ? 'Vigente' : 'Histórico · requer revisão da célula'}</strong>
        <p>{v.justificativa}</p><p>Recorte r{v.revisaoDaDefinicao} · {v.responsavel} · {autores[r.autor] ?? `Usuário ${r.autor}`} · {new Date(r.em).toLocaleString('pt-BR')}</p>
        <ul>{v.evidencias.map((e, i) => <li key={i}>{/^https?:\/\//i.test(e) ? <a href={e} target="_blank" rel="noreferrer">{e}</a> : e}</li>)}</ul>
        {Object.entries(v.shas).map(([repo, sha]) => <p key={repo}>{repo}: <code>{sha}</code></p>)}
      </article>;
    })}<p className="mx-nota">Editar a definição não reescreve a comprovação nem o resultado do avaliador.</p>
  </section>;
}

export function CelulaDaMatrix({ id, edicao: e, editar, itens, registros, historico, fechar, recorte, solicitar, consultarOrigem, aplicacao, autores }: {
  id: string; edicao: EdicaoDaMatrix; editar: boolean; itens: ItemDoProjeto[]; registros: RevisaoMatrix[];
  historico: RevisaoMatrix[]; fechar: () => void; recorte?: string[]; solicitar?: (motivo?: string) => void; consultarOrigem?: () => void; aplicacao?: string; autores?: Record<string, string>;
}) {
  const [aba, setAba] = useState('definicao');
  const [erro, setErro] = useState('');
  useEffect(() => { if (e.previa) fechar(); }, [e.previa, fechar]);
  const m = e.documento.matriz!, c = m.celulas.find(c => c.id === id)!;
  const definicao = lerDefinicao(c.justificativa);
  const permitido = editar && (!recorte || recorte.includes(id));
  const ocupado = e.ocupado || !!e.conflito || !!e.previa;
  const mudar = (parcial: Partial<CelulaMatrix>) => {
    if (!permitido) return;
    e.mudar({ ...e.documento, matriz: { ...m, celulas: m.celulas.map(x => x.id === id ? { ...x, ...parcial } : x) } });
  };
  const titulo = `${m.componentes.find(x => x.id === c.componente)?.nome} × ${m.layers.find(x => x.id === c.layer)?.nome}`;
  const alteradas = celulasAlteradas(e.rascunho.base.matriz, m);
  const tasks = [...new Set([...(e.rascunho.base.matriz?.celulas ?? []), ...m.celulas].filter(x => alteradas.includes(x.id)).flatMap(x => x.responsaveis.map(r => r.item ? `Item ${r.item}` : r.id)))];
  const task = c.responsaveis.map(r => r.item ? `Item ${r.item} · ${itens.find(i => i.numero === r.item)?.titulo ?? r.id}` : r.id).join(', ') || 'Responsável a definir';
  return <DialogoMatrix classe={`mx-cell-editor${e.conflito ? ' mx-conflict-editor' : ''}`} rotulo={e.conflito ? `CONFLITO · ${id.toUpperCase()}` : recorte ? 'IMPACTOS DA TASK · RECORTE' : `EDITAR CÉLULA · ${id.toUpperCase()}`} titulo={e.conflito ? 'Conflito de edição' : titulo}
    contexto={!e.conflito && <><strong>{e.documento.projeto || e.documento.id} / {task}</strong><span>Aplicação {aplicacao ?? e.documento.aplicacao} · r{e.versao}</span></>} fechar={fechar} rodape={<><div className="mx-change-summary"><strong>{alteradas.length} obrigação(ões) alterada(s) · {alteradas.length} célula(s){tasks.length > 0 && ` · afeta ${tasks.join(', ')}`}</strong>
      <small>{e.alterado ? 'Revisar alterações antes de salvar e distribuir às tasks responsáveis.' : `${id} · definição r${e.versao} · nenhuma alteração`}</small></div>
    <div className="mx-actions"><button onClick={fechar}>Fechar</button>{e.conflito && <><button onClick={() => exportarRascunho(e.rascunho)}>Exportar rascunho</button>
      <button className="mx-primary" onClick={e.reaplicarRevisao}>Reaplicar e revisar</button></>}{permitido && !e.previa && !e.conflito &&
      <button className="mx-primary" disabled={ocupado || !e.alterado} onClick={() => {
        const falha = validarCelula(c); setErro(falha); if (!falha) void e.revisar();
      }}>Revisar e salvar →</button>}</div></>}>
    <FalhaDaMatrix edicao={e} solicitar={solicitar} />
    {e.conflito ? <ConflitoDaMatrix edicao={e} acoesNoRodape /> : e.previa ? <PreviaDaMatrix edicao={e} /> : <>
      <div className="mx-columns"><div>
        <p className="mx-alerta">{recorte ? 'Somente as células atribuídas a esta task podem ser editadas. Dependências ficam em consulta.' : 'Esta edição vale somente para esta demanda. O mapa da aplicação permanece como referência.'}</p>
        {!permitido && <p role="status">Somente leitura{editar ? ' · célula de outra task' : ' · salvar exige editor ou dono'}. {!editar && solicitar && <button onClick={() => solicitar('Solicitar acesso de editor à definição da Matrix')}>Solicitar acesso</button>}</p>}
        <p className="mx-muted mx-origin">Origem: {c.origem} {consultarOrigem && <button onClick={consultarOrigem}>Consultar ↗</button>}</p>
        <nav className="mx-tabs" aria-label="Conteúdo da célula">{['definicao', 'evidencias', 'avaliacao'].map(a =>
          <button key={a} aria-pressed={aba === a} onClick={() => setAba(a)}>{a === 'definicao' ? 'Definição' : a === 'evidencias' ? 'Evidências' : 'Avaliação'}</button>)}</nav>
        {aba === 'definicao' && <fieldset disabled={!permitido || ocupado}>
          <label>Obrigação neste projeto<textarea value={definicao.obrigacao} onChange={v => mudar({ justificativa: escreverDefinicao(v.target.value, definicao.motivo) })} /></label>
          <div className="mx-columns mx-pair"><label>Ação<select value={c.obrigacao} onChange={v => mudar({ obrigacao: v.target.value as CelulaMatrix['obrigacao'] })}>
            <option value="alterar">Alterar</option><option value="preservar">Preservar</option><option value="verificar">Verificar</option></select></label>
            <label>Aplicabilidade<select value={c.aplicavel ? 'sim' : 'nao'} onChange={v => mudar({ aplicavel: v.target.value === 'sim' })}>
              <option value="sim">Obrigatório</option><option value="nao">Não se aplica</option></select></label></div>
          {!!itens.length && <label>Task responsável<select value={c.responsaveis.length > 1 ? 'compartilhada' : String(c.responsaveis[0]?.item ?? '')}
            onChange={v => mudar({ responsaveis: v.target.value ? [{ id: c.responsaveis.find(r => r.item === Number(v.target.value))?.id ?? `item-${v.target.value}`, item: Number(v.target.value) }] : [] })}>
            <option value="">A definir</option>{c.responsaveis.length > 1 && <option value="compartilhada">Compartilhada · {c.responsaveis.length} responsáveis</option>}
            {itens.map(i => <option key={i.numero} value={i.numero}>Item {i.numero} · {i.titulo}</option>)}
          </select></label>}
          <details><summary>Origem e responsáveis compartilhados</summary><label>Origem da obrigação<input value={c.origem} onChange={v => mudar({ origem: v.target.value })} /></label><fieldset><legend>Tasks responsáveis</legend>{itens.map(i => {
            const marcado = c.responsaveis.some(r => r.item === i.numero);
            return <label className="mx-check" key={i.numero}><input type="checkbox" checked={marcado} onChange={v => mudar({ responsaveis: v.target.checked ?
              [...c.responsaveis, { id: `item-${i.numero}`, item: i.numero }] : c.responsaveis.filter(r => r.item !== i.numero) })} />Item {i.numero} · {i.titulo}</label>;
          })}
            <label>Outros responsáveis (IDs separados por vírgula)<input value={c.responsaveis.filter(r => r.item === null).map(r => r.id).join(', ')}
              onChange={v => mudar({ responsaveis: [...c.responsaveis.filter(r => r.item !== null), ...v.target.value.split(',').map(s => s.trim()).filter(Boolean).map(id => ({ id, item: null }))] })} /></label>
          </fieldset></details>
        </fieldset>}
        {aba !== 'definicao' && <RegistrosDaCelula tipo={aba === 'evidencias' ? 'implementacao' : 'avaliacao'} celula={id} registros={registros} historico={historico} autores={autores} />}
      </div>{aba === 'definicao' && <div>
        <fieldset disabled={!permitido || ocupado}>
          <label>Justificativa / motivo<textarea value={definicao.motivo} onChange={v => mudar({ justificativa: escreverDefinicao(definicao.obrigacao, v.target.value) })} /></label>
          <label>Como validar<textarea value={c.criterio} onChange={v => mudar({ criterio: v.target.value })} /></label>
          <label>Evidência esperada<textarea value={c.evidenciaEsperada} onChange={v => mudar({ evidenciaEsperada: v.target.value })} /></label>
        </fieldset>
        <div className="mx-card mx-dashed"><strong>Comprovação separada da definição</strong><p>Implementador: atendimento e links. Avaliador: atendido, parcial, não atendido ou não aplicável, com motivo, revisão e SHAs.</p></div>
        {solicitar && <p className="mx-scope-request">Novo impacto fora deste recorte? <button type="button" onClick={() => solicitar('Solicitar atualização de impactos fora deste recorte')}>Solicitar atualização ao Orquestrador ↗</button></p>}
        {erro && <p role="alert" className="mx-alerta">{erro}</p>}
      </div>}</div>
    </>}
  </DialogoMatrix>;
}
