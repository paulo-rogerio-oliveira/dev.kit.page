import { useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api } from '../api/cliente';
import type { Projeto } from '../api/projetos';
import type { ComandoRemoto, MaquinaRemota } from '../api/remoto';
import { dispositivoAtual } from '../api/remoto';
import { useSessao } from '../sessao';

/** A disponibilidade é apenas informativa. API e host repetem os portões no comando real. */
export function AcoesDoProjeto({ projeto: p, tratar }: { projeto: Projeto; tratar: (e: unknown) => string }) {
  const { sessao } = useSessao();
  const token = sessao!.token;
  const [consulta, setConsulta] = useSearchParams();
  const envioAtual = useRef<AbortController | null>(null);
  const idDoPedido = useRef('');
  const [pendencia, setPendencia] = useState<string | null>('Conferindo matriz de impacto…');
  const [acao, setAcao] = useState<'disparar' | null>(null);
  const [maquinas, setMaquinas] = useState<MaquinaRemota[]>([]);
  const [maquina, setMaquina] = useState('');
  const [comando, setComando] = useState<ComandoRemoto | null>(null);
  const [erro, setErro] = useState('');
  const [ocupado, setOcupado] = useState(false);
  const pacoteEmAndamento = !!comando && ['pendente', 'na-fila', 'entregue'].includes(comando.estado);
  const editar = ['dono', 'editor'].includes(p.papel ?? '');
  const semModelo = p.itens.some(i => i.situacao === 'planejado' && (!i.agente || !i.modelo));
  const bloqueio = pendencia || (semModelo ? 'Há itens sem agente/modelo selecionado.' : null);
  useEffect(() => () => envioAtual.current?.abort(), []);
  useEffect(() => {
    const c = new AbortController(); setPendencia('Conferindo matriz de impacto…');
    void api.prontidaoDoProjeto(token, p.id, c.signal).then(r => { if (!c.signal.aborted) setPendencia(r.pendencia); })
      .catch(e => { if (!c.signal.aborted) setPendencia(`Conferência indisponível. ${tratar(e)}`); });
    return () => c.abort();
  }, [token, p.id, p.versao, tratar]);
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
    const c = new AbortController(); envioAtual.current = c;
    setOcupado(true); setErro('');
    try {
      const r = await api.enviarComandoRemoto(token, { id: idDoPedido.current, maquinaId: maquina,
        tipo: 'projeto', texto: JSON.stringify({ acao, projeto: p.slug }), dispositivo: dispositivoAtual() }, c.signal);
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
  return <>
    <div className="projeto-acoes">
      <button className="botao" disabled={!editar || ocupado || pacoteEmAndamento} title={pacoteEmAndamento ? 'Um pedido deste projeto está em andamento.' : !editar ? 'Exige editor ou dono do projeto.' : 'Gera o projeto inteiro no host, sem confirmação.'} onClick={() => void gerarPacote()}>Gerar pacote</button>
      <button className="botao botao-fantasma" disabled={!editar || !!bloqueio || p.situacao !== 'aprovado'} title={bloqueio ?? ''} onClick={() => setAcao('disparar')}>{p.situacao === 'aprovado' ? 'Disparar projeto' : 'Aprovar e disparar'}</button>
      <button className="botao botao-fantasma" disabled={!editar} title="Consultar e editar os itens na aba Ondas" onClick={() => { const q = new URLSearchParams(consulta); q.set('visao', 'ondas'); setConsulta(q); }}>Editar projeto</button>
    </div>
    {bloqueio && <div className="projeto-bloqueio" role="status"><strong>Disparo bloqueado · conferir matriz e plano</strong><p>{pendencia} {semModelo && 'Há itens sem agente/modelo selecionado.'} Abra Impactos para consultar a definição.</p></div>}
    {acao && <div className="cartao projeto-acao-remota"><strong>Disparar projeto · {p.nome}</strong>
      <label>Máquina<select value={maquina} onChange={e => setMaquina(e.target.value)}><option value="">Escolher máquina</option>{maquinas.map(m => <option key={m.maquinaId} value={m.maquinaId}>{m.nome}</option>)}</select></label>
      <button className="botao" disabled={!maquina || ocupado} onClick={() => void enviar()}>Enviar pedido</button>
      <button className="botao botao-fantasma" disabled={ocupado} onClick={() => setAcao(null)}>Cancelar pedido</button>
      <p className="projetos-nota">O host confere as permissões e o plano antes de executar. Enfileirar não confirma execução.</p>
    </div>}
    {comando && <p role="status">Pedido {comando.id}: {comando.estado}. {comando.erro || 'A execução é confirmada pelo host.'}</p>}
    {erro && <p className="erro" role="alert">{erro}</p>}
  </>;
}
