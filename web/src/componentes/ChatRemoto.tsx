import { Fragment, useEffect, useRef, useState } from 'react';
import type { ComandoRemoto, FalaRemota, MaquinaRemota, RespostaRemota } from '../api/remoto';
import type { ConversaDaMaquina } from './useConversaRemota';
import { ContextoDoChatRemoto, EstadoDoCatalogo, contextoVazio, textoComContexto } from './ContextoDoChatRemoto';
import { useCatalogoRemoto } from './useCatalogoRemoto';
import { comandosDoServico, rotuloDoComando } from './ControleDoDevKit';
import { rotuloDoDia } from './filtroDeDia';

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
    case 'concluido': return ['✓ Entregue', comandosDoServico.includes(comando.tipo) ? 'Concluído pelo serviço da máquina.' : 'Concluído pelo dev.kit.', true] as const;
    default: return ['✓ Entregue', '', true] as const;
  }
}

/** Uma linha do chat: o comando do site, a resposta do dev.kit a ele, ou a fala do chat do dev.kit (o desktop). */
type LinhaDoChat = { id: string; data: string; comando: ComandoRemoto | null; resposta: RespostaRemota | null; fala: FalaRemota | null };

/** Quem disse a linha: o dev pelo site (com o dispositivo) ou no dev.kit, o Orquestrador ou o serviço da máquina. */
function autorDaLinha(linha: LinhaDoChat, doServico: Set<string>) {
  if (linha.comando) return `Você · remoto · ${linha.comando.dispositivo}`;
  if (linha.fala) return linha.fala.autor === 'dev' ? 'Você · no dev.kit' : 'Orquestrador · dev.kit';
  return doServico.has(linha.resposta?.comandoId ?? '') ? 'Serviço da máquina · dev.kit' : 'Orquestrador · dev.kit';
}

/**
 * A fala de um turno AUTOMÁTICO do Orquestrador (remote-control-v3, item 3): o git.kit marca na origem o turno de lote — a
 * resposta aos avisos, retratos e gatilhos da varredura — com o id `fl-` (JobPonteRemota.TurnoDeLote). Não é conversa: o site
 * não a mostra, e o que ela anuncia já está no painel de decisões.
 */
export function turnoAutomatico(fala: FalaRemota) { return fala.autor !== 'dev' && fala.id.startsWith('fl-'); }

/**
 * A chave da fala espontânea do Orquestrador (a fala sem comando e a resposta sem comando): o mesmo texto repetido é o
 * mesmo aviso empilhado, e o chat mostra só a última vez. A fala do dev e a resposta a um comando nunca se juntam.
 */
function chaveDeRepeticao(linha: LinhaDoChat) {
  const texto = linha.fala && linha.fala.autor !== 'dev' && !linha.fala.comandoId ? linha.fala.texto
    : linha.resposta && !linha.resposta.comandoId ? linha.resposta.texto : null;
  return texto === null ? null : texto.replace(/\s+/g, ' ').trim();
}

/** As linhas em ordem, sem a repetição de uma fala espontânea do Orquestrador: fica a mais recente, no lugar dela. */
function semRepeticao(linhas: LinhaDoChat[]) {
  const ultimaVez = new Map<string, string>();
  for (const linha of linhas) { const chave = chaveDeRepeticao(linha); if (chave) ultimaVez.set(chave, linha.id); }
  return linhas.filter(linha => { const chave = chaveDeRepeticao(linha); return !chave || ultimaVez.get(chave) === linha.id; });
}

/**
 * O chat do Orquestrador, sobre a conversa que o Board abre (a mesma do controle do dev.kit, item 8): as mensagens, as
 * respostas e o Ligar/Reiniciar pedido ao serviço da máquina, com o desfecho que ele mandou — e as falas do chat do dev.kit
 * (o que o dev digitou no desktop e a resposta do Orquestrador), para o site mostrar a mesma conversa (item 8 do v3).
 * Só a CONVERSA (remote-control-v3, item 3): as decisões e aprovações ficam no painel de decisões, com o segundo fator e o
 * SHA conferido, e os turnos automáticos e os avisos repetidos não entram.
 */
export function ChatRemoto({ maquina, conectada, agora, conversaRemota }: {
  maquina: MaquinaRemota | undefined; conectada: boolean; agora: number; conversaRemota: ConversaDaMaquina;
}) {
  const { conversa, erro, falhaNoEnvio, carregando, enviando, enviar, recarregar } = conversaRemota;
  const [texto, setTexto] = useState('');
  const [contexto, setContexto] = useState(contextoVazio);
  const [atalho, setAtalho] = useState<'/' | '#' | '@' | null>(null);
  const mensagens = useRef<HTMLDivElement>(null);
  const caixa = useRef<HTMLTextAreaElement>(null);
  const pertoDoFim = useRef(true);
  const textoFinal = textoComContexto(texto, contexto);
  const excedeu = textoFinal.length > 4000;
  const desabilitado = !maquina || carregando || enviando;
  const doServico = new Set(conversa.comandos.filter(c => comandosDoServico.includes(c.tipo)).map(c => c.id));
  // O handshake dos atalhos (remote-control-v3) não é conversa: nem o pedido nem a resposta dele entram no chat.
  const catalogo = useCatalogoRemoto({ maquina, conectada, conversa: conversaRemota });
  const comandosDaConversa = new Set(conversa.comandos.map(c => c.id));
  const linhas = semRepeticao([
    ...conversa.comandos.filter(c => c.tipo === 'mensagem' || doServico.has(c.id)).map(c => ({ id: `c-${c.id}`, data: c.criadoEm, comando: c, resposta: null, fala: null })),
    ...conversa.respostas.filter(r => !r.comandoId || !catalogo.idsDosPedidos.has(r.comandoId))
      .map(r => ({ id: `r-${r.id}`, data: r.geradaEm, comando: null, resposta: r, fala: null })),
    // As falas do chat do dev.kit, na hora em que foram ditas; a de um comando do site já aparece como ele (não repete), e a
    // de um turno automático do Orquestrador não é conversa.
    ...(conversa.falas ?? []).filter(f => !(f.comandoId && comandosDaConversa.has(f.comandoId)) && !turnoAutomatico(f))
      .map(f => ({ id: `f-${f.id}`, data: f.ditaEm, comando: null, resposta: null, fala: f })),
  ].sort((a, b) => Date.parse(a.data) - Date.parse(b.data) || a.id.localeCompare(b.id)));
  const ultima = linhas.at(-1)?.id;
  // A rolagem fica na última mensagem: na mensagem nova e quando a área do chat muda de altura (um aviso que entra ou sai).
  useEffect(() => {
    if (pertoDoFim.current && mensagens.current) mensagens.current.scrollTop = mensagens.current.scrollHeight;
  }, [ultima, carregando]);
  useEffect(() => {
    const area = mensagens.current;
    if (!area || typeof ResizeObserver === 'undefined') return;
    const observador = new ResizeObserver(() => { if (pertoDoFim.current) area.scrollTop = area.scrollHeight; });
    observador.observe(area);
    return () => observador.disconnect();
  }, []);
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
    <div className="br-chat-aviso br-chat-seguranca">Conversa e consulta com login. Texto do chat nunca aprova.</div>
    {maquina && (!conectada || !maquina.appAberto) && <div className="br-chat-aviso" role="status">
      <strong>○ dev.kit fora do ar</strong><span>Enviado não significa entregue. Aguarda a conexão até o prazo vencer.</span>
    </div>}
    {maquina?.appAberto && conectada && ['pausado', 'limite'].includes(maquina.estadoDoAgente) && <div className="br-chat-aviso" role="status">
      Agente {maquina.estadoDoAgente === 'limite' ? 'em limite' : 'pausado'}. A mensagem pode aguardar atendimento no dev.kit.
    </div>}
    {erro && <div className="br-chat-aviso" role="alert"><span>{erro} O histórico exibido pode estar desatualizado.</span><button className="br-btn" onClick={recarregar}>Reconectar conversa</button></div>}
    <div className="br-mensagens br-conversa" ref={mensagens} role="log" aria-label="Conversa com o Orquestrador" aria-live="polite" aria-relevant="additions text"
      onScroll={() => { const e = mensagens.current!; pertoDoFim.current = e.scrollHeight - e.scrollTop - e.clientHeight < 60; }}>
      {!linhas.length && <p className="br-dia">Hoje · mesma conversa do dev.kit</p>}
      {carregando && <p className="br-note" role="status">Carregando conversa…</p>}
      {!carregando && !linhas.length && <p className="br-chat-vazio">{maquina ? 'Nenhuma mensagem recebida. Peça ou pergunte ao Orquestrador.' : 'Selecione uma máquina para conversar com o Orquestrador.'}</p>}
      {linhas.map((linha, indice) => {
        const { comando, fala } = linha;
        const estado = comando ? estadoDo(comando, agora) : null;
        const doDev = !!comando || fala?.autor === 'dev';
        const novoDia = indice === 0 || new Date(linha.data).toDateString() !== new Date(linhas[indice - 1].data).toDateString();
        return <Fragment key={linha.id}>
          {novoDia && <p className="br-dia">{rotuloDoDia(linha.data, agora)} · mesma conversa do dev.kit</p>}
          <article className={`br-msg${doDev ? ' br-voce' : ''}`}>
          <div className="br-meta"><span>{autorDaLinha(linha, doServico)} · <time dateTime={linha.data} title={dataCompleta(linha.data)}>{hora(linha.data)}</time></span>
            {estado && <strong className={estado[2] ? 'br-entregue' : 'br-espera'}>{estado[0]}</strong>}</div>
          <p>{comando ? (doServico.has(comando.id) ? rotuloDoComando(comando.tipo) : comando.texto) : fala ? fala.texto : linha.resposta?.texto} {estado?.[1] && <span className="br-msg-status">{estado[1]}</span>}</p>
          {comando?.estado === 'falhou' && comando.tipo === 'mensagem' && <button className="br-btn" disabled={enviando} onClick={() => void reenviar(comando)}>Tentar envio novamente</button>}
          </article>
        </Fragment>;
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
        <ContextoDoChatRemoto contexto={contexto} alterar={setContexto} atalho={atalho} abrir={setAtalho} desabilitado={desabilitado}
          catalogo={catalogo.catalogo?.catalogo ?? null} />
        <span className="br-spacer" /><button type="submit" className="br-send" disabled={desabilitado || !texto.trim() || excedeu} aria-label="Enviar mensagem">{enviando ? '…' : '➤'}</button>
      </div>
      {contexto.branch && <div className="br-branch">Branch existente · {contexto.branch}</div>}
      {excedeu && <p className="br-note" role="alert">Texto e contexto excedem o limite de 4000 caracteres.</p>}
    </form>
    {maquina && <EstadoDoCatalogo estado={catalogo} appAberto={conectada && maquina.appAberto} agora={agora} />}
    <p className="br-hint">/ fluxo, projeto ou design · # work item · @ repositório<br />Enter envia · Shift+Enter quebra linha</p>
  </section>;
}
