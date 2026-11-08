import { useEffect, useRef, useState } from 'react';
import type { ComandoRemoto, ConversaRemota, MaquinaRemota } from '../api/remoto';
import type { DesfechoDoAcionamento } from './useConversaRemota';

/** Os comandos que o SERVIÇO da máquina executa (item 8): ele liga e reinicia o dev.kit, o Orquestrador não. */
export const comandosDoServico = ['ligar', 'reiniciar'];
const emCurso = ['pendente', 'na-fila', 'entregue'];
/** Quanto tempo o desfecho do último Ligar/Reiniciar fica à vista abaixo do cabeçalho. */
const vistaDoDesfechoMs = 15 * 60_000;

function hora(data: string) { return new Date(data).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }); }
export function rotuloDoComando(tipo: string) { return tipo === 'ligar' ? 'Ligar o dev.kit' : 'Reiniciar o dev.kit'; }

/** Onde o último Ligar/Reiniciar está, com o texto do serviço quando ele já respondeu. */
function situacao(comando: ComandoRemoto, conversa: ConversaRemota) {
  const resposta = conversa.respostas.filter(r => r.comandoId === comando.id).at(-1);
  switch (comando.estado) {
    case 'pendente': case 'na-fila': return { classe: '', titulo: 'aguardando o serviço da máquina', texto: 'O pedido está na fila; quem executa é o serviço do dev.kit.' };
    case 'entregue': return { classe: '', titulo: 'em execução no serviço', texto: comando.tipo === 'reiniciar' ? 'Fechando o dev.kit e ligando de novo, sem janela.' : 'Abrindo o dev.kit sem janela.' };
    case 'concluido': return { classe: ' br-ok', titulo: '', texto: resposta?.texto || 'Concluído pelo serviço da máquina.' };
    case 'recusado': return { classe: ' br-recusa', titulo: 'não executado', texto: resposta?.texto || comando.erro || 'O serviço recusou o pedido.' };
    case 'expirado': return { classe: ' br-recusa', titulo: 'expirado', texto: 'O prazo venceu antes de o serviço receber o pedido: a máquina pode estar desligada.' };
    default: return { classe: ' br-recusa', titulo: 'falhou', texto: comando.erro || 'A fila não aceitou o pedido. Tente de novo.' };
  }
}

export interface DadosDoControle {
  maquina: MaquinaRemota | undefined; conectada: boolean; conversa: ConversaRemota; agora: number;
  acionar: (tipo: 'ligar' | 'reiniciar', confirmarReinicio?: boolean) => Promise<DesfechoDoAcionamento>;
}

/**
 * LIGAR e REINICIAR o dev.kit da máquina pelo site (item 8): quem executa é o serviço do Windows (GitKit.Scheduler). Com o
 * dev.kit fechado, o botão é Ligar; aberto, Reiniciar — e, com turno em andamento, o site avisa e pede confirmação, porque
 * reiniciar interrompe o turno. O desfecho é o do serviço (concluído ou recusado, com o motivo), nunca inferido do heartbeat.
 */
export function useControleDoDevKit({ maquina, conectada, conversa, acionar, agora }: DadosDoControle) {
  const [confirmando, setConfirmando] = useState(false);
  const [enviando, setEnviando] = useState(false);
  const [falha, setFalha] = useState('');
  const ultimo = conversa.comandos.filter(c => comandosDoServico.includes(c.tipo))
    .sort((a, b) => Date.parse(a.criadoEm) - Date.parse(b.criadoEm)).at(-1);
  const ocupado = enviando || (!!ultimo && emCurso.includes(ultimo.estado) && agora < Date.parse(ultimo.expiraEm));
  const tipo: 'ligar' | 'reiniciar' = maquina?.appAberto ? 'reiniciar' : 'ligar';
  const motivo = !maquina ? 'Nenhuma máquina conectada.'
    : !conectada ? 'A máquina está desconectada: o serviço do dev.kit precisa estar de pé para ligar ou reiniciar.'
      : ocupado ? 'Aguardando o serviço da máquina terminar o pedido anterior.'
        : tipo === 'ligar' ? 'O serviço da máquina abre o dev.kit sem janela.' : 'O serviço da máquina fecha o dev.kit e o liga de novo, sem janela.';

  useEffect(() => { setConfirmando(false); setFalha(''); }, [maquina?.maquinaId]);

  async function mandar(confirmarReinicio: boolean) {
    setEnviando(true); setFalha('');
    try {
      const desfecho = await acionar(tipo, confirmarReinicio);
      // O heartbeat que o site leu pode ser de antes do turno: a API pede a confirmação, e o site a mostra.
      if (!desfecho.ok && desfecho.turnoEmAndamento) { setConfirmando(true); return; }
      setConfirmando(false);
      if (!desfecho.ok) setFalha(desfecho.mensagem);
    } finally { setEnviando(false); }
  }

  return {
    maquina, tipo, motivo, confirmando, enviando, falha,
    desabilitado: !maquina || !conectada || ocupado,
    desfecho: ultimo && agora - Date.parse(ultimo.criadoEm) < vistaDoDesfechoMs ? { comando: ultimo, ...situacao(ultimo, conversa) } : null,
    clicar: () => { if (tipo === 'reiniciar' && maquina?.turnoEmAndamento) setConfirmando(true); else void mandar(false); },
    confirmar: () => void mandar(true),
    cancelar: () => setConfirmando(false),
  };
}

type Controle = ReturnType<typeof useControleDoDevKit>;

/** O botão do cabeçalho e, com turno em andamento, a confirmação do reinício. */
export function BotaoDoDevKit({ controle }: { controle: Controle }) {
  const cancelar = useRef<HTMLButtonElement>(null);
  useEffect(() => { if (controle.confirmando) cancelar.current?.focus(); }, [controle.confirmando]);
  return <>
    {/* A dica leva o que era o rodapé do Board (proposta chat-remoto v016): quem executa é o serviço da máquina. */}
    <button className={`br-btn br-acao${controle.tipo === 'ligar' ? ' br-ligar' : ''}`} disabled={controle.desabilitado}
      title={`${controle.motivo} Ligar e Reiniciar são executados pelo serviço do dev.kit na máquina.`} onClick={controle.clicar}>
      <span aria-hidden="true">↻</span>{controle.tipo === 'ligar' ? 'Ligar dev.kit' : 'Reiniciar dev.kit'}
    </button>
    {controle.confirmando && <div className="br-dialogo-fundo" onKeyDown={e => { if (e.key === 'Escape') controle.cancelar(); }}>
      <div className="br-dialogo" role="dialog" aria-modal="true" aria-labelledby="br-dialogo-titulo">
        <h2 id="br-dialogo-titulo">Reiniciar o dev.kit com um turno em andamento?</h2>
        <p>Há um turno do agente em andamento em <b>{controle.maquina?.nome || 'Máquina'}</b>. Reiniciar interrompe esse turno.</p>
        <p>O serviço fecha o dev.kit e o liga de novo, sem janela. As sessões voltam pelo restaurador, e o turno interrompido fica aguardando você.</p>
        <div className="br-row">
          <button ref={cancelar} className="br-btn" disabled={controle.enviando} onClick={controle.cancelar}>Cancelar</button>
          <button className="br-btn br-perigo" disabled={controle.enviando} onClick={controle.confirmar}>Reiniciar assim mesmo</button>
        </div>
      </div>
    </div>}
  </>;
}

/** A faixa abaixo do cabeçalho: onde o último Ligar/Reiniciar está e o desfecho que o serviço mandou. */
export function FaixaDoAcionamento({ controle }: { controle: Controle }) {
  const { falha, desfecho, tipo } = controle;
  if (!falha && !desfecho) return null;
  return <div className={`br-acionamento${falha ? ' br-recusa' : desfecho!.classe}`} role="status">
    {falha
      ? <><strong>{rotuloDoComando(tipo)} · não enviado</strong><span>{falha}</span></>
      : <><strong>{desfecho!.comando.estado === 'concluido' ? '✓ ' : ''}{rotuloDoComando(desfecho!.comando.tipo)} · {hora(desfecho!.comando.criadoEm)}{desfecho!.titulo ? ` · ${desfecho!.titulo}` : ''}</strong><span>{desfecho!.texto}</span></>}
  </div>;
}
