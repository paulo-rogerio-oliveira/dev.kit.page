import { useEffect, useRef, useState } from 'react';
import type { CatalogoRemoto, FluxoDoCatalogo, ParametroDoFluxo } from '../api/remoto';
import type { CatalogoDoChat } from './useCatalogoRemoto';
import type { FluxoDaMensagem } from './useConversaRemota';

/**
 * O contexto da mensagem: o fluxo (o nome no texto e, do catálogo, o id com o agente e os parâmetros — correções do board
 * remoto), o work item, o repositório (o valor COMPLETO do catálogo) e o branch.
 */
export interface ContextoDoChat {
  fluxo: string; item: string; repositorio: string; branch: string;
  fluxoId: number | null; agente: string; parametros: Record<string, string>;
}
export const contextoVazio: ContextoDoChat = { fluxo: '', item: '', repositorio: '', branch: '', fluxoId: null, agente: '', parametros: {} };

/** O fluxo do catálogo que o contexto escolheu (pelo id); nulo no texto livre e no catálogo antigo. */
export function fluxoEscolhido(contexto: ContextoDoChat, catalogo: CatalogoRemoto | null): FluxoDoCatalogo | null {
  return contexto.fluxoId === null ? null : catalogo?.fluxos?.find(f => f.id === contexto.fluxoId) ?? null;
}

export function rotuloDoParametro(p: ParametroDoFluxo) { return p.rotulo?.trim() || p.nome; }

/** Os parâmetros OBRIGATÓRIOS do fluxo escolhido ainda sem valor (nem padrão): o envio espera por eles. */
export function parametrosFaltando(contexto: ContextoDoChat, catalogo: CatalogoRemoto | null) {
  const fluxo = fluxoEscolhido(contexto, catalogo);
  return (fluxo?.parametros ?? []).filter(p => p.obrigatorio !== false && !(contexto.parametros[p.nome] ?? '').trim()).map(rotuloDoParametro);
}

/** O fluxo nos campos próprios da mensagem (o dev.kit novo abre com eles); nulo sem fluxo do catálogo. */
export function fluxoDaMensagem(contexto: ContextoDoChat): FluxoDaMensagem | undefined {
  if (contexto.fluxoId === null) return undefined;
  const parametros = Object.fromEntries(Object.entries(contexto.parametros).map(([k, v]) => [k, v.trim()]).filter(([, v]) => v));
  return { fluxo: contexto.fluxoId, agente: contexto.agente, parametros };
}

/** O texto ao Orquestrador com o contexto escrito — também o modelo e os parâmetros, que o dev.kit antigo só lê aqui. */
export function textoComContexto(texto: string, contexto: ContextoDoChat) {
  const parametros = contexto.fluxoId === null ? [] : Object.entries(contexto.parametros).filter(([, v]) => v.trim()).map(([k, v]) => `Parâmetro ${k}: ${v.trim()}`);
  const linhas = [contexto.fluxo && `/ ${contexto.fluxo}`, contexto.fluxoId !== null && contexto.agente && `Modelo: ${contexto.agente}`, ...parametros,
    contexto.item && `# ${contexto.item}`, contexto.repositorio && `@ ${contexto.repositorio}`, contexto.branch && `Branch existente: ${contexto.branch}`].filter(Boolean);
  return [...linhas, texto.trim()].join('\n');
}

/** O último segmento de `host/dono/repo`: o nome que o seletor e o chip mostram. */
export function nomeDoRepositorio(valor: string) {
  const partes = valor.replace(/\/+$/, '').split('/');
  return partes.at(-1) || valor;
}
function donoDoRepositorio(valor: string) { return valor.replace(/\/+$/, '').split('/').at(-2) ?? ''; }

type Atalho = '/' | '#' | '@';
interface Opcao { grupo: string; rotulo: string; detalhe: string; valor: string; busca: string[]; fluxo?: FluxoDoCatalogo }

/**
 * As opções do atalho a partir do catálogo da máquina (o handshake): `/` os fluxos, os projetos e os projetos de design; `#` os
 * work items em andamento; `@` os repositórios — pelo NOME (o último segmento), com o dono só quando dois têm o mesmo nome. O
 * valor é o que vai no texto ao Orquestrador (o repositório inteiro).
 */
export function opcoesDoAtalho(atalho: Atalho, catalogo: CatalogoRemoto | null): Opcao[] {
  if (!catalogo) return [];
  if (atalho === '#')
    return (catalogo.workItems ?? []).map(w => {
      const rotulo = `#${w.id} · ${w.titulo}`, detalhe = [w.tipo, w.estado].filter(Boolean).join(' · ');
      return { grupo: 'Work items em andamento', rotulo, detalhe, valor: String(w.id), busca: [rotulo, detalhe] };
    });
  if (atalho === '@') {
    const repositorios = catalogo.repositorios ?? [];
    const vezes = new Map<string, number>();
    for (const r of repositorios) vezes.set(nomeDoRepositorio(r.nome).toLowerCase(), (vezes.get(nomeDoRepositorio(r.nome).toLowerCase()) ?? 0) + 1);
    return repositorios.map(r => {
      const nome = nomeDoRepositorio(r.nome);
      const dono = (vezes.get(nome.toLowerCase()) ?? 0) > 1 ? donoDoRepositorio(r.nome) : '';
      return { grupo: 'Repositórios', rotulo: nome, detalhe: [dono, r.branchPadrao ? `branch padrão ${r.branchPadrao}` : ''].filter(Boolean).join(' · '),
        valor: r.nome, busca: [nome, donoDoRepositorio(r.nome), r.nome] };
    });
  }
  return [
    ...(catalogo.fluxos ?? []).map(f => ({ grupo: 'Fluxos', rotulo: f.nome, detalhe: f.descricao ?? '', valor: f.nome, busca: [f.nome, f.descricao ?? ''], fluxo: f })),
    ...(catalogo.projetos ?? []).map(p => {
      const detalhe = [p.slug !== p.nome ? p.slug : '', p.situacao].filter(Boolean).join(' · ');
      return { grupo: 'Projetos', rotulo: p.nome, detalhe, valor: `projeto ${p.slug}`, busca: [p.nome, detalhe, p.slug] };
    }),
    ...(catalogo.designs ?? []).map(d => {
      const detalhe = d.telas === 1 ? '1 tela' : `${d.telas} telas`;
      return { grupo: 'Projetos de design', rotulo: d.nome, detalhe, valor: `design ${d.nome}`, busca: [d.nome, detalhe] };
    }),
  ];
}

const nomeDaLista: Record<Atalho, string> = { '/': 'fluxo, projeto ou design', '#': 'work item em andamento', '@': 'repositório' };

/** No celular (toque), o painel não puxa o teclado sozinho: a lista do catálogo fica à vista e o toque escolhe. */
function focarSemTeclado(campo: HTMLInputElement | null | undefined) {
  if (campo && !window.matchMedia?.('(pointer: coarse)').matches) campo.focus();
}

/** O fluxo e os valores padrão dos parâmetros dele: o ponto de partida dos chips. */
function comFluxo(contexto: ContextoDoChat, fluxo: FluxoDoCatalogo | undefined, nome: string): ContextoDoChat {
  if (!fluxo?.id) return { ...contexto, fluxo: nome, fluxoId: null, agente: '', parametros: {} };
  const parametros = Object.fromEntries((fluxo.parametros ?? []).filter(p => p.padrao).map(p => [p.nome, p.padrao!]));
  return { ...contexto, fluxo: fluxo.nome, fluxoId: fluxo.id, agente: fluxo.agentePadrao ?? '', parametros };
}

export function ContextoDoChatRemoto({ contexto, alterar, atalho, abrir, desabilitado, catalogo = null }: {
  contexto: ContextoDoChat; alterar: (contexto: ContextoDoChat) => void;
  atalho: Atalho | null; abrir: (atalho: Atalho | null) => void; desabilitado: boolean; catalogo?: CatalogoRemoto | null;
}) {
  const [rascunho, setRascunho] = useState(contexto);
  const [pesquisa, setPesquisa] = useState('');
  const [digitou, setDigitou] = useState(false);
  const [chip, setChip] = useState<string | null>(null);
  const painel = useRef<HTMLDivElement>(null);
  const anterior = useRef<HTMLElement | null>(null);
  useEffect(() => {
    if (!atalho) { anterior.current?.focus(); return; }
    setChip(null);
    anterior.current = document.activeElement as HTMLElement;
    setRascunho(contexto);
    setPesquisa('');
    setDigitou(false);
    focarSemTeclado(painel.current?.querySelector('input'));
  }, [atalho, contexto]);
  const campo = atalho === '/' ? 'fluxo' : atalho === '#' ? 'item' : 'repositorio';
  const rotulo = atalho === '/' ? 'Fluxo, projeto ou design' : atalho === '#' ? 'Work item' : 'Repositório';
  const filtrando = atalho === '#' || atalho === '@';
  const filtro = (filtrando ? pesquisa : atalho ? rascunho[campo] : '').trim().toLowerCase();
  const todas = atalho ? opcoesDoAtalho(atalho, catalogo) : [];
  // Cada palavra pode aparecer em qualquer parte do título/origem, sem perder o valor exato da opção.
  const termos = filtrando ? filtro.split(/\s+/).filter(Boolean) : [filtro];
  const opcoes = todas.filter(o => termos.every(t => [o.valor, ...o.busca].some(b => b.toLowerCase().includes(t))));
  const escrito = filtrando ? pesquisa : rascunho[campo];
  const pesquisaDeTitulo = atalho === '#' && !!pesquisa.trim() && !/^\d+$/.test(pesquisa.trim());
  const fluxo = fluxoEscolhido(contexto, catalogo);
  function salvar() {
    if (pesquisaDeTitulo) return;
    if (atalho === '/') {
      const nome = rascunho.fluxo.trim();
      const doCatalogo = nome === contexto.fluxo ? fluxo ?? undefined : catalogo?.fluxos?.find(f => f.nome.trim().toLowerCase() === nome.toLowerCase());
      alterar({ ...comFluxo(rascunho, doCatalogo, nome), ...(nome === contexto.fluxo ? { agente: contexto.agente, parametros: contexto.parametros } : {}),
        branch: rascunho.branch.trim() });
    } else alterar({ ...rascunho, [campo]: digitou ? pesquisa.trim() : rascunho[campo].trim(), branch: rascunho.branch.trim() });
    abrir(null);
  }
  function escolher(opcao: Opcao) {
    // O branch continua opcional; no toque a seleção não abre o teclado sozinha.
    if (atalho === '@') {
      setRascunho({ ...rascunho, repositorio: opcao.valor });
      setPesquisa('');
      setDigitou(false);
      focarSemTeclado(painel.current?.querySelector<HTMLInputElement>('input[aria-label="Branch existente"]'));
      return;
    }
    if (atalho === '/') alterar({ ...comFluxo(rascunho, opcao.fluxo, opcao.valor), branch: rascunho.branch.trim() });
    else alterar({ ...rascunho, [campo]: opcao.valor, branch: rascunho.branch.trim() });
    abrir(null);
  }
  function abrirChip(nome: string) { abrir(null); setChip(chip === nome ? null : nome); }
  return <>
    <button type="button" disabled={desabilitado} aria-label="Trocar fluxo" aria-expanded={atalho === '/'} onClick={() => abrir(atalho === '/' ? null : '/')}>
      / {contexto.fluxo || 'Fluxo simples · padrão'}
    </button>
    {fluxo && <>
      {/* O MODELO vem primeiro, já com o agente padrão do fluxo; depois, um chip por parâmetro (correções do board remoto). */}
      <button type="button" className="br-chip-fluxo" disabled={desabilitado} aria-label="Modelo do fluxo" aria-expanded={chip === 'modelo'} onClick={() => abrirChip('modelo')}>
        ⚙ {contexto.agente || 'modelo padrão'}
      </button>
      {(fluxo.parametros ?? []).map(p => {
        const valor = (contexto.parametros[p.nome] ?? '').trim();
        const falta = !valor && p.obrigatorio !== false;
        return <button key={p.nome} type="button" className={`br-chip-fluxo${falta ? ' br-chip-falta' : ''}`} disabled={desabilitado}
          aria-label={`Parâmetro ${rotuloDoParametro(p)}`} aria-expanded={chip === p.nome} onClick={() => abrirChip(p.nome)}>
          {rotuloDoParametro(p)}: {valor || (falta ? 'obrigatório' : 'opcional')}
        </button>;
      })}
    </>}
    <button type="button" disabled={desabilitado} aria-label="# Work item" aria-expanded={atalho === '#'} onClick={() => abrir(atalho === '#' ? null : '#')}>
      # {contexto.item || 'Work item'}
    </button>
    <button type="button" disabled={desabilitado} aria-label="@ Repositório" aria-expanded={atalho === '@'} title={contexto.repositorio || undefined}
      onClick={() => abrir(atalho === '@' ? null : '@')}>
      @ {contexto.repositorio ? nomeDoRepositorio(contexto.repositorio) : 'Repositório'} ▾
    </button>
    {atalho && <div className={`br-contexto${filtrando ? ' br-contexto-filtro' : ''}`} role="dialog" aria-label={rotulo} ref={painel} onKeyDown={e => {
      if (e.key === 'Escape') { e.stopPropagation(); abrir(null); }
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        const botoes = Array.from(painel.current?.querySelectorAll<HTMLButtonElement>('.br-opcao') ?? []);
        if (botoes.length && (e.target === painel.current?.querySelector('input') || botoes.includes(e.target as HTMLButtonElement))) {
          e.preventDefault();
          const i = botoes.indexOf(e.target as HTMLButtonElement);
          botoes[(i + (e.key === 'ArrowDown' ? 1 : i < 0 ? 0 : -1) + botoes.length) % botoes.length].focus();
        }
      }
      // O Enter num botão (uma opção do catálogo, o Cancelar) é o clique dele; nos campos, aplica o contexto.
      if (e.key === 'Enter' && !(e.target instanceof HTMLButtonElement)) { e.preventDefault(); e.stopPropagation(); salvar(); }
    }}>
      {filtrando && <strong>{rotulo}</strong>}
      <label>{atalho === '#' ? 'Filtrar por ID ou título' : atalho === '@' ? 'Filtrar por nome ou origem' : rotulo}
        <input aria-label={rotulo} value={escrito} maxLength={atalho === '@' ? 2000 : 200}
          title={atalho === '#' ? 'Pesquise pelo ID ou título e selecione um resultado. Um ID numérico também pode ser aplicado diretamente.' : 'Busque no catálogo ou digite o valor completo.'}
          placeholder={atalho === '#' ? 'ID ou título do work item' : catalogo ? 'Buscar no catálogo ou digitar' : undefined} inputMode="text"
          onChange={e => {
            const valor = e.target.value;
            setPesquisa(valor);
            setDigitou(true);
            if (!filtrando) setRascunho({ ...rascunho, [campo]: valor });
          }} /></label>
      {filtrando && rascunho[campo] && <p className="br-contexto-selecionado">Selecionado: {rascunho[campo]}</p>}
      {filtrando && <span className="br-oculto" role="status">{opcoes.length} resultado(s) no catálogo</span>}
      {!!opcoes.length && <div className="br-opcoes" role="group" aria-label={`Do catálogo do dev.kit: ${nomeDaLista[atalho]}`}>
        {opcoes.slice(0, 50).map((o, i) => <div key={`${o.grupo}-${o.valor}`}>
          {(i === 0 || opcoes[i - 1].grupo !== o.grupo) && <small className="br-opcoes-grupo">{o.grupo}</small>}
          <button type="button" title={o.valor} aria-pressed={rascunho[campo] === o.valor}
            className={rascunho[campo] === o.valor ? 'br-opcao br-opcao-escolhida' : 'br-opcao'} onClick={() => escolher(o)}>
            <span>{o.rotulo}</span>{o.detalhe && <>{' '}<small>{o.detalhe}</small></>}
            {atalho === '@' && <small>{o.valor}</small>}
          </button>
        </div>)}
      </div>}
      {!catalogo && <p>Sem catálogo do dev.kit desta máquina: digite o valor, ou use ↻ com o dev.kit aberto.</p>}
      {catalogo && !todas.length && <p>O catálogo não tem {nomeDaLista[atalho]}: digite o valor.</p>}
      {catalogo && !!todas.length && !opcoes.length && <p>Nada no catálogo com “{escrito.trim()}”.{atalho !== '#' && ' O texto pode ser aplicado como está.'}</p>}
      {pesquisaDeTitulo && <p>Selecione um resultado ou digite somente o ID para aplicar.</p>}
      {atalho === '@' && <label>Branch existente (opcional)<input aria-label="Branch existente" title="Opcional: deixe vazio para o dev.kit resolver pela convenção." maxLength={250} value={rascunho.branch}
        onChange={e => setRascunho({ ...rascunho, branch: e.target.value })} /></label>}
      <p>Informe o contexto do dev.kit. Deixe vazio para usar a conversa atual.</p>
      <button type="button" disabled={pesquisaDeTitulo} title="Confirma o valor selecionado ou digitado para esta mensagem." onClick={salvar}>Aplicar contexto</button>
      {filtrando && rascunho[campo] && <button type="button" title="Limpa a seleção deste campo; confirme em Aplicar contexto."
        onClick={() => { setPesquisa(''); setDigitou(false); setRascunho({ ...rascunho, [campo]: '' }); }}>Limpar seleção</button>}
      <button type="button" onClick={() => abrir(null)}>Cancelar</button>
    </div>}
    {chip && fluxo && <PainelDoFluxo key={chip} chip={chip} fluxo={fluxo} contexto={contexto} catalogo={catalogo}
      alterar={c => { alterar(c); setChip(null); }} fechar={() => setChip(null)} />}
  </>;
}

/**
 * O painel de um chip do fluxo: o MODELO (os agentes da máquina, o padrão do fluxo primeiro) ou um PARÂMETRO — a lista com as
 * opções, o sim/não, o usuário do board com busca (como o work item) e o texto com as sugestões.
 */
function PainelDoFluxo({ chip, fluxo, contexto, catalogo, alterar, fechar }: {
  chip: string; fluxo: FluxoDoCatalogo; contexto: ContextoDoChat; catalogo: CatalogoRemoto | null;
  alterar: (contexto: ContextoDoChat) => void; fechar: () => void;
}) {
  const parametro = chip === 'modelo' ? null : (fluxo.parametros ?? []).find(p => p.nome === chip) ?? null;
  const atual = parametro ? contexto.parametros[parametro.nome] ?? '' : contexto.agente;
  const [valor, setValor] = useState(atual);
  // A lista filtra pelo que o dev DIGITA: aberta, ela mostra todas, com a atual marcada.
  const [buscou, setBuscou] = useState(false);
  const painel = useRef<HTMLDivElement>(null);
  useEffect(() => { focarSemTeclado(painel.current?.querySelector('input')); }, []);
  const rotulo = parametro ? rotuloDoParametro(parametro) : 'Modelo';
  const fechada = parametro?.tipo === 'lista' || parametro?.tipo === 'sim-nao';
  const todas = !parametro
    ? [...new Set([fluxo.agentePadrao, ...(catalogo?.agentes ?? [])].filter((a): a is string => !!a))]
    : parametro.tipo === 'sim-nao' ? ['sim', 'não']
      : parametro.usuario ? catalogo?.membros ?? [] : parametro.opcoes ?? [];
  const filtro = fechada || !buscou ? '' : valor.trim().toLowerCase();
  const opcoes = todas.filter(o => !filtro || o.toLowerCase().includes(filtro));
  function aplicar(novo: string) {
    alterar(parametro ? { ...contexto, parametros: { ...contexto.parametros, [parametro.nome]: novo.trim() } } : { ...contexto, agente: novo.trim() });
  }
  return <div className="br-contexto" role="dialog" aria-label={rotulo} ref={painel} onKeyDown={e => {
    if (e.key === 'Escape') { e.stopPropagation(); fechar(); }
    if (e.key === 'Enter' && !(e.target instanceof HTMLButtonElement)) { e.preventDefault(); e.stopPropagation(); if (!fechada) aplicar(valor); }
  }}>
    {!fechada && <label>{rotulo}<input aria-label={rotulo} value={valor} maxLength={parametro ? 500 : 100}
      placeholder={parametro?.usuario ? 'Buscar membro do time' : parametro ? 'Digite o valor' : 'cli ou cli:modelo'}
      onChange={e => { setValor(e.target.value); setBuscou(true); }} /></label>}
    {fechada && <p><b>{rotulo}</b>{parametro?.obrigatorio === false ? ' · opcional' : ' · obrigatório'}</p>}
    {!!opcoes.length && <div className="br-opcoes" role="group" aria-label={parametro?.usuario ? 'Membros do time' : parametro ? `Opções de ${rotulo}` : 'Agentes da máquina'}>
      {opcoes.slice(0, 50).map(o => <button key={o} type="button" className={atual === o ? 'br-opcao br-opcao-escolhida' : 'br-opcao'} onClick={() => aplicar(o)}>
        <span>{o}</span>{!parametro && o === fluxo.agentePadrao && <>{' '}<small>padrão do fluxo</small></>}
      </button>)}
    </div>}
    {!todas.length && <p>{parametro?.usuario ? 'O catálogo não trouxe os membros do time: digite o nome.' : !parametro ? 'O catálogo não trouxe os agentes da máquina: digite cli ou cli:modelo.' : 'Digite o valor.'}</p>}
    {!fechada && <button type="button" onClick={() => aplicar(valor)}>Aplicar</button>}
    {!!atual && <button type="button" onClick={() => aplicar('')}>Limpar</button>}
    <button type="button" onClick={fechar}>Cancelar</button>
  </div>;
}

function hora(data: string) { return new Date(data).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }); }
function quando(data: string) {
  return new Date(data).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
}
function idade(data: string, agora: number) {
  const minutos = Math.max(0, Math.floor((agora - Date.parse(data)) / 60_000));
  if (minutos < 1) return 'agora há pouco';
  if (minutos < 60) return `há ${minutos} min`;
  if (minutos < 48 * 60) return `há ${Math.floor(minutos / 60)} h`;
  return `há ${Math.floor(minutos / 1440)} dias`;
}

/**
 * O catálogo no CABEÇALHO do Orquestrador (complemento do ajuste 5, 19:01): o ↻ ao lado do Reiniciar pede o catálogo (o
 * handshake), e a idade dele vai na dica — nada de faixa embaixo da caixa. O estado fica também para o leitor de tela.
 */
export function EstadoDoCatalogo({ estado, appAberto, agora }: { estado: CatalogoDoChat; appAberto: boolean; agora: number }) {
  const { catalogo, carregado, pedido, atualizar, podeAtualizar } = estado;
  const atualizando = pedido.estado === 'enviando' || pedido.estado === 'aguardando';
  const completo = !carregado ? 'Carregando o catálogo do dev.kit…'
    : catalogo ? `Catálogo do dev.kit · atualizado ${idade(catalogo.recebidoEm, agora)} (${quando(catalogo.recebidoEm)})${appAberto ? '' : ' · dev.kit fechado: último catálogo recebido'}`
    : 'Atalhos sem catálogo: o dev.kit desta máquina ainda não enviou work items, repositórios e fluxos.';
  const aguardando = pedido.estado === 'aguardando' ? ` · atualização pedida às ${hora(pedido.desde)}${appAberto ? ', aguardando o dev.kit' : ': o dev.kit responde quando abrir'}` : '';
  return <>
    <button type="button" className={`br-btn br-icone-cabecalho${atualizando ? ' br-girando' : ''}`} disabled={!podeAtualizar} aria-label="Atualizar catálogo do dev.kit"
      title={`${completo}${aguardando}. ${podeAtualizar ? 'Clique para pedir ao dev.kit os work items, repositórios, fluxos, projetos e designs de agora.' : 'Atualizar fica disponível com a máquina conectada e sem outro pedido em curso.'}`}
      onClick={() => void atualizar()}><span aria-hidden="true">↻</span></button>
    <span className="br-oculto" role="status">
      {!carregado ? 'catálogo…' : catalogo ? <time dateTime={catalogo.recebidoEm}>catálogo {idade(catalogo.recebidoEm, agora)}</time> : 'sem catálogo'}{aguardando}
    </span>
  </>;
}

/** Os avisos do catálogo (a leitura que falhou, o pedido recusado ou vencido), abaixo do compositor. */
export function AvisosDoCatalogo({ estado }: { estado: CatalogoDoChat }) {
  const { erro, falhaNoPedido, pedido } = estado;
  return <>
    {erro && <p className="br-catalogo-aviso" role="alert">{erro}</p>}
    {falhaNoPedido && <p className="br-catalogo-aviso" role="alert">{falhaNoPedido}</p>}
    {(pedido.estado === 'recusado' || pedido.estado === 'vencido' || pedido.estado === 'falhou') && <p className="br-catalogo-aviso" role="alert">
      O catálogo não foi atualizado: {pedido.motivo}.{pedido.motivo.includes('não é conhecido') && ' Atualize o dev.kit desta máquina; enquanto isso, digite o contexto nos atalhos.'}
    </p>}
  </>;
}
