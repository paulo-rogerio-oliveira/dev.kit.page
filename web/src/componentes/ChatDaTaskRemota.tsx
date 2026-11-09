import { useEffect, useRef, useState } from 'react';
import type { MaquinaRemota } from '../api/remoto';
import { useConversaRemota } from './useConversaRemota';
import { rotuloDoDia } from './filtroDeDia';

export interface TaskDaConversa { conversaId: string; nome: string; agente?: string | null; etapa: string }

/** O histórico e o envio para uma task específica; o Orquestrador continua acessível pelo Voltar. */
export function ChatDaTaskRemota({ task, maquina, conectada, emExecucao, agora, voltar }: {
  task: TaskDaConversa; maquina: MaquinaRemota | undefined; conectada: boolean; emExecucao: boolean; agora: number; voltar: () => void;
}) {
  const { conversa, carregando, erro, erroDaAbertura, falhaNoEnvio, enviando, enviar, pedirConversa, recarregar } = useConversaRemota(maquina?.maquinaId ?? '', task.conversaId);
  const [renovacao, setRenovacao] = useState(0);
  const [texto, setTexto] = useState('');
  const caixa = useRef<HTMLTextAreaElement>(null);
  const mensagens = useRef<HTMLDivElement>(null);
  const pertoDoFim = useRef(true);
  useEffect(() => {
    if (!conectada || !emExecucao) return;
    void pedirConversa();
    const timer = window.setInterval(() => void pedirConversa(), 60_000);
    return () => window.clearInterval(timer);
  }, [conectada, emExecucao, pedirConversa, renovacao]);
  const pedidos = conversa.comandos.filter(c => c.tipo === 'mensagem-task');
  const abertura = conversa.comandos.filter(c => c.tipo === 'conversa-task').at(-1);
  const recusa = conversa.respostas.find(r => r.comandoId === abertura?.id && r.tipo === 'recusado');
  const linhas = [
    ...pedidos.map(c => ({ id: `c-${c.id}`, texto: c.texto, data: c.criadoEm, doDev: true, comando: c })),
    ...(conversa.falas ?? []).filter(f => !f.comandoId || !pedidos.some(c => c.id === f.comandoId))
      .map(f => ({ id: `f-${f.id}`, texto: f.texto, data: f.ditaEm, doDev: f.autor === 'dev', comando: null })),
  ].sort((a, b) => Date.parse(a.data) - Date.parse(b.data) || a.id.localeCompare(b.id));
  const ultima = linhas.at(-1)?.id;
  useEffect(() => {
    if (pertoDoFim.current && mensagens.current) mensagens.current.scrollTop = mensagens.current.scrollHeight;
  }, [ultima]);
  const desabilitado = !maquina || carregando || enviando || !emExecucao;
  async function submeter() {
    if (desabilitado || !texto.trim() || texto.length > 4000) return;
    if (await enviar(texto.trim())) { setTexto(''); caixa.current?.focus(); pertoDoFim.current = true; }
  }
  return <section className="br-chat" aria-label={`Conversa com o agente de ${task.nome}`}>
    <header className="br-chathead br-taskhead"><div><b>{task.nome}</b><small>{task.etapa}{task.agente && ` · ${task.agente}`}</small></div>
      <button type="button" className="br-btn" onClick={voltar}>Voltar ao Orquestrador</button></header>
    <div className="br-chat-aviso">Conversa com o agente desta task. Mensagens aguardam a vez se ele estiver em um turno.</div>
    {!conectada && <div className="br-chat-aviso" role="status">Máquina desconectada. O envio aguarda a conexão até o prazo vencer.</div>}
    {!emExecucao && <div className="br-chat-aviso" role="status">Esta task não está mais em execução. O histórico continua disponível.</div>}
    {(erro || erroDaAbertura) && <div className="br-chat-aviso" role="alert">{erro || erroDaAbertura}<button type="button" className="br-btn" onClick={() => { recarregar(); setRenovacao(n => n + 1); }}>Tentar novamente</button></div>}
    {recusa && <div className="br-chat-aviso" role="alert">{recusa.texto}</div>}
    <div className="br-mensagens br-conversa" ref={mensagens} role="log" aria-label="Conversa da task" aria-live="polite" aria-relevant="additions text"
      onScroll={() => { const e = mensagens.current!; pertoDoFim.current = e.scrollHeight - e.scrollTop - e.clientHeight < 60; }}>
      {carregando && <p role="status" className="br-note">Carregando conversa da task…</p>}
      {!carregando && !linhas.length && <p className="br-chat-vazio">Aguardando o histórico e as respostas do agente desta task.</p>}
      {linhas.map((linha, indice) => {
        const comando = linha.comando;
        const resposta = comando && conversa.respostas.find(r => r.comandoId === comando.id);
        const estado = !comando ? '' : comando.estado === 'concluido' || comando.estado === 'respondido' ? '✓ Registrada para o agente'
          : comando.estado === 'recusado' ? 'Recusada' : comando.estado === 'expirado' ? 'Prazo vencido'
            : comando.estado === 'falhou' ? 'Falha no envio' : 'Aguardando confirmação';
        return <article key={linha.id} className={`br-msg${linha.doDev ? ' br-voce' : ''}`}>
          {(indice === 0 || new Date(linha.data).toDateString() !== new Date(linhas[indice - 1].data).toDateString()) && <p className="br-dia">{rotuloDoDia(linha.data, agora)}</p>}
          <div className="br-meta"><span>{linha.doDev ? 'Você' : task.agente || 'Agente da task'} · <time dateTime={linha.data} title={new Date(linha.data).toLocaleString('pt-BR')}>{new Date(linha.data).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}</time></span>{estado && <strong>{estado}</strong>}</div>
          <p>{linha.texto}</p>
          {resposta && <p className="br-msg-status">{resposta.texto}</p>}
          {comando?.estado === 'falhou' && <button type="button" className="br-btn" disabled={desabilitado} onClick={() => void enviar(comando.texto, comando)}>Tentar envio novamente</button>}
        </article>;
      })}
    </div>
    {falhaNoEnvio && <div className="br-chat-aviso" role="alert">{falhaNoEnvio}</div>}
    <form className="br-composer br-composer-ativo" onSubmit={e => { e.preventDefault(); void submeter(); }}>
      <textarea ref={caixa} aria-label="Mensagem ao agente da task" disabled={desabilitado} placeholder="Peça ou pergunte ao agente desta task" value={texto}
        maxLength={4000} rows={2} onChange={e => setTexto(e.target.value)} onKeyDown={e => {
          if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) { e.preventDefault(); void submeter(); }
        }} />
      <div className="br-bottom"><span className="br-note">Texto do chat nunca aprova.</span><span className="br-spacer" />
        <button type="submit" className="br-send" aria-label="Enviar mensagem ao agente" disabled={desabilitado || !texto.trim()}>{enviando ? '…' : '➤'}</button></div>
    </form>
  </section>;
}
