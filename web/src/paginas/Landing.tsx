import { useEffect, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { BaixarDevKit } from '../componentes/BaixarDevKit';
import { Cta } from '../componentes/Cta';
import { FormularioDeDemonstracao } from '../componentes/FormularioDeDemonstracao';
import { Janela } from '../componentes/Janela';
import { Midia } from '../componentes/Midia';
import { Secao } from '../componentes/Secao';
import {
  AMOSTRA_DO_BOARD, AMOSTRA_DO_CELULAR, BENEFICIOS, BRACOS, CABECALHOS, CATALOGO, COMPARATIVO, CONCEITOS, DEMONSTRACAO_DOS_BRACOS, EMPRESAS,
  EVIDENCIAS, FABRICA, HERO, INTEGRACOES, MENU, PASSOS, PERGUNTAS, PLACAR, PLANOS, PRODUTOS_COMPARADOS, RECURSOS, RODAPE,
  ROTULO_DA_DISPONIBILIDADE, rotuloDaSecao, SEGURANCA, tituloDaJanela, type Braco, type NomeDoIcone,
} from '../conteudo/landing';
import './Landing.css';

/** A marca "dK" do cabeçalho e do rodapé (o quadrado da cor da marca e o ponto vermelho). */
function Marca() {
  return (
    <a className="marca marca-landing" href="#inicio">
      <span className="marca-icone" aria-hidden="true">dK</span><span>dev<span className="marca-ponto">.</span>kit</span>
    </a>
  );
}

/** O desenho de cada ícone de traço (24 × 24, na cor do texto em volta). */
const DESENHOS: Record<NomeDoIcone, ReactNode> = {
  monitor: <><rect x="3" y="4" width="18" height="12" rx="2" /><path d="M8 20h8M12 16v4" /></>,
  servico: <><rect x="3" y="4" width="18" height="7" rx="2" /><rect x="3" y="13" width="18" height="7" rx="2" /><path d="M7 7.5h.01M7 16.5h.01" /></>,
  celular: <><rect x="7" y="2.5" width="10" height="19" rx="2.5" /><path d="M11 18.5h2" /></>,
  terminal: <><rect x="3" y="4" width="18" height="16" rx="2" /><path d="m7 9 3 3-3 3M13 15h4" /></>,
  nota: <><circle cx="12" cy="12" r="9" /><path d="m8 12 3 3 5-6" /></>,
  ramo: <><circle cx="6" cy="5" r="2" /><circle cx="18" cy="5" r="2" /><circle cx="12" cy="19" r="2" /><path d="M6 7v2a4 4 0 0 0 4 4h4a4 4 0 0 0 4-4V7M12 13v4" /></>,
  relogio: <><circle cx="12" cy="13" r="8" /><path d="M12 9v4l2 2M9 2h6" /></>,
  arbitro: <><path d="M12 3v18M8 21h8M5 7h14" /><path d="m5 7-3 6a3 3 0 0 0 6 0L5 7ZM19 7l-3 6a3 3 0 0 0 6 0l-3-6Z" /></>,
  camadas: <><path d="m12 3 9 5-9 5-9-5 9-5Z" /><path d="m3 13 9 5 9-5" /></>,
  livro: <><path d="M4 4.5A2.5 2.5 0 0 1 6.5 2H20v17H6.5A2.5 2.5 0 0 0 4 21.5Z" /><path d="M4 21.5A2.5 2.5 0 0 1 6.5 19H20" /></>,
  entrega: <><rect x="5" y="4" width="14" height="17" rx="2" /><path d="M9 2.5h6v3H9zM9 13l2 2 4-4" /></>,
};

/** Um ícone de traço simples, decorativo (o texto ao lado é que diz o que é). */
function Icone({ nome, tamanho = 20 }: { nome: NomeDoIcone; tamanho?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={tamanho} height={tamanho} fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {DESENHOS[nome]}
    </svg>
  );
}

const ONDA = { integrada: 'lp-onda-integrada', rodando: 'lp-onda-rodando', proxima: undefined } as const;

/**
 * A vitrine do hero (a "Amostra do Board" da US #405, refeita): o Board do desktop — a fala do Orquestrador, o projeto
 * em ondas, o "Precisa de você" e a avaliação — com o Board do dev.kit.page no celular por cima, pedindo o segundo
 * fator, e a nota aceita embaixo. É uma imagem feita de HTML (nítida e sem foto a carregar), lida como uma figura só.
 */
function Vitrine() {
  const b = AMOSTRA_DO_BOARD;
  const c = AMOSTRA_DO_CELULAR;
  const descricao = `Amostra do dev.kit no desktop e no celular. No Board do desktop, o ${b.orquestrador.autor} diz "${b.orquestrador.texto}"; `
    + `o projeto ${b.projeto.nome} está na ${b.projeto.onda}, com ${b.projeto.detalhe}; Precisa de você com ${b.precisaDeVoce.length} itens; `
    + `${b.nota.valor}, ${b.nota.texto.toLowerCase()}. No celular, o Board do dev.kit.page pergunta "${c.decisao.pergunta}" `
    + `(${c.decisao.detalhe}) e pede: ${c.decisao.acao}.`;
  return (
    <figure className="lp-vitrine" role="img" aria-label={descricao}>
      <Janela titulo={b.titulo} className="lp-desktop" aria-hidden="true">
        <div className="lp-desktop-corpo">
          <div className="lp-desktop-lateral"><span /><span /><span /><span /><span /></div>
          <div className="lp-desktop-conteudo">
            <div className="lp-cartao lp-orquestrador">
              <span className="lp-avatar">O</span>
              <span className="lp-texto"><b>{b.orquestrador.autor}</b><small>{b.orquestrador.texto}</small></span>
            </div>
            <div className="lp-cartao">
              <span className="lp-linha"><strong>Projeto {b.projeto.nome}</strong><small>{b.projeto.onda}</small></span>
              <span className="lp-ondas">{b.projeto.ondas.map((onda, i) => <span key={i} className={ONDA[onda]} />)}</span>
              <small>{b.projeto.detalhe}</small>
            </div>
            <div className="lp-cartao">
              <strong>Precisa de você <span className="lp-contagem">{b.precisaDeVoce.length}</span></strong>
              {b.precisaDeVoce.map((item, i) => (
                <div key={item.titulo} className="lp-item">
                  <span className={`lp-item-avatar lp-item-avatar-${i}`} />
                  <span className="lp-texto"><b>{item.titulo}</b><small>{item.estado}</small></span>
                  <span className={i === 0 ? 'lp-acao lp-acao-forte' : 'lp-acao'}>{item.acao}</span>
                </div>
              ))}
            </div>
            <div className="lp-cartao">
              <span className="lp-linha">
                <strong>Avaliação</strong>
                <span className="lp-notas">{b.avaliador.notas.map((n, i) => <span key={i} className={n >= b.avaliador.minima ? 'lp-nota-boa' : 'lp-nota-baixa'}>{n}</span>)}</span>
              </span>
              <small>{b.avaliador.titulo} · {b.avaliador.gatilho} · nota mínima {b.avaliador.minima}</small>
            </div>
          </div>
        </div>
      </Janela>
      <div className="lp-celular" aria-hidden="true">
        <div className="lp-celular-tela">
          <div className="lp-celular-ilha" />
          <div className="lp-celular-topo"><b>dev.kit</b><span className="lp-celular-maquina"><i />{c.maquina}</span></div>
          <div className="lp-celular-abas"><span>Chat</span><span className="lp-ativa">Board</span></div>
          <div className="lp-celular-corpo">
            <p className="lp-celular-rotulo">{c.pendencias}</p>
            <div className="lp-decisao"><b>{c.decisao.pergunta}</b><small>{c.decisao.detalhe}</small><span className="lp-botao-celular">{c.decisao.acao}</span></div>
            <div className="lp-fala"><small>{c.fala.autor}</small>{c.fala.texto}</div>
            <p className="lp-celular-aviso">{c.aviso}</p>
          </div>
        </div>
      </div>
      <div className="lp-selo-nota" aria-hidden="true">
        <span className="lp-check">✓</span>
        <span><b>{b.nota.valor}</b><small>{b.nota.texto}</small></span>
      </div>
    </figure>
  );
}

/** Os quatro braços na primeira dobra: o resumo de cada um, que leva ao detalhe da seção Produto. */
function FaixaDosBracos() {
  return (
    <nav className="lp-faixa" aria-label="Os quatro braços do dev.kit">
      <ul>
        {BRACOS.map((braco) => (
          <li key={braco.id}>
            <a href={`#braco-${braco.id}`}>
              <span className="lp-faixa-icone" aria-hidden="true"><Icone nome={braco.icone} /></span>
              <span className="lp-faixa-nome">{braco.nome}{braco.novo && <span className="recurso-novo">novo</span>}</span>
              <span className="lp-faixa-texto">{braco.resumo}</span>
            </a>
          </li>
        ))}
      </ul>
    </nav>
  );
}

/** A ilustração de cada braço na seção Produto (dados de demonstração, decorativa: o texto do cartão é o que conta). */
function DemonstracaoDoBraco({ id }: { id: Braco['id'] }) {
  const d = DEMONSTRACAO_DOS_BRACOS;
  switch (id) {
    case 'desktop':
      return (
        <>
          <div className="lp-abas-da-task">{d.desktop.abas.map((aba) => <span key={aba} className={aba === d.desktop.ativa ? 'lp-ativa' : undefined}>{aba}</span>)}</div>
          <ul className="lp-evidencias">{EVIDENCIAS.map((e) => <li key={e}>{e}</li>)}</ul>
        </>
      );
    case 'servico':
      return (
        <>
          <span className="lp-estado-do-servico"><i />{d.servico.estado}</span>
          <ul className="lp-linhas-do-servico">{d.servico.linhas.map((l) => <li key={l.texto}><time>{l.quando}</time><span>{l.texto}</span></li>)}</ul>
        </>
      );
    case 'board':
      return (
        <>
          <div className="lp-notificacao">
            <span className="marca-icone">dK</span>
            <span><b>{d.board.notificacao.titulo}</b><small>{d.board.notificacao.texto}</small></span>
          </div>
          <div className="lp-conversa">
            {d.board.conversa.map((fala) => <p key={fala.texto} className={fala.sua ? 'lp-sua-fala' : undefined}><small>{fala.autor}</small>{fala.texto}</p>)}
          </div>
        </>
      );
    case 'devcli':
      return (
        <div className="lp-terminal">
          {d.devcli.comandos.map((c) => <div key={c.comando}><span>$</span> {c.comando}  <em># {c.nota}</em></div>)}
        </div>
      );
  }
}

/**
 * O tour dos recursos: as abas 01–08 (um tablist de verdade — setas, Home e End trocam de aba) e um painel por recurso,
 * com o texto, os marcadores e a mídia numa moldura de janela do app. Só a mídia da aba aberta é montada: um vídeo por
 * vez. O endereço antigo de um recurso (#recurso-<id>) abre a aba dele.
 */
function TourDosRecursos() {
  const [ativo, setAtivo] = useState(RECURSOS[0].id);
  const abas = useRef<(HTMLButtonElement | null)[]>([]);

  useEffect(() => {
    const doEndereco = () => {
      const id = window.location.hash.replace(/^#recurso-/, '');
      if (!RECURSOS.some((r) => r.id === id)) return;
      setAtivo(id);
      document.getElementById('recursos')?.scrollIntoView?.();
    };
    doEndereco();
    window.addEventListener('hashchange', doEndereco);
    return () => window.removeEventListener('hashchange', doEndereco);
  }, []);

  const escolher = (indice: number) => {
    const posicao = (indice + RECURSOS.length) % RECURSOS.length;
    setAtivo(RECURSOS[posicao].id);
    abas.current[posicao]?.focus();
  };

  const teclas = (evento: KeyboardEvent<HTMLButtonElement>, indice: number) => {
    const destino: Record<string, number> = { ArrowRight: indice + 1, ArrowLeft: indice - 1, Home: 0, End: RECURSOS.length - 1 };
    if (!(evento.key in destino)) return;
    evento.preventDefault();
    escolher(destino[evento.key]);
  };

  return (
    <div className="lp-tour">
      <div className="lp-abas" role="tablist" aria-label="Recursos em destaque">
        {RECURSOS.map((r, i) => (
          <button
            key={r.id}
            ref={(botao) => { abas.current[i] = botao; }}
            type="button"
            role="tab"
            id={`aba-recurso-${r.id}`}
            className="lp-aba"
            aria-selected={ativo === r.id}
            aria-controls={`recurso-${r.id}`}
            tabIndex={ativo === r.id ? 0 : -1}
            onClick={() => setAtivo(r.id)}
            onKeyDown={(evento) => teclas(evento, i)}
          >
            <span className="lp-aba-numero">{r.numero}</span> {r.aba}{r.novo && <span className="recurso-novo">novo</span>}
          </button>
        ))}
      </div>
      {RECURSOS.map((r) => (
        <div key={r.id} id={`recurso-${r.id}`} role="tabpanel" aria-labelledby={`aba-recurso-${r.id}`} className="lp-painel" hidden={ativo !== r.id} tabIndex={0}>
          <div className="lp-painel-texto">
            <p className="recurso-numero" aria-hidden="true">{r.numero}</p>
            <h3>{r.titulo}</h3>
            <p>{r.texto}</p>
            <ul className="marcadores">{r.marcadores.map((m) => <li key={m}>{m}</li>)}</ul>
          </div>
          <figure>
            <Janela titulo={tituloDaJanela(r.janela)} escura>
              {ativo === r.id && <Midia midia={r.midia} />}
            </Janela>
            <figcaption>{r.legenda}</figcaption>
          </figure>
        </div>
      ))}
    </div>
  );
}

/** O placar do cartão "Qualidade com nota mínima": a nota de cada avaliador contra a mínima. */
function Placar() {
  return (
    <>
      <ul className="lp-placar" aria-label={`Exemplo de avaliação, com nota mínima ${PLACAR.minima}`}>
        {PLACAR.avaliadores.map((a) => {
          const aceita = a.nota >= PLACAR.minima;
          return (
            <li key={a.nome} className={aceita ? undefined : 'lp-abaixo'}>
              <span>{a.nome}</span>
              <span className="lp-placar-trilho" style={{ '--minima': `${PLACAR.minima}%` } as CSSProperties} aria-hidden="true"><i style={{ width: `${a.nota}%` }} /></span>
              <b>{a.nota}</b>
              <small>{aceita ? 'aceita' : 'volta'}</small>
            </li>
          );
        })}
      </ul>
      <p className="lp-placar-nota">Nota mínima {PLACAR.minima}: abaixo dela, o trabalho volta ao agente.</p>
    </>
  );
}

/**
 * A landing pública — a página de venda do dev.kit, refeita em 12/11/2026 a partir dos recursos do projeto (os quatro
 * braços: o app desktop, o serviço do Windows, o Board do dev.kit.page e o devcli) e do que o site oferece. A primeira
 * dobra traz a promessa, os CTAs, o download, a vitrine do desktop com o celular e os quatro braços → como funciona em
 * sete etapas → os braços em detalhe → o tour dos recursos em abas → por que o dev.kit (o bento) → conceitos → a
 * fábrica agêntica → catálogo, comparativo, integrações, segurança, empresas, planos, contato e FAQ → CTA escuro →
 * rodapé. O texto e as mídias vêm de conteudo/landing.ts, na ordem de SECOES. A página vende o produto e NÃO explica a
 * área logada.
 */
export function Landing() {
  const [antes, depois] = HERO.titulo.split(HERO.destaque);
  // O menu das seções na tela estreita (proposta home-mobile v003): abaixo de 1100px ele some da barra e abre pelo botão.
  const [menuAberto, setMenuAberto] = useState(false);
  return (
    <div className="pagina landing">
      <a className="pular" href="#conteudo">Pular para o conteúdo</a>
      <header className="topo">
        <Marca />
        <nav aria-label="Seções" className="nav-landing">
          <ul id="menu-da-landing" className={menuAberto ? 'menu menu-aberto' : 'menu'}>
            {MENU.map((id) => <li key={id}><a href={`#${id}`} onClick={() => setMenuAberto(false)}>{rotuloDaSecao(id)}</a></li>)}
          </ul>
          <div className="nav-acoes">
            <Link className="link-entrar" to="/login">Entrar</Link>
            <Cta para="#contato">Pedir demonstração</Cta>
            <button type="button" className="botao-do-menu" aria-controls="menu-da-landing" aria-expanded={menuAberto}
              aria-label={menuAberto ? 'Fechar o menu' : 'Abrir o menu'} onClick={() => setMenuAberto((aberto) => !aberto)}>
              <span aria-hidden="true" /><span aria-hidden="true" /><span aria-hidden="true" />
            </button>
          </div>
        </nav>
      </header>

      <main id="conteudo">
        <section id="inicio" className="lp-hero" aria-labelledby="titulo-inicio">
          <div className="lp-hero-grade">
            <div className="lp-hero-texto">
              <p className="selo"><span className="selo-ponto" aria-hidden="true" />{HERO.selo}</p>
              <h1 id="titulo-inicio">{antes}<span className="destaque">{HERO.destaque}</span>{depois}</h1>
              <p className="lp-lead">{HERO.texto}</p>
              <div className="ctas">
                <Cta para="#contato">Quero uma demonstração <span aria-hidden="true">→</span></Cta>
                <Cta para="#como-funciona" variante="secundario">Ver como funciona</Cta>
              </div>
              <BaixarDevKit discreto />
            </div>
            <Vitrine />
          </div>
          <FaixaDosBracos />
        </section>

        <Secao id="como-funciona" rotulo={CABECALHOS.comoFunciona.rotulo} titulo={CABECALHOS.comoFunciona.titulo} subtitulo={CABECALHOS.comoFunciona.subtitulo}>
          <ol className="etapas">
            {PASSOS.map((p) => (
              <li key={p.titulo}><h3>{p.titulo}</h3><p>{p.texto}</p></li>
            ))}
          </ol>
        </Secao>

        <Secao id="bracos" rotulo={CABECALHOS.bracos.rotulo} titulo={CABECALHOS.bracos.titulo} subtitulo={CABECALHOS.bracos.subtitulo} className="secao-tinta">
          <div className="lp-bracos">
            {BRACOS.map((braco) => (
              <article key={braco.id} id={`braco-${braco.id}`} className="lp-braco" aria-labelledby={`titulo-braco-${braco.id}`}>
                <div className="lp-braco-visual" aria-hidden="true"><DemonstracaoDoBraco id={braco.id} /></div>
                <div className="lp-braco-texto">
                  <p className="lp-braco-nome"><Icone nome={braco.icone} tamanho={16} />{braco.nome}{braco.novo && <span className="recurso-novo">novo</span>}</p>
                  <h3 id={`titulo-braco-${braco.id}`}>{braco.titulo}</h3>
                  <p>{braco.texto}</p>
                  <ul className="marcadores">{braco.marcadores.map((m) => <li key={m}>{m}</li>)}</ul>
                </div>
              </article>
            ))}
          </div>
        </Secao>

        <Secao id="recursos" rotulo={CABECALHOS.recursos.rotulo} titulo={CABECALHOS.recursos.titulo} subtitulo={CABECALHOS.recursos.subtitulo}>
          <TourDosRecursos />
        </Secao>

        <Secao id="beneficios" rotulo={CABECALHOS.porQue.rotulo} titulo={CABECALHOS.porQue.titulo} className="secao-tinta">
          <ul className="lp-bento">
            {BENEFICIOS.map((b) => (
              <li key={b.titulo} className={b.largo ? 'lp-bento-largo' : undefined}>
                <span className="lp-icone" aria-hidden="true"><Icone nome={b.icone} tamanho={22} /></span>
                <h3>{b.titulo}</h3>
                <p>{b.texto}</p>
                {b.extra === 'placar' && <Placar />}
                {b.extra === 'evidencias' && <ul className="lp-evidencias" aria-label="O que a entrega guarda">{EVIDENCIAS.map((e) => <li key={e}>{e}</li>)}</ul>}
              </li>
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

        <Secao id="catalogo" rotulo="Catálogo" titulo={CATALOGO.titulo} subtitulo={CATALOGO.subtitulo}>
          <ul className="grade-cartoes grade-tres">
            {CATALOGO.itens.map((item) => (
              <li key={item.titulo} className="cartao"><h3>{item.titulo}</h3><p>{item.texto}</p></li>
            ))}
          </ul>
          <p className="lp-fluxos-titulo" id="titulo-fluxos-prontos">Fluxos prontos</p>
          <ul className="lp-fluxos" aria-labelledby="titulo-fluxos-prontos">
            {CATALOGO.fluxos.map((fluxo) => <li key={fluxo}>{fluxo}</li>)}
          </ul>
          <div className="ctas ctas-da-secao">
            <Cta para="/catalogo" variante="secundario">Ver o catálogo completo</Cta>
          </div>
        </Secao>

        <Secao id="comparativo" rotulo="Comparativo" titulo="dev.kit e as alternativas" subtitulo="O que pesa para quem trabalha no Azure DevOps, lado a lado." className="secao-tinta">
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
          <ul className="grade-cartoes grade-tres">
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

        <Secao id="planos" rotulo="Planos" titulo="Planos" subtitulo="Comece com o teste grátis; assine para você ou para o time." className="secao-tinta">
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

        <Secao id="contato" rotulo="Contato" titulo="Quero uma demonstração" subtitulo="Conte quem você é e o que quer ver: mostramos o dev.kit sobre um work item como os seus.">
          <FormularioDeDemonstracao />
        </Secao>

        <Secao id="faq" rotulo="Perguntas" titulo="Perguntas frequentes" className="secao-tinta">
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
