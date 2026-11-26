import { useCallback, useEffect, useRef, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { AcaoNoProjetoRemoto, Artefato, OpcaoDeSkillDoPacote, PedidoDoPacote, ProdutorDoPacote, ResultadoDoPacote, MetadadosDoProdutor } from '../api/projetos';
import type { EstadoDoArtefato } from '../api/remoto';
import './ArtefatosDoProjeto.css';

const releituraMs = 15_000;
const estadosAtivos = ['enfileirando', 'enfileirado', 'gerando', 'agente-pendente'];

/** A consulta fica suspensa sem token (painel fechado). Falhas preservam os metadados anteriores. */
export function useArtefatosDoBoard(token: string | undefined, versao: string) {
  const [porSlug, setPorSlug] = useState(new Map<string, Artefato[]>());
  const [erro, setErro] = useState('');
  const [bloqueado, setBloqueado] = useState(false);
  const [atualizando, setAtualizando] = useState(false);
  const [tentativa, setTentativa] = useState(0);
  const conta = useRef(token);
  const atualizar = useCallback(() => setTentativa(t => t + 1), []);
  useEffect(() => {
    if (conta.current !== token) { conta.current = token; setPorSlug(new Map()); setErro(''); setBloqueado(false); }
    if (!token) { setAtualizando(false); return; }
    const controller = new AbortController();
    let timer: number;
    async function carregar() {
      setAtualizando(true);
      try {
        const lista = await api.artefatosRecentes(token!, controller.signal);
        if (!controller.signal.aborted) { setPorSlug(new Map(lista.map(p => [p.slug, p.artefatos]))); setErro(''); setBloqueado(false); }
      } catch (falha) {
        if (!controller.signal.aborted) {
          if (falha instanceof ErroDaApi && [401, 403].includes(falha.status)) setBloqueado(true);
          setErro(`Não foi possível atualizar os artefatos. ${mensagemDaFalha(falha)}`);
        }
      } finally {
        if (!controller.signal.aborted) { setAtualizando(false); timer = window.setTimeout(carregar, releituraMs); }
      }
    }
    void carregar();
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [token, versao, tentativa]);
  return { porSlug: conta.current === token ? porSlug : new Map<string, Artefato[]>(), erro, bloqueado, atualizando, atualizar };
}

export function repositorioCurto(repo: string) { return repo.replace(/\.git$/i, '').split(/[/:]/).filter(Boolean).at(-1) ?? repo; }
export function tamanhoDoArtefato(bytes: number) {
  if (bytes < 1024) return `${bytes} bytes`;
  if (Math.round(bytes / 102.4) < 10240) return `${(bytes / 1024).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} KB`;
  return `${(bytes / 1048576).toLocaleString('pt-BR', { maximumFractionDigits: 1, minimumFractionDigits: 1 })} MB`;
}
function prazo(expiraEm: string, agora: number) {
  const minutos = Math.floor((Date.parse(expiraEm) - agora) / 60_000);
  return minutos <= 0 ? 'link vencido' : minutos < 60 ? `expira em ${minutos} min` : `expira em ${Math.floor(minutos / 60)} h`;
}
export function rotuloDoEstado(estado: string) {
  return ({ enfileirando: 'Gerando', enfileirado: 'Gerando · na fila', gerando: 'Gerando', 'em-andamento': 'Gerando',
    'agente-pendente': 'Gerando · agente resolvendo os parâmetros da skill', 'local-pronto': 'Gerado · local',
    'envio-pendente': 'Envio pendente', 'registro-pendente': 'Registro pendente', publicado: 'Gerado',
    'nao-aplicavel': 'Não gerado', falhou: 'Falhou', parcial: 'Parcial', expirado: 'Link vencido',
    'publicacao-pendente': 'Envio pendente' } as Record<string, string>)[estado] ?? 'Aguardando resultado';
}
const normalizarRepo = (repo: string) => repo.replace(/^https?:\/\//i, '').replace(/\.git$/i, '').replace(/\/$/, '').toLowerCase();
export const rotuloDoProdutor = (p?: MetadadosDoProdutor | string | null) => typeof p === 'string' ? p : p
  ? p.tipo === 'skill' ? `skill ${p.nome}` : ({ principal: 'executável principal', 'todos-os-executaveis': 'todos os executáveis', previa: 'prévia', 'nao-aplicavel': 'não gera' }[p.tipo]) : '';
const rotuloDoPedido = (pedido: PedidoDoPacote) => pedido.resultados.some(r => r.estado === 'agente-pendente')
  ? rotuloDoEstado('agente-pendente') : rotuloDoEstado(pedido.estado);
const corDoSelo = (estado: string) => ['publicado', 'local-pronto'].includes(estado) ? 'pacote-selo-sucesso'
  : ['nao-aplicavel', 'falhou', 'parcial'].includes(estado) ? 'pacote-selo-erro' : 'pacote-selo-pendente';
const seloCurto = (rotulo: string) => rotulo.startsWith('Gerando') ? 'Gerando' : rotulo;
function linkValido(link: string) {
  try { const url = new URL(link); return !url.username && !url.password && (url.protocol === 'https:' || url.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)); }
  catch { return false; }
}

type Opcoes = Omit<AcaoNoProjetoRemoto, 'acao' | 'projeto'>;
export interface PropsDoPacote {
  nome: string; slug: string; artefatos: Artefato[]; gerar: (slug: string, opcoes?: Opcoes) => void | Promise<boolean>;
  desabilitado: boolean; motivo: string; agora: number; repositorios?: string[]; branch?: string;
  estados?: EstadoDoArtefato[]; capacidade?: boolean; erro?: string; erroDoPedido?: string;
  atualizar?: () => void; atualizando?: boolean; linksAutorizados?: boolean;
  pedidos?: PedidoDoPacote[]; produtores?: ProdutorDoPacote[];
  skillDoPacote?: string; skillsDoPacote?: OpcaoDeSkillDoPacote[]; salvarSkill?: (nome: string) => Promise<void>;
}

/** Painel do projeto inteiro. As ações individuais existentes se limitam a retomar a etapa do mesmo resultado. */
export function ArtefatosDoCard({ nome, slug, artefatos, gerar, desabilitado, motivo, agora, repositorios = [], branch = '',
  estados = [], capacidade = false, erro = '', erroDoPedido = '', atualizar, atualizando = false, linksAutorizados = true,
  pedidos = [], produtores = [], skillDoPacote = '', skillsDoPacote = [], salvarSkill }: PropsDoPacote) {
  const [enviando, setEnviando] = useState(false);
  const [destinosAbertos, setDestinosAbertos] = useState<number[]>([]);
  const [aguardando, setAguardando] = useState(false);
  const [mensagem, setMensagem] = useState('');
  const trava = useRef(false);
  const resultados = pedidos.flatMap(p => p.resultados);
  const emAndamento = [...estados, ...resultados].some(e => estadosAtivos.includes(e.estado));
  const revisao = [...estados, ...resultados].map(e => `${e.id}/${e.estado}`).join('|') + artefatos.map(a => `${a.id}/${a.expiraEm}`).join('|');
  useEffect(() => { setAguardando(false); }, [revisao, erroDoPedido]);
  const bloqueado = desabilitado || enviando || aguardando || emAndamento || !capacidade;
  const razao = emAndamento || aguardando ? 'Um pedido deste projeto está em andamento. Aguarde o resultado.'
    : desabilitado && motivo ? motivo : !capacidade ? 'Atualize o host para gerar o pacote do projeto.' : motivo;
  async function pedir(opcoes: Opcoes = {}) {
    if (bloqueado || trava.current) return;
    trava.current = true; setEnviando(true); setMensagem('');
    try {
      const aceito = await gerar(slug, opcoes);
      if (aceito === false) { setMensagem('O pedido não foi enviado. Confira o motivo e tente novamente.'); return; }
      setAguardando(true);
      setMensagem('Pedido aceito na fila. Acompanhe a geração e o motivo por repositório.');
    } catch (falha) { setMensagem(mensagemDaFalha(falha)); }
    finally { trava.current = false; setEnviando(false); }
  }
  async function escolherSkill(nomeDaSkill: string) {
    if (bloqueado || !salvarSkill || trava.current) return;
    trava.current = true; setEnviando(true); setMensagem('');
    try { await salvarSkill(nomeDaSkill); setMensagem(nomeDaSkill ? 'Skill do pacote salva.' : 'Skill do pacote removida.'); }
    catch (falha) { setMensagem(mensagemDaFalha(falha)); }
    finally { trava.current = false; setEnviando(false); }
  }
  const ordenados = [...pedidos].sort((a, b) => Date.parse(b.criadoEm) - Date.parse(a.criadoEm));
  const ultimo = ordenados[0];
  function linha(r: ResultadoDoPacote | EstadoDoArtefato, link?: Artefato) {
    const completo = 'link' in r ? r : undefined;
    const url = link?.link ?? completo?.link ?? '';
    const expira = link?.expiraEm ?? completo?.validoAte;
    const retido = !link?.retidoAte || Date.parse(link.retidoAte) > agora;
    const vencido = !!expira && Date.parse(expira) <= agora;
    const valido = r.estado === 'publicado' && linksAutorizados && retido && !vencido && linkValido(url);
    const tipo = link?.tipo ?? r.tipo;
    const produtor = rotuloDoProdutor(r.produtor);
    const branchDaOrigem = completo?.ciclo?.branch ?? ('branch' in r ? r.branch : '') ?? branch;
    const destinoAusente = tipo === 'preview' && ['falhou', 'nao-aplicavel', 'local-pronto'].includes(r.estado)
      && /destino|--swa-app/i.test(r.detalhe);
    return <div className="pacote-resultado" key={`${r.id}-${r.repositorio}`}>
      <div className="pacote-resultado-acoes"><span className={`br-chip ${corDoSelo(vencido ? 'expirado' : r.estado)}`}>{vencido ? 'Link vencido' : seloCurto(rotuloDoEstado(r.estado))}</span>
        {destinoAusente && <button className="br-btn" onClick={() => setDestinosAbertos(ids => ids.includes(r.id) ? ids : [...ids, r.id])}>Configurar</button>}
        {valido && <a className="br-btn" href={url} target="_blank" rel="noopener noreferrer" aria-label={`${tipo === 'preview' ? 'Abrir a prévia' : 'Baixar o pacote'} de ${repositorioCurto(r.repositorio)} · ${r.sha.slice(0, 7)}`}>{tipo === 'preview' ? 'Abrir prévia' : 'Baixar'}</a>}
      </div>
      <strong>{repositorioCurto(r.repositorio)}</strong>
      <small><code>{r.sha.slice(0, 7)}</code>{branchDaOrigem && ` · ${branchDaOrigem}`} · {produtor || tipo || 'produtor a detectar'}
        {tipo !== 'preview' && ` · ${tamanhoDoArtefato(link?.tamanho ?? completo?.tamanho ?? 0)}`}{expira && ` · ${prazo(expira, agora)}`}</small>
      <p>{r.detalhe}</p>
      {destinoAusente && destinosAbertos.includes(r.id) && <div role="note" className="pacote-orientacao-destino">
        <p>Abra Configurar destino no desktop desta máquina ou execute no host o comando abaixo. O destino é compartilhado por todos os projetos da máquina.</p>
        <code>{`devcli projeto --projeto ${slug} --artefato --swa-app <app> --swa-grupo <grupo> --swa-assinatura <id> --aplicar`}</code>
        <p>Use o Static Web App da prévia e informe também --preview-api-url e --preview-site-url quando exigidos pelo projeto. Esta orientação não altera a configuração.</p>
      </div>}
      {r.estado === 'agente-pendente' && !r.detalhe.toLowerCase().includes('agente resolvendo os parâmetros da skill') && <p>Gerando · agente resolvendo os parâmetros da skill</p>}
      {!linksAutorizados && <p>Consulte a lista com uma sessão autorizada para abrir o resultado.</p>}
      {vencido && <p>O link venceu. Atualize os links sem gerar novamente.</p>}
      {(r.estado === 'envio-pendente' || r.estado === 'registro-pendente' || r.estado === 'local-pronto' && tipo === 'pacote') && <button className="br-btn" disabled={bloqueado} onClick={() => void pedir({ retomar: r.id })}>
        {r.estado === 'registro-pendente' ? 'Tentar registro novamente' : 'Tentar envio novamente'}</button>}
      {r.estado === 'local-pronto' && tipo === 'preview' && <p>Prévia disponível somente no host. Confira o destino da prévia antes de gerar novamente.</p>}
      {(r.estado === 'falhou' || r.estado === 'nao-aplicavel') && <p>Corrija o motivo informado no host e use Gerar pacote para tentar novamente.</p>}
    </div>;
  }
  const reposDoUltimo = new Set(ultimo?.resultados.map(r => normalizarRepo(r.repositorio)));
  const idsAgrupados = new Set(resultados.map(r => r.id));
  const semPedido = estados.filter(e => !idsAgrupados.has(e.id));
  const semEstado = artefatos.filter(a => !estados.some(e => e.idRemoto === a.id) && !resultados.some(r => r.idRemoto === a.id));
  return <section className="br-artefatos pacote-projeto" aria-label={`Artefatos de ${nome}`}>
    <div className="br-artefatos-titulo"><span>Pacote do projeto · {nome}</span>
      <button className="br-btn br-gerar" disabled={bloqueado} title={razao} onClick={() => void pedir()}>Gerar pacote</button></div>
    {bloqueado && razao && <p>{razao}</p>}
    <div className="pacote-card"><h3>Pacote do projeto</h3>
      <p>Um clique gera o projeto integrado: o estado atual de {branch || 'cada branch do projeto'} em todos os repositórios. Sem perguntas.</p>
      <label>Skill do pacote (opcional)<div className="pacote-skill-selecao"><select aria-label="Skill do pacote (opcional)" value={skillDoPacote} disabled={bloqueado || !salvarSkill} onChange={e => void escolherSkill(e.target.value)}>
        <option value="">Nenhuma</option>
        {skillDoPacote && !skillsDoPacote.some(s => s.nome === skillDoPacote) && <option value={skillDoPacote}>{skillDoPacote} · indisponível no cadastro atual</option>}
        {skillsDoPacote.map(s => <option key={s.nome} value={s.nome} aria-label={`${s.nome} · ${s.tipo === 'somente-comando' ? 'somente comando' : 'com parâmetros'}`}>{s.nome}</option>)}
      </select>{skillsDoPacote.find(s => s.nome === skillDoPacote) && <span className="pacote-skill-tipo" aria-hidden="true">{skillsDoPacote.find(s => s.nome === skillDoPacote)?.tipo === 'somente-comando' ? 'somente comando' : 'com parâmetros'}</span>}</div></label>
      <p>Sem skill: executável principal ou todos os executáveis, cada um na sua subpasta. Front web: prévia.</p>
      <div className="pacote-produtores">{repositorios.map(repo => {
        const produtor = produtores.find(p => normalizarRepo(p.repositorio) === normalizarRepo(repo));
        return <div key={repo}><strong>{repositorioCurto(repo)}</strong><span>{produtor
          ? `${produtor.falhou ? 'Falhou' : !produtor.aplicavel ? 'Não gera' : produtor.produtor || produtor.formato} · ${produtor.motivo}`
          : 'Aguardando a previsão do produtor no host.'}</span></div>;
      })}</div>
    </div>
    {(mensagem || erroDoPedido) && <p role="status">{erroDoPedido || mensagem}</p>}
    {erro && <p role="alert">{erro} Os resultados anteriores foram preservados; os dados podem estar desatualizados.</p>}
    <div className="pacote-card"><h3>Último pacote</h3>
      {ultimo ? <><small>{new Date(ultimo.criadoEm).toLocaleString('pt-BR')} · {rotuloDoPedido(ultimo)}</small>
        {ultimo.resultados.map(r => linha(r, artefatos.find(a => a.id === r.idRemoto)))}
        {repositorios.filter(r => !reposDoUltimo.has(normalizarRepo(r))).map(r => <p key={r}><strong>{repositorioCurto(r)}</strong> · Sem resultado neste pedido.</p>)}
      </> : <><p>Nenhum pedido identificado nesta consulta.</p>
        {repositorios.map(repo => {
          const estado = estados.find(e => normalizarRepo(e.repositorio) === normalizarRepo(repo));
          const a = artefatos.find(e => normalizarRepo(e.repositorio) === normalizarRepo(repo));
          return estado ? linha(estado, artefatos.find(a => a.id === estado.idRemoto)) : a ? linha(estadoDe(a), a)
            : <p key={repo}><strong>{repositorioCurto(repo)}</strong> · Aguardando resultado.</p>;
        })}
        {!repositorios.length && artefatos.map(a => linha(estadoDe(a), a))}
      </>}
      {atualizar && <button className="br-btn" disabled={atualizando} onClick={atualizar}>{atualizando ? 'Atualizando links…' : 'Atualizar lista e links'}</button>}
    </div>
    <div className="pacote-card"><h3>Anteriores</h3>
      {ordenados.slice(1).map(p => <details key={p.id}><summary><span className={`br-chip ${corDoSelo(p.estado)}`}>{seloCurto(rotuloDoPedido(p))}</span>{rotuloDoPedido(p).includes('agente resolvendo') && <span> · agente resolvendo os parâmetros da skill</span>}<span className="pacote-anterior-origem">{new Date(p.criadoEm).toLocaleString('pt-BR')} · {p.resultados.map(r => repositorioCurto(r.repositorio)).join(' · ')}</span></summary>
        {p.resultados.map(r => linha(r, artefatos.find(a => a.id === r.idRemoto)))}</details>)}
      {(ultimo ? semPedido : semPedido.filter(e => estados.find(a => normalizarRepo(a.repositorio) === normalizarRepo(e.repositorio))?.id !== e.id)).map(e => <details key={`local-${e.id}`}><summary>{rotuloDoEstado(e.estado)} · {repositorioCurto(e.repositorio)}</summary>{linha(e)}</details>)}
      {semEstado.filter(a => ultimo || artefatos.find(e => normalizarRepo(e.repositorio) === normalizarRepo(a.repositorio))?.id !== a.id).map(a => <details key={`remoto-${a.id}`}><summary>Gerado · {repositorioCurto(a.repositorio)} · {a.sha.slice(0, 7)}</summary>{linha(estadoDe(a), a)}</details>)}
    </div>
  </section>;
}

function estadoDe(a: Artefato): EstadoDoArtefato {
  return { id: a.id, tipo: a.tipo, repositorio: a.repositorio, sha: a.sha, estado: 'publicado', etapa: 'concluido', detalhe: '', idRemoto: a.id, criadoEm: a.criadoEm };
}
