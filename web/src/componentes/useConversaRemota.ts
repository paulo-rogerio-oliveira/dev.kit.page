import { useCallback, useEffect, useRef, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import { lerEventosDaConversa } from '../api/eventosDaConversa';
import type { ComandoNovo, ComandoRemoto, ConversaRemota } from '../api/remoto';
import { useSessao } from '../sessao';

const vazia: ConversaRemota = { comandos: [], respostas: [], ultimoEvento: 0, falas: [] };
function juntar<T extends { id: string }>(lista: T[], item: T) {
  return [...lista.filter(x => x.id !== item.id), item];
}

export function useConversaRemota(maquina: string, conversaId = '') {
  const { sessao, sair } = useSessao();
  const [conversa, setConversa] = useState(vazia);
  const [erro, setErro] = useState('');
  const [erroDaAbertura, setErroDaAbertura] = useState('');
  const [falhaNoEnvio, setFalhaNoEnvio] = useState('');
  const [carregando, setCarregando] = useState(true);
  const [enviando, setEnviando] = useState(false);
  const [tentativa, setTentativa] = useState(0);
  // De qual máquina é o estado acima: no render da troca (antes do efeito), o da anterior não pode aparecer como desta.
  const [carregadaPara, setCarregadaPara] = useState<string | null>(null);
  const ciclo = useRef<AbortController | null>(null);
  const ocupado = useRef(false);
  const revisoes = useRef(new Map<string, number>());
  const pendente = useRef<ComandoNovo | null>(null);
  const chave = `${maquina}:${conversaId}`;

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;
    ciclo.current = controller;
    let timer: number;
    let cursor = 0;
    let inicializada = false;
    setConversa(vazia); setCarregando(true); setErro(''); setFalhaNoEnvio(''); setCarregadaPara(chave);
    setErroDaAbertura('');
    if (pendente.current && (pendente.current.maquinaId !== maquina || (pendente.current.conversaId ?? '') !== conversaId))
      pendente.current = null;
    revisoes.current.clear(); ocupado.current = false; setEnviando(false);
    if (!sessao || !maquina) { setCarregando(false); return () => controller.abort(); }
    async function atualizar() {
      try {
        if (!inicializada) {
          const historico = await api.conversaRemota(sessao!.token, maquina, signal, conversaId);
          if (signal.aborted) return;
          setConversa({ ...historico, falas: historico.falas ?? [] }); cursor = historico.ultimoEvento; inicializada = true;
          setCarregando(false);
        }
        const resposta = await api.eventosDaConversa(sessao!.token, maquina, cursor, signal);
        if (signal.aborted) return;
        setErro('');
        await lerEventosDaConversa(resposta, evento => {
          if (signal.aborted || evento.id <= cursor) return;
          cursor = evento.id;
          if (evento.maquinaId !== maquina) return;
          const canal = evento.comando?.conversaId ?? evento.resposta?.conversaId ?? evento.fala?.conversaId ?? '';
          if (canal !== conversaId) return;
          if (evento.comando) revisoes.current.set(evento.comando.id, evento.id);
          setConversa(atual => ({
            comandos: evento.comando ? juntar(atual.comandos, evento.comando) : atual.comandos,
            respostas: evento.resposta ? juntar(atual.respostas, evento.resposta) : atual.respostas,
            // A fala do chat do dev.kit (o que o dev digitou no desktop e a resposta do Orquestrador a ela).
            falas: evento.fala ? juntar(atual.falas ?? [], evento.fala) : atual.falas,
            ultimoEvento: cursor,
          }));
        });
      } catch (falha) {
        if (signal.aborted) return;
        if (falha instanceof ErroDaApi && falha.status === 401) { sair('expirou'); return; }
        setErro(`Não foi possível atualizar a conversa. ${mensagemDaFalha(falha)}`);
        setCarregando(false);
      }
      if (!signal.aborted) timer = window.setTimeout(atualizar, 3000);
    }
    void atualizar();
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [maquina, conversaId, chave, sessao, sair, tentativa]);

  const enviar = useCallback(async (texto: string, reenvio?: ComandoRemoto) => {
    const controller = ciclo.current;
    if (!sessao || !maquina || ocupado.current || !controller || controller.signal.aborted) return false;
    const tipo = conversaId ? 'mensagem-task' : 'mensagem';
    const pedido: ComandoNovo = reenvio ? {
      id: reenvio.id, maquinaId: maquina, tipo, texto: reenvio.texto, ...(conversaId ? { conversaId } : {}),
      dispositivo: reenvio.dispositivo === 'celular' ? 'celular' : 'navegador',
    } : pendente.current?.texto === texto && pendente.current.maquinaId === maquina && (pendente.current.conversaId ?? '') === conversaId ? pendente.current : {
      id: crypto.randomUUID(), maquinaId: maquina, tipo, texto, ...(conversaId ? { conversaId } : {}),
      dispositivo: window.matchMedia('(max-width:700px)').matches ? 'celular' : 'navegador',
    };
    if (!reenvio) pendente.current = pedido;
    ocupado.current = true; setEnviando(true); setFalhaNoEnvio('');
    const revisao = revisoes.current.get(pedido.id);
    try {
      const comando = await api.enviarComandoRemoto(sessao.token, pedido, controller.signal);
      if (controller.signal.aborted) return false;
      // Um SSE pode confirmar entrega antes de o POST responder com o estado anterior.
      if (revisao === revisoes.current.get(pedido.id))
        setConversa(atual => ({ ...atual, comandos: juntar(atual.comandos, comando) }));
      if (pendente.current?.id === pedido.id) pendente.current = null;
      return true;
    } catch (falha) {
      if (controller.signal.aborted) return false;
      if (falha instanceof ErroDaApi && falha.status === 401) { sair('expirou'); return false; }
      const gravado = falha instanceof ErroDaApi ? (falha.corpo as { comando?: ComandoRemoto } | null)?.comando : undefined;
      if (gravado && revisao === revisoes.current.get(pedido.id))
        setConversa(atual => ({ ...atual, comandos: juntar(atual.comandos, gravado) }));
      setFalhaNoEnvio(`Envio não confirmado. ${mensagemDaFalha(falha)} Tente novamente; o mesmo ID evita duplicação.`);
      return false;
    } finally {
      if (!controller.signal.aborted) { ocupado.current = false; setEnviando(false); }
    }
  }, [sessao, maquina, conversaId, sair]);

  /** Abre/renova por dez minutos o espelho da task; não abre turno do agente. */
  const pedirConversa = useCallback(async () => {
    const controller = ciclo.current;
    if (!sessao || !maquina || !conversaId || !controller || controller.signal.aborted) return;
    try {
      const comando = await api.enviarComandoRemoto(sessao.token, {
        id: crypto.randomUUID(), maquinaId: maquina, tipo: 'conversa-task', conversaId, texto: '',
        dispositivo: window.matchMedia('(max-width:700px)').matches ? 'celular' : 'navegador',
      }, controller.signal);
      if (!controller.signal.aborted) setErroDaAbertura('');
      if (!controller.signal.aborted && !revisoes.current.has(comando.id))
        setConversa(atual => ({ ...atual, comandos: juntar(atual.comandos, comando) }));
    } catch (falha) {
      if (controller.signal.aborted) return;
      if (falha instanceof ErroDaApi && falha.status === 401) { sair('expirou'); return; }
      setErroDaAbertura(`Não foi possível abrir ou atualizar a conversa da task. ${mensagemDaFalha(falha)}`);
    }
  }, [sessao, maquina, conversaId, sair]);

  /**
   * (item 8) LIGAR ou REINICIAR o dev.kit: o comando vai ao serviço da máquina pela mesma fila, e o desfecho chega pela
   * conversa (o SSE). O 409 `turno-em-andamento` volta como tal: o heartbeat que o site leu pode ser de antes do turno, e
   * quem chama mostra a confirmação.
   */
  const acionar = useCallback(async (tipo: 'ligar' | 'reiniciar', confirmarReinicio = false): Promise<DesfechoDoAcionamento> => {
    const controller = ciclo.current;
    if (!sessao || !maquina || !controller || controller.signal.aborted) return { ok: false, turnoEmAndamento: false, mensagem: 'Nenhuma máquina selecionada.' };
    const pedido: ComandoNovo = {
      id: crypto.randomUUID(), maquinaId: maquina, tipo, texto: '',
      dispositivo: window.matchMedia('(max-width:700px)').matches ? 'celular' : 'navegador',
      ...(tipo === 'reiniciar' && confirmarReinicio ? { confirmarReinicio: true } : {}),
    };
    try {
      const comando = await api.enviarComandoRemoto(sessao.token, pedido, controller.signal);
      if (!controller.signal.aborted) setConversa(atual => ({ ...atual, comandos: juntar(atual.comandos, comando) }));
      return { ok: true };
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.status === 401) { sair('expirou'); return { ok: false, turnoEmAndamento: false, mensagem: '' }; }
      const corpo = falha instanceof ErroDaApi ? falha.corpo as { codigo?: string; comando?: ComandoRemoto } | null : null;
      if (corpo?.comando && !controller.signal.aborted) setConversa(atual => ({ ...atual, comandos: juntar(atual.comandos, corpo.comando!) }));
      return { ok: false, turnoEmAndamento: corpo?.codigo === 'turno-em-andamento', mensagem: mensagemDaFalha(falha) };
    }
  }, [sessao, maquina, sair]);

  /**
   * (remote-control-v3) O HANDSHAKE dos atalhos do chat: pede à máquina o catálogo pela mesma fila. O dev.kit responde publicando
   * o catálogo e fechando o pedido (`concluido`), que chega pelo SSE; a API devolve o pedido que ainda espera na fila em vez de
   * empilhar outro.
   */
  const pedirCatalogo = useCallback(async (): Promise<{ ok: true } | { ok: false; mensagem: string }> => {
    const controller = ciclo.current;
    if (!sessao || !maquina || !controller || controller.signal.aborted) return { ok: false, mensagem: 'Nenhuma máquina selecionada.' };
    const pedido: ComandoNovo = {
      id: crypto.randomUUID(), maquinaId: maquina, tipo: 'catalogo', texto: '',
      dispositivo: window.matchMedia('(max-width:700px)').matches ? 'celular' : 'navegador',
    };
    try {
      const comando = await api.enviarComandoRemoto(sessao.token, pedido, controller.signal);
      if (!controller.signal.aborted) setConversa(atual => ({ ...atual, comandos: juntar(atual.comandos, comando) }));
      return { ok: true };
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.status === 401) { sair('expirou'); return { ok: false, mensagem: '' }; }
      return { ok: false, mensagem: mensagemDaFalha(falha) };
    }
  }, [sessao, maquina, sair]);

  const atual = carregadaPara === chave;
  return {
    conversa: atual ? conversa : vazia, erro: atual ? erro : '', erroDaAbertura: atual ? erroDaAbertura : '', falhaNoEnvio: atual ? falhaNoEnvio : '',
    carregando: carregando || !atual, enviando, enviar, acionar, pedirCatalogo, pedirConversa, recarregar: () => setTentativa(x => x + 1),
  };
}

/** O envio do Ligar/Reiniciar: aceito pela API (o desfecho vem do serviço depois), ou recusado com o motivo. */
export type DesfechoDoAcionamento = { ok: true } | { ok: false; turnoEmAndamento: boolean; mensagem: string };

/** A conversa remota de uma máquina, como o Board a reparte entre o chat e o controle do dev.kit. */
export type ConversaDaMaquina = ReturnType<typeof useConversaRemota>;
