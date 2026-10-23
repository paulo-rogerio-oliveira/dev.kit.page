import type { CategoriaDeRelato, EstadoDoRelato, Pagina, RelatoResumo } from '../api/tipos';
import { formatar } from '../formatar';
import { Selo, type VarianteDoSelo } from './Selo';

/** As categorias na ordem dos filtros, com o rótulo em português e a cor do selo. */
export const CATEGORIAS_DE_RELATO: { valor: CategoriaDeRelato; rotulo: string; variante: VarianteDoSelo }[] = [
  { valor: 'defeito', rotulo: 'Defeito', variante: 'aviso' },
  { valor: 'sugestao', rotulo: 'Sugestão', variante: 'info' },
  { valor: 'duvida', rotulo: 'Dúvida', variante: 'papel' },
];

/** Os estados na ordem do ciclo (novo → em análise → resolvido). */
export const ESTADOS_DE_RELATO: { valor: EstadoDoRelato; rotulo: string; variante: VarianteDoSelo }[] = [
  { valor: 'novo', rotulo: 'Novo', variante: 'aviso' },
  { valor: 'em-analise', rotulo: 'Em análise', variante: 'info' },
  { valor: 'resolvido', rotulo: 'Resolvido', variante: 'sucesso' },
];

/** O selo da categoria (um valor desconhecido aparece como veio, em neutro). */
export function SeloDaCategoria({ categoria }: { categoria: string }) {
  const c = CATEGORIAS_DE_RELATO.find((x) => x.valor === categoria);
  return <Selo variante={c?.variante ?? 'neutro'}>{c?.rotulo ?? categoria}</Selo>;
}

/** O selo do estado. */
export function SeloDoEstado({ estado }: { estado: string }) {
  const e = ESTADOS_DE_RELATO.find((x) => x.valor === estado);
  return <Selo variante={e?.variante ?? 'neutro'} ponto={estado === 'resolvido'}>{e?.rotulo ?? estado}</Selo>;
}

/**
 * A lista paginada dos relatos (US #602): uma linha por relato, com a categoria e o estado em selo, o começo do texto
 * (que abre o detalhe), quem relatou, a empresa, quando chegou e os indicadores de captura e de resposta.
 */
export function ListaDeRelatos({ dados, aberto, aoAbrir, pagina, aoMudarDePagina, filtrando }: {
  dados: Pagina<RelatoResumo> | null;
  aberto: number | null;
  aoAbrir: (id: number | null) => void;
  pagina: number;
  aoMudarDePagina: (pagina: number) => void;
  /** Há filtro escolhido: a lista vazia diz isso. */
  filtrando: boolean;
}) {
  if (!dados) return <p className="carregando" role="status">Carregando…</p>;
  if (dados.total === 0) return <p className="vazio">{filtrando ? 'Nenhum relato com esses filtros.' : 'Nenhum relato recebido.'}</p>;

  const totalDePaginas = Math.max(1, Math.ceil(dados.total / dados.tamanho));
  return (
    <>
      <div className="tabela-rolavel-simples">
        <table className="tabela tabela-do-cartao" aria-label="Relatos recebidos">
          <thead>
            <tr>
              <th className="numero">Nº</th><th>Categoria</th><th>Estado</th><th>Relato</th><th>Autor</th><th>Empresa</th><th>Recebido em</th>
              <th>Captura</th><th>Resposta</th>
            </tr>
          </thead>
          <tbody>
            {dados.itens.map((r) => (
              <tr key={r.id} className={r.id === aberto ? 'linha-aberta' : undefined}>
                <td className="numero">{r.id}</td>
                <td><SeloDaCategoria categoria={r.categoria} /></td>
                <td><SeloDoEstado estado={r.estado} /></td>
                <td>
                  <button
                    className="link"
                    type="button"
                    aria-expanded={r.id === aberto}
                    aria-label={`Abrir o relato ${r.id}: ${r.resumo}`}
                    onClick={() => aoAbrir(r.id === aberto ? null : r.id)}
                  >
                    {r.resumo || '(sem texto)'}
                  </button>
                </td>
                <td>{r.autor}</td>
                <td>{r.empresa ?? '—'}</td>
                <td>{formatar.dataHora(r.criadoEm)}</td>
                <td>{r.temCaptura ? <Selo variante="neutro">Com captura</Selo> : '—'}</td>
                <td>{r.respondido ? <Selo variante="sucesso">Respondido</Selo> : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="paginacao">
        <button className="botao botao-fantasma" type="button" disabled={pagina <= 1} onClick={() => aoMudarDePagina(pagina - 1)}>Anterior</button>
        <span>Página {pagina} de {totalDePaginas} · {dados.total} relato(s)</span>
        <button className="botao botao-fantasma" type="button" disabled={pagina >= totalDePaginas} onClick={() => aoMudarDePagina(pagina + 1)}>Próxima</button>
      </div>
    </>
  );
}
