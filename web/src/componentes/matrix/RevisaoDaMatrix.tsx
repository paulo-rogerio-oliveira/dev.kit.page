import { Link } from 'react-router-dom';
import { useSessao } from '../../sessao';
import type { EdicaoDaMatrix } from './useEdicaoMatrix';
import type { CelulaMatrix, DocumentoMatrix } from '../../api/matrix';
import { celulasAlteradas, exportarRascunho, igual, lerDefinicao } from './modelo';

function campos(c: CelulaMatrix | undefined): Record<string, string> {
  if (!c) return { Situação: 'Célula ausente nesta revisão' };
  const d = lerDefinicao(c.justificativa);
  return { Obrigação: d.obrigacao, Motivo: d.motivo, Ação: c.obrigacao, Aplicabilidade: c.aplicavel ? 'Obrigatório' : 'Não se aplica',
    Responsáveis: c.responsaveis.map(r => r.item ? `Item ${r.item} (${r.id})` : r.id).join(', ') || 'A definir',
    'Como validar': c.criterio, 'Evidência esperada': c.evidenciaEsperada, Origem: c.origem };
}

export function ResumoDoDocumento({ documento: d }: { documento: DocumentoMatrix }) {
  const e = d.matriz ?? d.mapa;
  if (!e) return null;
  return <div><p>Componentes: {e.componentes.map(c => c.nome).join(' · ')}</p><p>Layers: {e.layers.map(l => l.nome).join(' · ')}</p>
    {d.matriz?.celulas.map(c => <details key={c.id}><summary>{e.componentes.find(x => x.id === c.componente)?.nome} × {e.layers.find(x => x.id === c.layer)?.nome} · {c.id}</summary>
      <dl className="mx-values">{Object.entries(campos(c)).map(([nome, valor]) => <div key={nome}><dt>{nome}</dt><dd>{valor}</dd></div>)}</dl></details>)}
    {d.mapa?.relacoes.map(r => <div className="mx-card" key={r.id}><strong>{e.componentes.find(c => c.id === r.origem)?.nome} → {e.componentes.find(c => c.id === r.destino)?.nome}</strong>
      <p>{r.motivo}</p><p>Gatilho: {r.gatilho}</p><p>Orientação: {r.propagacao}</p><p>Validação: {r.verificacao}</p></div>)}
  </div>;
}

export function ReentradaDaMatrix() {
  const { sair } = useSessao();
  return <Link to="/login" state={{ de: location.pathname + location.search }} onClick={() => sair('expirou')}>Entrar novamente</Link>;
}

export function FalhaDaMatrix({ edicao: e }: { edicao: EdicaoDaMatrix }) {
  if (e.conflito) return null;
  return <>{e.erro && <p role="alert" className="mx-alerta">{e.erro}
    {e.status === 401 && <> <ReentradaDaMatrix /></>}
    <button type="button" onClick={() => exportarRascunho(e.rascunho)}>Exportar rascunho</button></p>}</>;
}

export function ConflitoDaMatrix({ edicao: e, acoesNoRodape = false }: { edicao: EdicaoDaMatrix; acoesNoRodape?: boolean }) {
  if (!e.conflito) return null;
  const local = e.documento, servidor = e.conflito.documento;
  const ids = celulasAlteradas(e.rascunho.base.matriz, local.matriz);
  return <section aria-label="Conflito de edição">
    <h3>Existe uma revisão mais recente</h3>
    <p className="mx-alerta">Seu rascunho r{e.versao} · servidor r{e.conflito.versao}. Nenhum conteúdo foi substituído automaticamente.</p>
    {local.matriz ? ids.map(id => {
      const l = local.matriz?.celulas.find(c => c.id === id), r = servidor.matriz?.celulas.find(c => c.id === id);
      const b = e.rascunho.base.matriz?.celulas.find(c => c.id === id), a = campos(l), s = campos(r), anterior = campos(b);
      const nomes = [...new Set([...Object.keys(a), ...Object.keys(s)])].filter(k => a[k] !== anterior[k] || s[k] !== anterior[k]);
      return <div className="mx-card" key={id}><h4>{id} · {local.matriz?.componentes.find(c => c.id === l?.componente)?.nome} × {local.matriz?.layers.find(c => c.id === l?.layer)?.nome}</h4>
        {nomes.length ? nomes.map(nome => <div key={nome}><strong>{nome}</strong><div className="mx-columns">
          <label>Seu rascunho r{e.versao}<span className="mx-value">{a[nome] ?? 'Removido'}</span></label>
          <label>Servidor r{e.conflito!.versao}<span className="mx-value">{s[nome] ?? 'Removido'}</span></label></div></div>) : <p>Os eixos desta célula foram alterados. Confira os nomes e orientações na revisão.</p>}
      </div>;
    }) : <div className="mx-columns"><div><h4>Seu rascunho</h4><ResumoDoDocumento documento={local} /></div>
      <div><h4>Servidor</h4><ResumoDoDocumento documento={servidor} /></div></div>}
    <p>Reaplicar preserva os campos do servidor que você não editou. Quando o mesmo campo mudou nos dois clientes, usa o valor do seu rascunho mostrado acima. Revise novamente antes de salvar.</p>
    <p className="mx-nota">Autorização continua necessária. Comparar revisões ou reentrar não concede acesso de editor.</p>
    {!acoesNoRodape && <div className="mx-actions"><button onClick={() => exportarRascunho(e.rascunho)}>Exportar rascunho</button>
      <button className="mx-primary" onClick={e.reaplicarRevisao}>Reaplicar e revisar</button></div>}
  </section>;
}

export function PreviaDaMatrix({ edicao: e }: { edicao: EdicaoDaMatrix }) {
  if (!e.previa) return null;
  const m = e.previa.mudancas;
  return <section aria-label="Revisar alterações antes de salvar"><h3>Revisar alterações antes de salvar</h3>
    <p>Revisão r{e.versao} → r{e.previa.revisao.versao}</p>
    <div className="mx-card"><h4>Resumo da revisão</h4><p>{m.celulas.length} célula(s) alterada(s): {m.celulas.join(', ') || 'nenhuma obrigação do projeto'}</p>
      <p>Tasks afetadas: {m.responsaveis.join(', ') || 'nenhuma'}</p>
      <p className="mx-nota">Ao salvar, a revisão fica disponível às tasks afetadas pela mesma API. Definições e avaliações anteriores permanecem no histórico; somente células alteradas precisam de nova avaliação.</p>
      {m.celulas.map(id => {
        const antes = e.rascunho.base.matriz?.celulas.find(c => c.id === id), depois = e.documento.matriz?.celulas.find(c => c.id === id);
        const a = campos(antes), b = campos(depois);
        return <article className="mx-card" key={id}><h4>{id}</h4>{Object.keys({ ...a, ...b }).filter(k => !igual(a[k], b[k])).map(nome =>
          <div key={nome}><strong>{nome}</strong><p>Antes: {a[nome] ?? 'Não definida'}</p><p>Depois: {b[nome] ?? 'Removida'}</p></div>)}</article>;
      })}
      {!e.documento.matriz && <ResumoDoDocumento documento={e.documento} />}</div>
    <div className="mx-actions"><button onClick={e.voltar} disabled={e.ocupado}>Voltar à edição</button>
      <button className="mx-primary" disabled={e.ocupado} onClick={() => void e.salvar()}>Salvar r{e.previa.revisao.versao} e distribuir</button></div>
  </section>;
}
