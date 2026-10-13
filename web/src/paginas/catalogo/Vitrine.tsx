import { useState } from 'react';
import type { ItemDaVitrine, TipoDeItem, Vitrine as VitrineDoCatalogo } from '../../api/tipos';
import { Chips } from '../../componentes/Chips';
import { Selo } from '../../componentes/Selo';
import { NOME_DO_TIPO } from './tipos';

type FiltroDeTipo = 'todos' | TipoDeItem;

/** A busca ignora maiúsculas e acentos. */
const normalizar = (texto: string) => texto.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();

/** Quantos itens a vitrine tem — a pílula do cabeçalho conta os mesmos. */
export function itensDaVitrine(vitrine: VitrineDoCatalogo): number {
  return vitrine.grupos.reduce((soma, g) => soma + g.itens.length, 0);
}

/**
 * A aba Vitrine (US #425, pública; visual da US #461): o catálogo BÁSICO como chega às máquinas, agrupado por
 * tecnologia. A busca (no nome e na descrição), os chips de tipo com a contagem e a categoria da barra lateral
 * filtram no navegador — a vitrine já vem inteira.
 */
export function Vitrine({ vitrine }: { vitrine: VitrineDoCatalogo }) {
  const [busca, setBusca] = useState('');
  const [tipo, setTipo] = useState<FiltroDeTipo>('todos');
  const [categoria, setCategoria] = useState<string | null>(null);

  const termo = normalizar(busca.trim());
  const casaBusca = (i: ItemDaVitrine) => !termo || normalizar(`${i.nome} ${i.descricao}`).includes(termo);
  // A contagem de cada chip respeita a busca e a categoria — é quanto aquele chip mostraria.
  const doRecorte = vitrine.grupos
    .filter((g) => categoria === null || g.tecnologia === categoria)
    .flatMap((g) => g.itens)
    .filter(casaBusca);
  const conta = (t: TipoDeItem) => doRecorte.filter((i) => i.tipo === t).length;

  const grupos = vitrine.grupos
    .filter((g) => categoria === null || g.tecnologia === categoria)
    .map((g) => ({ ...g, itens: g.itens.filter((i) => casaBusca(i) && (tipo === 'todos' || i.tipo === tipo)) }))
    .filter((g) => g.itens.length > 0);

  return (
    <div className="vitrine">
      <div className="barra-de-filtros">
        <input
          className="campo-de-busca"
          type="search"
          aria-label="Buscar no catálogo"
          placeholder="Buscar no catálogo"
          value={busca}
          onChange={(e) => setBusca(e.target.value)}
        />
        <Chips<FiltroDeTipo>
          rotulo="Tipo do item"
          valor={tipo}
          aoMudar={setTipo}
          opcoes={[
            { valor: 'todos', rotulo: 'Todos', contagem: doRecorte.length },
            { valor: 'skill', rotulo: 'Skills', contagem: conta('skill') },
            { valor: 'plan', rotulo: 'Tech plans', contagem: conta('plan') },
            { valor: 'fluxo', rotulo: 'Fluxos', contagem: conta('fluxo') },
          ]}
        />
      </div>

      <div className="vitrine-corpo">
        <nav className="categorias" aria-label="Categorias">
          <h2>Categorias</h2>
          <ul>
            <li>
              <button type="button" className="categoria" aria-pressed={categoria === null} onClick={() => setCategoria(null)}>Todas</button>
            </li>
            {vitrine.grupos.map((g) => (
              <li key={g.tecnologia}>
                <button type="button" className="categoria" aria-pressed={categoria === g.tecnologia} onClick={() => setCategoria(g.tecnologia)}>
                  {g.tecnologia}
                </button>
              </li>
            ))}
          </ul>
        </nav>

        <div className="vitrine-grupos">
          {grupos.length === 0 && <p className="vazio">Nenhum item com esses filtros.</p>}
          {grupos.map((grupo) => (
            <section key={grupo.tecnologia} aria-labelledby={`tecnologia-${grupo.tecnologia}`} className="grupo-do-catalogo">
              <h2><span id={`tecnologia-${grupo.tecnologia}`}>{grupo.tecnologia}</span> <small>{grupo.itens.length}</small></h2>
              <ul className="grade-de-itens">
                {grupo.itens.map((item) => (
                  <li key={`${item.tipo}:${item.nome}`} className="cartao cartao-do-item">
                    <div className="cartao-do-item-topo">
                      <h3>{item.nome}</h3>
                      <Selo variante="sucesso">{NOME_DO_TIPO[item.tipo] ?? item.tipo}</Selo>
                    </div>
                    <p>{item.descricao}</p>
                    <small>versão {item.versao}</small>
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      </div>
    </div>
  );
}
