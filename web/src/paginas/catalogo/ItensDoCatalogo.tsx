import { useCallback, useEffect, useMemo, useState } from 'react';
import { api } from '../../api/cliente';
import type { CatalogoNomeado, EscopoDoItem, ItemEditavel, TipoDeItem } from '../../api/tipos';
import { Chips } from '../../componentes/Chips';
import { Selo } from '../../componentes/Selo';
import { EditorDeArtefato } from './EditorDeArtefato';
import { NOME_DO_TIPO, NOME_DO_TIPO_NO_PLURAL, rotuloDaCategoria } from './tipos';

type FiltroDeTipo = 'todos' | TipoDeItem;

/** O item aberto no editor: um da lista, ou um novo do tipo escolhido. */
type Aberto = { item: ItemEditavel } | { novo: TipoDeItem } | null;

const ROTULO_DO_ESCOPO: Record<EscopoDoItem, string> = { base: 'Escopo: base', empresa: 'Escopo: empresa', projeto: 'Escopo: projeto' };

/**
 * A aba ITENS do catálogo (US #687, proposta admin-catalogo v010): a lista compacta por categoria (a busca procura também
 * no conteúdo; o filtro por tipo e pelo escopo base/empresa/projeto; o selo de rascunho) e, ao lado, o EDITOR DE
 * ARTEFATO do item escolhido — ou do item novo. A lista mostra os itens de todos os catálogos; o escopo padrão é o base
 * (o catálogo básico, que toda máquina sincroniza).
 */
export function ItensDoCatalogo({ token, catalogos, novo, itemInicial = null, aoNovoUsado, aoMudarCatalogos, aoVerVitrine, tratar }: {
  token: string;
  catalogos: CatalogoNomeado[];
  /** O tipo pedido pelo "Novo item" do cabeçalho; nulo sem pedido. */
  novo: TipoDeItem | null;
  /** O item do link direto (?item=<id>): abre no editor em vez do primeiro da lista. */
  itemInicial?: number | null;
  aoNovoUsado: () => void;
  aoMudarCatalogos: () => Promise<unknown>;
  aoVerVitrine: () => void;
  tratar: (falha: unknown) => string;
}) {
  const [itens, setItens] = useState<ItemEditavel[] | null>(null);
  const [busca, setBusca] = useState('');
  const [buscaAplicada, setBuscaAplicada] = useState('');
  const [tipo, setTipo] = useState<FiltroDeTipo>('todos');
  const [escopo, setEscopo] = useState<EscopoDoItem | ''>('base');
  const [aberto, setAberto] = useState<Aberto>(null);
  const [erro, setErro] = useState('');

  const carregar = useCallback(
    () => api.todosOsItens(token, { busca: buscaAplicada, escopo }).then((lista) => { setItens(lista); setErro(''); }).catch((falha) => setErro(tratar(falha))),
    [token, buscaAplicada, escopo, tratar],
  );

  useEffect(() => {
    void carregar();
  }, [carregar]);

  // A busca vai à API depois de uma pausa (ela procura no conteúdo, que a lista não traz).
  useEffect(() => {
    const espera = setTimeout(() => setBuscaAplicada(busca.trim()), 300);
    return () => clearTimeout(espera);
  }, [busca]);

  // O "Novo item" do cabeçalho abre o editor vazio do tipo pedido.
  useEffect(() => {
    if (!novo) return;
    setAberto({ novo });
    aoNovoUsado();
  }, [novo, aoNovoUsado]);

  // Sem item aberto, o primeiro da lista (o editor da proposta nunca fica vazio).
  useEffect(() => {
    if (!aberto && itens && itens.length > 0) setAberto({ item: itens.find((i) => i.id === itemInicial) ?? itens[0] });
  }, [aberto, itens, itemInicial]);

  const visiveis = (itens ?? []).filter((i) => tipo === 'todos' || i.tipo === tipo);
  const conta = (t: TipoDeItem) => (itens ?? []).filter((i) => i.tipo === t).length;
  const grupos = useMemo(() => {
    const porCategoria = new Map<string, ItemEditavel[]>();
    for (const item of visiveis) porCategoria.set(item.categoria, [...(porCategoria.get(item.categoria) ?? []), item]);
    return [...porCategoria.entries()];
  }, [visiveis]);

  const idAberto = aberto && 'item' in aberto ? aberto.item.id : null;
  const basico = catalogos.find((c) => c.basico);
  const aoMudar = useCallback((salvo: ItemEditavel) => {
    setItens((lista) => {
      const sem = (lista ?? []).filter((i) => i.id !== salvo.id);
      return [...sem, { ...salvo, conteudo: null }].sort((a, b) => a.categoria.localeCompare(b.categoria, 'pt-BR') || a.nome.localeCompare(b.nome));
    });
    setAberto((a) => (a && 'novo' in a ? { item: salvo } : a));
    void aoMudarCatalogos();
  }, [aoMudarCatalogos]);

  return (
    <div className="itens-e-editor">
      <section className="cartao lista-de-itens" aria-label="Itens do catálogo">
        <input className="campo-de-busca" type="search" aria-label="Buscar por nome, descrição ou conteúdo" placeholder="Buscar por nome, descrição ou conteúdo"
          value={busca} onChange={(e) => setBusca(e.target.value)} />
        <div className="lista-filtros">
          <Chips<FiltroDeTipo>
            rotulo="Tipo do item"
            valor={tipo}
            aoMudar={setTipo}
            opcoes={[
              { valor: 'todos', rotulo: 'Todos', contagem: (itens ?? []).length },
              { valor: 'skill', rotulo: NOME_DO_TIPO_NO_PLURAL.skill, contagem: conta('skill') },
              { valor: 'plan', rotulo: NOME_DO_TIPO_NO_PLURAL.plan, contagem: conta('plan') },
              { valor: 'fluxo', rotulo: NOME_DO_TIPO_NO_PLURAL.fluxo, contagem: conta('fluxo') },
            ]}
          />
          <select aria-label="Filtrar por escopo" className="seletor-de-escopo" value={escopo} onChange={(e) => { setAberto(null); setEscopo(e.target.value as EscopoDoItem | ''); }}>
            {(Object.keys(ROTULO_DO_ESCOPO) as EscopoDoItem[]).map((e) => <option key={e} value={e}>{ROTULO_DO_ESCOPO[e]}</option>)}
            <option value="">Escopo: todos</option>
          </select>
        </div>
        {erro && <p className="erro" role="alert">{erro}</p>}
        {!itens ? (
          <p className="carregando" role="status">Carregando…</p>
        ) : visiveis.length === 0 ? (
          <p className="vazio">{busca ? 'Nenhum item com essa busca.' : 'Nenhum item neste escopo.'}</p>
        ) : (
          <div className="grupos-de-itens">
            {grupos.map(([categoria, doGrupo]) => (
              <div key={categoria} className="grupo-de-itens" role="group" aria-label={rotuloDaCategoria(categoria)}>
                <p className="grupo-titulo"><span>{rotuloDaCategoria(categoria)}</span><span>{doGrupo.length}</span></p>
                <ul>
                  {doGrupo.map((i) => (
                    <li key={i.id}>
                      <button type="button" className={`linha-do-item${idAberto === i.id ? ' linha-do-item-aberta' : ''}`} aria-current={idAberto === i.id}
                        onClick={() => setAberto({ item: i })}>
                        <span className="linha-do-item-texto">
                          <code className="nome-do-item">{i.nome}</code>
                          <small>{i.descricao}</small>
                        </span>
                        {(i.situacao === 'novo' || i.situacao === 'alterado') && <span className="marca-rascunho">rascunho</span>}
                        <Selo variante={i.tipo === 'skill' ? 'sucesso' : i.tipo === 'plan' ? 'papel' : 'aviso'}>{NOME_DO_TIPO[i.tipo]}</Selo>
                        <span className="texto-mudo versao-do-item">{i.versaoPublicada > 0 ? `v${i.versaoPublicada}` : '—'}</span>
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        )}
      </section>

      {aberto && (
        <EditorDeArtefato
          key={'item' in aberto ? `item-${aberto.item.id}` : `novo-${aberto.novo}`}
          token={token}
          item={'item' in aberto ? aberto.item : null}
          tipo={'item' in aberto ? aberto.item.tipo : aberto.novo}
          catalogos={catalogos}
          catalogoDoNovo={basico?.slug ?? 'basico'}
          todos={itens ?? []}
          aoMudar={aoMudar}
          aoDescartar={() => { setAberto(null); void carregar(); void aoMudarCatalogos(); }}
          aoVerVitrine={aoVerVitrine}
          tratar={tratar}
        />
      )}
    </div>
  );
}
