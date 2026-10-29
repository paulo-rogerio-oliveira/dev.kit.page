import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { Projeto } from '../api/projetos';
import { Pagina } from '../componentes/Pagina';
import { Topo } from '../componentes/Topo';
import { useSessao } from '../sessao';
import { formatar } from '../formatar';
import './Projetos.css';

const estados: Record<string, string> = {
  planejado: 'Planejado', rodando: 'Rodando', pergunta: 'Pergunta', integrado: 'Integrado', falhou: 'Falhou',
  rascunho: 'Rascunho', aguardando: 'Aguardando aprovação', aprovado: 'Em andamento', pausado: 'Pausado',
  fechando: 'Fechando', concluido: 'Concluído',
};
const situacoes = ['planejado', 'rodando', 'pergunta', 'integrado', 'falhou'];
const curto = (url: string) => url.replace(/\.git$/, '').split(/[/:]/).filter(Boolean).slice(-2).join('/');
const ondaAtual = (p: Projeto) => Math.min(...p.itens.filter(i => i.situacao !== 'integrado').map(i => i.onda));

export function Projetos() {
  const { sessao, sair } = useSessao();
  const [consulta, setConsulta] = useSearchParams();
  const [projetos, setProjetos] = useState<Projeto[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState('');
  const sequencia = useRef(0);
  const nivel = consulta.get('nivel') ?? '';
  const situacao = consulta.get('situacao') ?? '';
  const carregar = useCallback(async () => {
    const numero = ++sequencia.current;
    setCarregando(true);
    setErro('');
    try {
      const recebidos = await api.projetos(sessao!.token);
      if (numero === sequencia.current) setProjetos(recebidos);
    } catch (e) {
      if (numero !== sequencia.current) return;
      if (e instanceof ErroDaApi && e.status === 401) sair('expirou');
      else setErro(`Não foi possível carregar os projetos. ${mensagemDaFalha(e)}`);
    } finally { if (numero === sequencia.current) setCarregando(false); }
  }, [sessao, sair]);
  useEffect(() => { void carregar(); return () => { sequencia.current++; }; }, [carregar]);
  const visiveis = projetos.filter(p => (!nivel || p.nivel === nivel) && (!situacao || p.situacao === situacao));
  const escolhido = visiveis.find(p => String(p.id) === consulta.get('projeto')) ?? visiveis[0];
  function filtrar(chave: string, valor: string) {
    const proxima = new URLSearchParams(consulta);
    if (valor) proxima.set(chave, valor); else proxima.delete(chave);
    if (chave !== 'projeto') proxima.delete('projeto');
    setConsulta(proxima);
  }
  return <div className="pagina">
    <Topo secao="projetos"><Link className="botao botao-fantasma link-ativo" to="/projetos">Projetos</Link>
      <Link className="botao botao-fantasma" to="/design">Design</Link><Link className="botao botao-fantasma" to="/conta">Conta</Link></Topo>
    <Pagina rotulo={sessao!.empresa ?? 'Meu espaço'} titulo="Projetos" subtitulo="Os projetos sincronizados do dev.kit, com seus branches, plano e andamento.">
      <div className="projetos-filtros">
        <select aria-label="Nível de compartilhamento" value={nivel} onChange={e => filtrar('nivel', e.target.value)}>
          <option value="">Todos os projetos</option><option value="usuario">Meus projetos</option>
          {sessao!.empresa && <><option value="time">Time</option><option value="empresa">Empresa</option></>}
        </select>
        <select aria-label="Situação do projeto" value={situacao} onChange={e => filtrar('situacao', e.target.value)}>
          <option value="">Todas as situações</option>
          {['rascunho', 'aguardando', 'aprovado', 'pausado', 'fechando', 'concluido'].map(e => <option key={e} value={e}>{estados[e]}</option>)}
        </select>
        <button className="botao botao-fantasma" onClick={() => void carregar()} disabled={carregando}>↻ Atualizar</button>
        <span className="projetos-contagem">{visiveis.length} projeto(s)</span>
      </div>
      {erro && <div role="alert" className="erro">{erro} <button className="botao botao-fantasma" onClick={() => void carregar()}>Tentar novamente</button></div>}
      {carregando && <p role="status">Carregando projetos…</p>}
      {!carregando && !erro && visiveis.length === 0 && <p>Nenhum projeto sincronizado com esses filtros. No dev.kit, abra Projetos e use Sincronizar.</p>}
      {!carregando && !erro && escolhido && <div className="projetos-board">
        <aside className="projetos-lista" aria-label="Projetos sincronizados">
          {visiveis.map(p => {
            const integrados = p.itens.filter(i => i.situacao === 'integrado').length;
            return <button key={p.id} className={`cartao projeto-resumo${escolhido.id === p.id ? ' escolhido' : ''}`}
              aria-pressed={escolhido.id === p.id} onClick={() => filtrar('projeto', String(p.id))}>
              <strong>{p.nome}</strong><span>{p.repositorios.map(curto).join(' · ')}</span>
              <progress aria-label={`Andamento de ${p.nome}`} max={Math.max(1, p.itens.length)} value={integrados} />
              <span>{integrados} de {p.itens.length} itens integrados</span><span>{estados[p.situacao] ?? p.situacao}</span>
            </button>;
          })}
        </aside>
        <Detalhe projeto={escolhido} />
      </div>}
    </Pagina>
  </div>;
}

function Detalhe({ projeto: p }: { projeto: Projeto }) {
  const ondas = [...new Set(p.itens.map(i => i.onda))].sort((a, b) => a - b);
  const atual = ondaAtual(p);
  return <article className="cartao projeto-detalhe" aria-label="Detalhe do projeto">
    <h2>{p.nome}</h2><code className="projeto-branch">{p.branch}</code>
    <p>Repositórios: {p.repositorios.map(curto).join(' · ')}</p>
    <p>{Number.isFinite(atual) ? `Onda ${atual} de ${ondas.length}` : p.itens.length ? 'Todas as ondas integradas' : 'Ainda sem plano'}</p>
    <div className="projeto-situacoes">{situacoes.map(s => <span key={s} className={`projeto-situacao ${s}`}>{p.itens.filter(i => i.situacao === s).length} {estados[s]}</span>)}</div>
    <div className="projeto-ondas">{ondas.map(onda => <section className="projeto-onda" key={onda} aria-label={`Onda ${onda}`}>
      <h3>Onda {onda} · {onda < atual ? 'concluída' : onda === atual ? 'atual' : 'próxima'}</h3>
      {p.itens.filter(i => i.onda === onda).map(i => <div className="projeto-item" key={i.numero}>
        <h4>{i.numero} · {i.titulo}</h4><p className={`projeto-situacao ${i.situacao}`}>{estados[i.situacao] ?? i.situacao}</p>
        <span>{i.repositorios.map(curto).join(' · ')}</span><code>{i.branch || 'Branch ainda não informado'}</code>
        {i.dependencias.length > 0 && <p>Depende de: {i.dependencias.join(', ')}</p>}
        {i.workItemId > 0 && <small>Work item #{i.workItemId}</small>}
      </div>)}
    </section>)}</div>
    <p className="projetos-nota">Última sincronização: {formatar.dataHora(p.atualizadoEm)}. No dev.kit de outra máquina, use Sincronizar para recuperar o projeto e continuar.</p>
  </article>;
}
