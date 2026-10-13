import { useCallback, useEffect, useState } from 'react';
import { api, ErroDaApi } from '../../api/cliente';
import type { CatalogoNomeado, RelatorioDePublicacao } from '../../api/tipos';

/** O 409 da publicação traz o relatório (com os problemas) no corpo, e não um ProblemDetails. */
function relatorioDoErro(falha: unknown): RelatorioDePublicacao | null {
  if (!(falha instanceof ErroDaApi) || falha.status !== 409) return null;
  const corpo = falha.corpo as Partial<RelatorioDePublicacao> | null;
  return corpo && Array.isArray(corpo.problemas) && Array.isArray(corpo.mudancas) ? corpo as RelatorioDePublicacao : null;
}

/**
 * O Publicar de um catálogo (US #461): primeiro a CONFERÊNCIA (`GET .../publicacao`) — o que muda na próxima
 * release e o que a impede (a dependência sem item, a colisão com o básico, o conteúdo que não abre) —, com o
 * dev.kit mínimo que os itens novos vão exigir; depois a confirmação. Publicada, a página vai para a aba Releases
 * deste catálogo.
 */
export function Publicacao({ token, catalogo, aoPublicar, aoCancelar, tratar }: {
  token: string;
  catalogo: CatalogoNomeado;
  aoPublicar: (slug: string) => void;
  aoCancelar: () => void;
  tratar: (falha: unknown) => string;
}) {
  const [relatorio, setRelatorio] = useState<RelatorioDePublicacao | null>(null);
  const [minDevKit, setMinDevKit] = useState('');
  const [erro, setErro] = useState('');
  const [publicando, setPublicando] = useState(false);

  const conferir = useCallback(async (minimo: number | null) => {
    setErro('');
    try {
      const lido = await api.conferirPublicacao(token, catalogo.slug, minimo);
      setRelatorio(lido);
      setMinDevKit((atual) => (atual === '' ? String(lido.minDevKit) : atual));
    } catch (falha) {
      setErro(tratar(falha));
    }
  }, [token, catalogo.slug, tratar]);

  useEffect(() => {
    void conferir(null);
  }, [conferir]);

  const minimoInformado = minDevKit.trim() === '' ? null : Number(minDevKit);
  const minimoValido = minimoInformado === null || (Number.isInteger(minimoInformado) && minimoInformado >= 0);

  async function publicar() {
    setErro('');
    setPublicando(true);
    try {
      await api.publicarCatalogo(token, catalogo.slug, minimoInformado);
      aoPublicar(catalogo.slug);
    } catch (falha) {
      // A API recusou com o relatório: ele substitui o da conferência e mostra o que impede.
      const recusado = relatorioDoErro(falha);
      if (recusado) setRelatorio(recusado);
      else setErro(tratar(falha));
    } finally {
      setPublicando(false);
    }
  }

  return (
    <section className="cartao publicacao" aria-labelledby="titulo-da-publicacao">
      <h2 id="titulo-da-publicacao" className="titulo-do-cartao">Publicar {catalogo.nome}</h2>
      {erro && <p className="erro" role="alert">{erro}</p>}
      {!relatorio ? (
        !erro && <p className="carregando" role="status">Conferindo o que muda…</p>
      ) : (
        <>
          <p className="nota-da-secao">
            Da versão {relatorio.versaoAtual || '— (nunca publicado)'} para a <strong>{relatorio.proxima}</strong>: a release vira o zip e o
            manifesto que as máquinas baixam na próxima sincronia.
          </p>

          <h3>Mudanças</h3>
          {relatorio.mudancas.length === 0 ? (
            <p className="vazio">Nenhuma mudança desde a última release.</p>
          ) : (
            <ul className="mudancas" aria-label="Mudanças">
              {relatorio.mudancas.map((m) => <li key={m}><code>{m}</code></li>)}
            </ul>
          )}

          {relatorio.problemas.length > 0 && (
            <div className="problemas" role="alert">
              <h3>O que impede a publicação</h3>
              <ul aria-label="Problemas">
                {relatorio.problemas.map((p) => <li key={p}>{p}</li>)}
              </ul>
              <p>Corrija os itens (ou copie para este catálogo o que falta) e confira de novo.</p>
            </div>
          )}

          <div className="acoes">
            <label className="campo-em-linha">
              dev.kit mínimo
              <input type="number" min={0} value={minDevKit} onChange={(e) => setMinDevKit(e.target.value)} aria-invalid={!minimoValido} />
            </label>
            <button className="botao botao-fantasma" type="button" disabled={!minimoValido} onClick={() => void conferir(minimoInformado)}>Conferir de novo</button>
          </div>
          <p className="nota-do-formulario">A máquina com um dev.kit mais antigo que o mínimo continua com a versão que tem desses itens até atualizar.</p>

          <div className="acoes">
            <button
              className="botao botao-primario"
              type="button"
              disabled={!relatorio.podePublicar || relatorio.problemas.length > 0 || !minimoValido || publicando}
              onClick={() => void publicar()}
            >
              Confirmar publicação
            </button>
            <button className="botao botao-fantasma" type="button" onClick={aoCancelar}>Cancelar</button>
          </div>
        </>
      )}
    </section>
  );
}
