import { useCallback, useEffect, useRef, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { CatalogoDaMaquina, ComandoRemoto, MaquinaRemota } from '../api/remoto';
import type { ConversaDaMaquina } from './useConversaRemota';
import { useSessao } from '../sessao';

/** Mais velho que isto, o catálogo é pedido de novo ao conectar; o "Atualizar" pede a qualquer hora. */
export const catalogoVelhoMs = 10 * 60_000;

/** Onde está o pedido de atualização mais recente (o handshake), quando ele é mais novo que o catálogo exibido. */
export type PedidoDoCatalogo =
  | { estado: 'nenhum' }
  | { estado: 'enviando' }
  | { estado: 'aguardando'; desde: string }
  | { estado: 'recusado' | 'vencido' | 'falhou'; motivo: string };

const emCurso = (c: ComandoRemoto) => c.estado === 'pendente' || c.estado === 'na-fila' || c.estado === 'entregue';

/**
 * O CATÁLOGO dos atalhos do chat (remote-control-v3): o último que a máquina publicou — lido da API, então disponível também
 * com o dev.kit fechado, com a data — e o HANDSHAKE que o renova. Ao conectar, com o dev.kit aberto e o catálogo ausente ou
 * mais velho que {@link catalogoVelhoMs}, o pedido vai sozinho, uma vez por máquina; o "Atualizar" o repete. O desfecho chega
 * pela conversa (o pedido `concluido` relê o catálogo; o `recusado` traz o motivo — o dev.kit antigo não conhece o handshake).
 */
export function useCatalogoRemoto({ maquina, conectada, conversa }: { maquina: MaquinaRemota | undefined; conectada: boolean; conversa: ConversaDaMaquina }) {
  const { sessao, sair } = useSessao();
  const id = maquina?.maquinaId ?? '';
  const [lido, setLido] = useState<{ para: string; catalogo: CatalogoDaMaquina | null; erro: string } | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [falhaNoPedido, setFalhaNoPedido] = useState('');
  const pediuAoConectar = useRef('');
  const { pedirCatalogo } = conversa;

  const pedidos = conversa.conversa.comandos.filter(c => c.tipo === 'catalogo')
    .sort((a, b) => Date.parse(a.criadoEm) - Date.parse(b.criadoEm));
  const ultimo = pedidos.at(-1);
  // Cada pedido concluído (pelo SSE, ou no histórico) relê o catálogo que o dev.kit acabou de publicar.
  const concluidos = pedidos.filter(c => c.estado === 'concluido').map(c => c.id).join(',');

  useEffect(() => {
    const controller = new AbortController();
    if (!sessao || !id) return () => controller.abort();
    api.catalogoRemoto(sessao.token, id, controller.signal).then(catalogo => {
      if (!controller.signal.aborted) setLido({ para: id, catalogo, erro: '' });
    }).catch(falha => {
      if (controller.signal.aborted) return;
      if (falha instanceof ErroDaApi && falha.status === 401) { sair('expirou'); return; }
      const semCatalogo = falha instanceof ErroDaApi && falha.status === 404;
      setLido(atual => ({ para: id, catalogo: atual?.para === id ? atual.catalogo : null,
        erro: semCatalogo ? '' : `Não foi possível ler o catálogo. ${mensagemDaFalha(falha)}` }));
    });
    return () => controller.abort();
  }, [sessao, id, concluidos, sair]);

  const carregado = lido?.para === id;
  const catalogo = carregado ? lido.catalogo : null;
  const aguardando = !!ultimo && emCurso(ultimo);

  const atualizar = useCallback(async () => {
    setEnviando(true); setFalhaNoPedido('');
    const desfecho = await pedirCatalogo();
    setEnviando(false);
    if (!desfecho.ok && desfecho.mensagem) setFalhaNoPedido(`Pedido de atualização não enviado. ${desfecho.mensagem}`);
  }, [pedirCatalogo]);

  // O handshake ao conectar: só com o dev.kit aberto (é ele que responde), sem pedido em curso e uma vez por máquina. O
  // dev.kit que já recusou (o antigo, que não conhece o pedido) não recebe outro a cada abertura do Board: o Atualizar pede.
  const velho = !catalogo || Date.now() - Date.parse(catalogo.recebidoEm) >= catalogoVelhoMs;
  const recusou = ultimo?.estado === 'recusado';
  useEffect(() => {
    if (!id || !conectada || !maquina?.appAberto || !carregado || conversa.carregando || aguardando || recusou || !velho
        || pediuAoConectar.current === id) return;
    pediuAoConectar.current = id;
    void atualizar();
  }, [id, conectada, maquina?.appAberto, carregado, conversa.carregando, aguardando, recusou, velho, atualizar]);

  // O pedido só importa enquanto é mais novo que o catálogo exibido: o recusado de ontem não assombra o catálogo de hoje.
  const atual = ultimo && (!catalogo || Date.parse(ultimo.criadoEm) > Date.parse(catalogo.recebidoEm)) ? ultimo : undefined;
  const motivo = (c: ComandoRemoto) => {
    const texto = conversa.conversa.respostas.filter(r => r.comandoId === c.id).at(-1)?.texto || c.erro;
    return texto.replace(/^Não executado:\s*/, '').replace(/\.$/, '');
  };
  const pedido: PedidoDoCatalogo = enviando ? { estado: 'enviando' }
    : !atual ? { estado: 'nenhum' }
    : emCurso(atual) ? { estado: 'aguardando', desde: atual.criadoEm }
    : atual.estado === 'recusado' ? { estado: 'recusado', motivo: motivo(atual) }
    : atual.estado === 'expirado' ? { estado: 'vencido', motivo: 'o pedido venceu antes de o dev.kit recebê-lo' }
    : atual.estado === 'falhou' ? { estado: 'falhou', motivo: atual.erro || 'a fila não aceitou o pedido' }
    : { estado: 'nenhum' };

  return {
    catalogo, carregado, erro: carregado ? lido.erro : '', falhaNoPedido, pedido, atualizar,
    podeAtualizar: !!id && conectada && !enviando && !aguardando,
    /** Os pedidos do handshake: o chat não os mostra, nem as respostas deles. */
    idsDosPedidos: new Set(pedidos.map(c => c.id)),
  };
}

/** O catálogo de uma máquina, como o chat o reparte entre os atalhos e a linha de estado. */
export type CatalogoDoChat = ReturnType<typeof useCatalogoRemoto>;
