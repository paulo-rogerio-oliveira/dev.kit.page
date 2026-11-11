import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../../api/cliente';
import type { CabecalhoMatrix, DocumentoMatrix, RevisaoMatrix } from '../../api/matrix';
import type { Projeto } from '../../api/projetos';
import type { AcessosDoDesign } from '../../api/tipos';
import { dispositivoAtual } from '../../api/remoto';
import { useSessao } from '../../sessao';
import { EditorMatrix, type AlvoDoEditorMatrix } from './EditorMatrix';
import { DialogoMatrix } from './DialogoMatrix';
import { ReentradaDaMatrix } from './RevisaoDaMatrix';
import { MapaDaMatrix } from './MapaDaMatrix';
import { identificador, ultimo } from './modelo';

const contaDa = (s: { login: string; empresa?: string | null }) => `${s.empresa ?? ''}:${s.login}`;
const normalizarRepo = (s: string) => s.replace(/\.git\/?$/, '').replace(/\/$/, '').toLowerCase();
const compativel = (h: RevisaoMatrix[]) => h.every(r => r.documento.contrato === 1);
const autoresDoAcesso = (a: Pick<AcessosDoDesign, 'dono' | 'acessos'> | null) => Object.fromEntries(
  [a?.dono, ...(a?.acessos ?? []).map(x => x.usuario)].flatMap(u => u ? [[String(u.id), u.nome || u.login]] : []));

function FalhaDaConsulta({ erro, repetir }: { erro: unknown; repetir: () => void }) {
  const [recursos, setRecursos] = useState(false);
  const status = erro instanceof ErroDaApi ? erro.status : 0;
  return <div role="alert" className="mx-alerta"><p>{[404, 405, 501].includes(status) ? 'Impactos indisponíveis neste servidor ou cliente anterior. Atualize o recurso sem substituir definições.' :
    status === 401 ? 'Sessão expirada. O rascunho da aba está preservado; reentre para continuar.' : mensagemDaFalha(erro)}</p>
    {status === 401 ? <ReentradaDaMatrix /> : <button onClick={repetir}>Tentar novamente</button>}
    {[404, 405, 501].includes(status) && <><button onClick={() => setRecursos(v => !v)}>Ver recursos disponíveis</button>
      {recursos && <nav aria-label="Recursos disponíveis"><Link to="/board">Board</Link> · <Link to="/projetos">Projetos</Link><p>Continue pelos recursos do site ou tente novamente a consulta de Impactos após atualizar o servidor.</p></nav>}</>}</div>;
}

async function carregarAlvo(token: string, cabecalho: CabecalhoMatrix, lista: CabecalhoMatrix[], signal: AbortSignal): Promise<AlvoDoEditorMatrix> {
  const [historico, mapa, registros] = await Promise.all([
    api.historicoMatrix(token, cabecalho, signal),
    api.historicoMatrix(token, { aplicacao: cabecalho.aplicacao, tipo: 'mapa', id: cabecalho.aplicacao }, signal),
    lista.some(c => c.aplicacao === cabecalho.aplicacao && c.id === cabecalho.id && c.tipo === 'registro') ?
      api.historicoMatrix(token, { ...cabecalho, tipo: 'registro' }, signal) : Promise.resolve([]),
  ]);
  if (![historico, mapa, registros].every(compativel)) throw new Error('Contrato Matrix desconhecido. Atualize o cliente; nenhuma definição foi substituída.');
  const documento = ultimo(historico)?.documento;
  if (!documento?.matriz) throw new Error('Definição da matriz ausente.');
  return { documento, historico, mapa, registros };
}

export function ImpactosDoProjeto({ projeto, visao = 'matriz', aoRevisar }: { projeto: Projeto; visao?: string; aoRevisar?: (ativa: boolean) => void }) {
  const { sessao } = useSessao();
  const [tentativa, setTentativa] = useState(0);
  const [dados, setDados] = useState<{ lista: CabecalhoMatrix[]; mapas: RevisaoMatrix[][]; editar: boolean; autores: Record<string, string>; minhasTasks: Record<string, string[]> } | null>(null);
  const [erro, setErro] = useState<unknown>(null);
  const [selecao, setSelecao] = useState('');
  const [alvo, setAlvo] = useState<AlvoDoEditorMatrix | null>(null);
  const [novaAplicacao, setNovaAplicacao] = useState('');
  const [novoMapa, setNovoMapa] = useState<DocumentoMatrix | null>(null);
  const [maquina, setMaquina] = useState('');
  const [aviso, setAviso] = useState('');
  const token = sessao!.token;
  useEffect(() => {
    const ctrl = new AbortController(); setDados(null); setErro(null); setAlvo(null);
    void (async () => {
      try {
        const [lista, acesso, maquinas] = await Promise.all([api.matrizes(token, ctrl.signal), api.acessosDoRecurso(token, 'projeto', projeto.id), api.maquinasRemotas(token, ctrl.signal).catch(() => [])]);
        const mapas = await Promise.all(lista.filter(c => c.tipo === 'mapa').map(c => api.historicoMatrix(token, c, ctrl.signal)));
        const vinculos = (await Promise.all(lista.filter(c => c.tipo === 'vinculo' && c.projeto === projeto.slug).map(c => api.historicoMatrix(token, c, ctrl.signal)))).flat();
        if (ctrl.signal.aborted) return;
        if (!mapas.every(compativel) || vinculos.some(v => v.documento.contrato !== 1)) throw new Error('Contrato Matrix desconhecido. Atualize o cliente.');
        const proprios = mapas.filter(h => ultimo(h)?.documento.mapa?.repositorios.some(r => projeto.repositorios.some(p => normalizarRepo(p) === normalizarRepo(r))));
        const matrizes = lista.filter(c => c.tipo === 'matriz' && c.projeto === projeto.slug);
        const minhasTasks = Object.fromEntries(matrizes.map(m => [`${m.aplicacao}/${m.id}`, [...new Set(vinculos.filter(v => v.documento.aplicacao === m.aplicacao && v.documento.id === m.id &&
          maquinas.some(maquina => maquina.maquinaId === v.documento.vinculo?.maquina)).map(v => v.documento.vinculo!.responsavel))]]));
        setDados({ lista, mapas: proprios, editar: acesso.papel === 'dono' || acesso.papel === 'editor', autores: autoresDoAcesso(acesso), minhasTasks });
        setSelecao(matrizes[0] ? `${matrizes[0].aplicacao}/${matrizes[0].id}` : proprios[0] ? `${ultimo(proprios[0])!.documento.aplicacao}/` : '');
      } catch (falha) { if (!ctrl.signal.aborted) setErro(falha); }
    })();
    return () => ctrl.abort();
  }, [token, projeto.id, projeto.slug, projeto.repositorios, tentativa]);

  useEffect(() => {
    if (!dados || !selecao) return;
    const ctrl = new AbortController(); setAlvo(null); setErro(null);
    const [app, id] = selecao.split('/');
    const h = dados.lista.find(c => c.aplicacao === app && c.id === id && c.tipo === 'matriz');
    if (h) void carregarAlvo(token, h, dados.lista, ctrl.signal).then(a => { if (!ctrl.signal.aborted) setAlvo({ ...a, autores: dados.autores, minhasTasks: dados.minhasTasks[selecao] }); }).catch(e => { if (!ctrl.signal.aborted) setErro(e); });
    else {
      const mapa = dados.mapas.find(h => ultimo(h)?.documento.aplicacao === app);
      if (mapa) setAlvo({ documento: { contrato: 1, aplicacao: app, tipo: 'matriz', id: projeto.slug, projeto: projeto.slug, matriz: null }, historico: [], mapa, registros: [] });
    }
    return () => ctrl.abort();
  }, [dados, selecao, token, projeto.slug]);

  async function solicitar(motivo?: string) {
    setAviso('');
    try {
      const maquinas = await api.maquinasRemotas(token);
      const id = maquinas.some(m => m.maquinaId === maquina) ? maquina : maquinas.length === 1 ? maquinas[0].maquinaId : '';
      if (!id) { setAviso('Selecione a máquina do Orquestrador para enviar o pedido.'); return; }
      const r = await api.enviarComandoRemoto(token, { id: crypto.randomUUID(), maquinaId: id, tipo: 'mensagem', dispositivo: dispositivoAtual(),
        texto: `Projeto ${projeto.slug}: ${motivo ?? 'proponha o preenchimento/revisão explícita da matriz de impactos'} da aplicação ${alvo?.documento.aplicacao ?? selecao.split('/')[0]}. Preserve o recorte das tasks e os portões existentes.` });
      setAviso(`Pedido ao Orquestrador: ${r.estado}. Nenhuma obrigação foi ampliada automaticamente.`);
    } catch (e) { setAviso(`Pedido não enviado. ${mensagemDaFalha(e)}`); }
  }
  return <div className="mx">
    {!!erro && <FalhaDaConsulta erro={erro} repetir={() => setTentativa(t => t + 1)} />}
    {!dados && !erro && <p role="status">Carregando Impactos…</p>}
    {dados && <>
      <details className="mx-config"><summary>Aplicação / Orquestrador</summary>
      <label>Aplicação / matriz da demanda<select value={selecao} onChange={v => { setSelecao(v.target.value); setNovoMapa(null); }}>
        <option value="">Selecione a aplicação</option>
        {dados.lista.filter(c => c.tipo === 'matriz' && c.projeto === projeto.slug).map(c => <option key={`${c.aplicacao}/${c.id}`} value={`${c.aplicacao}/${c.id}`}>{c.aplicacao} · {c.id} · r{c.versao}</option>)}
        {dados.mapas.filter(h => !dados.lista.some(c => c.tipo === 'matriz' && c.projeto === projeto.slug && c.aplicacao === ultimo(h)?.documento.aplicacao)).map(h =>
          <option key={ultimo(h)!.documento.aplicacao} value={`${ultimo(h)!.documento.aplicacao}/`}>{ultimo(h)!.documento.mapa!.nome} · preparar demanda</option>)}
      </select></label>
      <MaquinasDaMatrix valor={maquina} mudar={setMaquina} token={token} /></details>
      {aviso && <p role="status" className="mx-nota">{aviso}</p>}
      {alvo && <EditorMatrix key={`${alvo.documento.aplicacao}/${alvo.documento.id}`} alvo={alvo} projeto={projeto} token={token} conta={contaDa(sessao!)} editar={dados.editar} solicitar={motivo => void solicitar(motivo)} visao={visao} aoRevisar={aoRevisar}
        aplicacoes={dados.mapas.map(h => ({ id: ultimo(h)!.documento.aplicacao, nome: ultimo(h)!.documento.mapa!.nome }))}
        selecionarAplicacao={app => setSelecao(`${app}/${dados.lista.find(c => c.tipo === 'matriz' && c.projeto === projeto.slug && c.aplicacao === app)?.id ?? ''}`)} />}
      {!alvo && selecao && !erro && <p role="status">Carregando a definição e o histórico…</p>}
      {!selecao && <p>Nenhuma aplicação selecionada. Use o mapa da aplicação vinculada aos repositórios deste projeto.</p>}
      {dados.editar && !selecao && <section className="mx-card"><h3>Novo mapa desta aplicação</h3>
        <label>Identificador da aplicação<input value={novaAplicacao} onChange={e => setNovaAplicacao(e.target.value)} /></label>
        <button onClick={() => {
          if (!identificador(novaAplicacao) || dados.lista.some(c => c.tipo === 'mapa' && c.aplicacao === novaAplicacao)) { setAviso('Use um identificador novo e estável (letras, números, ponto, hífen ou sublinhado).'); return; }
          setNovoMapa({ contrato: 1, aplicacao: novaAplicacao, tipo: 'mapa', id: novaAplicacao, projeto: projeto.slug, mapa: {
            nome: novaAplicacao, repositorios: projeto.repositorios, componentes: [{ id: 'componente', nome: 'Novo componente', orientacao: '' }],
            layers: [{ id: 'layer', nome: 'Nova layer', orientacao: '' }], relacoes: [],
          } });
        }}>Preparar mapa</button>
      </section>}
      {novoMapa && <MapaDaMatrix key={novoMapa.aplicacao} documento={novoMapa} versao={0} token={token} conta={contaDa(sessao!)} editar={dados.editar} matriz={null}
        aoSalvar={() => { setNovoMapa(null); setTentativa(t => t + 1); }} preparar={() => {}} />}
    </>}
  </div>;
}

function MaquinasDaMatrix({ token, valor, mudar }: { token: string; valor: string; mudar: (id: string) => void }) {
  const [lista, setLista] = useState<{ maquinaId: string; nome: string }[]>([]);
  useEffect(() => { const c = new AbortController(); void api.maquinasRemotas(token, c.signal).then(setLista).catch(() => {}); return () => c.abort(); }, [token]);
  return <label>Máquina<select value={valor} onChange={e => mudar(e.target.value)}><option value="">Selecione</option>{lista.map(m => <option key={m.maquinaId} value={m.maquinaId}>{m.nome}</option>)}</select></label>;
}

export function ImpactosDaTask({ maquina, sessao: task, projeto, item, solicitar }: {
  maquina: string; sessao: string; projeto?: string | null; item?: number | null; solicitar: (motivo?: string) => void;
}) {
  const { sessao } = useSessao();
  const [aberto, setAberto] = useState(false);
  const [tentativa, setTentativa] = useState(0);
  const [dados, setDados] = useState<{ alvos: AlvoDoEditorMatrix[]; projeto?: Projeto; editar: boolean } | null>(null);
  const [indice, setIndice] = useState(0);
  const [erro, setErro] = useState<unknown>(null);
  useEffect(() => {
    if (!aberto) return;
    const c = new AbortController(); setDados(null); setErro(null);
    void (async () => {
      try {
        const token = sessao!.token, lista = await api.matrizes(token, c.signal);
        const links = await Promise.all(lista.filter(h => h.tipo === 'vinculo' && h.projeto === (projeto ?? '')).map(h => api.historicoMatrix(token, h, c.signal)));
        if (!links.every(compativel)) throw new Error('Contrato Matrix desconhecido. Atualize o cliente.');
        const correspondentes = links.flatMap(h => h.filter(r => r.documento.vinculo?.sessao === task && r.documento.vinculo.maquina === maquina && r.documento.vinculo.item === (item ?? null)));
        const porAlvo = new Map<string, RevisaoMatrix[]>();
        for (const r of correspondentes) { const chave = `${r.documento.aplicacao}/${r.documento.id}`; porAlvo.set(chave, [...(porAlvo.get(chave) ?? []), r]); }
        const projetos = projeto ? await api.projetos(token) : [];
        const p = projetos.find(p => p.slug === projeto);
        const acesso = p ? await api.acessosDoRecurso(token, 'projeto', p.id) : null;
        const alvos = await Promise.all([...porAlvo.values()].map(async h => {
          const d = h[0].documento;
          const a = await carregarAlvo(token, { ...d, tipo: 'matriz', versao: 0 }, lista, c.signal);
          const responsaveis = h.map(r => r.documento.vinculo!.responsavel);
          return { ...a, autores: autoresDoAcesso(acesso), responsavel: responsaveis[0], recorte: a.documento.matriz!.celulas.filter(c => c.responsaveis.some(r => responsaveis.includes(r.id) && r.item === (item ?? null))).map(c => c.id) };
        }));
        if (!c.signal.aborted) setDados({ alvos, projeto: p, editar: projeto ? acesso?.papel === 'dono' || acesso?.papel === 'editor' : true });
      } catch (e) { if (!c.signal.aborted) setErro(e); }
    })();
    return () => c.abort();
  }, [aberto, tentativa, sessao, maquina, task, projeto, item]);
  return <><button className="br-btn" onClick={() => setAberto(true)}>Impactos</button>
    {aberto && <DialogoMatrix titulo={`Impactos da task${item ? ` · Item ${item}` : ''}`} fechar={() => setAberto(false)}>
      {!!erro && <FalhaDaConsulta erro={erro} repetir={() => setTentativa(t => t + 1)} />}
      {!dados && !erro && <p role="status">Carregando recorte…</p>}
      {dados && !dados.alvos.length && <p role="status">Nenhum recorte vinculado a esta task na máquina selecionada. {projeto && <Link to={`/projetos?slug=${encodeURIComponent(projeto)}&visao=impactos`}>Consultar Impactos do projeto</Link>}</p>}
      {dados && dados.alvos.length > 1 && <label>Matriz<select value={indice} onChange={e => setIndice(Number(e.target.value))}>{dados.alvos.map((a, i) => <option key={i} value={i}>{a.documento.aplicacao} · {a.documento.id}</option>)}</select></label>}
      {dados?.alvos[indice] && <EditorMatrix key={`${dados.alvos[indice].documento.aplicacao}/${dados.alvos[indice].documento.id}`} alvo={dados.alvos[indice]}
        projeto={dados.projeto} token={sessao!.token} conta={`${contaDa(sessao!)}:task:${maquina}:${task}`} editar={dados.editar} solicitar={solicitar} />}
    </DialogoMatrix>}
  </>;
}
