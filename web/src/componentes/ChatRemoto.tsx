import { useEffect, useRef, useState } from 'react';
import type { ComandoRemoto, MaquinaRemota } from '../api/remoto';
import { useConversaRemota } from './useConversaRemota';
import { ContextoDoChatRemoto, contextoVazio, textoComContexto } from './ContextoDoChatRemoto';

function hora(data: string) { return new Date(data).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }); }
function dataCompleta(data: string) { return new Date(data).toLocaleString('pt-BR'); }
function estadoDo(comando: ComandoRemoto, agora: number) {
  switch (comando.estado) {
    case 'pendente': return ['Registrado', 'Entrando na fila; envio ainda não confirmado.', false] as const;
    case 'na-fila': return ['Enviado', agora >= Date.parse(comando.expiraEm)
      ? 'Prazo vencido; aguardando confirmação da API.'
      : `Aguardando dev.kit; expira em ${Math.max(1, Math.ceil((Date.parse(comando.expiraEm) - agora) / 60000))} min.`, false] as const;
    case 'expirado': return ['Expirado', 'Prazo vencido; não entregue.', false] as const;
    case 'falhou': return ['Falhou', comando.erro || 'A fila não aceitou a mensagem.', false] as const;
    case 'recusado': return ['Recusado', comando.erro || 'O dev.kit recusou o pedido.', false] as const;
    case 'respondido': return ['✓ Entregue', 'Respondido pelo dev.kit.', true] as const;
    case 'concluido': return ['✓ Entregue', 'Concluído pelo dev.kit.', true] as const;
    default: return ['✓ Entregue', '', true] as const;
  }
}

export function ChatRemoto({ maquina, conectada, agora }: { maquina: MaquinaRemota | undefined; conectada: boolean; agora: number }) {
  const { conversa, erro, falhaNoEnvio, carregando, enviando, enviar, recarregar } = useConversaRemota(maquina?.maquinaId ?? '');
  const [texto, setTexto] = useState('');
  const [contexto, setContexto] = useState(contextoVazio);
  const [atalho, setAtalho] = useState<'/' | '#' | '@' | null>(null);
  const mensagens = useRef<HTMLDivElement>(null);
  const caixa = useRef<HTMLTextAreaElement>(null);
  const pertoDoFim = useRef(true);
  const textoFinal = textoComContexto(texto, contexto);
  const excedeu = textoFinal.length > 4000;
  const desabilitado = !maquina || carregando || enviando;
  const linhas = [
    ...conversa.comandos.filter(c => c.tipo === 'mensagem').map(c => ({ id: `c-${c.id}`, data: c.criadoEm, comando: c, resposta: null })),
    ...conversa.respostas.map(r => ({ id: `r-${r.id}`, data: r.geradaEm, comando: null, resposta: r })),
  ].sort((a, b) => Date.parse(a.data) - Date.parse(b.data) || a.id.localeCompare(b.id));
  const ultima = linhas.at(-1)?.id;
  useEffect(() => {
    if (pertoDoFim.current && mensagens.current) mensagens.current.scrollTop = mensagens.current.scrollHeight;
  }, [ultima]);
  async function submeter() {
    if (desabilitado || excedeu || !texto.trim()) return;
    if (await enviar(textoFinal)) { setTexto(''); caixa.current?.focus(); pertoDoFim.current = true; }
  }
  async function reenviar(comando: ComandoRemoto) {
    if (await enviar(comando.texto, comando)) {
      if (textoFinal === comando.texto) setTexto('');
      caixa.current?.focus();
    }
  }
  return <section className="br-chat" aria-label="Orquestrador">
    <header className="br-chathead"><span className="br-logo">O</span><div><b>Orquestrador</b><small>Mesma conversa do Board do dev.kit</small></div></header>
    {maquina && (!conectada || !maquina.appAberto) && <div className="br-chat-aviso" role="status">
      <strong>○ dev.kit fora do ar</strong><span>Enviado não significa entregue. Aguarda a conexão até o prazo vencer.</span>
    </div>}
    {maquina?.appAberto && conectada && ['pausado', 'limite'].includes(maquina.estadoDoAgente) && <div className="br-chat-aviso" role="status">
      Agente {maquina.estadoDoAgente === 'limite' ? 'em limite' : 'pausado'}. A mensagem pode aguardar atendimento no dev.kit.
    </div>}
    {erro && <div className="br-chat-aviso" role="alert"><span>{erro} O histórico exibido pode estar desatualizado.</span><button className="br-btn" onClick={recarregar}>Reconectar conversa</button></div>}
    <div className="br-mensagens br-conversa" ref={mensagens} role="log" aria-label="Conversa com o Orquestrador" aria-live="polite" aria-relevant="additions text"
      onScroll={() => { const e = mensagens.current!; pertoDoFim.current = e.scrollHeight - e.scrollTop - e.clientHeight < 60; }}>
      <p className="br-dia">Mesma conversa do dev.kit</p>
      {carregando && <p className="br-note" role="status">Carregando conversa…</p>}
      {!carregando && !linhas.length && <p className="br-chat-vazio">{maquina ? 'Nenhuma mensagem recebida. Peça ou pergunte ao Orquestrador.' : 'Selecione uma máquina para conversar com o Orquestrador.'}</p>}
      {linhas.map(linha => {
        const comando = linha.comando;
        const estado = comando ? estadoDo(comando, agora) : null;
        return <article key={linha.id} className={`br-msg${comando ? ' br-voce' : ''}${comando?.estado === 'expirado' ? ' br-expirada' : ''}`}>
          <div className="br-meta"><span>{comando ? `Você · remoto · ${comando.dispositivo}` : 'Orquestrador · dev.kit'} · <time dateTime={linha.data} title={dataCompleta(linha.data)}>{hora(linha.data)}</time></span>
            {estado && <strong className={estado[2] ? 'br-entregue' : 'br-espera'}>{estado[0]}</strong>}</div>
          <p>{comando?.texto ?? linha.resposta?.texto} {estado?.[1] && <span className="br-msg-status">{estado[1]}</span>}</p>
          {comando?.estado === 'falhou' && <button className="br-btn" disabled={enviando} onClick={() => void reenviar(comando)}>Tentar envio novamente</button>}
        </article>;
      })}
    </div>
    {falhaNoEnvio && <div className="br-chat-aviso" role="alert">{falhaNoEnvio}</div>}
    <form className="br-composer br-composer-ativo" onSubmit={e => { e.preventDefault(); void submeter(); }}>
      <textarea ref={caixa} aria-label="Mensagem ao Orquestrador" disabled={desabilitado} placeholder="Peça ou pergunte ao Orquestrador" value={texto}
        maxLength={4000} rows={2} onChange={e => setTexto(e.target.value)} onKeyDown={e => {
          if (e.nativeEvent.isComposing) return;
          if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void submeter(); }
          if (!texto && ['/', '#', '@'].includes(e.key)) { e.preventDefault(); setAtalho(e.key as '/' | '#' | '@'); }
        }} />
      <div className="br-bottom">
        <ContextoDoChatRemoto contexto={contexto} alterar={setContexto} atalho={atalho} abrir={setAtalho} desabilitado={desabilitado} />
        <span className="br-spacer" /><button type="submit" className="br-send" disabled={desabilitado || !texto.trim() || excedeu} aria-label="Enviar mensagem">{enviando ? '…' : '➤'}</button>
      </div>
      {contexto.branch && <div className="br-branch">Branch existente · {contexto.branch}</div>}
      {excedeu && <p className="br-note" role="alert">Texto e contexto excedem o limite de 4000 caracteres.</p>}
    </form>
    <p className="br-hint">/ trocar o fluxo ou design · # work item · @ repositório<br />Enter envia · Shift+Enter quebra linha</p>
  </section>;
}
