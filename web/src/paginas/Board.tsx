import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { MaquinaRemota, RetratoDaMaquina } from '../api/remoto';
import { useSessao } from '../sessao';
import { prepararBoardPwa } from '../boardPwa';
import './Board.css';

const motivo = 'Disponível somente no dev.kit da máquina nesta fase.';
const atualizacaoMs = 15_000;

function idade(data: string | null | undefined, agora: number) {
  if (!data) return 'ainda não recebida';
  const segundos = Math.max(0, Math.floor((agora - Date.parse(data)) / 1000));
  if (!Number.isFinite(segundos)) return 'data indisponível';
  if (segundos < 60) return `há ${segundos} s`;
  if (segundos < 3600) return `há ${Math.floor(segundos / 60)} min`;
  return `há ${Math.floor(segundos / 3600)} h`;
}

function ChatIndisponivel() {
  return <section className="br-chat" aria-label="Orquestrador">
    <header className="br-chathead"><span className="br-logo">O</span><div><b>Orquestrador</b><small>Chat indisponível nesta fase</small></div></header>
    <div className="br-mensagens"><div className="br-indisponivel">
      <div className="br-lock" aria-hidden="true">○</div>
      <h1>Por enquanto, acompanhe o Board</h1>
      <p>O painel está disponível somente para leitura.<br />O envio de mensagens ao Orquestrador será liberado na fase 2.</p>
      <small>Consulte as pendências, os projetos e o Executando no Board.</small>
    </div></div>
    <div className="br-composer">
      <textarea aria-label="Mensagem ao Orquestrador" disabled placeholder="Peça ou pergunte ao Orquestrador" />
      <div className="br-bottom">
        <button disabled>Trocar fluxo</button><button disabled># Work item</button><button disabled>@ Repositório</button>
        <span className="br-spacer" /><button className="br-send" disabled aria-label="Enviar mensagem">➤</button>
      </div>
    </div>
    <p className="br-hint">Chat desabilitado · mensagens disponíveis na fase 2.<br />/ trocar o fluxo · # work item · @ repositório</p>
  </section>;
}

function ConteudoDoBoard({ foto, agora }: { foto: RetratoDaMaquina | null; agora: number }) {
  const retrato = foto?.retrato;
  return <>
    <h2>Precisa de você · {retrato?.pendencias?.length ?? 0}</h2>
    {retrato?.pendencias?.map(p => <section className="br-card br-need" key={p.id}>
      <b>{p.titulo}</b>
      {p.pergunta && <p>{p.pergunta}</p>}
      {p.jaTentei && <p>{p.jaTentei}</p>}
      {p.opcoes && p.opcoes.length > 0 && <p>Opções: {p.opcoes.join(' · ')}</p>}
      <div className="br-row"><button className="br-btn" title={motivo} disabled>Ver detalhes</button><button className="br-btn" title={motivo} disabled>Decidir</button></div>
    </section>)}
    {!retrato?.pendencias?.length && <p className="br-note">Nenhuma pendência recebida.</p>}
    <p className="br-note">Decida no dev.kit. As decisões remotas serão liberadas em uma próxima fase.</p>
    <h2>Projetos</h2>
    {retrato?.projetos?.map(p => {
      const itens = p.itens ?? [];
      const integrados = itens.filter(i => i.situacao === 'integrado').length;
      const rodando = itens.filter(i => i.situacao === 'rodando').length;
      const ondas = Math.max(p.ondaAtual, ...itens.map(i => i.onda));
      return <section className="br-card br-proj" key={p.slug}>
        <b>{p.nome}<span>{integrados} de {itens.length}</span></b>
        <div className="br-bar" role="progressbar" aria-label={`Progresso de ${p.nome}`} aria-valuenow={integrados} aria-valuemin={0} aria-valuemax={itens.length || 1}><i style={{ width: `${itens.length ? integrados / itens.length * 100 : 0}%` }} /></div>
        <small>{p.situacao === 'concluido' ? 'incorporado ao projeto' : `onda ${p.ondaAtual} de ${ondas} · ${rodando} rodando`}</small>
      </section>;
    })}
    {!retrato?.projetos?.length && <p className="br-note">Nenhum projeto recebido.</p>}
    <h2>Executando</h2>
    {!!retrato?.fluxos?.length && <section className="br-card">{retrato.fluxos.map(f => <div className="br-item" key={f.id}>
      <div><span>{f.nome}</span>{f.etapa && <small>{f.etapa}{f.agente ? ` · ${f.agente}` : ''}</small>}</div>
      <span className={`br-chip${f.situacao === 'fila' || f.situacao === 'na-fila' ? ' br-warn' : ''}`}>{f.situacao === 'fila' || f.situacao === 'na-fila' ? 'na fila' : f.situacao}</span>
    </div>)}</section>}
    {!retrato?.fluxos?.length && <p className="br-note">Nenhuma execução recebida.</p>}
    <p className="br-note">Última atualização {idade(retrato?.geradoEm, agora)}.</p>
  </>;
}

/** Board isolado da administração: somente GET autenticado, sem comandos remotos. */
export function Board() {
  useEffect(prepararBoardPwa, []);
  const { sessao, sair } = useSessao();
  const [parametros, setParametros] = useSearchParams();
  const aba = parametros.get('aba') === 'chat' ? 'chat' : 'board';
  const [maquinas, setMaquinas] = useState<MaquinaRemota[]>([]);
  const [selecionada, setSelecionada] = useState('');
  const [foto, setFoto] = useState<RetratoDaMaquina | null>(null);
  const [erro, setErro] = useState('');
  const [carregando, setCarregando] = useState(true);
  const [tentativa, setTentativa] = useState(0);
  const [agora, setAgora] = useState(Date.now());
  const geracao = useRef(0);

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
        const id = lista.some(m => m.maquinaId === selecionada) ? selecionada : lista[0]?.maquinaId ?? '';
        if (id !== selecionada) {
          setFoto(null);
          setSelecionada(id);
          if (id) { mudouMaquina = true; setCarregando(true); return; }
        }
        let novaFoto: RetratoDaMaquina | null = null;
        if (id) {
          try { novaFoto = await api.retratoRemoto(sessao!.token, id, controller.signal); }
          catch (falha) { if (!(falha instanceof ErroDaApi && falha.status === 404)) throw falha; }
        }
        if (controller.signal.aborted || atual !== geracao.current) return;
        setFoto(novaFoto);
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
  }, [sessao, selecionada, tentativa, sair]);

  useEffect(() => {
    const timer = window.setInterval(() => setAgora(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  const maquina = maquinas.find(m => m.maquinaId === selecionada);
  // O heartbeat perde validade mesmo que uma atualização da API falhe.
  const conectada = !!maquina?.conectada && !!maquina.ultimoHeartbeatEm && agora - Date.parse(maquina.ultimoHeartbeatEm) <= 180_000;
  const estado = maquina ? `${conectada ? (maquina.origem === 'servico' ? 'Serviço ativo' : 'dev.kit conectado') : 'Máquina desconectada'} · dev.kit ${maquina.appAberto ? 'aberto' : 'fechado'}` : 'Nenhuma máquina conectada';
  const agente = maquina?.estadoDoAgente === 'limite' ? 'Agente em limite' : maquina?.estadoDoAgente === 'pausado' ? 'Agente pausado' : '';

  return <div className="board-remoto" data-aba={aba}>
    <header className="br-top">
      <b>dev.kit</b><span className="br-spacer" />
      <div className="br-machine">
        <div className={`br-host${conectada ? '' : ' br-offline'}`}><i aria-hidden="true" />
          {maquinas.length > 1 ? <select aria-label="Máquina" value={selecionada} onChange={e => { setFoto(null); setErro(''); setCarregando(true); setSelecionada(e.target.value); }}>
            {maquinas.map(m => <option key={m.maquinaId} value={m.maquinaId}>{m.nome || 'Máquina'}</option>)}
          </select> : <span>{maquina?.nome || 'Máquina'}</span>}
          <span className="br-desktop-state"> · {estado.toLowerCase()}</span>
        </div>
        <small><span className="br-mobile-state">{estado} · </span>{idade(maquina?.ultimoHeartbeatEm, agora)}{agente && ` · ${agente}`}{maquina?.turnoEmAndamento && ' · turno em andamento'}</small>
      </div>
      <button className="br-btn br-restart" disabled title={motivo}>Reiniciar dev.kit</button>
    </header>
    <nav className="br-tabs" aria-label="Navegação do Board">
      <button aria-current={aba === 'chat' ? 'page' : undefined} onClick={() => setParametros({ aba: 'chat' }, { replace: true })}>Chat</button>
      <button aria-current={aba === 'board' ? 'page' : undefined} onClick={() => setParametros({ aba: 'board' }, { replace: true })}>Board</button>
    </nav>
    <main className="br-main">
      <aside className="br-side" aria-label="Board">
        <div className="br-readonly"><strong>Somente leitura</strong><span>Fase 1</span></div>
        {carregando && <p role="status" className="br-note">Carregando Board…</p>}
        {erro && <div role="alert" className="br-card br-need"><p>{erro}</p>{foto && <p>Exibindo o último retrato recebido; os dados podem estar desatualizados.</p>}<button className="br-btn" onClick={() => setTentativa(t => t + 1)}>Tentar novamente</button></div>}
        {!carregando && !erro && !maquina && <p className="br-note">Nenhuma máquina cadastrada. Abra o dev.kit na máquina para enviar o primeiro heartbeat.</p>}
        {maquina && !conectada && <p role="status" className="br-note">Máquina desconectada. O último retrato pode estar desatualizado.</p>}
        {!carregando && maquina && !foto && !erro && <p className="br-note">Aguardando o primeiro retrato desta máquina.</p>}
        {(!carregando || foto) && <ConteudoDoBoard foto={foto} agora={agora} />}
      </aside>
      <ChatIndisponivel />
    </main>
    <footer className="br-pwa"><strong>Instale o dev.kit</strong> pelo menu do navegador, quando a instalação estiver disponível.<span className="br-desktop-state"> Reinicie o dev.kit pela máquina nesta fase.</span></footer>
  </div>;
}
