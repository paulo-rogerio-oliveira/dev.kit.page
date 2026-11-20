import { useCallback, useEffect, useRef, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { AcaoNoProjetoRemoto, Artefato, OrigemDoArtefato } from '../api/projetos';
import type { EstadoDoArtefato } from '../api/remoto';

/** Quanto tempo os links do card valem antes de o Board relê-los (o link de download vence; a API devolve um novo a cada leitura). */
const releituraMs = 15_000;

/**
 * (controle-v4, item 2) Os últimos artefatos de cada projeto, por slug (o do retrato da máquina). Relê a cada minuto e quando
 * `versao` muda — o aceite de um "Gerar artefato". A API antiga e as falhas preservam a lista e mostram o motivo.
 */
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
        if (!controller.signal.aborted && falha instanceof ErroDaApi && [401, 403].includes(falha.status)) setBloqueado(true);
        if (!controller.signal.aborted) setErro(falha instanceof ErroDaApi && falha.status === 401
          ? 'Entre novamente no site para atualizar os links.'
          : falha instanceof ErroDaApi && falha.status === 404 ? 'Esta API ainda não oferece a consulta de artefatos. Atualize o serviço.'
            : `Não foi possível atualizar os artefatos. ${mensagemDaFalha(falha)}`);
      } finally {
        if (!controller.signal.aborted) { setAtualizando(false); timer = window.setTimeout(carregar, releituraMs); }
      }
    }
    void carregar();
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [token, versao, tentativa]);
  return { porSlug: conta.current === token ? porSlug : new Map<string, Artefato[]>(), erro, bloqueado, atualizando, atualizar };
}

function repositorioCurto(repositorio: string) {
  return repositorio.replace(/\.git$/i, '').split(/[/:]/).filter(Boolean).at(-1) ?? repositorio;
}

/**
 * O tamanho do pacote no card. (controle-v4, item 5) Abaixo de 1 MB ele saía "0 MB" — o pacote pequeno parecia vazio —, e
 * agora sai em KB (ou em bytes, abaixo de 1 KB).
 */
export function tamanhoDoArtefato(bytes: number) {
  if (bytes < 1024) return `${bytes} bytes`;
  // O arredondado a 1 KB do teto já é 1 MB: 1.048.575 bytes saem "1,0 MB", e não "1.024 KB".
  if (Math.round(bytes / 102.4) < 10240) return `${(bytes / 1024).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} KB`;
  return `${(bytes / 1048576).toLocaleString('pt-BR', { maximumFractionDigits: 1, minimumFractionDigits: 1 })} MB`;
}

function prazo(expiraEm: string, agora: number) {
  const minutos = Math.floor((Date.parse(expiraEm) - agora) / 60_000);
  if (minutos <= 0) return 'link vencido: renove o link';
  return minutos < 60 ? `expira em ${minutos} min` : `expira em ${Math.floor(minutos / 60)} h`;
}

/**
 * A seção Artefatos do card do projeto: o "Gerar artefato" (o pedido vai pela fila ao dev.kit da máquina, que gera no
 * executor) e os últimos links — o pacote .NET para baixar e o preview da página estática. Nunca publica em produção.
 */
export function ArtefatosDoCard({ nome, slug, artefatos, gerar, desabilitado, motivo, agora,
  repositorios = [], branch = '', estados = [], capacidade = false, plano = null, erro = '', erroDoPedido = '', atualizar, atualizando = false, linksAutorizados = true }: {
  nome: string; slug: string; artefatos: Artefato[];
  gerar: (slug: string, opcoes?: Omit<AcaoNoProjetoRemoto, 'acao' | 'projeto'>) => void | Promise<boolean>;
  desabilitado: boolean; motivo: string; agora: number; repositorios?: string[]; branch?: string;
  estados?: EstadoDoArtefato[]; capacidade?: boolean; plano?: OrigemDoArtefato | null; erro?: string; erroDoPedido?: string;
  atualizar?: () => void; atualizando?: boolean; linksAutorizados?: boolean;
}) {
  const [aberto, setAberto] = useState(false);
  const [repo, setRepo] = useState(repositorios[0] ?? '');
  const [formato, setFormato] = useState<'' | 'pacote' | 'preview'>('');
  const [sha, setSha] = useState('');
  const [enviando, setEnviando] = useState(false);
  const [mensagem, setMensagem] = useState('');
  const formulario = useRef<HTMLFormElement>(null);
  const abrir = useRef<HTMLButtonElement>(null);
  const escolhido = repositorios.includes(repo) ? repo : repositorios[0] ?? '';
  const origem = plano?.projeto === slug && normalizarRepo(plano.repositorio) === normalizarRepo(escolhido)
    && plano.formato === formato && (!sha || plano.sha.startsWith(sha.toLowerCase())) ? plano : null;
  useEffect(() => { if (aberto) formulario.current?.querySelector('select')?.focus(); }, [aberto]);
  function fechar() { setAberto(false); abrir.current?.focus(); }
  function prepararNovoPedido(anterior: EstadoDoArtefato) {
    if (desabilitado || enviando || !capacidade) return;
    const repositorio = repositorios.find(r => normalizarRepo(r) === normalizarRepo(anterior.repositorio));
    if (!repositorio) { setMensagem('O repositório anterior não pertence mais ao projeto. Confira os repositórios antes de gerar.'); return; }
    setRepo(repositorio);
    setFormato(anterior.tipo === 'pacote' || anterior.tipo === 'preview' ? anterior.tipo : '');
    setSha(anterior.sha);
    setAberto(true);
    formulario.current?.querySelector('select')?.focus();
    setMensagem(`Corrija a causa informada no artefato nº ${anterior.id}. Repositório, tipo e commit foram preenchidos; confira a origem e confirme um novo pedido. O resultado anterior será preservado.`);
  }
  async function pedir(opcoes: Omit<AcaoNoProjetoRemoto, 'acao' | 'projeto'>) {
    if (desabilitado || enviando) return;
    setEnviando(true); setMensagem('');
    try {
      const aceito = await gerar(slug, opcoes);
      if (aceito === false) { setMensagem('O pedido não foi enviado. Confira o erro e tente novamente.'); return; }
      setMensagem(opcoes.conferir ? 'Consulta de origem na fila. Aguarde o commit confirmado pelo host.' : 'Pedido aceito na fila. A execução e a publicação ainda precisam terminar.');
      if (!opcoes.conferir) fechar();
    } catch (falha) { setMensagem(`Pedido não enviado. ${mensagemDaFalha(falha)}`); }
    finally { setEnviando(false); }
  }
  const semLink = estados.filter(e => !e.idRemoto || !artefatos.some(a => a.id === e.idRemoto));
  return <div className="br-artefatos" role="group" aria-label={`Artefatos de ${nome}`}>
    <div className="br-artefatos-titulo"><span>Artefatos{artefatos.length + semLink.length > 0 && ` · ${artefatos.length + semLink.length}`}</span>
      <button ref={abrir} className="br-btn br-gerar" disabled={desabilitado || enviando} onClick={() => setAberto(true)} aria-expanded={aberto}
        title={desabilitado ? motivo : 'Escolher repositório e tipo, conferir o commit e pedir a geração no executor. Nunca publica em produção.'}>
        Gerar artefato</button></div>
    {aberto && <form ref={formulario} className="br-artefato-form" aria-label="Gerar artefato" onKeyDown={e => { if (e.key === 'Escape') fechar(); }} onSubmit={e => {
      e.preventDefault();
      if (!capacidade || !escolhido || (sha && !/^[a-f0-9]{7,40}$/i.test(sha))) return;
      void pedir({ repositorio: escolhido, ...(formato ? { formato } : {}), ...(origem ? { sha: origem.sha } : { conferir: true, ...(sha ? { sha } : {}) }) });
    }}>
      <h3>Gerar artefato</h3><p>Escolha o que deseja disponibilizar deste projeto.</p>
      <div className="br-artefato-campos">
        <label>Repositório<select value={escolhido} disabled={enviando || !repositorios.length} onChange={e => setRepo(e.target.value)}
          title="Seleciona o repositório do projeto que será usado pelo executor.">
          {!repositorios.length && <option value="">Indisponível neste host</option>}
          {repositorios.map(r => <option key={r} value={r}>{r}</option>)}
        </select></label>
        <label>Artefato<select value={formato} disabled={enviando} onChange={e => setFormato(e.target.value as typeof formato)}
          title="Escolhe pacote desktop .NET ou preview web, ou deixa o executor detectar o tipo suportado.">
          <option value="">Detectar no repositório</option><option value="pacote">Pacote desktop para download</option><option value="preview">Preview web</option>
        </select></label>
      </div>
      <label>Commit (opcional)<input value={sha} onChange={e => setSha(e.target.value.trim())} disabled={enviando} maxLength={40} pattern="[a-fA-F0-9]{7,40}"
        title="SHA de 7 a 40 caracteres. Vazio consulta o HEAD do branch do projeto; a confirmação usa o SHA integral." /></label>
      <p className="br-artefato-origem">Origem: {origem?.branch || branch || 'branch informado pelo host'}{origem && <> · commit <code>{origem.sha}</code></>}</p>
      <p>O resultado aparecerá abaixo. A publicação web usa o ambiente de preview do projeto. Nunca produção.</p>
      {!capacidade && <p role="alert">Este host não informa a capacidade de conferir a origem. Atualize o dev.kit antes de gerar pelo site.</p>}
      <div className="br-row"><button type="button" className="br-btn" onClick={fechar} title="Fecha sem enviar outro pedido; a fila existente continua.">Cancelar</button>
        <button type="submit" className="br-btn br-perigo" disabled={desabilitado || enviando || !capacidade || !escolhido}
          title={origem ? 'Gera a partir do SHA integral mostrado acima.' : 'Consulta a origem no host, sem gerar ou publicar.'}>
          {enviando ? 'Enviando…' : origem ? 'Confirmar geração' : 'Conferir origem'}</button></div>
    </form>}
    {mensagem && <p role="status">{mensagem}</p>}
    {erroDoPedido && (aberto || mensagem) && <p role="alert">{erroDoPedido}</p>}
    {erro && <p role="alert">{erro} Os resultados anteriores foram preservados; os dados podem estar desatualizados.</p>}
    {atualizar && <button className="br-btn" onClick={atualizar} disabled={atualizando} title="Consulta a lista autorizada e renova links, sem gerar outro pacote ou preview.">
      {atualizando ? 'Atualizando links…' : 'Atualizar lista e links'}</button>}
    {semLink.map(a => <div className="br-artefato-estado" key={`host-${a.id}`}>
      <span className={`br-chip${a.estado === 'publicado' ? '' : ' br-warn'}`}>{rotuloDoEstado(a.estado)}</span>
      <strong>{a.tipo === 'preview' ? 'Preview web' : a.tipo === 'pacote' ? 'Pacote desktop' : 'Artefato'} · {repositorioCurto(a.repositorio)}</strong>
      <small>Commit <code>{a.sha}</code> · etapa {a.etapa} · artefato nº {a.id}</small>
      <p>{a.detalhe}</p>
      {a.estado === 'local-pronto' && <p>{a.tipo === 'preview'
        ? 'Este preview ainda não tem link publicado disponível no Board. Para tentar novamente, prepare e confirme um novo pedido.'
        : 'Disponível somente na máquina. O Board aguarda um link publicado.'}</p>}
      {a.estado === 'publicado' && <p>Aguardando link autorizado pela API. Atualize a lista.</p>}
      {(a.estado === 'envio-pendente' || a.estado === 'registro-pendente' || a.estado === 'local-pronto' && a.tipo === 'pacote') && <button className="br-btn" disabled={desabilitado || enviando || !capacidade}
        onClick={() => void pedir({ retomar: a.id })} title="Retoma envio ou registro pelo mesmo ID, preservando o resultado já gerado.">
        {a.estado === 'registro-pendente' ? 'Tentar registro novamente' : 'Tentar envio novamente'}</button>}
      {(a.estado === 'falhou' || a.estado === 'local-pronto' && a.tipo === 'preview') && <button className="br-btn" disabled={desabilitado || enviando || !capacidade}
        onClick={() => prepararNovoPedido(a)} title="Preenche repositório, tipo e commit anteriores. Corrija a causa e confirme um novo pedido após conferir a origem.">
        Preparar novo pedido</button>}
    </div>)}
    {artefatos.map(a => {
      const preview = a.tipo === 'preview';
      const alvo = `${repositorioCurto(a.repositorio)} · ${a.sha.slice(0, 7)}`;
      const vencido = !!a.expiraEm && !(Date.parse(a.expiraEm) > agora);
      const retido = !a.retidoAte || Date.parse(a.retidoAte) > agora;
      const valido = linksAutorizados && !vencido && retido && linkRemotoValido(a.link);
      const conteudo = <>
        <span className={`br-chip${preview ? ' br-chip-preview' : ''}`}>{preview ? 'preview' : 'pacote .NET'}</span>
        <span>{alvo}</span>
        <small><b>{valido ? preview ? 'Abrir preview' : 'Baixar pacote' : 'Indisponível'}</b> · {preview ? `ambiente ${a.ambiente}` : `${tamanhoDoArtefato(a.tamanho)} · ${a.expiraEm ? prazo(a.expiraEm, agora) : 'sem prazo'}`}</small>
      </>;
      return <div key={a.id} className="br-artefato-publicado"><span className="br-chip">Publicado</span>
        {valido ? <a className="br-artefato" href={a.link} target="_blank" rel="noopener noreferrer"
          title={preview ? 'Abre o preview publicado no ambiente do projeto.' : 'Baixa o pacote pelo link autorizado e vigente.'}
          aria-label={`${preview ? 'Abrir o preview' : 'Baixar o pacote'} de ${alvo}`}>{conteudo}</a>
          : <div className="br-artefato">{conteudo}</div>}
        {!valido && <p>{!linksAutorizados ? 'Consulte a lista com uma sessão autorizada para voltar a abrir o resultado.'
          : !retido ? 'A retenção deste pacote expirou.' : vencido ? 'O link venceu. Atualize os links sem gerar novamente.' : 'Link indisponível. Atualize a lista autorizada.'}</p>}
      </div>;
    })}
    <p className="br-artefatos-aviso">{artefatos.length + semLink.length
      ? 'Só preview e download, nunca produção. Links vencidos podem ser renovados sem gerar novamente.'
      : 'Nenhum artefato ainda: o pacote .NET ou o preview aparecem aqui. Nunca produção.'}</p>
  </div>;
}

function normalizarRepo(repo: string) { return repo.replace(/^https?:\/\//i, '').replace(/\.git$/i, '').replace(/\/$/, '').toLowerCase(); }
function linkRemotoValido(link: string) {
  try { const url = new URL(link); return !url.username && !url.password && (url.protocol === 'https:' || url.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)); }
  catch { return false; }
}
export function rotuloDoEstado(estado: string) {
  return ({ enfileirando: 'Preparando pedido', enfileirado: 'Na fila do executor', gerando: 'Em execução', 'local-pronto': 'Arquivo local pronto',
    'envio-pendente': 'Envio pendente', 'registro-pendente': 'Registro pendente', publicado: 'Publicado', falhou: 'Falhou' } as Record<string, string>)[estado] ?? 'Estado indisponível';
}
