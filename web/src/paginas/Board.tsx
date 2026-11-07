import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { CatalogoRemoto, MaquinaRemota, RetratoDaMaquina } from '../api/remoto';
import type { DecisaoPendente, PendenciaRemota, ResponderDecisao } from '../api/remoto';
import { BotaoDeDetalhes, CartaoDaDecisao, OrigemDaPendencia, RevisaoDaDecisao, type AoDecidir } from '../componentes/DecisoesRemotas';
import { useSessao } from '../sessao';
import { prepararBoardPwa } from '../boardPwa';
import { AvisosDoBoard, HistoricoDeAvisos, NotificacoesDoBoard, pendenciasNovas, useAvisosDoBoard } from '../componentes/NotificacoesDoBoard';
import { Abas } from '../componentes/Abas';
import { Chips } from '../componentes/Chips';
import { filtrarPorDia, periodoDaUrl, periodosDoDia, type PeriodoDoDia } from '../componentes/filtroDeDia';
import { ChatRemoto } from '../componentes/ChatRemoto';
import { BotaoDoDevKit, FaixaDoAcionamento, useControleDoDevKit } from '../componentes/ControleDoDevKit';
import { useConversaRemota } from '../componentes/useConversaRemota';
import { useCatalogoRemoto } from '../componentes/useCatalogoRemoto';
import { DesfechoDaCriacao, NovoDesignNoBoard, NovoProjetoNoBoard, useCriacaoRemota, type CriacaoDoBoard } from '../componentes/CriacaoRemota';
import { VinculoDoServico } from '../componentes/VinculoDoServico';
import './Board.css';
import '../componentes/ChatRemoto.css';

const atualizacaoMs = 15_000;

function idade(data: string | null | undefined, agora: number) {
  if (!data) return 'ainda não recebida';
  const segundos = Math.max(0, Math.floor((agora - Date.parse(data)) / 1000));
  if (!Number.isFinite(segundos)) return 'data indisponível';
  if (segundos < 60) return `há ${segundos} s`;
  if (segundos < 3600) return `há ${Math.floor(segundos / 60)} min`;
  return `há ${Math.floor(segundos / 3600)} h`;
}

/** Listas longas do painel mostram as primeiras entradas e um Ver mais (US #858: menos coisa renderizada de uma vez). */
const primeirasEntradas = 5;

function VerMais({ total, todos, alternar }: { total: number; todos: boolean; alternar: () => void }) {
  if (total <= primeirasEntradas) return null;
  return <button className="br-mais" aria-expanded={todos} onClick={alternar}>{todos ? 'Ver menos' : `Ver mais (${total - primeirasEntradas})`}</button>;
}

function PendenciaSemDados({ pendencia: p }: { pendencia: PendenciaRemota }) {
  const [aberto, setAberto] = useState(false);
  const detalhes = `board-pendencia-${encodeURIComponent(p.id)}-detalhes`;
  return <section className="br-card br-need br-decisao-card br-compacta" aria-label={p.titulo}>
    <div className="br-decisao-titulo"><b>{p.titulo}</b></div>
    <OrigemDaPendencia pendencia={p} />
    <div id={detalhes} className="br-detalhes">{aberto && <>{p.pergunta && <p>{p.pergunta}</p>}{p.jaTentei && <p>{p.jaTentei}</p>}</>}</div>
    <p className="br-note">Aguardando os dados da decisão.</p>
    <div className="br-row"><BotaoDeDetalhes aberto={aberto} alternar={() => setAberto(a => !a)} controla={detalhes} /></div>
  </section>;
}

function ConteudoDoBoard({ foto, agora, periodo, decisoes, destacada, desabilitado, decidir, revisar, designs, criacao }: { foto: RetratoDaMaquina | null; agora: number; periodo: PeriodoDoDia; decisoes: DecisaoPendente[]; destacada: string; desabilitado: boolean; decidir: AoDecidir; revisar: (d: DecisaoPendente) => void; designs: NonNullable<CatalogoRemoto['designs']>; criacao: CriacaoDoBoard }) {
  const retrato = foto?.retrato;
  const [todosOsProjetos, setTodosOsProjetos] = useState(false);
  const [todosOsDesigns, setTodosOsDesigns] = useState(false);
  const [todasAsExecucoes, setTodasAsExecucoes] = useState(false);
  const projetos = retrato?.projetos ?? [];
  const recebidas = retrato?.fluxos ?? [];
  // O filtro de dia vale para as execuções; as pendências nunca são escondidas por data.
  const execucoes = filtrarPorDia(recebidas, periodo, agora);
  return <>
    <h2>Precisa de você · {retrato?.pendencias?.length ?? 0}</h2>
    {retrato?.pendencias?.map(p => {
      const d = decisoes.find(d => d.pendencia.id === p.id);
      return d ? <CartaoDaDecisao key={`${p.id}-${d.snapshotId}`} decisao={d} compacta destacada={destacada === p.id} desabilitado={desabilitado} decidir={decidir} revisar={revisar} />
        : <PendenciaSemDados key={p.id} pendencia={p} />;
    })}
    {!retrato?.pendencias?.length && <p className="br-note">Nenhuma pendência recebida.</p>}
    <p className="br-note">Confira os detalhes antes de decidir. O texto do chat nunca aprova.</p>
    {/* (item 4) Os projetos abrem a página Projetos no projeto, e o Novo projeto cria pelo dev.kit da máquina. */}
    <div className="br-secao-titulo"><h2>Projetos{projetos.length > 0 && ` · ${projetos.length}`}</h2>
      <Link className="br-ver" to="/projetos">Ver todos</Link>
      <button className="br-btn br-novo" disabled={criacao.desabilitado} title={criacao.motivo} onClick={() => criacao.abrir('projeto')}>+ Novo projeto</button></div>
    {(todosOsProjetos ? projetos : projetos.slice(0, primeirasEntradas)).map(p => {
      const itens = p.itens ?? [];
      const integrados = itens.filter(i => i.situacao === 'integrado').length;
      const rodando = itens.filter(i => i.situacao === 'rodando').length;
      const ondas = Math.max(p.ondaAtual, ...itens.map(i => i.onda));
      return <Link className="br-card br-proj br-abrir" key={p.slug} to={`/projetos?slug=${encodeURIComponent(p.slug)}`} aria-label={`Abrir ${p.nome}`}>
        <b>{p.nome}<span>{integrados} de {itens.length}</span></b>
        <div className="br-bar" role="progressbar" aria-label={`Progresso de ${p.nome}`} aria-valuenow={integrados} aria-valuemin={0} aria-valuemax={itens.length || 1}><i style={{ width: `${itens.length ? integrados / itens.length * 100 : 0}%` }} /></div>
        <small>{p.situacao === 'concluido' ? 'incorporado ao projeto' : `onda ${p.ondaAtual} de ${ondas} · ${rodando} rodando`}</small>
      </Link>;
    })}
    <VerMais total={projetos.length} todos={todosOsProjetos} alternar={() => setTodosOsProjetos(t => !t)} />
    {!projetos.length && <p className="br-note">Nenhum projeto recebido.</p>}
    <DesfechoDaCriacao criacao={criacao} />
    {/* Os projetos de design vêm do catálogo da máquina (o handshake): abrem o Design compartilhado no repositório. */}
    <div className="br-secao-titulo"><h2>Design{designs.length > 0 && ` · ${designs.length}`}</h2>
      <Link className="br-ver" to="/design">Ver todos</Link>
      <button className="br-btn br-novo" disabled={criacao.desabilitado} title={criacao.motivo} onClick={() => criacao.abrir('design')}>+ Novo design</button></div>
    {(todosOsDesigns ? designs : designs.slice(0, primeirasEntradas)).map(d => {
      const destino = d.repositorio ? `/design?repositorio=${encodeURIComponent(d.repositorio)}` : '/design';
      return <Link className="br-card br-design br-abrir" key={d.nome} to={destino} aria-label={`Abrir o design de ${d.nome}`}>
        <b>{d.nome}<span>{d.telas === 1 ? '1 tela' : `${d.telas} telas`}</span></b><small>Abrir no Design</small>
      </Link>;
    })}
    <VerMais total={designs.length} todos={todosOsDesigns} alternar={() => setTodosOsDesigns(t => !t)} />
    {!designs.length && <p className="br-note">Nenhum projeto de design no catálogo da máquina.</p>}
    <p className="br-note">Criar pelo site vira um pedido na fila: o dev.kit executa como o devcli e devolve o resultado aqui. Aprovar e disparar continuam no dev.kit.</p>
    <h2>Executando · {execucoes.length}<span className="br-periodo"> {periodosDoDia.find(p => p.valor === periodo)!.rotulo.toLowerCase()}</span></h2>
    {!!execucoes.length && <section className="br-card">{(todasAsExecucoes ? execucoes : execucoes.slice(0, primeirasEntradas)).map(f => <div className="br-item" key={f.id}>
      <div><span>{f.nome}</span>{f.etapa && <small>{f.etapa}{f.agente ? ` · ${f.agente}` : ''}</small>}</div>
      <span className={`br-chip${f.situacao === 'fila' || f.situacao === 'na-fila' ? ' br-warn' : ''}`}>{f.situacao === 'fila' || f.situacao === 'na-fila' ? 'na fila' : f.situacao}</span>
    </div>)}</section>}
    <VerMais total={execucoes.length} todos={todasAsExecucoes} alternar={() => setTodasAsExecucoes(t => !t)} />
    {!execucoes.length && <p className="br-note">{recebidas.length ? 'Nenhuma execução neste período.' : 'Nenhuma execução recebida.'}</p>}
    <p className="br-note">Última atualização {idade(retrato?.geradoEm, agora)}.</p>
  </>;
}

/**
 * Board isolado da administração, com decisões auditadas e aprovação vinculada ao snapshot.
 * O Ligar e o Reiniciar vão ao serviço da máquina pela mesma conversa do chat.
 */
export function Board() {
  useEffect(prepararBoardPwa, []);
  const { sessao, sair } = useSessao();
  const [parametros, setParametros] = useSearchParams();
  const aba = parametros.get('aba') === 'chat' ? 'chat' : 'board';
  const periodo = periodoDaUrl(parametros.get('dia'));
  function mudarParametro(nome: string, valor: string) { const p = new URLSearchParams(parametros); p.set(nome, valor); setParametros(p, { replace: true }); }
  const [maquinas, setMaquinas] = useState<MaquinaRemota[]>([]);
  const [selecionada, setSelecionada] = useState('');
  const [foto, setFoto] = useState<RetratoDaMaquina | null>(null);
  const [decisoes, setDecisoes] = useState<DecisaoPendente[]>([]);
  const [revisao, setRevisao] = useState<DecisaoPendente | null>(null);
  const [erro, setErro] = useState('');
  const [carregando, setCarregando] = useState(true);
  const [tentativa, setTentativa] = useState(0);
  const [agora, setAgora] = useState(Date.now());
  const geracao = useRef(0);
  const notificacao = parametros.get('notificacao');
  const [destinoPush, setDestinoPush] = useState<{ maquinaId: string; pendenciaId: string; snapshotId: string } | null>(null);
  // O aviso do site some sozinho, agrupa os repetidos e fica na seção Avisos do painel (US #858).
  const { avisos, avisar, encerrar } = useAvisosDoBoard();
  const [destacada, setDestacada] = useState('');
  const conhecidas = useRef<{ maquinaId: string; ids: Set<string> } | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    setDestinoPush(null);
    if (!notificacao || !sessao) return;
    if (!/^[a-f0-9]{32}$/.test(notificacao)) { avisar('Notificação inválida. Consulte as pendências do Board.'); return; }
    // Sem aviso de "abrindo": o desfecho (aberta, resolvida ou indisponível) é o aviso que importa.
    void api.destinoDoPush(sessao.token, notificacao, ctrl.signal).then(d => {
      if (!ctrl.signal.aborted) setDestinoPush(d);
    }).catch(e => {
      if (ctrl.signal.aborted) return;
      if (e instanceof ErroDaApi && e.status === 401) sair('expirou');
      else avisar(mensagemDaFalha(e));
    });
    return () => ctrl.abort();
  }, [notificacao, sessao, sair, avisar]);

  // As pendências que chegam entre um retrato e o seguinte viram aviso; o primeiro retrato de cada máquina, não.
  useEffect(() => {
    const pendencias = foto?.retrato?.pendencias;
    if (!foto || !pendencias) return;
    const anteriores = conhecidas.current?.maquinaId === foto.maquina.maquinaId ? conhecidas.current.ids : null;
    for (const p of pendenciasNovas(anteriores, pendencias)) avisar(`Nova pendência: ${p.titulo}`, `pendencia:${p.id}`);
    conhecidas.current = { maquinaId: foto.maquina.maquinaId, ids: new Set(pendencias.map(p => p.id)) };
  }, [foto, avisar]);

  useEffect(() => {
    if (!sessao) return;
    const controller = new AbortController();
    const atual = ++geracao.current;
    let timer: number;
    async function carregar() {
      let mudouMaquina = false;
      try {
        const lista = await api.maquinasRemotas(sessao!.token, controller.signal);
        if (controller.signal.aborted || atual !== geracao.current) return;
        setMaquinas(lista);
        if (destinoPush && !lista.some(m => m.maquinaId === destinoPush.maquinaId)) {
          setDestinoPush(null); avisar('A máquina desta notificação não está disponível para sua conta.');
        }
        const preferida = destinoPush?.maquinaId ?? selecionada;
        const id = lista.some(m => m.maquinaId === preferida) ? preferida : lista[0]?.maquinaId ?? '';
        if (id !== selecionada) {
          setFoto(null);
          setSelecionada(id);
          if (id) { mudouMaquina = true; setCarregando(true); return; }
        }
        let novaFoto: RetratoDaMaquina | null = null;
        let novasDecisoes: DecisaoPendente[] = [];
        if (id) {
          try { [novaFoto, novasDecisoes] = await Promise.all([api.retratoRemoto(sessao!.token, id, controller.signal), api.decisoesRemotas(sessao!.token, id, controller.signal)]); }
          catch (falha) { if (!(falha instanceof ErroDaApi && falha.status === 404)) throw falha; }
        }
        if (controller.signal.aborted || atual !== geracao.current) return;
        setFoto(novaFoto);
        setDecisoes(novasDecisoes);
        setErro('');
      } catch (falha) {
        if (controller.signal.aborted || atual !== geracao.current) return;
        if (falha instanceof ErroDaApi && falha.status === 401) { sair('expirou'); return; }
        setErro(`Não foi possível atualizar o Board. ${mensagemDaFalha(falha)}`);
      } finally {
        if (!mudouMaquina && !controller.signal.aborted && atual === geracao.current) {
          setCarregando(false);
          timer = window.setTimeout(carregar, atualizacaoMs);
        }
      }
    }
    void carregar();
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [sessao, selecionada, tentativa, sair, destinoPush, avisar]);

  useEffect(() => {
    if (!destinoPush || carregando || erro || selecionada !== destinoPush.maquinaId || !foto) return;
    const d = decisoes.find(d => d.pendencia.id === destinoPush.pendenciaId && d.snapshotId === destinoPush.snapshotId);
    setDestinoPush(null);
    if (!d || (d.resposta && d.resposta.estado !== 'vencida')) {
      avisar('Esta decisão foi resolvida ou o snapshot mudou. Consulte as pendências atuais.'); return;
    }
    avisar('Decisão aberta a partir da notificação. Confira os dados atuais antes de decidir.');
    if (d.pendencia.tipo === 'aprovacao') setRevisao(d);
    else {
      const p = new URLSearchParams(parametros); p.set('aba', 'board'); setParametros(p, { replace: true });
      // O cartão apontado pela notificação abre os detalhes, além de receber o foco.
      setDestacada(d.pendencia.id);
      const cartao = document.getElementById(`board-decisao-${encodeURIComponent(d.pendencia.id)}`);
      cartao?.scrollIntoView?.({ block: 'nearest' }); cartao?.focus();
    }
  }, [destinoPush, carregando, erro, selecionada, foto, decisoes, parametros, setParametros, avisar]);

  useEffect(() => {
    const timer = window.setInterval(() => setAgora(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  const maquina = maquinas.find(m => m.maquinaId === selecionada);
  // O heartbeat perde validade mesmo que uma atualização da API falhe.
  const conectada = !!maquina?.conectada && !!maquina.ultimoHeartbeatEm && agora - Date.parse(maquina.ultimoHeartbeatEm) <= 180_000;
  const estado = maquina ? `${conectada ? (maquina.origem === 'servico' ? 'Serviço ativo' : 'dev.kit conectado') : 'Máquina desconectada'} · dev.kit ${maquina.appAberto ? 'aberto' : 'fechado'}` : 'Nenhuma máquina conectada';
  const agente = maquina?.estadoDoAgente === 'limite' ? 'Agente em limite' : maquina?.estadoDoAgente === 'pausado' ? 'Agente pausado' : '';
  const desabilitado = carregando || !!erro || !conectada;
  async function decidir(d: DecisaoPendente, pedido: ResponderDecisao) {
    const atual = geracao.current;
    try {
      const resposta = await api.responderDecisao(sessao!.token, selecionada, d.pendencia.id, pedido);
      if (atual === geracao.current)
        setDecisoes(lista => lista.map(item => item.pendencia.id === d.pendencia.id && item.snapshotId === d.snapshotId ? { ...item, resposta } : item));
      return resposta;
    } catch (e) {
      if (e instanceof ErroDaApi && e.status === 401) sair('expirou');
      throw e;
    }
  }
  // O Desvincular do serviço: a lista já mostra a máquina sem vínculo, e a próxima leitura confirma pela API.
  async function desvincular() {
    const maquinaId = selecionada;
    try {
      await api.desvincularMaquina(sessao!.token, maquinaId);
    } catch (e) {
      if (e instanceof ErroDaApi && e.status === 401) sair('expirou');
      throw e;
    }
    setMaquinas(lista => lista.map(m => m.maquinaId === maquinaId ? { ...m, vinculada: false } : m));
    avisar('Serviço desvinculado: o heartbeat e a fila desta máquina pararam até o próximo login no dev.kit nela.');
  }
  const mudou = !!revisao && !decisoes.some(d => d.pendencia.id === revisao.pendencia.id && d.snapshotId === revisao.snapshotId);
  // UMA conversa por máquina para o chat e o controle do dev.kit: o mesmo histórico e o mesmo SSE.
  const conversa = useConversaRemota(selecionada);
  const controle = useControleDoDevKit({ maquina, conectada, conversa: conversa.conversa, acionar: conversa.acionar, agora });
  // (item 4) O catálogo da máquina (o handshake) é UM para o chat e o painel: os atalhos, os designs e os formulários.
  const catalogo = useCatalogoRemoto({ maquina, conectada, conversa });
  const criacao = useCriacaoRemota({ maquina, conversa, agora });
  const doCatalogo = catalogo.catalogo?.catalogo ?? null;

  return <div className="board-remoto" data-aba={aba}>
    <header className="br-top">
      <b>dev.kit</b><span className="br-spacer" />
      <div className="br-machine">
        <div className={`br-host${conectada ? '' : ' br-offline'}`}><i aria-hidden="true" />
          {maquinas.length > 1 ? <select aria-label="Máquina" value={selecionada} onChange={e => { ++geracao.current; setFoto(null); setDecisoes([]); setRevisao(null); setErro(''); setCarregando(true); setSelecionada(e.target.value); }}>
            {maquinas.map(m => <option key={m.maquinaId} value={m.maquinaId}>{m.nome || 'Máquina'}</option>)}
          </select> : <span className="br-name" title={maquina?.nome || 'Máquina'}>{maquina?.nome || 'Máquina'}</span>}
          <span className="br-desktop-state"> · {estado.toLowerCase()}</span>
        </div>
        <small title="Idade do último heartbeat recebido"><span className="br-mobile-state">{estado} · </span><span className="br-desktop-state">Última atualização </span>{idade(maquina?.ultimoHeartbeatEm, agora)}{agente && ` · ${agente}`}{maquina?.turnoEmAndamento && ' · turno em andamento'}</small>
      </div>
      <BotaoDoDevKit controle={controle} />
    </header>
    <FaixaDoAcionamento controle={controle} />
    {revisao && <RevisaoDaDecisao key={`${selecionada}-${revisao.snapshotId}`} decisao={mudou ? { ...revisao, bloqueio: 'O snapshot mudou. Volte ao Board e revise as novas evidências.' } : revisao} decidir={decidir} voltar={() => { setRevisao(null); setTentativa(t => t + 1); }} desabilitado={desabilitado || mudou} />}
    <div className="br-navegacao" hidden={!!revisao}><Abas rotulo="Navegação do Board" prefixo="board-aba" abas={[{ id: 'chat', rotulo: 'Chat' }, { id: 'board', rotulo: 'Board', contagem: foto?.retrato?.pendencias?.length }]} ativa={aba} aoMudar={id => mudarParametro('aba', id)}>
    {/* Como no Board do dev.kit: o chat na coluna principal e o painel de auxílio à direita, na mesma ordem de leitura. */}
    <main className="br-main">
      <ChatRemoto key={selecionada} maquina={maquina} conectada={conectada} agora={agora} conversaRemota={conversa} catalogo={catalogo} decisoes={decisoes} desabilitarDecisoes={desabilitado} decidir={decidir} revisar={setRevisao} />
      <aside className="br-side" aria-label="Board">
        <div className="br-readonly"><strong>Decisões remotas</strong><span>Fase 3</span></div>
        <Chips rotulo="Filtro de dia" opcoes={periodosDoDia} valor={periodo} aoMudar={valor => mudarParametro('dia', valor)} />
        {carregando && <p role="status" className="br-note">Carregando Board…</p>}
        {erro && <div role="alert" className="br-card br-need"><p>{erro}</p>{foto && <p>Exibindo o último retrato recebido; os dados podem estar desatualizados.</p>}<button className="br-btn" onClick={() => setTentativa(t => t + 1)}>Tentar novamente</button></div>}
        {!carregando && !erro && !maquina && <p className="br-note">Nenhuma máquina cadastrada. Abra o dev.kit na máquina para enviar o primeiro heartbeat.</p>}
        {maquina && !conectada && <p role="status" className="br-note">Máquina desconectada. O último retrato pode estar desatualizado.</p>}
        {!carregando && maquina && !foto && !erro && <p className="br-note">Aguardando o primeiro retrato desta máquina.</p>}
        {(!carregando || foto) && <ConteudoDoBoard key={selecionada} foto={foto} agora={agora} periodo={periodo} decisoes={decisoes} destacada={destacada} desabilitado={desabilitado} decidir={decidir} revisar={setRevisao}
          designs={doCatalogo?.designs ?? []} criacao={criacao} />}
        {/* A chave é própria: o ConteudoDoBoard, irmão dele, já usa a da máquina. */}
        {maquina && <VinculoDoServico key={`vinculo-${selecionada}`} maquina={maquina} desvincular={desvincular} />}
        <HistoricoDeAvisos avisos={avisos} />
        <NotificacoesDoBoard />
      </aside>
    </main>
    </Abas></div>
    {criacao.aberta === 'projeto' && <NovoProjetoNoBoard key={`projeto-${selecionada}`} criacao={criacao} catalogo={doCatalogo} recebidoEm={catalogo.catalogo?.recebidoEm ?? null} />}
    {criacao.aberta === 'design' && <NovoDesignNoBoard key={`design-${selecionada}`} criacao={criacao} catalogo={doCatalogo} />}
    <AvisosDoBoard avisos={avisos} encerrar={encerrar} />
    <div className="br-pwa"><strong>Instale o dev.kit</strong> pelo menu do navegador, quando a instalação estiver disponível.</div>
    <footer className="br-footer"><span className="br-desktop-state">Ligar e Reiniciar são executados pelo serviço do dev.kit na máquina.</span><span className="br-mobile-state">Texto do chat nunca aprova · decisões auditadas</span></footer>
  </div>;
}
