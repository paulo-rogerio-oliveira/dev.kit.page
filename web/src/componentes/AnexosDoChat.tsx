import { useCallback, useEffect, useRef, useState } from 'react';
import type { AnexoDaFala } from '../api/remoto';
import { useSessao } from '../sessao';
import { ImagemDoDesign } from './DesignCompartilhado';

/** Os marcadores de imagem do chat do dev.kit (`[GITKIT:IMAGEM]` e `[GITKIT:DESIGN]`): no site, o caminho local não abre. */
const marcadores = /\[GITKIT:(IMAGEM|DESIGN)\][\s\S]*?\[\/GITKIT:\1\]/g;

/** O texto da fala sem os marcadores de imagem, e quantos saíram — a imagem que veio só como marcador vira o aviso. */
export function semMarcadores(texto: string) {
  let removidos = 0;
  const limpo = texto.replace(marcadores, () => { removidos++; return ''; }).replace(/\n{3,}/g, '\n\n').trim();
  return { texto: limpo, removidos };
}

/** A sessão para baixar a imagem (a rota é autenticada): o 401 encerra a sessão, como no resto do Board. */
function useTokenDoDesign() {
  const { sessao, sair } = useSessao();
  const tratar = useCallback(() => sair('expirou'), [sair]);
  return { token: sessao?.token ?? '', tratar };
}

/** A miniatura de uma versão de design: o botão que abre a imagem no modal. */
export function MiniaturaDoDesign({ titulo, link, ampliar }: { titulo: string; link: string; ampliar: (anexo: { titulo: string; link: string }) => void }) {
  const { token, tratar } = useTokenDoDesign();
  if (!token) return null;
  return <button type="button" className="br-miniatura" aria-label={`Ampliar ${titulo}`} title={`${titulo} · ampliar`} onClick={() => ampliar({ titulo, link })}>
    <ImagemDoDesign token={token} link={link} alt={titulo} tratar={tratar} className="br-miniatura-img" />
  </button>;
}

/** A imagem inteira num modal (o padrão `br-dialogo` do Board): Esc ou Fechar voltam ao chat. */
export function ImagemAmpliada({ titulo, link, fechar }: { titulo: string; link: string; fechar: () => void }) {
  const { token, tratar } = useTokenDoDesign();
  const botao = useRef<HTMLButtonElement>(null);
  useEffect(() => { botao.current?.focus(); }, []);
  return <div className="br-dialogo-fundo" onClick={e => { if (e.target === e.currentTarget) fechar(); }} onKeyDown={e => { if (e.key === 'Escape') fechar(); }}>
    <div className="br-dialogo br-dialogo-imagem" role="dialog" aria-modal="true" aria-labelledby="br-imagem-titulo">
      <div className="br-row"><h2 id="br-imagem-titulo">{titulo}</h2><button ref={botao} type="button" className="br-btn" onClick={fechar}>Fechar</button></div>
      {token && <ImagemDoDesign token={token} link={link} alt={titulo} tratar={tratar} className="br-imagem-ampliada" />}
    </div>
  </div>;
}

/**
 * As imagens de uma fala do Orquestrador (correções do board remoto): a versão de design que chegou ao site vira a miniatura, e
 * o clique a abre no modal; a que não chegou (o teto do plano, o envio que falhou) vira o aviso, sem link quebrado. O marcador
 * que veio sem anexo (o dev.kit antigo) também vira aviso.
 */
export function AnexosDaFala({ anexos, soltos }: { anexos: AnexoDaFala[]; soltos: number }) {
  const [aberta, setAberta] = useState<{ titulo: string; link: string } | null>(null);
  const comLink = anexos.filter(a => a.url);
  const avisos = anexos.filter(a => !a.url);
  if (!anexos.length && !soltos) return null;
  return <div className="br-anexos">
    {!!comLink.length && <div className="br-miniaturas">
      {comLink.map(a => <MiniaturaDoDesign key={a.url!} titulo={a.titulo} link={a.url!} ampliar={setAberta} />)}
    </div>}
    {avisos.map(a => <p key={a.titulo} className="br-anexo-aviso" role="note">⚠ {a.titulo}: {a.aviso || 'a imagem não chegou ao site.'}</p>)}
    {!anexos.length && soltos > 0 && <p className="br-anexo-aviso" role="note">⚠ {soltos === 1 ? 'A imagem citada fica' : `As ${soltos} imagens citadas ficam`} só no dev.kit desta máquina.</p>}
    {aberta && <ImagemAmpliada {...aberta} fechar={() => setAberta(null)} />}
  </div>;
}
