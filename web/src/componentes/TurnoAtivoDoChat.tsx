import { useRef, useState } from 'react';
import type { TurnoDoChat } from '../api/remoto';
import type { ConversaDaMaquina } from './useConversaRemota';

/** A mesma mensagem ativa nos chats de task e Orquestrador; cancelar nunca é uma fala de texto. */
export function TurnoAtivoDoChat({ turno, conversa, disponivel }: {
  turno?: TurnoDoChat; conversa: ConversaDaMaquina; disponivel: boolean;
}) {
  const [enviando, setEnviando] = useState(false);
  const [falha, setFalha] = useState('');
  const ocupado = useRef(false);
  const pedidos = conversa.conversa.comandos.filter(c => c.tipo === 'cancelar-turno');
  const pedido = turno ? pedidos.find(c => c.texto === turno.id) : pedidos.at(-1);
  const resposta = pedido && conversa.conversa.respostas.find(r => r.comandoId === pedido.id);
  const concluido = pedido?.estado === 'concluido' || pedido?.estado === 'respondido';
  const recusado = pedido?.estado === 'recusado' || pedido?.estado === 'expirado';
  const aguardando = !!pedido && ['pendente', 'na-fila', 'entregue'].includes(pedido.estado);
  if (!turno && !pedido) return null;
  const estado = pedido ? concluido ? resposta?.texto || 'Turno finalizado; conversa preservada.'
    : recusado ? resposta?.texto || pedido.erro || 'O cancelamento não foi executado. Atualize a conversa.'
    : pedido.estado === 'falhou' ? pedido.erro || 'Falha ao enviar o cancelamento. Tente novamente.'
    : 'Cancelamento solicitado; aguardando confirmação do dev.kit.' : '';
  async function cancelar() {
    if (!turno || !disponivel || ocupado.current || aguardando) return;
    ocupado.current = true; setEnviando(true); setFalha('');
    try {
      const resultado = await conversa.cancelarTurno(turno);
      if (!resultado.ok) setFalha(resultado.mensagem || 'Cancelamento não confirmado.');
    } finally { ocupado.current = false; setEnviando(false); }
  }
  return <article className="br-turno-ativo" aria-label="Mensagem ativa do agente">
    <span className={turno && !concluido ? 'br-turno-spinner' : 'br-turno-fim'} aria-hidden="true" />
    <div className="br-turno-corpo">
      {turno && !concluido && <span className="br-turno-titulo">Agente está trabalhando…</span>}
      {estado && <p className="br-turno-estado" role="status">{estado}</p>}
      {falha && <p className="br-turno-estado" role="alert">{falha}</p>}
      {turno && !concluido && !recusado && <button type="button" className="br-btn br-cancelar-turno" aria-label="Cancelar turno"
        title="Interrompe somente o turno corrente. A conversa e a pasta de trabalho continuam disponíveis."
        disabled={!disponivel || conversa.carregando || enviando || aguardando} onClick={() => void cancelar()}>Cancelar</button>}
    </div>
  </article>;
}
