import { useState } from 'react';
import type { CelulaMatrix, RegistroMatrix, RevisaoMatrix } from '../../api/matrix';
import type { ItemDoProjeto } from '../../api/projetos';
import { DialogoMatrix } from './DialogoMatrix';
import { ConflitoDaMatrix, FalhaDaMatrix, PreviaDaMatrix } from './RevisaoDaMatrix';
import { escreverDefinicao, exportarRascunho, lerDefinicao, registroVigente, validarCelula } from './modelo';
import type { EdicaoDaMatrix } from './useEdicaoMatrix';

const resultados: Record<RegistroMatrix['resultado'], string> = {
  atendido: 'Atendido', parcial: 'Parcial', 'nao-atendido': 'Não atendido', 'nao-aplicavel': 'Não aplicável',
};
export function RegistrosDaCelula({ tipo, celula, registros, historico }: {
  tipo: RegistroMatrix['tipo']; celula: string; registros: RevisaoMatrix[]; historico: RevisaoMatrix[];
}) {
  const lista = registros.filter(r => r.documento.registro?.celula === celula && r.documento.registro.tipo === tipo);
  return <section><h3>{tipo === 'implementacao' ? 'Evidências do implementador' : 'Resultado do avaliador'}</h3>
    {!lista.length && <p>Ainda sem {tipo === 'implementacao' ? 'comprovação' : 'avaliação'} registrada.</p>}
    {lista.slice().reverse().map(r => {
      const v = r.documento.registro!, vigente = registroVigente(v, historico);
      return <article className="mx-card" key={`${r.documento.tipo}-${r.versao}`}>
        <strong>{resultados[v.resultado]} · {vigente ? 'Vigente' : 'Histórico · requer revisão da célula'}</strong>
        <p>{v.justificativa}</p><p>Recorte r{v.revisaoDaDefinicao} · {v.responsavel} · autor {r.autor} · {new Date(r.em).toLocaleString('pt-BR')}</p>
        <ul>{v.evidencias.map((e, i) => <li key={i}>{/^https?:\/\//i.test(e) ? <a href={e} target="_blank" rel="noreferrer">{e}</a> : e}</li>)}</ul>
        {Object.entries(v.shas).map(([repo, sha]) => <p key={repo}>{repo}: <code>{sha}</code></p>)}
      </article>;
    })}<p className="mx-nota">Editar a definição não reescreve a comprovação nem o resultado do avaliador.</p>
  </section>;
}

export function CelulaDaMatrix({ id, edicao: e, editar, itens, registros, historico, fechar, recorte, solicitar }: {
  id: string; edicao: EdicaoDaMatrix; editar: boolean; itens: ItemDoProjeto[]; registros: RevisaoMatrix[];
  historico: RevisaoMatrix[]; fechar: () => void; recorte?: string[]; solicitar?: () => void;
}) {
  const [aba, setAba] = useState('definicao');
  const [erro, setErro] = useState('');
  const m = e.documento.matriz!, c = m.celulas.find(c => c.id === id)!;
  const definicao = lerDefinicao(c.justificativa);
  const permitido = editar && (!recorte || recorte.includes(id));
  const ocupado = e.ocupado || !!e.conflito || !!e.previa;
  const mudar = (parcial: Partial<CelulaMatrix>) => {
    if (!permitido) return;
    e.mudar({ ...e.documento, matriz: { ...m, celulas: m.celulas.map(x => x.id === id ? { ...x, ...parcial } : x) } });
  };
  const titulo = `${m.componentes.find(x => x.id === c.componente)?.nome} × ${m.layers.find(x => x.id === c.layer)?.nome}`;
  return <DialogoMatrix titulo={titulo} contexto={`${e.documento.projeto || e.documento.id} · Aplicação ${e.documento.aplicacao} · definição r${e.versao}`} fechar={fechar} rodape={<><span>{id} · definição r{e.versao}{e.alterado && ' · rascunho preservado'}</span>
    <div className="mx-actions"><button onClick={fechar}>Fechar</button>{e.conflito && <><button onClick={() => exportarRascunho(e.rascunho)}>Exportar rascunho</button>
      <button className="mx-primary" onClick={e.reaplicarRevisao}>Reaplicar e revisar</button></>}{permitido && !e.previa && !e.conflito &&
      <button className="mx-primary" disabled={ocupado || !e.alterado} onClick={() => {
        const falha = validarCelula(c); setErro(falha); if (!falha) void e.revisar();
      }}>Revisar e salvar →</button>}</div></>}>
    <FalhaDaMatrix edicao={e} />
    {e.conflito ? <ConflitoDaMatrix edicao={e} acoesNoRodape /> : e.previa ? <PreviaDaMatrix edicao={e} /> : <>
      <div className="mx-columns"><div>
        <p className="mx-alerta">{recorte ? 'Somente as células atribuídas a esta task podem ser editadas. Dependências ficam em consulta.' : 'Esta edição vale somente para esta demanda. O mapa da aplicação permanece como referência.'}</p>
        {!permitido && <p role="status">Somente leitura{editar ? ' · célula de outra task' : ' · salvar exige editor ou dono'}.</p>}
        <p className="mx-muted">Origem: {c.origem}</p>
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
        {aba !== 'definicao' && <RegistrosDaCelula tipo={aba === 'evidencias' ? 'implementacao' : 'avaliacao'} celula={id} registros={registros} historico={historico} />}
      </div><div>
        <fieldset disabled={!permitido || ocupado}>
          <label>Justificativa / motivo<textarea value={definicao.motivo} onChange={v => mudar({ justificativa: escreverDefinicao(definicao.obrigacao, v.target.value) })} /></label>
          <label>Como validar<textarea value={c.criterio} onChange={v => mudar({ criterio: v.target.value })} /></label>
          <label>Evidência esperada<textarea value={c.evidenciaEsperada} onChange={v => mudar({ evidenciaEsperada: v.target.value })} /></label>
        </fieldset>
        <div className="mx-card mx-dashed"><strong>Comprovação separada da definição</strong><p>Implementador: atendimento e links. Avaliador: atendido, parcial, não atendido ou não aplicável, com motivo, revisão e SHAs.</p></div>
        {solicitar && <p className="mx-nota">Novo impacto fora deste recorte? <button onClick={solicitar}>Solicitar atualização ao Orquestrador ↗</button></p>}
        {erro && <p role="alert" className="mx-alerta">{erro}</p>}
      </div></div>
    </>}
  </DialogoMatrix>;
}
