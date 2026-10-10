import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import type { ReleaseDoCatalogo } from '../api/tipos';
import { Topo } from '../componentes/Topo';
import { formatar } from '../formatar';
import { useSessao } from '../sessao';

/**
 * As releases do catálogo BÁSICO (US #425, /catalogo-base) — só do admin, e só leitura: a versão (a tag
 * catalogo-vN), quantos itens e retirados o manifesto traz, o tamanho e o hash do zip. Publicar é do repositório
 * do dev.kit (a tag dispara o workflow), e não desta página.
 */
export function CatalogoBase() {
  const { sessao, sair } = useSessao();
  const [releases, setReleases] = useState<ReleaseDoCatalogo[] | null>(null);
  const [erro, setErro] = useState('');

  useEffect(() => {
    api.releasesDoCatalogo(sessao!.token).then(setReleases).catch((falha) => {
      if (falha instanceof ErroDaApi && falha.status === 401) sair('expirou');
      else setErro(falha instanceof ErroDaApi && falha.status === 503 ? 'O catálogo está indisponível no momento.' : 'Falha ao consultar a API.');
    });
  }, [sessao, sair]);

  return (
    <div className="pagina">
      <Topo secao="catálogo básico">
        <Link className="botao botao-fantasma" to="/dashboard">Dashboard</Link>
        <Link className="botao botao-fantasma" to="/catalogo">Vitrine</Link>
      </Topo>
      <main className="painel">
        <section aria-labelledby="titulo-catalogo-base">
          <h1 id="titulo-catalogo-base">Releases do catálogo básico</h1>
          <p className="nota-da-secao">Cada release catalogo-vN publica o zip e o manifesto; as máquinas pegam só o que mudou desde a versão que têm.</p>
          {erro && <p className="erro" role="alert">{erro}</p>}
          {!releases ? (
            !erro && <p className="carregando" role="status">Carregando…</p>
          ) : releases.length === 0 ? (
            <p className="vazio">Nenhuma release do catálogo publicada.</p>
          ) : (
            <table className="tabela">
              <thead>
                <tr><th>Release</th><th>Publicada em</th><th className="numero">Itens</th><th className="numero">Retirados</th><th className="numero">Tamanho</th><th>SHA-256</th></tr>
              </thead>
              <tbody>
                {releases.map((r) => (
                  <tr key={r.tag}>
                    <td>{r.tag}</td>
                    <td>{formatar.dataHora(r.publicadaEm)}</td>
                    <td className="numero">{r.itens}</td>
                    <td className="numero">{r.retirados}</td>
                    <td className="numero">{formatar.megabytes(r.tamanhoBytes)}</td>
                    <td><code>{r.sha256 ? r.sha256.slice(0, 12) : '—'}</code></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      </main>
    </div>
  );
}
