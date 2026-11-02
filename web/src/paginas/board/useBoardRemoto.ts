import { useEffect, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../../api/cliente';
import type { MaquinaRemota, PainelRemoto } from '../../api/remoto';
import { useSessao } from '../../sessao';

/** Consulta só o dono autenticado; não persiste retratos e descarta respostas da seleção anterior. */
export function useBoardRemoto(maquinaId: string, tentativa: number) {
  const { sessao, sair } = useSessao();
  const [maquinas, setMaquinas] = useState<MaquinaRemota[]>([]);
  const [painel, setPainel] = useState<PainelRemoto | null>(null);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState('');
  const [atual, setAtual] = useState(false);

  useEffect(() => {
    let cancelado = false;
    let relogio: number;
    const token = sessao!.token;
    setPainel(null);
    setCarregando(true);
    setAtual(false);
    setErro('');
    async function consultar() {
      try {
        const lista = await api.maquinasRemotas(token);
        if (cancelado) return;
        setMaquinas(lista);
        const id = lista.find(m => m.maquinaId === maquinaId)?.maquinaId ?? lista[0]?.maquinaId;
        const recebido = id ? await api.painelRemoto(token, id) : null;
        if (cancelado) return;
        setPainel(recebido);
        setErro('');
        setAtual(true);
      } catch (falha) {
        if (cancelado) return;
        setAtual(false);
        if (falha instanceof ErroDaApi && falha.status === 401) sair('expirou');
        else {
          // Uma máquina removida não pode continuar aparecendo como conectada.
          if (falha instanceof ErroDaApi && falha.status === 404) setPainel(null);
          setErro(`Não foi possível atualizar o Board. ${mensagemDaFalha(falha)}`);
        }
      } finally {
        if (!cancelado) {
          setCarregando(false);
          relogio = window.setTimeout(() => void consultar(), 15_000);
        }
      }
    }
    void consultar();
    return () => { cancelado = true; window.clearTimeout(relogio); };
  }, [sessao, sair, maquinaId, tentativa]);

  // Não exibe a resposta da máquina anterior durante a troca.
  const visivel = painel && (!maquinaId || painel.maquina.maquinaId === maquinaId || !maquinas.some(m => m.maquinaId === maquinaId)) ? painel : null;
  return { maquinas, painel: visivel, carregando, erro, atual };
}
