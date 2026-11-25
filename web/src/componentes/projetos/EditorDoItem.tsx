import { useEffect, useRef, useState } from 'react';
import { api, ErroDaApi } from '../../api/cliente';
import type { ItemDoProjeto, Projeto } from '../../api/projetos';
import { useSessao } from '../../sessao';
import { fluxoDoItem } from '../OndasDoProjeto';

export function EditorDoItem({ projeto, item, fechar, salvo, tratar }: {
  projeto: Projeto; item: ItemDoProjeto | null; fechar: () => void; salvo: (p: Projeto) => void; tratar: (e: unknown) => string;
}) {
  const { sessao } = useSessao();
  const dialogo = useRef<HTMLDialogElement>(null);
  const requisicao = useRef<AbortController | null>(null);
  const [titulo, setTitulo] = useState(item?.titulo ?? '');
  const [descricao, setDescricao] = useState(item?.descricao ?? '');
  const [fluxo, setFluxo] = useState(item?.fluxo ?? '');
  const [modelo, setModelo] = useState(`${item?.agente ?? ''}\n${item?.modelo ?? ''}`);
  const [dependencias, setDependencias] = useState(item?.dependencias.join(', ') ?? '');
  const [componentes, setComponentes] = useState(item?.componentes.join(', ') ?? '');
  const [prioridade, setPrioridade] = useState(item?.prioridade ?? 0);
  const [versao, setVersao] = useState(projeto.versao);
  const [atual, setAtual] = useState<Projeto | null>(null);
  const [erro, setErro] = useState('');
  const [conflito, setConflito] = useState(false);
  const [ocupado, setOcupado] = useState(false);
  const opcoes = atual ?? projeto;
  const fluxos = [...new Set(['', opcoes.fluxoPadrao ?? '', ...opcoes.itens.map(i => i.fluxo)])];
  const modelos = [...new Set(['\n', `${opcoes.agente ?? ''}\n${opcoes.modelo ?? ''}`,
    ...(opcoes.modelos ?? []).map(m => `${m.agente}\n${m.modelo}`), ...opcoes.itens.map(i => `${i.agente ?? ''}\n${i.modelo ?? ''}`)])];
  useEffect(() => {
    const anterior = document.activeElement as HTMLElement | null;
    const overflow = document.documentElement.style.overflow;
    document.documentElement.style.overflow = 'hidden'; dialogo.current?.showModal();
    dialogo.current?.querySelector<HTMLInputElement>('input')?.focus();
    return () => { requisicao.current?.abort(); document.documentElement.style.overflow = overflow; anterior?.focus(); };
  }, []);
  async function executar(acao: (signal: AbortSignal) => Promise<void>) {
    requisicao.current?.abort(); const c = new AbortController(); requisicao.current = c;
    setOcupado(true); setErro('');
    try { await acao(c.signal); }
    catch (e) { if (!c.signal.aborted) { setErro(tratar(e)); if (e instanceof ErroDaApi && e.status === 409) setConflito(true); } }
    finally { if (!c.signal.aborted) setOcupado(false); }
  }
  async function gravar() {
    if (dependencias.trim() && !/^\d+(\s*,\s*\d+)*$/.test(dependencias.trim())) { setErro('Dependências: informe números separados por vírgula.'); return; }
    await executar(async signal => {
      const [agente, modeloEscolhido] = modelo.split('\n');
      const p = await api.editarItemDoProjeto(sessao!.token, projeto.id, item?.numero ?? 0, {
        versaoEsperada: versao, titulo, descricao, fluxo, agente, modelo: modeloEscolhido,
        dependencias: dependencias.trim() ? dependencias.split(',').map(Number) : [],
        componentes: componentes.split(',').map(c => c.trim()).filter(Boolean), prioridade,
      }, signal);
      if (!signal.aborted) salvo(p);
    });
  }
  return <dialog ref={dialogo} className="projeto-editor" aria-labelledby="titulo-editor-item"
    onKeyDown={e => {
      if (e.key !== 'Tab') return;
      const controles = [...e.currentTarget.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href], summary')];
      const primeiro = controles[0], ultimo = controles.at(-1);
      if (e.shiftKey && document.activeElement === primeiro) { e.preventDefault(); ultimo?.focus(); }
      else if (!e.shiftKey && document.activeElement === ultimo) { e.preventDefault(); primeiro?.focus(); }
    }} onCancel={e => { e.preventDefault(); if (!ocupado) fechar(); }}>
    <form onSubmit={e => { e.preventDefault(); void gravar(); }}>
      <header><h2 id="titulo-editor-item">{item ? `Editar item ${item.numero}` : 'Novo item'}</h2><button type="button" aria-label="Fechar editor" disabled={ocupado} onClick={fechar}>×</button></header>
      <p className="projetos-nota">{item ? `${item.titulo} · Onda ${item.onda} · Planejado` : 'Acrescentar ao plano do projeto'}</p>
      <fieldset disabled={ocupado}>
        <label>Título<input required maxLength={200} value={titulo} onChange={e => setTitulo(e.target.value)} /></label>
        <label>Descrição<textarea rows={3} maxLength={2000} value={descricao} onChange={e => setDescricao(e.target.value)} /></label>
        <div className="projeto-editor-pares">
          <label>Fluxo<select value={fluxo} onChange={e => setFluxo(e.target.value)}>{fluxos.map(f => <option key={f} value={f}>{f ? fluxoDoItem(opcoes, { ...opcoes.itens[0], fluxo: f }) : 'Padrão do projeto'}</option>)}</select></label>
          <label>Agente e modelo<select value={modelo} onChange={e => setModelo(e.target.value)}>{modelos.map(m => <option key={m} value={m}>{m === '\n' ? 'Sem modelo selecionado' : m.replace('\n', ' · ')}</option>)}</select></label>
        </div>
        <div className="projeto-editor-pares dependencias"><label>Dependências<input value={dependencias} onChange={e => setDependencias(e.target.value)} /></label>
          <label>Prioridade<select value={prioridade} onChange={e => setPrioridade(Number(e.target.value))}>{[0, 1, 2, 3, 4].map(n => <option key={n} value={n}>{n || 'Sem prioridade'}</option>)}</select></label></div>
        <label>Componentes<input value={componentes} onChange={e => setComponentes(e.target.value)} /></label>
      </fieldset>
      <p className="projetos-nota">A escolha de modelo é deste item. Salvar preserva o modelo dos outros itens.<br />Alterações no plano recalculam as ondas quando necessário e exigem nova aprovação.</p>
      {erro && <p role="alert" className="erro">{erro}</p>}
      {conflito && <div className="projeto-conflito"><p>Seu rascunho foi preservado.</p>
        <button type="button" disabled={ocupado} onClick={() => void executar(async signal => { const p = await api.projeto(sessao!.token, projeto.id, signal); if (!signal.aborted) setAtual(p); })}>Reler versão atual</button>
        {atual && <><p>Versão {atual.versao}: {atual.itens.find(i => i.numero === item?.numero)?.titulo ?? 'Item ausente'}</p>
          <details><summary>Comparar versão atual</summary><pre>{JSON.stringify(atual.itens.find(i => i.numero === item?.numero), null, 2)}</pre></details>
          <button type="button" disabled={ocupado} onClick={() => { setVersao(atual.versao); setConflito(false); setErro(''); }}>Aplicar rascunho à versão relida</button></>}
      </div>}
      <footer><button className="botao botao-fantasma" type="button" disabled={ocupado} onClick={fechar}>Cancelar</button>
        <button className="botao botao-primario" type="submit" disabled={ocupado || conflito}>{ocupado ? 'Salvando…' : 'Salvar item'}</button></footer>
    </form>
  </dialog>;
}
