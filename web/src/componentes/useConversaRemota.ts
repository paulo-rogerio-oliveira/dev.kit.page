import { useCallback, useEffect, useRef, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import { lerEventosDaConversa } from '../api/eventosDaConversa';
import type { ComandoNovo, ComandoRemoto, ConversaRemota } from '../api/remoto';
import { useSessao } from '../sessao';

const vazia: ConversaRemota = { comandos: [], respostas: [], ultimoEvento: 0 };
function juntar<T extends { id: string }>(lista: T[], item: T) {
  return [...lista.filter(x => x.id !== item.id), item];
}

export function useConversaRemota(maquina: string) {
  const { sessao, sair } = useSessao();
  const [conversa, setConversa] = useState(vazia);
  const [erro, setErro] = useState('');
  const [falhaNoEnvio, setFalhaNoEnvio] = useState('');
  const [carregando, setCarregando] = useState(true);
  const [enviando, setEnviando] = useState(false);
  const [tentativa, setTentativa] = useState(0);
  const ciclo = useRef<AbortController | null>(null);
  const ocupado = useRef(false);
  const revisoes = useRef(new Map<string, number>());
  const pendente = useRef<ComandoNovo | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;
    ciclo.current = controller;
    let timer: number;
    let cursor = 0;
    let inicializada = false;
    setConversa(vazia); setCarregando(true); setErro(''); setFalhaNoEnvio('');
    revisoes.current.clear(); ocupado.current = false; setEnviando(false);
    if (!sessao || !maquina) { setCarregando(false); return () => controller.abort(); }
    async function atualizar() {
      try {
        if (!inicializada) {
          const historico = await api.conversaRemota(sessao!.token, maquina, signal);
          if (signal.aborted) return;
          setConversa(historico); cursor = historico.ultimoEvento; inicializada = true;
          setCarregando(false);
        }
        const resposta = await api.eventosDaConversa(sessao!.token, maquina, cursor, signal);
        if (signal.aborted) return;
        setErro('');
        await lerEventosDaConversa(resposta, evento => {
          if (signal.aborted || evento.id <= cursor) return;
          cursor = evento.id;
          if (evento.maquinaId !== maquina) return;
          if (evento.comando) revisoes.current.set(evento.comando.id, evento.id);
          setConversa(atual => ({
            comandos: evento.comando ? juntar(atual.comandos, evento.comando) : atual.comandos,
            respostas: evento.resposta ? juntar(atual.respostas, evento.resposta) : atual.respostas,
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
  }, [maquina, sessao, sair, tentativa]);

  const enviar = useCallback(async (texto: string, reenvio?: ComandoRemoto) => {
    const controller = ciclo.current;
    if (!sessao || !maquina || ocupado.current || !controller || controller.signal.aborted) return false;
    const pedido: ComandoNovo = reenvio ? {
      id: reenvio.id, maquinaId: maquina, tipo: 'mensagem', texto: reenvio.texto,
      dispositivo: reenvio.dispositivo === 'celular' ? 'celular' : 'navegador',
    } : pendente.current?.texto === texto ? pendente.current : {
      id: crypto.randomUUID(), maquinaId: maquina, tipo: 'mensagem', texto,
      dispositivo: window.matchMedia('(max-width:700px)').matches ? 'celular' : 'navegador',
    };
    if (!reenvio) pendente.current = pedido;
    ocupado.current = true; setEnviando(true); setFalhaNoEnvio('');
    const revisao = revisoes.current.get(pedido.id);
    try {
      const comando = await api.enviarMensagemRemota(sessao.token, pedido, controller.signal);
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
  }, [sessao, maquina, sair]);

  return { conversa, erro, falhaNoEnvio, carregando, enviando, enviar, recarregar: () => setTentativa(x => x + 1) };
}
