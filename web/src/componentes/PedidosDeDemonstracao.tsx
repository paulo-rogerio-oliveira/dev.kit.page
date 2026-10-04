import { useCallback, useEffect, useState } from 'react';
import { api } from '../api/cliente';
import type { DemonstracaoResumo, Pagina } from '../api/tipos';
import { formatar } from '../formatar';

/** Quantos pedidos por página. */
export const TAMANHO_DA_PAGINA = 10;

/**
 * Os pedidos de demonstração no dashboard: a lista paginada (do mais novo para o mais antigo) e a
 * exclusão — o atendimento ao pedido de eliminação do titular (LGPD). Um erro da API vai para quem
 * compõe (o dashboard trata o 401 voltando ao login).
 */
export function PedidosDeDemonstracao({ token, aoFalhar }: { token: string; aoFalhar: (falha: unknown) => void }) {
  const [pagina, setPagina] = useState(1);
  const [dados, setDados] = useState<Pagina<DemonstracaoResumo> | null>(null);

  const carregar = useCallback(() => api.demonstracoes(token, pagina, TAMANHO_DA_PAGINA).then(setDados).catch(aoFalhar), [token, pagina, aoFalhar]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  async function excluir(pedido: DemonstracaoResumo) {
    if (!window.confirm(`Excluir o pedido de ${pedido.nome} (${pedido.email})? Não há como desfazer.`)) return;
    try {
      await api.excluirDemonstracao(token, pedido.id);
      // A última linha de uma página que não é a primeira: volta uma página.
      if (dados && dados.itens.length === 1 && pagina > 1) setPagina(pagina - 1);
      else await carregar();
    } catch (falha) {
      aoFalhar(falha);
    }
  }

  const totalDePaginas = dados ? Math.max(1, Math.ceil(dados.total / dados.tamanho)) : 1;

  return (
    <section aria-labelledby="titulo-demonstracoes">
      <h2 id="titulo-demonstracoes">Pedidos de demonstração</h2>
      {!dados ? (
        <p className="carregando" role="status">Carregando…</p>
      ) : dados.total === 0 ? (
        <p className="vazio">Nenhum pedido de demonstração recebido.</p>
      ) : (
        <>
          <table className="tabela">
            <thead>
              <tr><th>Quando</th><th>Nome</th><th>E-mail</th><th>Empresa</th><th>Mensagem</th><th><span className="visualmente-oculto">Ações</span></th></tr>
            </thead>
            <tbody>
              {dados.itens.map((p) => (
                <tr key={p.id}>
                  <td>{formatar.dataHora(p.recebidoEm)}</td>
                  <td>{p.nome}</td>
                  <td><a href={`mailto:${p.email}`}>{p.email}</a></td>
                  <td>{p.empresa || '—'}</td>
                  <td>{p.mensagem || '—'}</td>
                  <td>
                    <button className="botao botao-fantasma" type="button" onClick={() => void excluir(p)} aria-label={`Excluir o pedido de ${p.nome}`}>
                      Excluir
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="paginacao">
            <button className="botao botao-fantasma" type="button" disabled={pagina <= 1} onClick={() => setPagina(pagina - 1)}>Anterior</button>
            <span>Página {pagina} de {totalDePaginas} · {dados.total} pedido(s)</span>
            <button className="botao botao-fantasma" type="button" disabled={pagina >= totalDePaginas} onClick={() => setPagina(pagina + 1)}>Próxima</button>
          </div>
        </>
      )}
    </section>
  );
}
