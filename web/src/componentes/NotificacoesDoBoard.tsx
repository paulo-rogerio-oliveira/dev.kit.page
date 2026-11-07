import { useCallback, useEffect, useRef, useState, type FocusEvent } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { PendenciaRemota } from '../api/remoto';
import { ativarPush, conferirPush, desligarPush, suportaPush } from '../boardPush';
import { useSessao } from '../sessao';
import { usePrefereMenosMovimento } from './usePrefereMenosMovimento';
import './NotificacoesDoBoard.css';

/** O aviso do site (US #858): some sozinho, agrupa os repetidos pela chave e fica no histórico do painel. */
export interface AvisoDoBoard { id: number; chave: string; texto: string; contagem: number; em: number; ativo: boolean }

export const duracaoDoAviso = 6_000;
const avisosNoHistorico = 20;
const avisosNaTela = 3;

export function useAvisosDoBoard() {
  const [avisos, setAvisos] = useState<AvisoDoBoard[]>([]);
  const proximo = useRef(0);
  /** Um aviso igual (mesma chave) ainda na tela vira um só, com contador, e o tempo recomeça. */
  const avisar = useCallback((texto: string, chave = texto) => {
    const id = ++proximo.current;
    setAvisos(lista => {
      const igual = lista.find(a => a.ativo && a.chave === chave);
      if (igual) return lista.map(a => a === igual ? { ...a, texto, contagem: a.contagem + 1, em: Date.now() } : a);
      return [{ id, chave, texto, contagem: 1, em: Date.now(), ativo: true }, ...lista].slice(0, avisosNoHistorico);
    });
  }, []);
  const encerrar = useCallback((id: number) => setAvisos(lista => lista.map(a => a.id === id ? { ...a, ativo: false } : a)), []);
  return { avisos, avisar, encerrar };
}

/** As pendências que chegaram entre um retrato e o seguinte; o primeiro retrato da máquina não avisa nada. */
export function pendenciasNovas(anteriores: ReadonlySet<string> | null, atuais: PendenciaRemota[]) {
  return anteriores ? atuais.filter(p => !anteriores.has(p.id)) : [];
}

function AvisoComTimer({ aviso, pausado, menosMovimento, encerrar }: { aviso: AvisoDoBoard; pausado: boolean; menosMovimento: boolean; encerrar: (id: number) => void }) {
  const restante = useRef(duracaoDoAviso);
  const contagem = useRef(aviso.contagem);
  useEffect(() => {
    if (contagem.current !== aviso.contagem) { contagem.current = aviso.contagem; restante.current = duracaoDoAviso; }
    if (pausado) return;
    const inicio = Date.now();
    const timer = window.setTimeout(() => encerrar(aviso.id), restante.current);
    return () => { window.clearTimeout(timer); restante.current -= Date.now() - inicio; };
  }, [pausado, aviso.id, aviso.contagem, encerrar]);
  return <div className="br-aviso">
    <p>{aviso.texto}</p>
    {aviso.contagem > 1 && <span className="br-aviso-contagem" title={`Repetido ${aviso.contagem} vezes`}>×{aviso.contagem}</span>}
    <button className="br-aviso-fechar" aria-label="Fechar aviso" onClick={() => encerrar(aviso.id)}>×</button>
    {!menosMovimento && <i key={aviso.contagem} className="br-aviso-tempo" aria-hidden="true" style={{ animationDuration: `${duracaoDoAviso}ms`, animationPlayState: pausado ? 'paused' : 'running' }} />}
  </div>;
}

/** Os avisos na tela: pausam com o mouse ou o foco, fecham no × e somem depois de `duracaoDoAviso`. */
export function AvisosDoBoard({ avisos, encerrar }: { avisos: AvisoDoBoard[]; encerrar: (id: number) => void }) {
  const [pausado, setPausado] = useState(false);
  const menosMovimento = usePrefereMenosMovimento();
  const ativos = avisos.filter(a => a.ativo).slice(0, avisosNaTela);
  const sair = (e: FocusEvent<HTMLDivElement>) => { if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setPausado(false); };
  return <div className={`br-avisos${menosMovimento ? '' : ' br-avisos-animados'}`} aria-live="polite"
    onMouseEnter={() => setPausado(true)} onMouseLeave={() => setPausado(false)} onFocus={() => setPausado(true)} onBlur={sair}>
    {ativos.map(a => <AvisoComTimer key={a.id} aviso={a} pausado={pausado} menosMovimento={menosMovimento} encerrar={encerrar} />)}
  </div>;
}

function hora(momento: number) { return new Date(momento).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }); }

/** A seção Avisos do painel, recolhida: os últimos avisos da sessão, inclusive os fechados e os expirados. */
export function HistoricoDeAvisos({ avisos }: { avisos: AvisoDoBoard[] }) {
  const [aberto, setAberto] = useState(false);
  return <section className="br-historico">
    <button className="br-secao" aria-expanded={aberto} aria-controls="board-avisos" onClick={() => setAberto(a => !a)}>
      Avisos · {avisos.length}<span aria-hidden="true">{aberto ? '▾' : '▸'}</span>
    </button>
    <div id="board-avisos">{aberto && (avisos.length
      ? <ul>{avisos.map(a => <li key={a.id}><span>{a.texto}{a.contagem > 1 && ` ×${a.contagem}`}</span><small>{hora(a.em)}</small></li>)}</ul>
      : <p className="br-note">Nenhum aviso nesta sessão.</p>)}</div>
  </section>;
}

export function NotificacoesDoBoard() {
  const { sessao, sair } = useSessao();
  const [chave, setChave] = useState('');
  const [ativa, setAtiva] = useState(false);
  const [ocupada, setOcupada] = useState(true);
  const [limite, setLimite] = useState('');
  const [erro, setErro] = useState('');
  const [revisao, setRevisao] = useState(0);
  useEffect(() => {
    const conferir = () => { if (document.visibilityState === 'visible') setRevisao(n => n + 1); };
    const mensagem = (evento: MessageEvent) => { if (evento.data?.tipo === 'push-alterado') conferir(); };
    document.addEventListener('visibilitychange', conferir);
    navigator.serviceWorker?.addEventListener('message', mensagem);
    return () => { document.removeEventListener('visibilitychange', conferir); navigator.serviceWorker?.removeEventListener('message', mensagem); };
  }, []);
  useEffect(() => {
    let terminou = false;
    async function conferir() {
      if (!sessao) return;
      try {
        if (!suportaPush()) { setLimite('Notificações indisponíveis neste navegador. Confira a compatibilidade abaixo.'); return; }
        const config = await api.configuracaoDoPush(sessao.token);
        if (terminou) return;
        setChave(config.chavePublica ?? '');
        if (!config.disponivel) setLimite('Notificações indisponíveis neste ambiente.');
        else { const assinada = await conferirPush(sessao.token); if (!terminou) setAtiva(assinada); }
      } catch (e) { if (!terminou) setErro(mensagemDaFalha(e)); }
      finally { if (!terminou) setOcupada(false); }
    }
    void conferir();
    return () => { terminou = true; };
  }, [sessao, revisao]);
  async function alternar() {
    if (!sessao || ocupada) return;
    setOcupada(true); setErro('');
    try {
      if (ativa) await desligarPush(sessao.token); else await ativarPush(sessao.token, chave);
      setAtiva(!ativa);
    } catch (e) {
      if (e instanceof ErroDaApi && e.status === 401) sair('expirou');
      else setErro(mensagemDaFalha(e));
    } finally { setOcupada(false); }
  }
  return <details className="br-push">
    <summary>Notificações neste dispositivo <span>{ocupada ? 'Verificando…' : ativa ? 'Ativadas' : limite ? 'Indisponíveis' : 'Desligadas'}</span></summary>
    <div className="br-push-conteudo"><div><strong>Avise quando precisar de mim</strong>
      <p>Receba um aviso de pergunta ou aprovação pendente. Toque na notificação para abrir a decisão.</p>
      <small>O aviso não mostra perguntas, respostas nem detalhes do projeto.</small></div>
      <button className={`br-btn${ativa ? '' : ' br-primary'}`} disabled={ocupada || (!ativa && (!chave || !!limite))} onClick={() => void alternar()}>{ocupada ? 'Verificando…' : ativa ? 'Desligar notificações' : 'Ativar notificações'}</button>
      {limite && <p className="br-push-mensagem">{limite}</p>}
      {erro && <p className="br-push-mensagem" role="alert">{erro}</p>}
      {erro && !chave && <button className="br-btn" onClick={() => { setErro(''); setOcupada(true); setRevisao(n => n + 1); }}>Tentar novamente</button>}
      <details><summary>Compatibilidade e privacidade</summary>
        <p>iPhone e iPad: iOS/iPadOS 16.4 ou superior, com o dev.kit adicionado à Tela de Início. Android e computador: navegador com suporte a push e permissão de notificações.</p>
        <p>O sistema pode atrasar ou silenciar avisos. Você pode desligar neste dispositivo a qualquer momento. Avisos já enviados podem chegar por até cinco minutos.</p>
        <p>Chrome, Edge e Firefox usam o serviço de push do navegador. Safari no Mac requer macOS Ventura/Safari 16 ou superior. Navegação privada e políticas do dispositivo podem impedir o recurso.</p>
      </details>
    </div>
  </details>;
}
