import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, mensagemDaFalha } from '../api/cliente';
import type { ItemDoProjeto, ReferenciaDeProposta } from '../api/projetos';
import type { ProjetoDeDesign, VersaoDeDesign } from '../api/tipos';
import { ImagemDaVersao } from './DesignCompartilhado';

/** Identidade portátil do cache, com a mesma chave normalizada devolvida pela API de Design. */
export async function identidadeDoDesign(chave: string): Promise<string> {
  const nome = chave.replace(/[^a-zA-Z0-9_-]/g, '-').replace(/^-+|-+$/g, '') || 'projeto';
  const hash = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(chave));
  return `${nome.slice(0, 48)}-${Array.from(new Uint8Array(hash), b => b.toString(16).padStart(2, '0')).join('').slice(0, 16)}`;
}

interface PropostaResolvida { referencia: ReferenciaDeProposta; projeto?: number; versao?: VersaoDeDesign; erro?: string }

export async function resolverProposta(referencia: ReferenciaDeProposta, projetos: ProjetoDeDesign[]): Promise<PropostaResolvida> {
  const candidatos = (await Promise.all(projetos.map(async p => ({ p, identidade: await identidadeDoDesign(p.chave) }))))
    .filter(p => p.identidade === referencia.projeto).map(p => p.p);
  const exatos = candidatos.flatMap(p => p.telas.filter(t => t.id === referencia.tela).flatMap(t => t.versoes
    .filter(v => v.numero === referencia.versao && v.tipo === 'proposta' && v.sha256.toLowerCase() === referencia.hash.toLowerCase())
    .map(versao => ({ projeto: p.id, versao }))));
  if (exatos.length !== 1) return { referencia, erro: exatos.length > 1
    ? 'A proposta fixada está ambígua entre escopos de Design. Confira o vínculo do planejamento.'
    : 'Proposta fixada indisponível, sem acesso ou com hash divergente. Sincronize o Design; a referência será preservada.' };
  return { referencia, ...exatos[0] };
}

export function PropostasDoPlano({ token, itens, tratar }: { token: string; itens: ItemDoProjeto[]; tratar: (erro: unknown) => void }) {
  const [resolvidas, setResolvidas] = useState<PropostaResolvida[]>([]);
  const [erro, setErro] = useState('');
  const [carregando, setCarregando] = useState(true);
  useEffect(() => {
    let ativo = true;
    setErro(''); setCarregando(true); setResolvidas([]);
    const referencias = itens.flatMap(i => i.design?.propostas ?? []);
    if (referencias.length === 0) { setCarregando(false); return () => { ativo = false; }; }
    void api.projetosDeDesign(token).then(projetos => Promise.all(referencias.map(r => resolverProposta(r, projetos))))
      .then(resultado => { if (ativo) setResolvidas(resultado); })
      .catch(falha => { if (ativo) { setErro(mensagemDaFalha(falha)); tratar(falha); } })
      .finally(() => { if (ativo) setCarregando(false); });
    return () => { ativo = false; };
  }, [itens, token, tratar]);
  return <section aria-label="Propostas visuais do planejamento">
    <h3>Propostas definidas no planejamento</h3>
    <p>Implementador e avaliador recebem a mesma referência. A recaptura real da implementação é entregue separadamente, com o SHA enviado.</p>
    {carregando && <p role="status">Conferindo as propostas fixadas…</p>}
    {erro && <p role="alert">Não foi possível conferir o Design. {erro}</p>}
    {itens.map(item => <section key={item.numero} className="projeto-propostas-item" aria-label={`Propostas do item ${item.numero}`}>
      <h4>{item.numero} · {item.titulo}</h4>
      {!item.design ? <p>Este item não tem proposta visual vinculada.</p> : <>
        <p>Revisão {item.design.revisao} · {item.design.alteraTela ? 'Altera tela' : 'Sem alteração de tela'}</p>
        {item.design.alteraTela && item.design.propostas.length === 0 && <p role="alert">Proposta visual ausente. Complete o planejamento antes da aprovação ou execução.</p>}
        {item.design.propostas.map(ref => {
          const resultado = resolvidas.find(r => r.referencia === ref);
          return <div className="projeto-proposta" key={`${ref.projeto}/${ref.tela}`}>
            <code>{ref.projeto}/{ref.tela}/v{ref.versao}</code><code>SHA-256: {ref.hash}</code>
            {resultado?.erro && <p role="alert">{resultado.erro}</p>}
            {resultado?.versao && <>
              <ImagemDaVersao token={token} versao={resultado.versao} tratar={tratar} />
              <Link className="botao botao-fantasma" to={`/design/${resultado.projeto}/${encodeURIComponent(ref.tela)}/v${ref.versao}?hash=${encodeURIComponent(ref.hash)}`}>Ver proposta fixada · v{ref.versao}</Link>
            </>}
          </div>;
        })}
      </>}
    </section>)}
  </section>;
}
