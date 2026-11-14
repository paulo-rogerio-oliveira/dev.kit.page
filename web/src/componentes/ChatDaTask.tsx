import { useEffect, useRef, useState } from 'react';
import type { ComandoRemoto, MaquinaRemota, RetratoDaMaquina } from '../api/remoto';
import { useConversaRemota } from './useConversaRemota';

/** Uma execução do "Executando" do retrato: a task cuja conversa abre aqui. */
export type ExecucaoDoRetrato = NonNullable<NonNullable<RetratoDaMaquina['retrato']>['fluxos']>[number];

function hora(data: string) { return new Date(data).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }); }

/** Onde a mensagem do site está: na fila, entregue à task ou não entregue (com o porquê). */
function estadoDaMensagem(c: ComandoRemoto): [string, boolean] {
  switch (c.estado) {
    case 'pendente': case 'na-fila': return ['aguardando o dev.kit', false];
    case 'entregue': case 'respondido': case 'concluido': return ['entregue à task', true];
    case 'recusado': return ['não entregue', false];
    case 'expirado': return ['expirou sem entrega', false];
    default: return ['envio falhou', false];
  }
}

/**
 * (semana 7, item 4) A CONVERSA COM O AGENTE DE UMA TASK pelo site e pelo celular: as últimas falas do agente e do dev, e o que
 * o dev escreve entra na conversa da task como mensagem dele — como se digitasse na janela da task no desktop. O pedido é só
 * texto: aprovar, enviar e fazer merge seguem pelas decisões do painel.
 */
export function ChatDaTask({ maquina, execucao, conectada, voltar }: {
  maquina: MaquinaRemota; execucao: ExecucaoDoRetrato; conectada: boolean; voltar: () => void;
}) {
  const { conversa, erro, falhaNoEnvio, carregando, enviando, enviar, recarregar } = useConversaRemota(maquina.maquinaId, execucao.id);
  const [texto, setTexto] = useState('');
  const mensagens = useRef<HTMLDivElement>(null);
  const desabilitado = carregando || enviando;
  const doSite = new Set(conversa.comandos.map(c => c.id));
  // A mensagem do site aparece como ela (com o estado); a fala que o dev.kit publica dela não repete.
  const linhas = [
    ...conversa.comandos.filter(c => c.tipo === 'mensagem').map(c => ({ id: `c-${c.id}`, data: c.criadoEm, doDev: true, texto: c.texto, onde: 'pelo site', comando: c })),
    ...(conversa.falas ?? []).filter(f => !(f.comandoId && doSite.has(f.comandoId)))
      .map(f => ({ id: `f-${f.id}`, data: f.ditaEm, doDev: f.autor !== 'agente', texto: f.texto, onde: f.origem === 'remoto' ? 'pelo site' : 'no dev.kit', comando: null })),
  ].sort((a, b) => Date.parse(a.data) - Date.parse(b.data) || a.id.localeCompare(b.id));
  const ultima = linhas.at(-1)?.id;
  useEffect(() => { if (mensagens.current) mensagens.current.scrollTop = mensagens.current.scrollHeight; }, [ultima, carregando]);
  async function submeter() {
    if (desabilitado || !texto.trim()) return;
    if (await enviar(texto.trim())) setTexto('');
  }
  return <section className="br-chat br-chat-task" aria-label={`Conversa da task ${execucao.nome}`}>
    <header className="br-chathead">
      <button type="button" className="br-btn br-voltar-orquestrador" onClick={voltar}>‹ Orquestrador</button>
      <span className="br-logo br-logo-task" aria-hidden="true">A</span>
      <div><b>{execucao.nome}</b><small>Agente da task{execucao.etapa ? ` · ${execucao.etapa}` : ''}{execucao.agente ? ` · ${execucao.agente}` : ''}</small></div>
      <span className="br-spacer" />
      <span className="br-selo" title="A mensagem entra na conversa da task. Aprovar, enviar e fazer merge seguem pelas decisões do painel.">
        <span className="br-desktop-state">Só texto · decisões no painel</span><span className="br-mobile-state">Só texto</span>
      </span>
    </header>
    {(!conectada || !maquina.appAberto) && <div className="br-chat-aviso" role="status">
      <strong>○ dev.kit fora do ar</strong><span>Enviado não significa entregue. A mensagem aguarda a conexão até o prazo vencer.</span>
    </div>}
    {erro && <div className="br-chat-aviso" role="alert"><span>{erro}</span><button className="br-btn" onClick={recarregar}>Reconectar conversa</button></div>}
    <div className="br-mensagens br-conversa" ref={mensagens} role="log" aria-label={`Mensagens da task ${execucao.nome}`} aria-live="polite" aria-relevant="additions text">
      {carregando && <p className="br-note" role="status">Carregando conversa…</p>}
      {!carregando && !linhas.length && <p className="br-chat-vazio">Nenhuma fala desta task ainda. Escreva ao agente: a mensagem entra na conversa da task.</p>}
      {linhas.map(l => {
        const estado = l.comando ? estadoDaMensagem(l.comando) : null;
        const motivo = l.comando?.estado === 'recusado' ? conversa.respostas.find(r => r.comandoId === l.comando!.id)?.texto : undefined;
        return <article key={l.id} className={`br-msg${l.doDev ? ' br-voce' : ''}`}>
          <div className="br-meta"><span>{l.doDev ? 'Você' : 'Agente'} · {l.onde} · <time dateTime={l.data}>{hora(l.data)}</time></span>
            {estado && <strong className={estado[1] ? 'br-entregue' : 'br-espera'}>{estado[0]}</strong>}</div>
          <p>{l.texto}</p>
          {motivo && <p className="br-msg-status">{motivo}</p>}
        </article>;
      })}
    </div>
    {falhaNoEnvio && <div className="br-chat-aviso" role="alert">{falhaNoEnvio}</div>}
    <form className="br-composer br-composer-ativo" onSubmit={e => { e.preventDefault(); void submeter(); }}>
      <textarea aria-label="Mensagem à task" disabled={desabilitado} rows={3} maxLength={4000} value={texto}
        placeholder="Escreva ao agente desta task — só texto; aprovar, enviar e merge seguem pelas decisões"
        onChange={e => setTexto(e.target.value)}
        onKeyDown={e => { if (e.nativeEvent.isComposing) return; if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void submeter(); } }} />
      <div className="br-bottom"><span className="br-spacer" />
        <button type="submit" className="br-send" disabled={desabilitado || !texto.trim()} aria-label="Enviar à task">{enviando ? '…' : '➤'}</button>
      </div>
    </form>
  </section>;
}
