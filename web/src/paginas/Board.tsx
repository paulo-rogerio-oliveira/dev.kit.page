import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { MaquinaRemota, RetratoDaMaquina } from '../api/remoto';
import type { DecisaoPendente, ResponderDecisao } from '../api/remoto';
import { CartaoDaDecisao, RevisaoDaDecisao, type AoDecidir } from '../componentes/DecisoesRemotas';
import { useSessao } from '../sessao';
import { prepararBoardPwa } from '../boardPwa';
import { Abas } from '../componentes/Abas';
import { ChatRemoto } from '../componentes/ChatRemoto';
import './Board.css';
import '../componentes/ChatRemoto.css';

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

function ConteudoDoBoard({ foto, agora, decisoes, desabilitado, decidir, revisar }: { foto: RetratoDaMaquina | null; agora: number; decisoes: DecisaoPendente[]; desabilitado: boolean; decidir: AoDecidir; revisar: (d: DecisaoPendente) => void }) {
  const retrato = foto?.retrato;
  return <>
    <h2>Precisa de você · {retrato?.pendencias?.length ?? 0}</h2>
    {retrato?.pendencias?.map(p => {
      const d = decisoes.find(d => d.pendencia.id === p.id);
      return d ? <CartaoDaDecisao key={`${p.id}-${d.snapshotId}`} decisao={d} compacta desabilitado={desabilitado} decidir={decidir} revisar={revisar} />
        : <section className="br-card br-need" key={p.id}><b>{p.titulo}</b><p>{p.pergunta}</p><p>{p.jaTentei}</p><p className="br-note">Aguardando os dados da decisão.</p></section>;
    })}
    {!retrato?.pendencias?.length && <p className="br-note">Nenhuma pendência recebida.</p>}
    <p className="br-note">Confira os detalhes antes de decidir. O texto do chat nunca aprova.</p>
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

/** Board isolado da administração, com decisões auditadas e aprovação vinculada ao snapshot. */
export function Board() {
  useEffect(prepararBoardPwa, []);
  const { sessao, sair } = useSessao();
  const [parametros, setParametros] = useSearchParams();
  const aba = parametros.get('aba') === 'chat' ? 'chat' : 'board';
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
  const mudou = !!revisao && !decisoes.some(d => d.pendencia.id === revisao.pendencia.id && d.snapshotId === revisao.snapshotId);

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
      <button className="br-btn br-restart" disabled title={motivo}>Reiniciar dev.kit</button>
    </header>
    {revisao && <RevisaoDaDecisao key={`${selecionada}-${revisao.snapshotId}`} decisao={mudou ? { ...revisao, bloqueio: 'O snapshot mudou. Volte ao Board e revise as novas evidências.' } : revisao} decidir={decidir} voltar={() => { setRevisao(null); setTentativa(t => t + 1); }} desabilitado={desabilitado || mudou} />}
    <div className="br-navegacao" hidden={!!revisao}><Abas rotulo="Navegação do Board" prefixo="board-aba" abas={[{ id: 'chat', rotulo: 'Chat' }, { id: 'board', rotulo: 'Board' }]} ativa={aba} aoMudar={id => setParametros({ aba: id }, { replace: true })}>
    <main className="br-main">
      <aside className="br-side" aria-label="Board">
        <div className="br-readonly"><strong>Decisões remotas</strong><span>Fase 3</span></div>
        {carregando && <p role="status" className="br-note">Carregando Board…</p>}
        {erro && <div role="alert" className="br-card br-need"><p>{erro}</p>{foto && <p>Exibindo o último retrato recebido; os dados podem estar desatualizados.</p>}<button className="br-btn" onClick={() => setTentativa(t => t + 1)}>Tentar novamente</button></div>}
        {!carregando && !erro && !maquina && <p className="br-note">Nenhuma máquina cadastrada. Abra o dev.kit na máquina para enviar o primeiro heartbeat.</p>}
        {maquina && !conectada && <p role="status" className="br-note">Máquina desconectada. O último retrato pode estar desatualizado.</p>}
        {!carregando && maquina && !foto && !erro && <p className="br-note">Aguardando o primeiro retrato desta máquina.</p>}
        {(!carregando || foto) && <ConteudoDoBoard key={selecionada} foto={foto} agora={agora} decisoes={decisoes} desabilitado={desabilitado} decidir={decidir} revisar={setRevisao} />}
      </aside>
      <ChatRemoto key={selecionada} maquina={maquina} conectada={conectada} agora={agora} decisoes={decisoes} desabilitarDecisoes={desabilitado} decidir={decidir} revisar={setRevisao} />
    </main>
    </Abas></div>
    <div className="br-pwa"><strong>Instale o dev.kit</strong> pelo menu do navegador, quando a instalação estiver disponível.</div>
    <footer className="br-footer"><span className="br-desktop-state">Reinicie o dev.kit pela máquina nesta fase.</span><span className="br-mobile-state">Texto do chat nunca aprova · decisões auditadas</span></footer>
  </div>;
}
