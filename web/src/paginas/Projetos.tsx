import { lazy, Suspense, useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { ItemDoProjeto, Projeto, ResumoDoProjeto } from '../api/projetos';
import { Pagina } from '../componentes/Pagina';
import { Topo } from '../componentes/Topo';
import { AcessoAoProjeto } from '../componentes/AcessoAoProjeto';
import { OndasDoProjeto } from '../componentes/OndasDoProjeto';
import { AcoesDoProjeto } from '../componentes/AcoesDoProjeto';
import { disparadoNoDetalhe, espelhoNoResumo, estadosDoConjunto, situacaoDoDetalhe, situacaoDoResumo, workItemsCriados,
  type ConjuntoDoEspelho, type SituacaoExibida } from '../componentes/projetos/situacaoDoProjeto';
import { useSessao } from '../sessao';
import { formatar } from '../formatar';
import './Projetos.css';

const Propostas = lazy(() => import('../componentes/PropostasDoPlano').then(m => ({ default: m.PropostasDoPlano })));
const Impactos = lazy(() => import('../componentes/matrix/Impactos').then(m => ({ default: m.ImpactosDoProjeto })));
/** O filtro usa a situação do contrato: o aprovado reúne o que aguarda disparo e o que está em execução. */
const estados: Record<string, string> = {
  rascunho: 'Rascunho', aguardando: 'Aguardando aprovação', aprovado: 'Aprovado', pausado: 'Pausado',
  fechando: 'Fechando', concluido: 'Concluído',
};
const curto = (url: string) => url.replace(/\.git$/, '').split(/[/:]/).filter(Boolean).slice(-2).join('/');

/** (boards-estados, item 5) A situação do projeto como etiqueta: o aprovado separa o aguardando disparo do em execução. */
const Situacao = ({ situacao }: { situacao: SituacaoExibida }) => <span className={`projeto-chip ${situacao.chave}`}>{situacao.rotulo}</span>;

/** Um conjunto de estados do espelho, só leitura: o configurado em destaque; tudo no padrão, indicado como tal. */
function EstadosDoConjunto({ conjunto, projeto }: { conjunto: ConjuntoDoEspelho; projeto: Projeto }) {
  const doConjunto = estadosDoConjunto(conjunto, projeto.estadosNoBoard);
  return <span className="projeto-estados-conjunto"><strong>{conjunto === 'pai' ? 'Pai' : 'Itens'}</strong>{' '}
    {doConjunto.map((e, i) => <span key={i}>{i > 0 && ' → '}{e.configurado ? <><strong>{e.nome}</strong><span className="somente-leitor"> (configurado)</span></> : e.nome}</span>)}
    {doConjunto.every(e => !e.configurado) && <small> (padrão)</small>}</span>;
}

export function Projetos() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const [consulta, setConsulta] = useSearchParams();
  const [projetos, setProjetos] = useState<ResumoDoProjeto[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState('');
  const [detalhe, setDetalhe] = useState<Projeto | null>(null);
  const [erroDoDetalhe, setErroDoDetalhe] = useState('');
  const [revisao, setRevisao] = useState(0);
  // (boards-estados, item 5) O topo das ações: Aprovar/Disparar e o pedido à máquina ficam antes dos filtros, como na proposta.
  const [topoDasAcoes, setTopoDasAcoes] = useState<HTMLDivElement | null>(null);
  const cache = useRef(new Map<string, Projeto>());
  const nivel = consulta.get('nivel') ?? '';
  const situacao = consulta.get('situacao') ?? '';
  const slug = consulta.get('slug');
  const visiveis = projetos.filter(p => (!nivel || p.nivel === nivel) && (!situacao || p.situacao === situacao));
  const escolhido = projetos.find(p => String(p.id) === consulta.get('projeto'))
    ?? projetos.find(p => p.slug === slug) ?? visiveis[0];
  const tratar = useCallback((e: unknown) => {
    if (e instanceof ErroDaApi && e.status === 401) sair('expirou');
    return mensagemDaFalha(e);
  }, [sair]);
  useEffect(() => {
    const controlador = new AbortController();
    setCarregando(true); setErro('');
    void api.resumosDosProjetos(token, controlador.signal).then(lista => {
      if (!controlador.signal.aborted) setProjetos(lista);
    }).catch(e => {
      if (!controlador.signal.aborted) setErro(`Não foi possível carregar os projetos. ${tratar(e)}`);
    }).finally(() => { if (!controlador.signal.aborted) setCarregando(false); });
    return () => controlador.abort();
  }, [token, revisao, tratar]);
  const id = escolhido?.id, versao = escolhido?.versao;
  useEffect(() => {
    if (id === undefined || versao === undefined) return;
    const controlador = new AbortController();
    const chave = `${token}/${id}/${versao}`;
    const salvo = cache.current.get(chave);
    setErroDoDetalhe('');
    if (salvo) { setDetalhe(salvo); return; }
    void api.projeto(token, id, controlador.signal).then(p => {
      if (!controlador.signal.aborted) { cache.current.set(chave, p); setDetalhe(p); }
    }).catch(e => { if (!controlador.signal.aborted) setErroDoDetalhe(tratar(e)); });
    return () => controlador.abort();
  }, [id, versao, token, revisao, tratar]);
  function atualizar() { cache.current.clear(); setRevisao(r => r + 1); }
  function salvo(p: Projeto) {
    cache.current.clear(); cache.current.set(`${token}/${p.id}/${p.versao}`, p); setDetalhe(p);
    setProjetos(lista => lista.map(r => r.id === p.id ? { ...r, versao: p.versao, situacao: p.situacao,
      atualizadoEm: p.atualizadoEm, totalDeItens: p.itens.length } : r));
  }
  function filtrar(chave: string, valor: string) {
    const q = new URLSearchParams(consulta);
    if (valor) q.set(chave, valor); else q.delete(chave);
    if (chave !== 'projeto') q.delete('projeto');
    q.delete('slug'); setConsulta(q);
  }
  const p = detalhe?.id === id ? detalhe : null;
  const entradaDoPlano = !consulta.has('projeto') && !slug && (consulta.get('visao') ?? 'plano') === 'plano';
  const lista = <aside className="projetos-lista" aria-label="Projetos sincronizados"><h3>Projetos</h3>{visiveis.map(r => {
    const q = new URLSearchParams(consulta); q.set('projeto', String(r.id)); q.delete('slug');
    return <div key={r.id} className={`cartao projeto-resumo${id === r.id ? ' escolhido' : ''}`}>
      <button type="button" className="projeto-selecionar" aria-pressed={id === r.id} onClick={() => filtrar('projeto', String(r.id))}>
        <strong>{r.nome}</strong><span>{situacaoDoResumo(r).rotulo}</span>
        <span>{r.itensConcluidos} de {r.totalDeItens} itens integrados</span>
      </button><Link to={`/projetos?${q}`} aria-label={`Abrir projeto ${r.nome}`}>Abrir projeto →</Link>
    </div>;
  })}</aside>;
  return <div className={`pagina projetos-pagina${consulta.has('projeto') || slug ? ' com-projeto-aberto' : ''}`}>
    <Topo secao="projetos" />
    <Pagina rotulo="Orquestrador › Projetos" titulo={escolhido?.nome ?? 'Projetos'} subtitulo={escolhido
      ? <span className="projeto-subtitulo"><Situacao situacao={p ? situacaoDoDetalhe(p) : situacaoDoResumo(escolhido)} />
        <span className="projeto-subtitulo-branch">{p?.branch ?? 'Projetos sincronizados do dev.kit'}</span></span>
      : 'Projetos sincronizados do dev.kit'}>
      <div ref={setTopoDasAcoes} className="projeto-topo-das-acoes" />
      <div className="projetos-filtros">
        <select aria-label="Projeto ativo" value={escolhido?.id ?? ''} onChange={e => filtrar('projeto', e.target.value)}>
          {!escolhido && <option value="">Escolher projeto</option>}
          {/* (boards-estados, item 5) O seletor mostra o slug, como a proposta; o nome fica no título e na lista. */}
          {escolhido && !visiveis.some(r => r.id === escolhido.id) && <option value={escolhido.id}>{escolhido.slug} · fora do filtro atual</option>}
          {visiveis.map(r => <option key={r.id} value={r.id}>{r.slug}</option>)}
        </select>
        <select aria-label="Nível de compartilhamento" value={nivel} onChange={e => filtrar('nivel', e.target.value)}>
          <option value="">Todos os projetos</option><option value="usuario">Meus projetos</option>
          {sessao!.empresa && <><option value="time">Time</option><option value="empresa">Empresa</option></>}
        </select>
        <select aria-label="Situação do projeto" value={situacao} onChange={e => filtrar('situacao', e.target.value)}>
          <option value="">Todas as situações</option>
          {['rascunho', 'aguardando', 'aprovado', 'pausado', 'fechando', 'concluido'].map(e => <option key={e} value={e}>{estados[e]}</option>)}
        </select>
        <button className="botao botao-fantasma" onClick={atualizar} disabled={carregando}>↻ Atualizar</button>
        <span className="projetos-contagem">{visiveis.length} projeto(s)</span>
        {escolhido && (entradaDoPlano || consulta.get('visao') && consulta.get('visao') !== 'plano') && (() => {
          const q = new URLSearchParams(consulta); q.set('projeto', String(escolhido.id)); q.delete('slug');
          return <Link to={`/projetos?${q}`} aria-label={`Abrir projeto ${entradaDoPlano ? 'selecionado ' : ''}${escolhido.nome}`}>Abrir projeto →</Link>;
        })()}
      </div>
      {erro && <div role="alert" className="erro">{erro} <button onClick={atualizar}>Tentar novamente</button></div>}
      {carregando && <p role="status">Carregando projetos…</p>}
      {!carregando && !erro && slug && !projetos.some(r => r.slug === slug) && <p role="status">O projeto {slug} ainda não chegou ao site: use Sincronizar no dev.kit.</p>}
      {!carregando && !erro && !escolhido && <p>Nenhum projeto sincronizado com esses filtros.</p>}
      {escolhido && erroDoDetalhe && <div role="alert" className="erro">{erroDoDetalhe} <button onClick={atualizar}>Reler projeto</button></div>}
      {escolhido && !p && !erroDoDetalhe && <p role="status">Carregando detalhe do projeto…</p>}
      {p && escolhido && <Detalhe key={`${token}/${p.id}`} projeto={p} resumo={escolhido} lista={lista} salvo={salvo} tratar={tratar} topoDasAcoes={topoDasAcoes} />}
    </Pagina>
  </div>;
}

/** (boards-estados, item 5) O aprovado que aguarda o disparo: o que a aprovação criou no board e o que o disparo abre. */
function AvisoDoAprovado({ projeto: p, itens }: { projeto: Projeto; itens: ItemDoProjeto[] }) {
  const criados = workItemsCriados(p, itens);
  const espelho = espelhoNoResumo(p);
  const primeira = itens.length ? Math.min(...itens.map(i => i.onda)) : 1;
  const iniciais = [...new Set([...(p.board === 'criar' && p.featureId > 0 ? [estadosDoConjunto('pai', p.estadosNoBoard)[0].nome] : []),
    estadosDoConjunto('itens', p.estadosNoBoard)[0].nome])];
  return <div className="projeto-aviso-aprovado" role="status">
    <strong>Plano aprovado · {!espelho ? 'sem espelho no Azure Boards.'
      : criados > 0 ? <>{criados} {criados === 1 ? 'work item criado' : 'work items criados'}<span className="projeto-largo"> no board</span> como {iniciais.join(' e ')}.</>
        : 'work items ainda não criados no board.'}</strong>{' '}
    Disparar abre a onda {primeira}<span className="projeto-largo">{p.autonomia ? '; com autonomia, o Orquestrador segue as próximas' : '; sem autonomia, cada onda seguinte espera o próprio disparo'}</span>.
  </div>;
}

/** (boards-estados, item 5) O espelho no resumo do projeto, só leitura: o pai (tipo e id) e a escolha do projeto. */
function EspelhoDoResumo({ projeto: p }: { projeto: Projeto }) {
  const espelho = espelhoNoResumo(p);
  if (!espelho) return <div><strong>{p.board === 'nao' ? 'Sem Azure Boards' : 'Azure Boards não configurado'}</strong><small>escolha do projeto</small></div>;
  return <div><strong>{espelho.tipoDoPai}{espelho.id > 0 && ` #${espelho.id}`}<span className="projeto-largo"> · Azure Boards</span></strong>
    <small><span className="projeto-largo">escolha do projeto · {espelho.detalhe}</span><span className="projeto-estreito">Azure Boards</span></small></div>;
}

function Detalhe({ projeto: p, resumo, lista, salvo, tratar, topoDasAcoes }: {
  projeto: Projeto; resumo: ResumoDoProjeto; lista: ReactNode; salvo: (p: Projeto) => void; tratar: (e: unknown) => string;
  topoDasAcoes: HTMLElement | null;
}) {
  const { sessao } = useSessao();
  const [consulta, setConsulta] = useSearchParams();
  const [emRevisao, setEmRevisao] = useState(false);
  const [erro, setErro] = useState('');
  const visao = consulta.get('visao') ?? 'plano';
  const itens = p.itensDoGrafo == null ? p.itens : p.itens.filter(i => p.itensDoGrafo!.includes(i.numero));
  const ondas = [...new Set(itens.map(i => i.onda))];
  const atual = Math.min(...itens.filter(i => i.situacao !== 'integrado').map(i => i.onda));
  // (boards-estados, item 5) Antes do disparo nenhuma onda está em andamento: o destaque da onda atual espera o disparo.
  const emAndamento = p.situacao === 'pausado' || (p.situacao === 'aprovado' && disparadoNoDetalhe(p));
  const comEspelho = !!espelhoNoResumo(p);
  return <article className="projeto-detalhe" aria-label="Detalhe do projeto">
    <h2 className="somente-leitor" tabIndex={-1} data-foco-navegacao={consulta.has('projeto') || consulta.has('slug') ? 'projeto' : undefined}>{p.nome}</h2>
    <AcoesDoProjeto projeto={p} itens={itens} tratar={tratar} destino={topoDasAcoes} />
    <nav className="projeto-abas" aria-label="Visões do projeto">{(['plano', 'ondas', 'impactos', 'propostas', 'entregas', 'historico'] as const).map(v => <button key={v} aria-pressed={(emRevisao ? 'historico' : visao) === v}
      onClick={() => { const q = new URLSearchParams(consulta); q.set('visao', v); setConsulta(q); }}>{({ plano: 'Plano', ondas: 'Ondas', impactos: 'Impactos', propostas: 'Propostas visuais', entregas: 'Entregas', historico: 'Histórico' })[v]}</button>)}</nav>
    {p.situacao === 'aprovado' && !disparadoNoDetalhe(p) && <AvisoDoAprovado projeto={p} itens={itens} />}
    <div className="projeto-metricas cartao">
      <div className="projeto-metricas-numeros">
        <div><strong>{resumo.itensConcluidos} de {resumo.totalDeItens}</strong><small>itens integrados</small></div>
        <div><strong>{ondas.length} ondas</strong><small>até {p.paralelismo} <span className="projeto-largo">itens </span>em paralelo</small></div>
        <div><strong>{p.agente && p.modelo ? `${p.agente} · ${p.modelo}` : 'Sem modelo selecionado'}</strong><small>{p.agente && p.modelo ? <>modelos<span className="projeto-largo"> do projeto</span></> : 'escolha pendente'}</small></div>
        <EspelhoDoResumo projeto={p} />
      </div>
      {comEspelho && <p className="projeto-estados-no-board" title="Configurados no dev.kit e no devcli. Vazio é o padrão pela categoria do tipo: o inicial do tipo, o primeiro em andamento e, na finalização, Completed nos itens e Resolved no pai.">
        <span className="projeto-estados-rotulo">Estados no board (<span className="projeto-largo">configurados no dev.kit, </span>só leitura)<span className="projeto-largo">:</span></span>
        <EstadosDoConjunto conjunto="itens" projeto={p} /><EstadosDoConjunto conjunto="pai" projeto={p} /></p>}
    </div>
    {p.pendenciaDoGrafo && <p role="alert" className="erro">{p.pendenciaDoGrafo}</p>}
    <Suspense fallback={<p role="status">Carregando aba…</p>}>
      {visao === 'ondas' ? <OndasDoProjeto key={p.id} projeto={p} itens={itens} ondaAtual={emAndamento && Number.isFinite(atual) ? atual : null} salvo={salvo} tratar={tratar} />
        : visao === 'propostas' ? <Propostas key={`propostas-${p.id}`} token={sessao!.token} itens={p.itens} tratar={e => setErro(tratar(e))} />
          : ['impactos', 'entregas', 'historico'].includes(visao) ? <Impactos key={`impactos-${p.id}`} projeto={p} visao={visao === 'impactos' ? 'matriz' : visao} aoRevisar={setEmRevisao} />
            : <div className="projetos-board">{lista}<div className="projeto-plano">
              <section className="cartao"><h3>Definição do projeto</h3><p className="projeto-definicao">{p.definicao || 'Ainda sem definição'}</p></section>
              <section className="cartao"><h3>Repositórios e execução</h3><p>{p.repositorios.map(curto).join(' · ')}</p>
                <p>Base: <code>{p.baseDoBranch || 'Não informada'}</code> · Destino da PR: <code>{p.destinoDaPr || 'Não informado'}</code></p>
                <p>{Number.isFinite(atual) ? `Onda ${atual} de ${ondas.length}` : itens.length ? 'Todas as ondas integradas' : 'Ainda sem plano'}</p>
                <p className="projetos-nota">Abra Ondas para consultar ou editar os itens deste projeto.</p></section>
              <section className="cartao"><h3>Resumo disponível</h3><p>Os painéis de Ondas, Impactos e Entregas são montados ao abrir cada aba.</p></section>
            </div></div>}
    </Suspense>
    <AcessoAoProjeto token={sessao!.token} recurso="projeto" projeto={{ id: p.id, chave: p.nome }} tratar={e => setErro(tratar(e))} />
    {erro && <p role="alert" className="erro">{erro}</p>}
    <p className="projetos-nota">Última sincronização: {formatar.dataHora(p.atualizadoEm)}. Dados preservados enquanto uma nova consulta está em andamento.</p>
  </article>;
}
