import { useEffect, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../../api/cliente';
import type { DocumentoMatrix, ResultadoMatrix, RevisaoMatrix } from '../../api/matrix';
import { chaveRascunho, igual, lerRascunho, reaplicar, ultimo, type RascunhoMatrix } from './modelo';

export function useEdicaoMatrix({ documento, versao, token, conta, aoSalvar }: {
  documento: DocumentoMatrix; versao: number; token: string; conta: string; aoSalvar: (r: RevisaoMatrix) => void;
}) {
  const chave = chaveRascunho(conta, documento);
  const [rascunho, setRascunho] = useState<RascunhoMatrix>(() => lerRascunho(chave) ?? {
    edicao: { versaoEsperada: versao, documento }, base: documento,
  });
  const [previa, setPrevia] = useState<ResultadoMatrix | null>(null);
  const [conflito, setConflito] = useState<RevisaoMatrix | null>(() =>
    rascunho.edicao.versaoEsperada !== versao ? { versao, documento, autor: '', em: '' } : null);
  const [erro, setErro] = useState('');
  const [status, setStatus] = useState<number | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const alterado = !igual(rascunho.base, rascunho.edicao.documento) ||
    (rascunho.edicao.versaoEsperada === 0 && !!(rascunho.edicao.documento.mapa || rascunho.edicao.documento.matriz));

  useEffect(() => {
    if (!alterado) return;
    try { sessionStorage.setItem(chave, JSON.stringify(rascunho)); }
    catch { setErro('O navegador não conseguiu preservar o rascunho. Exporte antes de sair.'); }
  }, [rascunho, alterado, chave]);
  useEffect(() => {
    if (!alterado) return;
    const avisar = (e: BeforeUnloadEvent) => { e.preventDefault(); };
    window.addEventListener('beforeunload', avisar);
    return () => window.removeEventListener('beforeunload', avisar);
  }, [alterado]);

  function mudar(d: DocumentoMatrix) {
    setRascunho(r => ({ ...r, edicao: { ...r.edicao, documento: d } })); setPrevia(null); setErro('');
  }
  async function enviar(aplicar: boolean) {
    if (ocupado || conflito || (aplicar && !previa)) return;
    setOcupado(true); setErro(''); setStatus(null);
    try {
      const r = await api.editarMatrix(token, rascunho.edicao, aplicar);
      if (aplicar && r.aplicado) {
        sessionStorage.removeItem(chave); setPrevia(null);
        setRascunho({ base: r.revisao.documento, edicao: { versaoEsperada: r.revisao.versao, documento: r.revisao.documento } });
        aoSalvar(r.revisao);
      } else setPrevia(r);
    } catch (e) {
      const codigo = e instanceof ErroDaApi ? e.status : 0;
      setStatus(codigo);
      setErro(codigo === 401 ? 'Sessão expirada. Reentre para continuar; seu rascunho está preservado nesta aba.' :
        codigo === 403 ? 'Leitura permitida; salvar exige editor ou dono. O rascunho foi preservado.' :
        codigo === 409 ? 'Existe uma revisão mais recente. Compare antes de reaplicar.' :
        `Não foi possível salvar. Rascunho preservado. ${mensagemDaFalha(e)}`);
      if (codigo === 409) {
        try {
          const h = await api.historicoMatrix(token, rascunho.edicao.documento);
          setConflito(ultimo(h) ?? null);
        } catch (falha) { setErro(`Rascunho preservado; não foi possível ler a revisão atual. ${mensagemDaFalha(falha)}`); }
      }
    } finally { setOcupado(false); }
  }
  function reaplicarRevisao() {
    if (!conflito) return;
    setRascunho(r => ({ base: conflito.documento, edicao: { versaoEsperada: conflito.versao,
      documento: reaplicar(r.base, r.edicao.documento, conflito.documento) } }));
    setConflito(null); setPrevia(null); setErro('');
  }
  return { documento: rascunho.edicao.documento, versao: rascunho.edicao.versaoEsperada,
    rascunho, mudar, previa, voltar: () => setPrevia(null), conflito, reaplicarRevisao, erro, status, ocupado, alterado,
    revisar: () => enviar(false), salvar: () => enviar(true) };
}
export type EdicaoDaMatrix = ReturnType<typeof useEdicaoMatrix>;
