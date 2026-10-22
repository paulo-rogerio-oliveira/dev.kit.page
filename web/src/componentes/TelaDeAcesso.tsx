import type { ReactNode } from 'react';
import { Marca } from './Topo';

/**
 * A tela dividida do login, da adesão e da compra (as propostas de marca da versão 5): à esquerda o painel da marca —
 * o rótulo em mono vermelho, o título pesado com o trecho em destaque, o texto e os marcadores com o check —, à
 * direita o formulário num cartão com o fio vermelho no topo. O painel é decorativo e vende o produto; o formulário
 * (o `children`) é que tem o título da página. No celular, o painel vem antes e o formulário logo abaixo.
 */
export function TelaDeAcesso({ rotulo, titulo, destaque, texto, itens, topo, children }: {
  rotulo: string;
  /** O título do painel; o `destaque` é o trecho dele em vermelho. */
  titulo: string;
  destaque: string;
  texto: string;
  itens: ReactNode[];
  /** O cabeçalho da página, quando ela tem um (a compra tem o Topo); sem ele, a marca entra no painel. */
  topo?: ReactNode;
  children: ReactNode;
}) {
  const [antes, depois] = titulo.split(destaque);
  return (
    <div className="pagina tela-de-acesso">
      {topo}
      <div className="acesso">
        <section className="acesso-marca" aria-label="dev.kit">
          {!topo && <Marca />}
          <div className="acesso-chamada">
            <p className="rotulo-da-pagina">{rotulo}</p>
            <p className="acesso-titulo">{antes}<span className="destaque">{destaque}</span>{depois}</p>
            <p className="acesso-texto">{texto}</p>
            <ul className="acesso-itens">
              {itens.map((item, i) => <li key={i}>{item}</li>)}
            </ul>
          </div>
          <p className="acesso-rodape">dev.kit versão 5 · para times no Azure DevOps</p>
        </section>
        <div className="acesso-formulario">{children}</div>
      </div>
    </div>
  );
}
