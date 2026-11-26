import { useEffect, useRef, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { PedidoDoPacote, Projeto, Artefato, PrevisaoRemotaDoPacote } from '../api/projetos';
import type { EstadoDoArtefato } from '../api/remoto';
import { useSessao } from '../sessao';
import { ArtefatosDoCard, rotuloDoProdutor } from './ArtefatosDoProjeto';
import type { CriacaoDoBoard } from './CriacaoRemota';

/** Só o painel aberto lê o detalhe, os pedidos e seus links. Chat e Board continuam montados. */
export function PacoteDoBoard({ nome, slug, branch, repositorios, estados, previsaoRemota, skillNoRetrato, capacidade, desabilitado, criacao, agora }: {
  nome: string; slug: string; branch: string; repositorios: string[]; estados: EstadoDoArtefato[];
  capacidade: boolean; desabilitado: boolean; criacao: CriacaoDoBoard; agora: number;
  previsaoRemota?: PrevisaoRemotaDoPacote[] | null;
  skillNoRetrato?: string | null;
}) {
  const { sessao } = useSessao();
  const [aberto, setAberto] = useState(false);
  const [projeto, setProjeto] = useState<Projeto | null>(null);
  const [artefatos, setArtefatos] = useState<Artefato[]>([]);
  const [pedidos, setPedidos] = useState<PedidoDoPacote[]>([]);
  const [erro, setErro] = useState('');
  const [semAcesso, setSemAcesso] = useState(false);
  const [edicaoConflitante, setEdicaoConflitante] = useState(false);
  const [revisao, setRevisao] = useState(0);
  const [atualizando, setAtualizando] = useState(false);
  const previsaoPedida = useRef('');
  const [consultaDaPrevisao, setConsultaDaPrevisao] = useState<{ assinatura: string; comandoId: string | null }>();
  const token = sessao?.token;
  const ids = [...new Set([...estados.map(e => e.pedidoId).filter((id): id is string => !!id),
    ...criacao.pedidosDoPacote.filter(p => p.projeto === slug).map(p => p.id)])].sort().join(',');
  const id = projeto?.id;
  const desfecho = `${criacao.desfecho?.comando.id ?? ''}/${criacao.desfecho?.comando.estado ?? ''}`;
  useEffect(() => {
    if (!aberto || !token) return;
    const controlador = new AbortController();
    void api.resumosDosProjetos(token, controlador.signal).then(async resumos => {
      const resumo = resumos.find(p => p.slug === slug);
      if (!resumo) throw new Error('Sincronize este projeto no host para editar a skill do pacote.');
      const p = await api.projeto(token, resumo.id, controlador.signal);
      if (!controlador.signal.aborted) { setProjeto(p); setSemAcesso(false); setEdicaoConflitante(false); setErro(''); }
    }).catch(e => { if (!controlador.signal.aborted) {
      setErro(mensagemDaFalha(e));
      if (e instanceof ErroDaApi && [401, 403].includes(e.status)) setSemAcesso(true);
    } });
    return () => controlador.abort();
  }, [aberto, token, slug, revisao]);

  useEffect(() => {
    if (!aberto || !token || id === undefined) return;
    const controlador = new AbortController();
    let timer: number;
    async function carregar() {
      setAtualizando(true);
      try {
        const [links, grupos] = await Promise.all([
          api.artefatosDoProjeto(token!, id!, controlador.signal),
          api.pedidosDoPacote(token!, id!, controlador.signal),
        ]);
        if (!controlador.signal.aborted) { setArtefatos(links); setPedidos(grupos); setErro(''); setSemAcesso(false); }
      } catch (e) {
        if (!controlador.signal.aborted) {
          setErro(mensagemDaFalha(e));
          if (e instanceof ErroDaApi && [401, 403].includes(e.status)) setSemAcesso(true);
        }
      } finally {
        if (!controlador.signal.aborted) { setAtualizando(false); timer = window.setTimeout(carregar, 15_000); }
      }
    }
    void carregar();
    return () => { controlador.abort(); window.clearTimeout(timer); };
  }, [aberto, token, id, ids, revisao, desfecho]);

  const podeEditar = !projeto || projeto.papel === 'editor' || projeto.papel === 'dono';
  const bloqueado = desabilitado || criacao.desabilitado || semAcesso || edicaoConflitante || !podeEditar || !capacidade;
  const motivo = !podeEditar || semAcesso ? 'Gerar pacote e editar a skill exige editor ou dono do projeto.' : criacao.motivo;
  const assinatura = JSON.stringify([slug, projeto?.versao, projeto?.branch, projeto?.skillDoPacote ?? '', projeto?.repositorios, revisao]);
  const previsao = consultaDaPrevisao?.assinatura === assinatura && consultaDaPrevisao.comandoId
    ? criacao.origensDoPacote.find(p => p.projeto === slug && p.comandoId === consultaDaPrevisao.comandoId) : undefined;
  const previsaoRemotaAtual = previsaoRemota != null && !!projeto && projeto.branch === branch
    && (projeto.skillDoPacote ?? '') === (skillNoRetrato ?? '')
    && JSON.stringify([...projeto.repositorios].sort()) === JSON.stringify([...repositorios].sort());
  const produtores = previsaoRemotaAtual
    ? previsaoRemota.map(p => ({ ...p, produtor: rotuloDoProdutor(p.produtor) })) : previsao?.resultados;
  // A previsão usa a consulta do projeto inteiro já oferecida pelo host; não enfileira geração.
  useEffect(() => {
    if (!aberto || bloqueado || !projeto || previsaoRemotaAtual || previsaoPedida.current === assinatura) return;
    previsaoPedida.current = assinatura;
    setConsultaDaPrevisao({ assinatura, comandoId: null });
    void criacao.conferirPacote(slug).then(comandoId => {
      if (previsaoPedida.current === assinatura) setConsultaDaPrevisao({ assinatura, comandoId });
    });
  }, [aberto, bloqueado, projeto, previsaoRemotaAtual, assinatura, criacao, slug]);

  async function salvarSkill(nomeDaSkill: string) {
    if (!projeto || !token || bloqueado) throw new Error(motivo);
    try { setProjeto(await api.editarSkillDoPacote(token, projeto.id, projeto.versao, nomeDaSkill)); }
    catch (e) {
      if (e instanceof ErroDaApi && [401, 403].includes(e.status)) setSemAcesso(true);
      if (e instanceof ErroDaApi && e.status === 409) setEdicaoConflitante(true);
      throw e;
    }
  }
  async function gerar(projeto: string, opcoes = {}) { setAberto(true); return criacao.gerarArtefato(projeto, opcoes); }
  return <div className="pacote-do-board">
    <button className="br-ver" aria-expanded={aberto} onClick={() => setAberto(a => !a)}>{aberto ? 'Recolher pacote' : 'Ver pacote do projeto'}</button>
    {aberto ? <ArtefatosDoCard nome={nome} slug={slug} branch={branch} repositorios={repositorios} estados={estados}
      artefatos={artefatos} pedidos={pedidos} produtores={produtores} gerar={gerar} desabilitado={bloqueado}
      capacidade={capacidade} motivo={motivo} agora={agora} erro={erro} erroDoPedido={criacao.falha}
      atualizar={() => setRevisao(r => r + 1)} atualizando={atualizando} linksAutorizados={!semAcesso}
      skillDoPacote={projeto?.skillDoPacote ?? ''} skillsDoPacote={projeto?.skillsDoPacote ?? []} salvarSkill={projeto ? salvarSkill : undefined} />
      : <button className="br-btn br-gerar" disabled={bloqueado || estados.some(e => ['enfileirando', 'enfileirado', 'gerando', 'agente-pendente'].includes(e.estado))}
        title={motivo} onClick={() => void gerar(slug)}>Gerar pacote</button>}
  </div>;
}
