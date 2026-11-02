import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import type { MaquinaRemota, RetratoRemoto } from '../api/remoto';
import { Abas } from '../componentes/Abas';
import { InstalarBoard } from './board/InstalarBoard';
import { useBoardRemoto } from './board/useBoardRemoto';
import './Board.css';

const hora = (valor: string) => new Date(valor).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
const dataHora = (valor: string) => new Date(valor).toLocaleString('pt-BR');
const estados: Record<string, string> = { rodando: 'rodando', 'na-fila': 'na fila', pergunta: 'pergunta', falhou: 'falhou' };

export function Board() {
  const [consulta, setConsulta] = useSearchParams();
  const [tentativa, setTentativa] = useState(0);
  const { maquinas, painel, carregando, erro, atual } = useBoardRemoto(consulta.get('maquina') ?? '', tentativa);
  const aba = consulta.get('aba') === 'chat' ? 'chat' : 'board';
  function escolher(chave: string, valor: string) {
    const proxima = new URLSearchParams(consulta);
    proxima.set(chave, valor);
    setConsulta(proxima, { replace: true });
  }
  const maquina = painel?.maquina;
  const conectada = !!maquina?.conectada && atual;
  return <main className="board-remoto">
    <header className="board-topo">
      <Link className="board-marca" to="/conta" aria-label="dev.kit · sua conta">dev.kit</Link>
      <div className={`board-host${conectada ? '' : ' board-host-inativo'}`}>
        <span className="board-dot" aria-hidden="true" />
        {maquinas.length > 1 ? <select aria-label="Máquina" value={maquina?.maquinaId ?? ''} onChange={e => escolher('maquina', e.target.value)}>
          {maquinas.map(m => <option key={m.maquinaId} value={m.maquinaId}>{m.apelido}</option>)}
        </select> : <span>{maquina?.apelido ?? 'Máquina'}</span>}
        <span className="board-host-desktop"> · {estadoDaMaquina(maquina, atual)}</span>
      </div>
      <button className="board-btn board-reiniciar-topo" disabled>Reiniciar dev.kit</button>
      <small className="board-host-mobile">{estadoDaMaquina(maquina, atual)}</small>
    </header>
    <div className="board-corpo">
      <Abas rotulo="Board remoto" abas={[{ id: 'chat', rotulo: 'Chat' }, { id: 'board', rotulo: 'Board' }]} ativa={aba}
        aoMudar={valor => escolher('aba', valor)} prefixo="board">
        <div className="board-aviso"><strong>Somente leitura · fase 1</strong><span className="board-aviso-desktop">Acompanhe pendências, projetos e execução. Chat, decisões e comandos remotos ainda não estão disponíveis.</span><span className="board-aviso-mobile">Chat, decisões e comandos remotos indisponíveis.</span></div>
        {erro && <div className="board-erro" role="alert">{erro} {painel && 'Exibindo o último estado recebido; a conexão atual não está confirmada.'}
          <button className="board-btn" onClick={() => setTentativa(t => t + 1)}>Tentar novamente</button></div>}
        {maquina && !maquina.conectada && <p className="board-desconectada" role="status">Máquina desconectada. Serviço e dev.kit abaixo representam o último estado conhecido, de {dataHora(maquina.ultimoHeartbeat)}.</p>}
        <div className={`board-conteudo board-aba-${aba}`}>
          <aside className="board-lateral" aria-label="Estado do Board">
            {carregando && <p role="status">Carregando Board…</p>}
            {!carregando && !erro && maquinas.length === 0 && <p role="status">Nenhuma máquina conectada à sua conta. Entre no dev.kit da máquina para publicar o estado.</p>}
            {painel && !painel.retrato && <p role="status">A máquina já enviou sinal, mas ainda não publicou o retrato do Board.</p>}
            {painel?.retrato && <Cartoes retrato={painel.retrato} />}
            <div className="board-maquina-mobile"><Sinal maquina={maquina} atual={atual} /><button className="board-btn" disabled>Reiniciar dev.kit</button></div>
          </aside>
          <ChatIndisponivel />
        </div>
      </Abas>
    </div>
    <footer className="board-rodape"><div className="board-sinal-desktop"><Sinal maquina={maquina} atual={atual} /></div><InstalarBoard /></footer>
  </main>;
}

function estadoDaMaquina(maquina: MaquinaRemota | undefined, atual: boolean) {
  if (!maquina) return 'Aguardando sinal da máquina';
  const estado = `serviço ${maquina.conectada && atual ? 'ativo' : 'sem conexão confirmada'} · dev.kit ${maquina.devKitAberto ? 'aberto' : 'fechado'}`;
  return maquina.conectada && atual ? estado : `Último estado: ${estado}`;
}

function Sinal({ maquina, atual }: { maquina: MaquinaRemota | undefined; atual: boolean }) {
  return <span>{maquina ? <>{maquina.apelido} · último sinal às <time dateTime={maquina.ultimoHeartbeat} title={dataHora(maquina.ultimoHeartbeat)}>{hora(maquina.ultimoHeartbeat)}</time> · {atual ? (maquina.conectada ? 'conectado' : 'desconectado') : 'conexão não confirmada'}</> : 'Aguardando sinal da máquina'}</span>;
}

function Cartoes({ retrato }: { retrato: RetratoRemoto }) {
  return <>
    <h2>Precisa de você · {retrato.pendencias.length}</h2>
    {retrato.pendencias.length === 0 && <p className="board-vazio">Nenhuma pendência.</p>}
    {retrato.pendencias.map(p => <article className="board-card board-pendencia" key={p.id}>
      <strong>{p.titulo}</strong><p>{p.resumo}</p>
      <div className="board-acoes"><button className="board-btn" disabled>Ver detalhes</button><button className="board-btn" disabled>Decidir</button></div>
      <small>Decisões disponíveis no dev.kit da máquina.</small>
    </article>)}
    <h2>Projetos</h2>
    {retrato.projetos.length === 0 && <p className="board-vazio">Nenhum projeto publicado.</p>}
    {retrato.projetos.map(p => <article className="board-card board-projeto" key={p.slug}>
      <strong>{p.nome}<span>{p.integrados} de {p.itens}</span></strong>
      <progress aria-label={`Andamento de ${p.nome}`} max={Math.max(1, p.itens)} value={p.integrados} />
      <small>{p.situacao === 'concluido' || (p.itens > 0 && p.integrados === p.itens) ? 'incorporado ao projeto' : `onda ${p.onda} de ${p.ondas} · ${p.rodando} rodando`}</small>
    </article>)}
    <h2>Executando</h2>
    <div className="board-card">{retrato.fluxos.length === 0 ? <p className="board-vazio">Nenhuma execução publicada.</p> : retrato.fluxos.map(f => <div className="board-execucao" key={f.id}>
      <span title={[f.fluxo, f.etapa].filter(Boolean).join(' · ')}>{f.titulo}</span>
      <span className={`board-chip${f.situacao === 'na-fila' ? ' board-chip-aviso' : estados[f.situacao] ? ' board-chip-conhecido' : ' board-chip-neutro'}`}>{estados[f.situacao] ?? f.situacao}</span>
    </div>)}</div>
  </>;
}

function ChatIndisponivel() {
  return <section className="board-chat" aria-label="Chat do Orquestrador">
    <header><span className="board-logo" aria-hidden="true">O</span><div><strong>Orquestrador</strong><small>Mesma conversa do Board do dev.kit</small></div><span className="board-badge">Disponível na fase 2</span></header>
    <div className="board-mensagens"><div className="board-chat-vazio"><span className="board-bloqueio" aria-hidden="true">⊘</span>
      <h1>Chat indisponível nesta fase</h1><p>A fase 1 permite apenas acompanhar o Board.<br />O envio de mensagens será liberado na fase 2.</p>
      <p>Para conversar ou decidir, use o dev.kit na máquina.</p><span className="board-badge">Somente leitura</span></div></div>
    <div className="board-compositor"><textarea disabled aria-label="Mensagem ao Orquestrador" placeholder="Peça ou pergunte ao Orquestrador" rows={1} />
      <div><button disabled>Trocar fluxo</button><button disabled># Work item</button><button disabled>@ Repositório</button><span />
        <button className="board-modelo" disabled>Claude Code · Opus</button><button className="board-enviar" disabled aria-label="Enviar mensagem indisponível">➤</button></div></div>
    <p className="board-dica">Envio e atalhos / # @ indisponíveis na fase 1.</p>
  </section>;
}
