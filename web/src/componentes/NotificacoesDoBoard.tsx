import { useEffect, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import { ativarPush, conferirPush, desligarPush, suportaPush } from '../boardPush';
import { useSessao } from '../sessao';
import './NotificacoesDoBoard.css';

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
