import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { Projeto } from '../api/projetos';
import { Pagina } from '../componentes/Pagina';
import { Topo } from '../componentes/Topo';
import { AcessoAoProjeto } from '../componentes/AcessoAoProjeto';
import { PropostasDoPlano } from '../componentes/PropostasDoPlano';
import { ImpactosDoProjeto } from '../componentes/matrix/Impactos';
import { DecisaoDoItem } from '../componentes/DecisaoDoItem';
import { useSessao } from '../sessao';
import { formatar } from '../formatar';
import './Projetos.css';

const estados: Record<string, string> = {
  planejado: 'Planejado', rodando: 'Rodando', pergunta: 'Pergunta', integrado: 'Integrado', falhou: 'Falhou',
  rascunho: 'Rascunho', aguardando: 'Aguardando aprovação', aprovado: 'Em andamento', pausado: 'Pausado',
  fechando: 'Fechando', concluido: 'Concluído',
  'concluido-manual': 'Concluído pelo dev', cancelado: 'Cancelado pelo dev',
};
const situacoes = ['planejado', 'rodando', 'pergunta', 'integrado', 'falhou', 'concluido-manual', 'cancelado'];
const curto = (url: string) => url.replace(/\.git$/, '').split(/[/:]/).filter(Boolean).slice(-2).join('/');
const itensDoGrafo = (p: Projeto) => p.itensDoGrafo == null ? p.itens : p.itens.filter(i => p.itensDoGrafo!.includes(i.numero));
// (runners · item 6) O item retirado por decisão humana não segura a onda corrente.
const ondaAtual = (p: Projeto) => Math.min(...itensDoGrafo(p).filter(i => !i.decisaoHumana && !['integrado', 'concluido-manual', 'cancelado'].includes(i.situacao)).map(i => i.onda));

export function Projetos() {
  const { sessao, sair } = useSessao();
  const [consulta, setConsulta] = useSearchParams();
  const [projetos, setProjetos] = useState<Projeto[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [erro, setErro] = useState('');
  const sequencia = useRef(0);
  const atualizarProjeto = useCallback((atual: Projeto) => setProjetos(lista => lista.map(p => p.id === atual.id && atual.versao >= p.versao ? atual : p)), []);
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
  // O Board remoto (item 4 do remote-control-v3) abre o projeto pelo slug, que é o que a máquina conhece.
  const slug = consulta.get('slug');
  const peloSlug = slug ? visiveis.find(p => p.slug === slug) : undefined;
  const escolhido = visiveis.find(p => String(p.id) === consulta.get('projeto')) ?? peloSlug ?? visiveis[0];
  function filtrar(chave: string, valor: string) {
    const proxima = new URLSearchParams(consulta);
    if (valor) proxima.set(chave, valor); else proxima.delete(chave);
    if (chave !== 'projeto') proxima.delete('projeto');
    proxima.delete('slug');
    setConsulta(proxima);
  }
  function linkDoProjeto(id: number) {
    const q = new URLSearchParams(consulta);
    q.set('projeto', String(id)); q.delete('slug');
    return `/projetos?${q}`;
  }
  return <div className={`pagina projetos-pagina${consulta.has('projeto') || slug ? ' com-projeto-aberto' : ''}`}>
    <Topo secao="projetos" />
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
      {!carregando && !erro && slug && !peloSlug && !consulta.get('projeto') && <p role="status" className="projetos-nota">
        O projeto {slug} ainda não chegou ao site: no dev.kit, abra Projetos e use Sincronizar (o criado pelo Board vai sozinho quando há login).</p>}
      {!carregando && !erro && visiveis.length === 0 && <p>Nenhum projeto sincronizado com esses filtros. No dev.kit, abra Projetos e use Sincronizar.</p>}
      {!carregando && !erro && escolhido && <div className="projetos-board">
        <aside className="projetos-lista" aria-label="Projetos sincronizados">
          {visiveis.map(p => {
            const itens = itensDoGrafo(p), integrados = itens.filter(i => i.situacao === 'integrado').length;
            return <div key={p.id} className={`cartao projeto-resumo${escolhido.id === p.id ? ' escolhido' : ''}`}>
              <button type="button" className="projeto-selecionar" aria-pressed={escolhido.id === p.id} onClick={() => filtrar('projeto', String(p.id))}>
              <strong>{p.nome}</strong><span className="projeto-resumo-repositorios">{p.repositorios.map(curto).join(' · ')}</span>
              <code className="projeto-branch">{p.branch}</code>
              <progress aria-label={`Andamento de ${p.nome}`} max={Math.max(1, itens.length)} value={integrados} />
              <span>{integrados} de {itens.length} itens integrados</span><span>{estados[p.situacao] ?? p.situacao}</span>
              </button>
              <Link className="projeto-abrir" to={linkDoProjeto(p.id)} aria-label={`Abrir projeto ${p.nome}`}>Abrir projeto →</Link>
            </div>;
          })}
        </aside>
        <Detalhe projeto={escolhido} atualizar={atualizarProjeto} />
      </div>}
    </Pagina>
  </div>;
}

function Detalhe({ projeto: p, atualizar }: { projeto: Projeto; atualizar: (projeto: Projeto) => void }) {
  const { sessao, sair } = useSessao();
  const [consulta, setConsulta] = useSearchParams();
  const [emRevisao, setEmRevisao] = useState(false);
  const [selecao, setSelecao] = useState<{ projeto: number; numero: number } | null>(null);
  const itemSelecionado = selecao?.projeto === p.id ? selecao.numero : null;
  const visao = consulta.get('visao') ?? 'plano';
  const impactos = ['impactos', 'entregas', 'historico'].includes(visao);
  const [erro, setErro] = useState('');
  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) sair('expirou');
    else setErro(mensagemDaFalha(falha));
  }, [sair]);
  useEffect(() => { setErro(''); setSelecao(null); setEmRevisao(false); }, [p.id]);
  const itens = itensDoGrafo(p);
  const ondas = [...new Set(itens.map(i => i.onda))].sort((a, b) => a - b);
  const atual = ondaAtual(p);
  // (runners · item 6, F10) Sem trabalho restante, "integradas" só quando todos integraram: o retirado não vira integrado.
  const semTrabalhoRestante = itens.every(i => i.situacao === 'integrado') ? 'Todas as ondas integradas' : 'Sem trabalho restante nas ondas';
  return <article className="cartao projeto-detalhe" aria-label="Detalhe do projeto">
    <h2 tabIndex={-1} data-foco-navegacao={consulta.has('projeto') || consulta.has('slug') ? 'projeto' : undefined}>{p.nome}</h2><code className="projeto-branch">{p.branch}</code>
    <p>Repositórios: {p.repositorios.map(curto).join(' · ')}</p>
    <p>{p.pendenciaDoGrafo || (Number.isFinite(atual) ? `Onda ${atual} de ${ondas.length}` : itens.length ? semTrabalhoRestante : 'Ainda sem plano')}</p>
    <div className="projeto-situacoes">{situacoes.map(s => <span key={s} className={`projeto-situacao ${s}`}>{itens.filter(i => i.situacao === s).length} {estados[s]}</span>)}</div>
    <nav className="projeto-abas" aria-label="Visões do projeto">{(['plano', 'impactos', 'propostas', 'entregas', 'historico'] as const).map(v => <button key={v} aria-pressed={(emRevisao ? 'historico' : visao) === v}
      onClick={() => { const q = new URLSearchParams(consulta); q.set('visao', v); setConsulta(q); }}>{({ plano: 'Plano', impactos: 'Impactos', propostas: 'Propostas visuais', entregas: 'Entregas', historico: 'Histórico' })[v]}</button>)}</nav>
    {visao === 'propostas' ? <PropostasDoPlano key={`propostas-${p.id}`} token={sessao!.token} itens={p.itens} tratar={tratar} /> : impactos ? <ImpactosDoProjeto key={`impactos-${p.id}`} projeto={p} visao={visao === 'impactos' ? 'matriz' : visao} aoRevisar={setEmRevisao} /> : <div className="projeto-ondas">{ondas.map(onda => <section className="projeto-onda" key={onda} aria-label={`Onda ${onda}`}>
      <h3>Onda {onda} · {onda < atual ? 'concluída' : onda === atual ? 'atual' : 'próxima'}</h3>
      {itens.filter(i => i.onda === onda).map(i => <div className={`projeto-item${itemSelecionado === i.numero ? ' selecionado' : ''}`} key={i.numero}>
        <h4><button className="projeto-item-seletor" aria-pressed={itemSelecionado === i.numero}
          onClick={() => setSelecao({ projeto: p.id, numero: i.numero })}>{i.numero} · {i.titulo}</button></h4><p className={`projeto-situacao ${i.situacao}`}>{estados[i.situacao] ?? i.situacao}</p>
        <span>{i.repositorios.map(curto).join(' · ')}</span><code>{i.branch || 'Branch ainda não informado'}</code>
        {i.dependencias.length > 0 && <p>Depende de: {i.dependencias.join(', ')}</p>}
        {i.workItemId > 0 && <small>Work item #{i.workItemId}</small>}
      </div>)}
    </section>)}</div>}
    {!impactos && visao !== 'propostas' && itemSelecionado !== null && <DecisaoDoItem key={`${p.id}-${itemSelecionado}`} projeto={p} numero={itemSelecionado}
      token={sessao!.token} atualizar={atualizar} />}
    <p className="projetos-nota">Última sincronização: {formatar.dataHora(p.atualizadoEm)}. No dev.kit de outra máquina, use Sincronizar para recuperar o projeto e continuar.</p>
    {erro && <p role="alert" className="erro">{erro}</p>}
    <AcessoAoProjeto key={p.id} token={sessao!.token} recurso="projeto" projeto={{ id: p.id, chave: p.nome }} tratar={tratar} />
  </article>;
}
