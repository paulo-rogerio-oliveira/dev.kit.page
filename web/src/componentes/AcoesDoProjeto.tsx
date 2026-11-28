import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useSearchParams } from 'react-router-dom';
import { api } from '../api/cliente';
import type { ItemDoProjeto, Projeto } from '../api/projetos';
import type { ComandoRemoto, MaquinaRemota } from '../api/remoto';
import { dispositivoAtual } from '../api/remoto';
import { useSessao } from '../sessao';
import { acaoDoProjeto, espelhoNoResumo, estadosDoConjunto, type AcaoRemota } from './projetos/situacaoDoProjeto';

/** O que a aprovação faz no Azure Boards, no texto do pedido: cria o pai (quando é para criar) e os itens, sem disparar. */
function efeitoDaAprovacao(p: Projeto) {
  const espelho = espelhoNoResumo(p);
  if (!espelho) return 'Sem espelho no Azure Boards: aprovar só grava a aprovação e não dispara.';
  const inicialDosItens = estadosDoConjunto('itens', p.estadosNoBoard)[0].nome;
  if (p.board === 'criar' && espelho.id === 0) {
    const artigo = espelho.tipoDoPai === 'Feature' ? 'a' : 'o';
    const inicialDoPai = estadosDoConjunto('pai', p.estadosNoBoard)[0].nome;
    const estados = inicialDoPai === inicialDosItens ? inicialDosItens : `${inicialDosItens} (${artigo} ${espelho.tipoDoPai}, ${inicialDoPai})`;
    return `Aprovar cria ${artigo} ${espelho.tipoDoPai} e os itens no Azure Boards como ${estados} e não dispara.`;
  }
  return `Aprovar cria os itens sob ${espelho.tipoDoPai}${espelho.id > 0 ? ` #${espelho.id}` : ''} no Azure Boards como ${inicialDosItens} e não dispara.`;
}

/**
 * As ações do projeto no site. A disponibilidade é apenas informativa: API e host repetem os portões no comando real.
 * (boards-estados, item 5) Aprovar plano e Disparar projeto são ações distintas, pelo pedido tipado à máquina, só para dono
 * e editor (o leitor não vê a ação); Gerar pacote e Editar projeto seguem como antes. Os botões e o pedido vão para
 * <paramref name="destino"/> — o topo da página, antes dos filtros —; o aviso de bloqueio fica no detalhe.
 */
export function AcoesDoProjeto({ projeto: p, itens, tratar, destino }: {
  projeto: Projeto; itens: ItemDoProjeto[]; tratar: (e: unknown) => string; destino?: HTMLElement | null;
}) {
  const { sessao } = useSessao();
  const token = sessao!.token;
  const [consulta, setConsulta] = useSearchParams();
  const envioAtual = useRef<AbortController | null>(null);
  const idDoPedido = useRef('');
  const disponivel = acaoDoProjeto(p, itens);
  const tipo = disponivel?.acao ?? null;
  const [pendencia, setPendencia] = useState<string | null>(tipo ? 'Conferindo matriz de impacto…' : null);
  const [acao, setAcao] = useState<AcaoRemota | null>(null);
  const [maquinas, setMaquinas] = useState<MaquinaRemota[]>([]);
  const [maquina, setMaquina] = useState('');
  const [comando, setComando] = useState<ComandoRemoto | null>(null);
  const [erro, setErro] = useState('');
  const [ocupado, setOcupado] = useState(false);
  const pacoteEmAndamento = !!comando && ['pendente', 'na-fila', 'entregue'].includes(comando.estado);
  const editar = ['dono', 'editor'].includes(p.papel ?? '');
  const semModelo = p.itens.some(i => i.situacao === 'planejado' && (!i.agente || !i.modelo));
  const bloqueio = tipo ? pendencia || (semModelo ? 'Há itens sem agente/modelo selecionado.' : null) : null;
  // O pedido aberto só vale para a ação que o projeto ainda oferece: a situação mudou, ele fecha.
  const pedido = acao !== null && acao === tipo ? disponivel : null;
  useEffect(() => () => envioAtual.current?.abort(), []);
  useEffect(() => {
    if (!tipo) { setPendencia(null); return; }
    const c = new AbortController(); setPendencia('Conferindo matriz de impacto…');
    void api.prontidaoDoProjeto(token, p.id, tipo, c.signal).then(r => { if (!c.signal.aborted) setPendencia(r.pendencia); })
      .catch(e => { if (!c.signal.aborted) setPendencia(`Conferência indisponível. ${tratar(e)}`); });
    return () => c.abort();
  }, [token, p.id, p.versao, tipo, tratar]);
  useEffect(() => {
    if (!acao) return;
    const c = new AbortController(); setErro(''); setMaquinas([]); setMaquina(''); idDoPedido.current = crypto.randomUUID().replaceAll('-', '');
    void api.maquinasRemotas(token, c.signal).then(lista => {
      if (!c.signal.aborted) { setMaquinas(lista); if (lista.length === 1) setMaquina(lista[0].maquinaId); }
    }).catch(e => { if (!c.signal.aborted) setErro(tratar(e)); });
    return () => c.abort();
  }, [acao, token, tratar]);
  useEffect(() => {
    if (!comando || ['concluido', 'recusado', 'expirado', 'falhou'].includes(comando.estado)) return;
    const c = new AbortController();
    const timer = window.setTimeout(() => {
      void api.comandoRemoto(token, comando.id, c.signal).then(r => { if (!c.signal.aborted) setComando(r); })
        .catch(e => { if (!c.signal.aborted) setErro(tratar(e)); });
    }, 1500);
    return () => { c.abort(); window.clearTimeout(timer); };
  }, [token, comando, tratar]);
  async function enviar() {
    if (!pedido) return;
    const c = new AbortController(); envioAtual.current = c;
    setOcupado(true); setErro('');
    try {
      const r = await api.enviarComandoRemoto(token, { id: idDoPedido.current, maquinaId: maquina,
        tipo: 'projeto', texto: JSON.stringify({ acao: pedido.acao, projeto: p.slug }), dispositivo: dispositivoAtual() }, c.signal);
      if (!c.signal.aborted) { setComando(r); setAcao(null); }
    } catch (e) { if (!c.signal.aborted) setErro(tratar(e)); }
    finally { if (!c.signal.aborted) setOcupado(false); }
  }
  async function gerarPacote() {
    if (!editar || ocupado || pacoteEmAndamento) return;
    const c = new AbortController(); envioAtual.current = c;
    setOcupado(true); setErro('');
    try {
      const hosts = await api.maquinasRemotas(token, c.signal);
      const host = hosts.find(m => m.maquinaId === consulta.get('maquina')) ?? (hosts.length === 1 ? hosts[0] : undefined);
      if (!host) throw new Error('Abra o Board e use a máquina do projeto para gerar o pacote.');
      idDoPedido.current ||= crypto.randomUUID().replaceAll('-', '');
      const r = await api.enviarComandoRemoto(token, { id: idDoPedido.current, maquinaId: host.maquinaId, tipo: 'projeto',
        texto: JSON.stringify({ acao: 'artefato', projeto: p.slug }), dispositivo: dispositivoAtual() }, c.signal);
      if (!c.signal.aborted) { setComando(r); idDoPedido.current = ''; }
    } catch (e) { if (!c.signal.aborted) setErro(tratar(e)); }
    finally { if (!c.signal.aborted) setOcupado(false); }
  }
  const topo = <>
    <div className="projeto-acoes">
      <button className="botao botao-primario" disabled={!editar || ocupado || pacoteEmAndamento} title={pacoteEmAndamento ? 'Um pedido deste projeto está em andamento.' : !editar ? 'Exige editor ou dono do projeto.' : 'Gera o projeto inteiro no host, sem confirmação.'} onClick={() => void gerarPacote()}>Gerar pacote</button>
      {disponivel && <button className="botao botao-fantasma projeto-acao-principal" aria-expanded={!!pedido} disabled={!!bloqueio} title={bloqueio ?? ''}
        onClick={() => setAcao(disponivel.acao)}><span aria-hidden="true">{disponivel.acao === 'aprovar' ? '✓' : '▶'}</span> {disponivel.rotulo}</button>}
      <button className="botao botao-fantasma" disabled={!editar} title="Consultar e editar os itens na aba Ondas" onClick={() => { const q = new URLSearchParams(consulta); q.set('visao', 'ondas'); setConsulta(q); }}>Editar projeto</button>
    </div>
    {pedido && <div className="cartao projeto-acao-remota" role="group" aria-label={`${pedido.rotulo} · ${p.nome}`}><strong>{pedido.rotulo} · {p.nome}</strong>
      <label>Máquina<select value={maquina} onChange={e => setMaquina(e.target.value)}><option value="">Escolher máquina</option>{maquinas.map(m => <option key={m.maquinaId} value={m.maquinaId}>{m.nome}</option>)}</select></label>
      <button className="botao botao-primario" disabled={!maquina || ocupado} onClick={() => void enviar()}>Enviar pedido</button>
      <button className="botao botao-fantasma" disabled={ocupado} onClick={() => setAcao(null)}>Cancelar pedido</button>
      <p className="projetos-nota">{pedido.acao === 'aprovar'
        ? `O host confere as permissões, a matriz e o plano antes de aprovar. ${efeitoDaAprovacao(p)} Enfileirar não confirma execução.`
        : 'O host confere as permissões e o plano antes de executar. Enfileirar não confirma execução.'}</p>
    </div>}
    {comando && <p role="status" className="projeto-pedido">Pedido {comando.id}: {comando.estado}. {comando.erro || 'A execução é confirmada pelo host.'}</p>}
    {erro && <p className="erro" role="alert">{erro}</p>}
  </>;
  return <>
    {destino ? createPortal(topo, destino) : topo}
    {disponivel && bloqueio && <div className="projeto-bloqueio" role="status">
      <strong>{disponivel.acao === 'aprovar' ? 'Aprovação bloqueada' : 'Disparo bloqueado'} · conferir matriz e plano</strong>
      <p>{pendencia} {semModelo && 'Há itens sem agente/modelo selecionado.'} Abra Impactos para consultar a definição.</p></div>}
  </>;
}
