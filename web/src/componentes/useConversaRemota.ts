import { useCallback, useEffect, useRef, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import { lerEventosDaConversa } from '../api/eventosDaConversa';
import {
  dispositivoAtual, type ComandoNovo, type ComandoRemoto, type ConversaRemota, type NovoDesignRemoto, type NovoProjetoRemoto, type TipoDeCriacao,
} from '../api/remoto';
import type { AcaoNoProjetoRemoto } from '../api/projetos';
import { useSessao } from '../sessao';

const vazia: ConversaRemota = { comandos: [], respostas: [], ultimoEvento: 0, falas: [] };
function juntar<T extends { id: string }>(lista: T[], item: T) {
  return [...lista.filter(x => x.id !== item.id), item];
}

/**
 * A conversa remota com uma máquina: a do Orquestrador ou (semana 7) a do agente de UMA task, quando `sessaoDaTask` (o id da
 * execução no retrato) vem. O SSE é o mesmo da máquina: cada conversa fica só com os eventos dela — a do Orquestrador ignora os
 * das tasks, e a da task, os dos outros.
 */
export function useConversaRemota(maquina: string, sessaoDaTask?: string) {
  const { sessao, sair } = useSessao();
  const [conversa, setConversa] = useState(vazia);
  const [erro, setErro] = useState('');
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
  // A conversa carregada é desta máquina E desta task: trocar de task não mostra a anterior.
  const chave = `${maquina}|${sessaoDaTask ?? ''}`;

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;
    ciclo.current = controller;
    let timer: number;
    let cursor = 0;
    let inicializada = false;
    // (semana 7) Os comandos DESTA conversa e os das outras: a resposta vem só com o comandoId, e é por ele que se separa.
    const destaConversa = new Set<string>();
    const deOutra = new Set<string>();
    const daConversa = (s?: string | null) => (s ?? '') === (sessaoDaTask ?? '');
    setConversa(vazia); setCarregando(true); setErro(''); setFalhaNoEnvio(''); setCarregadaPara(chave);
    revisoes.current.clear(); ocupado.current = false; setEnviando(false);
    if (!sessao || !maquina) { setCarregando(false); return () => controller.abort(); }
    async function atualizar() {
      try {
        if (!inicializada) {
          const historico = sessaoDaTask
            ? await api.conversaDaTask(sessao!.token, maquina, sessaoDaTask, signal)
            : await api.conversaRemota(sessao!.token, maquina, signal);
          if (signal.aborted) return;
          for (const c of historico.comandos) destaConversa.add(c.id);
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
          const comando = evento.comando && daConversa(evento.comando.sessao) ? evento.comando : null;
          if (evento.comando) (comando ? destaConversa : deOutra).add(evento.comando.id);
          const fala = evento.fala && daConversa(evento.fala.sessao) ? evento.fala : null;
          const doComando = evento.resposta?.comandoId;
          // Na task, só a resposta a uma mensagem dela; no Orquestrador, tudo menos a resposta à mensagem de uma task.
          const respostaDaConversa = evento.resposta && (sessaoDaTask ? !!doComando && destaConversa.has(doComando) : !(doComando && deOutra.has(doComando)))
            ? evento.resposta : null;
          if (!comando && !fala && !respostaDaConversa) { setConversa(atual => ({ ...atual, ultimoEvento: cursor })); return; }
          if (comando) revisoes.current.set(comando.id, evento.id);
          setConversa(atual => ({
            comandos: comando ? juntar(atual.comandos, comando) : atual.comandos,
            respostas: respostaDaConversa ? juntar(atual.respostas, respostaDaConversa) : atual.respostas,
            // A fala do chat do dev.kit (o que o dev digitou no desktop e a resposta do Orquestrador a ela).
            falas: fala ? juntar(atual.falas ?? [], fala) : atual.falas,
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
  }, [maquina, sessaoDaTask, chave, sessao, sair, tentativa]);

  /**
   * A mensagem ao Orquestrador. `fluxo` (correções do board remoto) leva o fluxo escolhido no `/`, o agente e os parâmetros em
   * campos próprios; o texto continua com eles escritos, para o dev.kit antigo.
   */
  const enviar = useCallback(async (texto: string, reenvio?: ComandoRemoto, fluxo?: FluxoDaMensagem) => {
    const controller = ciclo.current;
    if (!sessao || !maquina || ocupado.current || !controller || controller.signal.aborted) return false;
    // (semana 7) Na conversa da task, a mensagem leva o destino e é só texto: o fluxo abre execução, e a task já existe.
    const destino = sessaoDaTask ? { sessao: sessaoDaTask } : {};
    const pedido: ComandoNovo = reenvio ? {
      id: reenvio.id, maquinaId: maquina, tipo: 'mensagem', texto: reenvio.texto,
      dispositivo: reenvio.dispositivo === 'celular' ? 'celular' : 'navegador', ...destino,
    } : pendente.current?.texto === texto && pendente.current.sessao === sessaoDaTask ? pendente.current : {
      id: crypto.randomUUID(), maquinaId: maquina, tipo: 'mensagem', texto,
      dispositivo: dispositivoAtual(), ...destino,
      ...(fluxo && !sessaoDaTask ? { fluxo: fluxo.fluxo, ...(fluxo.agente ? { agente: fluxo.agente } : {}), ...(Object.keys(fluxo.parametros).length ? { parametros: fluxo.parametros } : {}) } : {}),
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
  }, [sessao, maquina, sessaoDaTask, sair]);

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
      dispositivo: dispositivoAtual(),
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
      dispositivo: dispositivoAtual(),
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

  /**
   * (remote-control-v3, item 4) A CRIAÇÃO de projeto ou de projeto de design pela mesma fila: o formulário vai em JSON no
   * texto, e o dev.kit responde `concluido` (o que criou) ou `recusado` (o motivo) — o desfecho chega pelo SSE. O mesmo id no
   * reenvio (o duplo clique) devolve o comando que já existe.
   */
  const criar = useCallback(async (tipo: TipoDeCriacao, formulario: NovoProjetoRemoto | NovoDesignRemoto | AcaoNoProjetoRemoto, id: string)
    : Promise<{ ok: true; comando: ComandoRemoto } | { ok: false; mensagem: string }> => {
    const controller = ciclo.current;
    if (!sessao || !maquina || !controller || controller.signal.aborted) return { ok: false, mensagem: 'Nenhuma máquina selecionada.' };
    const pedido: ComandoNovo = {
      id, maquinaId: maquina, tipo, texto: JSON.stringify(formulario),
      dispositivo: dispositivoAtual(),
    };
    const revisao = revisoes.current.get(id);
    try {
      const comando = await api.enviarComandoRemoto(sessao.token, pedido, controller.signal);
      // O SSE pode trazer o desfecho do dev.kit antes de o POST responder com o "na-fila": o mais novo fica.
      if (!controller.signal.aborted && revisao === revisoes.current.get(id))
        setConversa(atual => ({ ...atual, comandos: juntar(atual.comandos, comando) }));
      return { ok: true, comando };
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.status === 401) { sair('expirou'); return { ok: false, mensagem: '' }; }
      return { ok: false, mensagem: mensagemDaFalha(falha) };
    }
  }, [sessao, maquina, sair]);

  const atual = carregadaPara === chave;
  return {
    conversa: atual ? conversa : vazia, erro: atual ? erro : '', falhaNoEnvio: atual ? falhaNoEnvio : '',
    carregando: carregando || !atual, enviando, enviar, acionar, pedirCatalogo, criar, recarregar: () => setTentativa(x => x + 1),
  };
}

/** O fluxo do catálogo escolhido para a mensagem: o id, o agente `cli:modelo` (vazio, o padrão) e os parâmetros preenchidos. */
export interface FluxoDaMensagem { fluxo: number; agente: string; parametros: Record<string, string> }

/** O envio do Ligar/Reiniciar: aceito pela API (o desfecho vem do serviço depois), ou recusado com o motivo. */
export type DesfechoDoAcionamento = { ok: true } | { ok: false; turnoEmAndamento: boolean; mensagem: string };

/** A conversa remota de uma máquina, como o Board a reparte entre o chat e o controle do dev.kit. */
export type ConversaDaMaquina = ReturnType<typeof useConversaRemota>;
