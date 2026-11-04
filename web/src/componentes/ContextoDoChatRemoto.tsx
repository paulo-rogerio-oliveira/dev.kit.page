import { useEffect, useRef, useState } from 'react';

export interface ContextoDoChat { fluxo: string; item: string; repositorio: string; branch: string }
export const contextoVazio: ContextoDoChat = { fluxo: '', item: '', repositorio: '', branch: '' };
export function textoComContexto(texto: string, contexto: ContextoDoChat) {
  const linhas = [contexto.fluxo && `/ ${contexto.fluxo}`, contexto.item && `# ${contexto.item}`,
    contexto.repositorio && `@ ${contexto.repositorio}`, contexto.branch && `Branch existente: ${contexto.branch}`].filter(Boolean);
  return [...linhas, texto.trim()].join('\n');
}

type Atalho = '/' | '#' | '@';
export function ContextoDoChatRemoto({ contexto, alterar, atalho, abrir, desabilitado }: {
  contexto: ContextoDoChat; alterar: (contexto: ContextoDoChat) => void;
  atalho: Atalho | null; abrir: (atalho: Atalho | null) => void; desabilitado: boolean;
}) {
  const [rascunho, setRascunho] = useState(contexto);
  const painel = useRef<HTMLDivElement>(null);
  const anterior = useRef<HTMLElement | null>(null);
  useEffect(() => {
    if (!atalho) { anterior.current?.focus(); return; }
    anterior.current = document.activeElement as HTMLElement;
    setRascunho(contexto);
    painel.current?.querySelector('input')?.focus();
  }, [atalho, contexto]);
  const campo = atalho === '/' ? 'fluxo' : atalho === '#' ? 'item' : 'repositorio';
  const rotulo = atalho === '/' ? 'Fluxo ou design' : atalho === '#' ? 'Work item' : 'Repositório';
  function salvar() {
    alterar({ ...rascunho, [campo]: rascunho[campo].trim(), branch: rascunho.branch.trim() }); abrir(null);
  }
  return <>
    <button type="button" disabled={desabilitado} aria-label="Trocar fluxo" aria-expanded={atalho === '/'} onClick={() => abrir(atalho === '/' ? null : '/')}>
      / {contexto.fluxo || 'Fluxo simples · padrão'}
    </button>
    <button type="button" disabled={desabilitado} aria-label="# Work item" aria-expanded={atalho === '#'} onClick={() => abrir(atalho === '#' ? null : '#')}>
      # {contexto.item || 'Work item'}
    </button>
    <button type="button" disabled={desabilitado} aria-label="@ Repositório" aria-expanded={atalho === '@'} onClick={() => abrir(atalho === '@' ? null : '@')}>
      @ {contexto.repositorio || 'Repositório'} ▾
    </button>
    {atalho && <div className="br-contexto" role="dialog" aria-label={rotulo} ref={painel} onKeyDown={e => {
      if (e.key === 'Escape') { e.stopPropagation(); abrir(null); }
      if (e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); salvar(); }
    }}>
      <label>{rotulo}<input aria-label={rotulo} value={rascunho[campo]} maxLength={200}
        inputMode={atalho === '#' ? 'numeric' : 'text'} pattern={atalho === '#' ? '[0-9]*' : undefined}
        onChange={e => setRascunho({ ...rascunho, [campo]: atalho === '#' ? e.target.value.replace(/\D/g, '') : e.target.value })} /></label>
      {atalho === '@' && <label>Branch existente<input aria-label="Branch existente" maxLength={250} value={rascunho.branch}
        onChange={e => setRascunho({ ...rascunho, branch: e.target.value })} /></label>}
      <p>Informe o contexto do dev.kit. Deixe vazio para usar a conversa atual.</p>
      <button type="button" onClick={salvar}>Aplicar contexto</button>
      <button type="button" onClick={() => abrir(null)}>Cancelar</button>
    </div>}
  </>;
}
