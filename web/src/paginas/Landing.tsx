import { Link } from 'react-router-dom';
import { BaixarDevKit } from '../componentes/BaixarDevKit';
import { Cta } from '../componentes/Cta';
import { FormularioDeDemonstracao } from '../componentes/FormularioDeDemonstracao';
import { Janela } from '../componentes/Janela';
import { Midia } from '../componentes/Midia';
import { Secao } from '../componentes/Secao';
import {
  AMOSTRA_DO_BOARD, BENEFICIOS, CABECALHOS, CATALOGO, COMPARATIVO, CONCEITOS, EMPRESAS, FABRICA, HERO, INTEGRACOES, MENU, PASSOS, PERGUNTAS,
  PLANOS, PRODUTOS_COMPARADOS, RECURSOS, RODAPE, ROTULO_DA_DISPONIBILIDADE, rotuloDaSecao, SEGURANCA, tituloDaJanela,
} from '../conteudo/landing';

/** A marca "dK" do cabeçalho e do rodapé (o quadrado da cor da marca e o ponto vermelho). */
function Marca() {
  return (
    <a className="marca marca-landing" href="#inicio">
      <span className="marca-icone" aria-hidden="true">dK</span><span>dev<span className="marca-ponto">.</span>kit</span>
    </a>
  );
}

/** Os ícones dos cartões de "Por que o dev.kit" — traço simples, na cor da marca. */
function Icone({ tipo }: { tipo: 'ramo' | 'nota' | 'relogio' }) {
  const desenho = {
    ramo: <><circle cx="6" cy="5" r="2" /><circle cx="18" cy="5" r="2" /><circle cx="12" cy="19" r="2" /><path d="M6 7v2a4 4 0 0 0 4 4h4a4 4 0 0 0 4-4V7M12 13v4" /></>,
    nota: <><circle cx="12" cy="12" r="9" /><path d="m8 12 3 3 5-6" /></>,
    relogio: <><circle cx="12" cy="13" r="8" /><path d="M12 9v4l2 2M9 2h6" /></>,
  }[tipo];
  return (
    <span className="icone" aria-hidden="true">
      <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">{desenho}</svg>
    </span>
  );
}

/**
 * A amostra do Board no hero (US #405): o "Precisa de você", o avaliador com a nota e as horas do dia,
 * com o selo "Nota 92/100 · Aceito". É uma imagem feita de HTML (sem foto de tela a carregar), lida como
 * uma figura só pelo leitor de tela.
 */
function AmostraDoBoard() {
  const b = AMOSTRA_DO_BOARD;
  return (
    <figure className="amostra" role="img" aria-label={`Amostra do Board do dev.kit: o ${b.orquestrador.autor} diz "${b.orquestrador.texto}"; Precisa de você com ${b.precisaDeVoce.length} itens; ${b.nota.valor}, ${b.nota.texto.toLowerCase()}; ${b.horas.texto}.`}>
      <Janela titulo={b.titulo} className="amostra-janela" aria-hidden="true">
        <div className="amostra-corpo">
          <div className="amostra-lateral"><span /><span /><span /><span /></div>
          <div className="amostra-conteudo">
            <div className="amostra-cartao amostra-orquestrador">
              <span className="amostra-o">O</span>
              <span className="amostra-texto"><b>{b.orquestrador.autor}</b><small>{b.orquestrador.texto}</small></span>
            </div>
            <div className="amostra-cartao">
              <strong>Precisa de você <span className="amostra-contagem">3</span></strong>
              {b.precisaDeVoce.map((item, i) => (
                <div key={item.titulo} className="amostra-item">
                  <span className={`amostra-avatar amostra-avatar-${i}`} />
                  <span className="amostra-texto"><b>{item.titulo}</b><small>{item.estado}</small></span>
                  <span className={i === 0 ? 'amostra-acao amostra-acao-forte' : 'amostra-acao'}>{item.acao}</span>
                </div>
              ))}
            </div>
            <div className="amostra-linha">
              <div className="amostra-cartao amostra-avaliador">
                <small>Avaliação</small>
                <span className="amostra-notas">{b.avaliador.notas.map((n) => <span key={n} className={n >= 90 ? 'nota-boa' : 'nota-baixa'}>{n}</span>)}</span>
                <small>{b.avaliador.titulo} · {b.avaliador.gatilho}</small>
              </div>
              <div className="amostra-cartao">
                <strong>{b.horas.titulo}</strong>
                <span className="amostra-horas">{b.horas.partes.map((p, i) => <span key={i} style={{ flexGrow: p }} className={`hora-${i}`} />)}</span>
                <small>{b.horas.texto}</small>
              </div>
            </div>
          </div>
        </div>
      </Janela>
      <div className="amostra-selo" aria-hidden="true">
        <span className="amostra-check">✓</span>
        <span><b>{b.nota.valor}</b><small>{b.nota.texto}</small></span>
      </div>
    </figure>
  );
}

/**
 * A landing pública — a página de venda do dev.kit, na disposição da proposta da home (US #467):
 * cabeçalho (marca, menu, Entrar e Pedir demonstração), hero com o selo, o título com "Pull Request" em
 * destaque, os dois botões, o link discreto de download e a amostra do Board → como funciona em sete
 * etapas → recursos (as abas 01–04 e, em blocos alternados, o texto com os marcadores e a mídia numa
 * moldura de janela com a legenda abaixo) → por que o dev.kit (três cartões) → os conceitos (agente, skill e
 * rule, cada um com o exemplo, e o exemplo de ponta a ponta) → a fábrica agêntica e o time agêntico → as seções que o menu, o
 * rodapé e o contato apontam (catálogo, comparativo, integrações, segurança, empresas, planos, contato,
 * FAQ) → CTA escuro → rodapé. O texto e as mídias vêm de conteudo/landing.ts, na ordem de SECOES. A
 * página vende o produto e NÃO explica a área logada.
 */
export function Landing() {
  const [antes, depois] = HERO.titulo.split(HERO.destaque);
  return (
    <div className="pagina landing">
      <a className="pular" href="#conteudo">Pular para o conteúdo</a>
      <header className="topo">
        <Marca />
        <nav aria-label="Seções" className="nav-landing">
          <ul className="menu">
            {MENU.map((id) => <li key={id}><a href={`#${id}`}>{rotuloDaSecao(id)}</a></li>)}
          </ul>
          <div className="nav-acoes">
            <Link className="link-entrar" to="/login">Entrar</Link>
            <Cta para="#contato">Pedir demonstração</Cta>
          </div>
        </nav>
      </header>

      <main id="conteudo">
        <section id="inicio" className="hero" aria-labelledby="titulo-inicio">
          <div className="hero-texto">
            <p className="selo"><span className="selo-ponto" aria-hidden="true" />{HERO.selo}</p>
            <h1 id="titulo-inicio">{antes}<span className="destaque">{HERO.destaque}</span>{depois}</h1>
            <p>{HERO.texto}</p>
            <div className="ctas">
              <Cta para="#contato">Quero uma demonstração <span aria-hidden="true">→</span></Cta>
              <Cta para="#como-funciona" variante="secundario">Ver como funciona</Cta>
            </div>
            <BaixarDevKit discreto />
          </div>
          <AmostraDoBoard />
        </section>

        <Secao id="como-funciona" rotulo={CABECALHOS.comoFunciona.rotulo} titulo={CABECALHOS.comoFunciona.titulo} subtitulo={CABECALHOS.comoFunciona.subtitulo}>
          <ol className="etapas">
            {PASSOS.map((p) => (
              <li key={p.titulo}><h3>{p.titulo}</h3><p>{p.texto}</p></li>
            ))}
          </ol>
        </Secao>

        <Secao id="recursos" rotulo={CABECALHOS.recursos.rotulo} titulo={CABECALHOS.recursos.titulo} subtitulo={CABECALHOS.recursos.subtitulo}>
          <nav className="abas-dos-recursos" aria-label="Recursos em destaque">
            <ol>
              {RECURSOS.map((r) => (
                <li key={r.id}>
                  <a href={`#recurso-${r.id}`}>
                    <span className="aba-numero">{r.numero}</span> {r.aba}{r.novo && <span className="recurso-novo">novo</span>}
                  </a>
                </li>
              ))}
            </ol>
          </nav>
          <div className="recursos">
            {RECURSOS.map((recurso) => (
              <article key={recurso.id} id={`recurso-${recurso.id}`} className="recurso" aria-labelledby={`titulo-recurso-${recurso.id}`}>
                <div className="recurso-texto">
                  <p className="recurso-numero" aria-hidden="true">{recurso.numero}</p>
                  <h3 id={`titulo-recurso-${recurso.id}`}>{recurso.titulo}</h3>
                  <p>{recurso.texto}</p>
                  <ul className="marcadores">
                    {recurso.marcadores.map((m) => <li key={m}>{m}</li>)}
                  </ul>
                </div>
                <figure className="recurso-midia">
                  <Janela titulo={tituloDaJanela(recurso.janela)} escura>
                    <Midia midia={recurso.midia} />
                  </Janela>
                  <figcaption>{recurso.legenda}</figcaption>
                </figure>
              </article>
            ))}
          </div>
        </Secao>

        <Secao id="beneficios" rotulo={CABECALHOS.porQue.rotulo} titulo={CABECALHOS.porQue.titulo} className="secao-tinta">
          <ul className="grade-cartoes grade-tres">
            {BENEFICIOS.map((b) => (
              <li key={b.titulo} className="cartao"><Icone tipo={b.icone} /><h3>{b.titulo}</h3><p>{b.texto}</p></li>
            ))}
          </ul>
        </Secao>

        <Secao id="conceitos" rotulo={CONCEITOS.rotulo} titulo={CONCEITOS.titulo} subtitulo={CONCEITOS.subtitulo}>
          <div className="conceitos">
            {CONCEITOS.itens.map((c) => (
              <article key={c.id} id={`conceito-${c.id}`} className="cartao conceito" aria-labelledby={`titulo-conceito-${c.id}`}>
                <h3 id={`titulo-conceito-${c.id}`}>{c.titulo}</h3>
                <p>{c.texto}</p>
                <ul className="marcadores">
                  {c.marcadores.map((m) => <li key={m}>{m}</li>)}
                </ul>
                <figure className="exemplo">
                  <figcaption>Exemplo · <code>{c.exemplo.arquivo}</code></figcaption>
                  <pre><code>{c.exemplo.codigo}</code></pre>
                </figure>
              </article>
            ))}
          </div>
          <h3 className="subtitulo-da-secao" id="titulo-exemplo-de-ponta-a-ponta">{CONCEITOS.exemplo.titulo}</h3>
          <ol className="etapas etapas-quatro" aria-labelledby="titulo-exemplo-de-ponta-a-ponta">
            {CONCEITOS.exemplo.passos.map((p) => (
              <li key={p.titulo}><h4>{p.titulo}</h4><p>{p.texto}</p></li>
            ))}
          </ol>
        </Secao>

        <Secao id="fabrica" rotulo={FABRICA.rotulo} titulo={FABRICA.titulo} subtitulo={FABRICA.subtitulo} className="secao-tinta">
          <div className="fabrica">
            <article aria-labelledby="titulo-fabrica-agentica">
              <h3 id="titulo-fabrica-agentica">{FABRICA.fabrica.titulo}</h3>
              <p>{FABRICA.fabrica.texto}</p>
              <ol className="etapas etapas-seis" aria-label="Estações da fábrica">
                {FABRICA.fabrica.estacoes.map((e) => (
                  <li key={e.titulo}><h4>{e.titulo}</h4><p>{e.texto}</p></li>
                ))}
              </ol>
              <ul className="marcadores">
                {FABRICA.fabrica.marcadores.map((m) => <li key={m}>{m}</li>)}
              </ul>
            </article>
            <article aria-labelledby="titulo-time-agentico">
              <h3 id="titulo-time-agentico">{FABRICA.time.titulo}</h3>
              <p>{FABRICA.time.texto}</p>
              <ul className="grade-cartoes grade-tres" aria-label="Papéis do time agêntico">
                {FABRICA.time.papeis.map((p) => (
                  <li key={p.titulo} className="cartao"><h4>{p.titulo}</h4><p>{p.texto}</p></li>
                ))}
              </ul>
            </article>
          </div>
        </Secao>

        <Secao id="catalogo" rotulo="Catálogo" titulo={CATALOGO.titulo} subtitulo={CATALOGO.subtitulo} className="secao-tinta">
          <ul className="grade-cartoes grade-tres">
            {CATALOGO.itens.map((item) => (
              <li key={item.titulo} className="cartao"><h3>{item.titulo}</h3><p>{item.texto}</p></li>
            ))}
          </ul>
          <div className="ctas ctas-da-secao">
            <Cta para="/catalogo" variante="secundario">Ver o catálogo completo</Cta>
          </div>
        </Secao>

        <Secao id="comparativo" rotulo="Comparativo" titulo="dev.kit e as alternativas" subtitulo="O que pesa para quem trabalha no Azure DevOps, lado a lado.">
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

        <Secao id="integracoes" rotulo="Ecossistema" titulo="Integrações" subtitulo="Com as ferramentas que o time já usa.">
          <ul className="grade-cartoes integracoes">
            {INTEGRACOES.map((i) => (
              <li key={i.nome} className="cartao"><h3>{i.nome}</h3><p>{i.texto}</p></li>
            ))}
          </ul>
        </Secao>

        <Secao id="seguranca" rotulo="Segurança" titulo="Segurança e privacidade" subtitulo="O agente trabalha com o que o time permite, e nada além." className="secao-tinta">
          <ul className="grade-cartoes">
            {SEGURANCA.map((s) => (
              <li key={s.titulo} className="cartao"><h3>{s.titulo}</h3><p>{s.texto}</p></li>
            ))}
          </ul>
        </Secao>

        <Secao id="empresas" rotulo="Empresas" titulo={EMPRESAS.titulo} subtitulo={EMPRESAS.subtitulo}>
          <ul className="grade-cartoes">
            {EMPRESAS.itens.map((item) => (
              <li key={item.titulo} className="cartao"><h3>{item.titulo}</h3><p>{item.texto}</p></li>
            ))}
          </ul>
          <div className="ctas ctas-da-secao">
            <Cta para="#contato">Falar sobre o plano para empresas</Cta>
          </div>
        </Secao>

        <Secao id="planos" rotulo="Planos" titulo="Planos" subtitulo="Comece com o teste grátis; assine para você ou para o time.">
          <ul className="grade-cartoes grade-tres planos">
            {PLANOS.map((plano) => (
              <li key={plano.id} className="cartao plano" aria-labelledby={`plano-${plano.id}`}>
                <h3 id={`plano-${plano.id}`}>{plano.nome}</h3>
                <p>{plano.resumo}</p>
                <ul>{plano.itens.map((item) => <li key={item}>{item}</li>)}</ul>
                {plano.id === 'teste' && <BaixarDevKit rotulo="Testar grátis por 14 dias" />}
                {plano.id === 'individual' && <Cta para="/comprar">Assinar o individual</Cta>}
                {plano.id === 'empresas' && <Cta para="#contato" variante="secundario">Falar sobre o plano para empresas</Cta>}
              </li>
            ))}
          </ul>
        </Secao>

        <Secao id="contato" rotulo="Contato" titulo="Quero uma demonstração" subtitulo="Conte quem você é e o que quer ver: mostramos o dev.kit sobre um work item como os seus." className="secao-tinta">
          <FormularioDeDemonstracao />
        </Secao>

        <Secao id="faq" rotulo="Perguntas" titulo="Perguntas frequentes">
          <div className="faq">
            {PERGUNTAS.map((p) => (
              <details key={p.pergunta}>
                <summary>{p.pergunta}</summary>
                <p>{p.resposta}</p>
              </details>
            ))}
          </div>
        </Secao>

        <Secao id="comecar" titulo={CABECALHOS.ctaFinal.titulo} subtitulo={CABECALHOS.ctaFinal.texto} className="cta-final">
          <div className="ctas">
            <Cta para="#contato">Quero uma demonstração</Cta>
            <Cta para="/login" variante="secundario">Entrar</Cta>
          </div>
        </Secao>
      </main>

      <footer className="rodape">
        <div className="rodape-marca"><Marca /><span>{CABECALHOS.rodape}</span></div>
        <nav aria-label="Rodapé">
          <ul>
            {RODAPE.map((id) => <li key={id}><a href={`#${id}`}>{rotuloDaSecao(id)}</a></li>)}
            <li><Link to="/login">Entrar</Link></li>
          </ul>
        </nav>
      </footer>
    </div>
  );
}
