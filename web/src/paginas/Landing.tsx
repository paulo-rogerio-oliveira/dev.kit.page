import { Link } from 'react-router-dom';
import { Cta } from '../componentes/Cta';
import { FormularioDeDemonstracao } from '../componentes/FormularioDeDemonstracao';
import { Midia } from '../componentes/Midia';
import { Secao } from '../componentes/Secao';
import {
  BENEFICIOS, COMPARATIVO, EMPRESAS, HERO, INTEGRACOES, PASSOS, PERGUNTAS, PRODUTOS_COMPARADOS, RECURSOS, ROTULO_DA_DISPONIBILIDADE, SECOES, SEGURANCA,
} from '../conteudo/landing';

/** As seções que aparecem no menu do topo (o início e o fechamento ficam de fora). */
const NO_MENU = SECOES.filter((s) => s.id !== 'inicio' && s.id !== 'comecar' && s.id !== 'contato');

/**
 * A landing pública — a página de venda do dev.kit, na ordem da lista SECOES: hero com a mídia em
 * movimento → benefícios → como funciona (o fluxo em etapas nomeadas) → recursos → comparativo →
 * integrações → segurança → para empresas → contato (o pedido de demonstração) → FAQ → CTA final. O
 * texto e as mídias vêm de conteudo/landing.ts. A página vende o produto e NÃO explica a área logada
 * (US #381): quem tem acesso entra pelo link "Entrar".
 */
export function Landing() {
  return (
    <div className="pagina landing">
      <a className="pular" href="#conteudo">Pular para o conteúdo</a>
      <header className="topo">
        <a className="marca" href="#inicio">dev<span className="marca-ponto">.</span>kit</a>
        <nav aria-label="Seções">
          <ul className="menu">
            {NO_MENU.map((s) => <li key={s.id}><a href={`#${s.id}`}>{s.rotulo}</a></li>)}
          </ul>
          <Cta para="#contato" variante="secundario">Pedir demonstração</Cta>
          <Link className="botao botao-primario" to="/login">Entrar</Link>
        </nav>
      </header>

      <main id="conteudo">
        <section id="inicio" className="hero" aria-labelledby="titulo-inicio">
          <div className="hero-texto">
            <h1 id="titulo-inicio">{HERO.titulo}</h1>
            <p>{HERO.texto}</p>
            <div className="ctas">
              <Cta para="#contato">Quero uma demonstração</Cta>
              <Cta para="#como-funciona" variante="secundario">Ver como funciona</Cta>
            </div>
          </div>
          <Midia midia={HERO.midia} className="hero-midia" />
        </section>

        <Secao id="beneficios" titulo="Por que o dev.kit" subtitulo="O trabalho repetitivo da task com o agente; as decisões com o time.">
          <ul className="grade-cartoes">
            {BENEFICIOS.map((b) => (
              <li key={b.titulo} className="cartao"><h3>{b.titulo}</h3><p>{b.texto}</p></li>
            ))}
          </ul>
        </Secao>

        <Secao id="como-funciona" titulo="Como funciona" subtitulo="Do work item à Pull Request, em sete etapas — e a decisão final é sua.">
          <ol className="passos">
            {PASSOS.map((p) => (
              <li key={p.titulo} className="cartao"><h3>{p.titulo}</h3><p>{p.texto}</p></li>
            ))}
          </ol>
        </Secao>

        <Secao id="recursos" titulo="Recursos" subtitulo="O dev.kit em funcionamento — cada recurso com a sua demonstração.">
          <div className="recursos">
            {RECURSOS.map((recurso) => (
              <article key={recurso.id} className="recurso" aria-labelledby={`recurso-${recurso.id}`}>
                <div>
                  <h3 id={`recurso-${recurso.id}`}>{recurso.titulo}</h3>
                  <p>{recurso.texto}</p>
                </div>
                <Midia midia={recurso.midia} />
              </article>
            ))}
          </div>
        </Secao>

        <Secao id="comparativo" titulo="dev.kit e as alternativas" subtitulo="O que pesa para quem trabalha no Azure DevOps, lado a lado.">
          <div className="tabela-rolavel">
            <table className="comparativo">
              <caption>Comparativo do dev.kit com Copilot coding agent + Azure Boards, Devin e Cursor, conforme as páginas públicas de cada produto.</caption>
              <thead>
                <tr>
                  <th scope="col">Recurso</th>
                  {PRODUTOS_COMPARADOS.map((p) => <th key={p.id} scope="col">{p.nome}</th>)}
                </tr>
              </thead>
              <tbody>
                {COMPARATIVO.map((linha) => (
                  <tr key={linha.recurso}>
                    <th scope="row">{linha.recurso}</th>
                    {PRODUTOS_COMPARADOS.map((p) => (
                      <td key={p.id} className={`valor-${linha.valores[p.id]}`}>{ROTULO_DA_DISPONIBILIDADE[linha.valores[p.id]]}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Secao>

        <Secao id="integracoes" titulo="Integrações" subtitulo="Com as ferramentas que o time já usa.">
          <ul className="grade-cartoes integracoes">
            {INTEGRACOES.map((i) => (
              <li key={i.nome} className="cartao"><h3>{i.nome}</h3><p>{i.texto}</p></li>
            ))}
          </ul>
        </Secao>

        <Secao id="seguranca" titulo="Segurança e privacidade" subtitulo="O agente trabalha com o que o time permite, e nada além.">
          <ul className="grade-cartoes">
            {SEGURANCA.map((s) => (
              <li key={s.titulo} className="cartao"><h3>{s.titulo}</h3><p>{s.texto}</p></li>
            ))}
          </ul>
        </Secao>

        <Secao id="empresas" titulo={EMPRESAS.titulo} subtitulo={EMPRESAS.subtitulo}>
          <ul className="grade-cartoes">
            {EMPRESAS.itens.map((item) => (
              <li key={item.titulo} className="cartao"><h3>{item.titulo}</h3><p>{item.texto}</p></li>
            ))}
          </ul>
          <div className="ctas ctas-da-secao">
            <Cta para="#contato">Falar sobre o plano para empresas</Cta>
          </div>
        </Secao>

        <Secao id="contato" titulo="Quero uma demonstração" subtitulo="Conte quem você é e o que quer ver: mostramos o dev.kit sobre um work item como os seus.">
          <FormularioDeDemonstracao />
        </Secao>

        <Secao id="faq" titulo="Perguntas frequentes">
          <div className="faq">
            {PERGUNTAS.map((p) => (
              <details key={p.pergunta}>
                <summary>{p.pergunta}</summary>
                <p>{p.resposta}</p>
              </details>
            ))}
          </div>
        </Secao>

        <Secao id="comecar" titulo="Ponha um agente na sua próxima task" className="cta-final">
          <div className="ctas">
            <Cta para="#contato">Quero uma demonstração</Cta>
            <Cta para="/login" variante="secundario">Entrar</Cta>
          </div>
        </Secao>
      </main>

      <footer className="rodape">
        <Link to="/login">Entrar</Link>
      </footer>
    </div>
  );
}
