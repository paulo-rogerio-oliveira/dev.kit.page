import { useEffect, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../../api/cliente';
import type { CatalogoNomeado, ReleaseDoCatalogo } from '../../api/tipos';
import { salvarArquivo } from '../../arquivos';
import { BotaoCopiar } from '../../componentes/BotaoCopiar';
import { Selo } from '../../componentes/Selo';
import { formatar } from '../../formatar';

/** O SHA-256 abreviado (os 12 primeiros) — o inteiro vai no copiar. */
const abreviado = (sha: string | null) => (sha ? sha.slice(0, 12) : '—');

/**
 * A aba Releases (US #425 era a página /catalogo-base; US #461) — só do admin, e só leitura: a release ATUAL do
 * catálogo escolhido (a tag, quando saiu, os itens e os retirados do manifesto, o tamanho e o SHA-256 do zip, o
 * manifesto e o zip) e o histórico. Publicar é da aba Catálogos.
 */
export function Releases({ token, slug, catalogos, aoMudarCatalogo, aoExpirar }: {
  token: string;
  slug: string;
  catalogos: CatalogoNomeado[];
  aoMudarCatalogo: (slug: string) => void;
  aoExpirar: () => void;
}) {
  const [releases, setReleases] = useState<ReleaseDoCatalogo[] | null>(null);
  const [manifesto, setManifesto] = useState<{ tag: string; texto: string } | null>(null);
  const [erro, setErro] = useState('');

  useEffect(() => {
    let vivo = true;
    setReleases(null);
    setManifesto(null);
    setErro('');
    api.releasesDoCatalogo(token, slug).then((r) => vivo && setReleases(r)).catch((falha) => {
      if (!vivo) return;
      if (falha instanceof ErroDaApi && falha.status === 401) aoExpirar();
      else setErro(falha instanceof ErroDaApi && falha.status === 503 ? 'O catálogo está indisponível no momento.' : mensagemDaFalha(falha));
    });
    return () => {
      vivo = false;
    };
  }, [token, slug, aoExpirar]);

  async function verManifesto(release: ReleaseDoCatalogo) {
    setErro('');
    try {
      const lido = await api.manifestoDaRelease(token, slug, release.versao);
      setManifesto({ tag: release.tag, texto: JSON.stringify(lido, null, 2) });
    } catch (falha) {
      setErro(mensagemDaFalha(falha));
    }
  }

  async function baixarZip(release: ReleaseDoCatalogo) {
    setErro('');
    try {
      salvarArquivo(await api.baixarRelease(token, slug, release));
    } catch (falha) {
      setErro(mensagemDaFalha(falha));
    }
  }

  const atual = releases?.[0];
  // O catálogo da URL pode não estar na lista (a lista falhou, ou ainda não chegou): ele entra como opção mesmo assim.
  const opcoes = catalogos.some((c) => c.slug === slug) ? catalogos : [{ slug, nome: slug === 'basico' ? 'Catálogo básico' : slug }, ...catalogos];

  return (
    <div className="releases">
      <div className="releases-topo">
        <p className="nota-da-secao">
          Cada release <code>catalogo-vN</code> publica o zip e o manifesto. As máquinas baixam só o que mudou desde a versão que têm.
        </p>
        <label className="campo-em-linha">
          Catálogo
          <select value={slug} onChange={(e) => aoMudarCatalogo(e.target.value)}>
            {opcoes.map((c) => <option key={c.slug} value={c.slug}>{c.nome}</option>)}
          </select>
        </label>
      </div>

      {erro && <p className="erro" role="alert">{erro}</p>}
      {!releases ? (
        !erro && <p className="carregando" role="status">Carregando…</p>
      ) : !atual ? (
        <p className="vazio">Nenhuma release deste catálogo publicada ainda.</p>
      ) : (
        <>
          <article className="cartao release-atual" aria-label={`Release atual ${atual.tag}`}>
            <div className="release-identidade">
              <p className="release-tag"><code>{atual.tag}</code> <Selo variante="sucesso">Atual</Selo></p>
              <p className="release-data">Publicada em {formatar.dataHora(atual.publicadaEm)}</p>
            </div>
            <dl className="release-numeros">
              <div><dt>Itens</dt><dd>{atual.itens}</dd></div>
              <div><dt>Retirados</dt><dd>{atual.retirados}</dd></div>
              <div><dt>Tamanho</dt><dd>{formatar.megabytes(atual.tamanhoBytes)}</dd></div>
              <div>
                <dt>SHA-256</dt>
                <dd className="valor-copiavel">
                  <code className="codigo" title={atual.sha256 ?? undefined}>{atual.sha256 ? `${abreviado(atual.sha256)}…` : '—'}</code>
                  {atual.sha256 && <BotaoCopiar texto={atual.sha256} rotulo={`Copiar o SHA-256 da ${atual.tag}`} />}
                </dd>
              </div>
            </dl>
            <div className="acoes release-acoes">
              <button className="botao botao-fantasma" type="button" onClick={() => void verManifesto(atual)}>Ver manifesto</button>
              <button className="botao botao-fantasma" type="button" onClick={() => void baixarZip(atual)}>⤓ Baixar zip</button>
            </div>
          </article>

          {manifesto && (
            <section className="cartao manifesto" role="dialog" aria-label={`Manifesto da ${manifesto.tag}`}>
              <div className="manifesto-topo">
                <h2>Manifesto da {manifesto.tag}</h2>
                <button className="botao botao-fantasma botao-pequeno" type="button" onClick={() => setManifesto(null)}>Fechar</button>
              </div>
              <pre className="trace" tabIndex={0}>{manifesto.texto}</pre>
            </section>
          )}

          <section className="cartao cartao-de-tabela" aria-labelledby="titulo-historico">
            <h2 id="titulo-historico" className="titulo-do-cartao">Histórico</h2>
            <div className="tabela-rolavel-simples">
              <table className="tabela tabela-do-cartao">
                <thead>
                  <tr>
                    <th>Release</th><th>Publicada em</th><th className="numero">Itens</th><th className="numero">Retirados</th>
                    <th className="numero">Tamanho</th><th>SHA-256</th>
                  </tr>
                </thead>
                <tbody>
                  {releases.map((r) => (
                    <tr key={r.tag}>
                      <td><code>{r.tag}</code></td>
                      <td>{formatar.dataHora(r.publicadaEm)}</td>
                      <td className="numero">{r.itens}</td>
                      <td className="numero">{r.retirados}</td>
                      <td className="numero">{formatar.megabytes(r.tamanhoBytes)}</td>
                      <td><code className="texto-mudo">{abreviado(r.sha256)}</code></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <p className="rodape-do-cartao">Releases anteriores aparecem aqui conforme forem publicadas.</p>
          </section>
        </>
      )}
    </div>
  );
}
