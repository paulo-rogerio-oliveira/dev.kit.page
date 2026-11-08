import { useEffect, useRef, useState } from 'react';
import type { CatalogoRemoto } from '../api/remoto';
import type { CatalogoDoChat } from './useCatalogoRemoto';

/** `modelo` (US #867): o `cli:modelo` escolhido DENTRO do fluxo, como o branch dentro do repositório; vazio é o padrão do fluxo. */
export interface ContextoDoChat { fluxo: string; modelo: string; item: string; repositorio: string; branch: string }
export const contextoVazio: ContextoDoChat = { fluxo: '', modelo: '', item: '', repositorio: '', branch: '' };
export function textoComContexto(texto: string, contexto: ContextoDoChat) {
  const linhas = [contexto.fluxo && `/ ${contexto.fluxo}`, contexto.fluxo && contexto.modelo && `Modelo do fluxo: ${contexto.modelo}`,
    contexto.item && `# ${contexto.item}`,
    contexto.repositorio && `@ ${contexto.repositorio}`, contexto.branch && `Branch existente: ${contexto.branch}`].filter(Boolean);
  return [...linhas, texto.trim()].join('\n');
}

/** O fluxo do catálogo com esse nome (só fluxo: projeto e design não têm modelo). */
function fluxoDoCatalogo(catalogo: CatalogoRemoto | null, nome: string) {
  return (catalogo?.fluxos ?? []).find(f => f.nome === nome.trim()) ?? null;
}

/** O rótulo de um `cli:modelo` pelo catálogo ("Claude · Opus"); sem ele no catálogo, o próprio valor. */
function rotuloDoModelo(catalogo: CatalogoRemoto | null, valor: string) {
  return (catalogo?.modelos ?? []).find(m => m.valor === valor)?.rotulo ?? valor;
}

type Atalho = '/' | '#' | '@';
interface Opcao { grupo: string; rotulo: string; detalhe: string; valor: string }

/**
 * As opções do atalho a partir do catálogo da máquina (o handshake): `/` os fluxos, os projetos e os projetos de design; `#` os
 * work items em andamento; `@` os repositórios. O valor é o que vai no texto ao Orquestrador.
 */
export function opcoesDoAtalho(atalho: Atalho, catalogo: CatalogoRemoto | null): Opcao[] {
  if (!catalogo) return [];
  if (atalho === '#')
    return (catalogo.workItems ?? []).map(w => ({ grupo: 'Work items em andamento', rotulo: `#${w.id} · ${w.titulo}`,
      detalhe: [w.tipo, w.estado].filter(Boolean).join(' · '), valor: String(w.id) }));
  if (atalho === '@')
    return (catalogo.repositorios ?? []).map(r => ({ grupo: 'Repositórios', rotulo: r.nome,
      detalhe: r.branchPadrao ? `branch padrão ${r.branchPadrao}` : '', valor: r.nome }));
  return [
    ...(catalogo.fluxos ?? []).map(f => ({ grupo: 'Fluxos', rotulo: f.nome, detalhe: f.descricao ?? '', valor: f.nome })),
    ...(catalogo.projetos ?? []).map(p => ({ grupo: 'Projetos', rotulo: p.nome,
      detalhe: [p.slug !== p.nome ? p.slug : '', p.situacao].filter(Boolean).join(' · '), valor: `projeto ${p.slug}` })),
    ...(catalogo.designs ?? []).map(d => ({ grupo: 'Projetos de design', rotulo: d.nome,
      detalhe: d.telas === 1 ? '1 tela' : `${d.telas} telas`, valor: `design ${d.nome}` })),
  ];
}

const nomeDaLista: Record<Atalho, string> = { '/': 'fluxo, projeto ou design', '#': 'work item em andamento', '@': 'repositório' };

export function ContextoDoChatRemoto({ contexto, alterar, atalho, abrir, desabilitado, catalogo = null }: {
  contexto: ContextoDoChat; alterar: (contexto: ContextoDoChat) => void;
  atalho: Atalho | null; abrir: (atalho: Atalho | null) => void; desabilitado: boolean; catalogo?: CatalogoRemoto | null;
}) {
  const [rascunho, setRascunho] = useState(contexto);
  const painel = useRef<HTMLDivElement>(null);
  const anterior = useRef<HTMLElement | null>(null);
  useEffect(() => {
    if (!atalho) { anterior.current?.focus(); return; }
    anterior.current = document.activeElement as HTMLElement;
    setRascunho(contexto);
    painel.current?.querySelector('input')?.focus();
  }, [atalho, contexto]);
  const campo = atalho === '/' ? 'fluxo' : atalho === '#' ? 'item' : 'repositorio';
  const rotulo = atalho === '/' ? 'Fluxo, projeto ou design' : atalho === '#' ? 'Work item' : 'Repositório';
  const filtro = atalho ? rascunho[campo].trim().toLowerCase() : '';
  const todas = atalho ? opcoesDoAtalho(atalho, catalogo) : [];
  const opcoes = todas.filter(o => !filtro || [o.rotulo, o.detalhe, o.valor].some(t => t.toLowerCase().includes(filtro)));
  // US #867: o modelo se escolhe DENTRO do fluxo (não há atalho próprio), e só com fluxo do catálogo e modelos dele.
  const modelos = catalogo?.modelos ?? [];
  const fluxoEscolhido = atalho === '/' ? fluxoDoCatalogo(catalogo, rascunho.fluxo) : null;
  const pedeModelo = !!fluxoEscolhido && !!modelos.length;
  // O modelo é do fluxo: o texto que não é um fluxo do catálogo (projeto, design, digitado) vai sem modelo.
  const modeloDo = (r: ContextoDoChat) => fluxoDoCatalogo(catalogo, r.fluxo) ? r.modelo : '';
  function salvar() {
    alterar({ ...rascunho, [campo]: rascunho[campo].trim(), branch: rascunho.branch.trim(), modelo: modeloDo(rascunho) }); abrir(null);
  }
  function escolher(opcao: Opcao) {
    // O repositório ainda pede o branch: a escolha preenche e o foco vai ao branch. Os outros aplicam já.
    if (atalho === '@') {
      setRascunho({ ...rascunho, repositorio: opcao.valor });
      painel.current?.querySelector<HTMLInputElement>('input[aria-label="Branch existente"]')?.focus();
      return;
    }
    // Outro fluxo começa no padrão dele, como outro repositório começa sem branch.
    const proximo = { ...rascunho, [campo]: opcao.valor, modelo: atalho === '/' && opcao.valor !== rascunho.fluxo ? '' : rascunho.modelo };
    // O fluxo, com modelos no catálogo, ainda pede o modelo: a escolha preenche e o foco vai ao modelo, como o branch no @.
    if (atalho === '/' && modelos.length && fluxoDoCatalogo(catalogo, opcao.valor)) {
      setRascunho(proximo);
      setTimeout(() => painel.current?.querySelector<HTMLSelectElement>('select[aria-label="Modelo do fluxo"]')?.focus());
      return;
    }
    alterar({ ...proximo, branch: rascunho.branch.trim(), modelo: modeloDo(proximo) }); abrir(null);
  }
  const padraoDoFluxo = fluxoEscolhido?.agente ? rotuloDoModelo(catalogo, fluxoEscolhido.agente) : 'o das Configurações';
  return <>
    <button type="button" disabled={desabilitado} aria-label="Trocar fluxo" aria-expanded={atalho === '/'} onClick={() => abrir(atalho === '/' ? null : '/')}>
      / {contexto.fluxo || 'Fluxo simples · padrão'}{contexto.fluxo && contexto.modelo && ` · ${rotuloDoModelo(catalogo, contexto.modelo)}`}
    </button>
    <button type="button" disabled={desabilitado} aria-label="# Work item" aria-expanded={atalho === '#'} onClick={() => abrir(atalho === '#' ? null : '#')}>
      # {contexto.item || 'Work item'}
    </button>
    <button type="button" disabled={desabilitado} aria-label="@ Repositório" aria-expanded={atalho === '@'} onClick={() => abrir(atalho === '@' ? null : '@')}>
      @ {contexto.repositorio || 'Repositório'} ▾
    </button>
    {atalho && <div className="br-contexto" role="dialog" aria-label={rotulo} ref={painel} onKeyDown={e => {
      if (e.key === 'Escape') { e.stopPropagation(); abrir(null); }
      // O Enter num botão (uma opção do catálogo, o Cancelar) é o clique dele; nos campos, aplica o contexto.
      if (e.key === 'Enter' && !(e.target instanceof HTMLButtonElement)) { e.preventDefault(); e.stopPropagation(); salvar(); }
    }}>
      <label>{rotulo}<input aria-label={rotulo} value={rascunho[campo]} maxLength={200} placeholder={catalogo ? 'Buscar no catálogo ou digitar' : undefined}
        inputMode={atalho === '#' ? 'numeric' : 'text'} pattern={atalho === '#' ? '[0-9]*' : undefined}
        onChange={e => setRascunho({ ...rascunho, [campo]: atalho === '#' ? e.target.value.replace(/\D/g, '') : e.target.value })} /></label>
      {!!opcoes.length && <div className="br-opcoes" role="group" aria-label={`Do catálogo do dev.kit: ${nomeDaLista[atalho]}`}>
        {opcoes.slice(0, 50).map((o, i) => <div key={`${o.grupo}-${o.valor}`}>
          {(i === 0 || opcoes[i - 1].grupo !== o.grupo) && <small className="br-opcoes-grupo">{o.grupo}</small>}
          <button type="button" className={rascunho[campo] === o.valor ? 'br-opcao br-opcao-escolhida' : 'br-opcao'} onClick={() => escolher(o)}>
            <span>{o.rotulo}</span>{o.detalhe && <>{' '}<small>{o.detalhe}</small></>}
          </button>
        </div>)}
      </div>}
      {!catalogo && <p>Sem catálogo do dev.kit desta máquina: digite o valor, ou use Atualizar com o dev.kit aberto.</p>}
      {catalogo && !todas.length && <p>O catálogo não tem {nomeDaLista[atalho]}: digite o valor.</p>}
      {catalogo && !!todas.length && !opcoes.length && <p>Nada no catálogo com “{rascunho[campo].trim()}”: o texto vai como está.</p>}
      {atalho === '@' && <label>Branch existente<input aria-label="Branch existente" maxLength={250} value={rascunho.branch}
        onChange={e => setRascunho({ ...rascunho, branch: e.target.value })} /></label>}
      {pedeModelo && <label>Modelo do fluxo<select aria-label="Modelo do fluxo" value={rascunho.modelo}
        onChange={e => setRascunho({ ...rascunho, modelo: e.target.value })}>
        <option value="">Padrão do fluxo · {padraoDoFluxo}</option>
        {modelos.map(m => <option key={m.valor} value={m.valor}>{m.rotulo}</option>)}
      </select></label>}
      <p>Informe o contexto do dev.kit. Deixe vazio para usar a conversa atual.</p>
      <button type="button" onClick={salvar}>Aplicar contexto</button>
      <button type="button" onClick={() => abrir(null)}>Cancelar</button>
    </div>}
  </>;
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
 * A linha do catálogo no chat: quando os atalhos foram atualizados (a data do último catálogo recebido, mostrada também com o
 * dev.kit fechado), o pedido em curso ou o motivo da recusa, e a ação Atualizar (o handshake).
 */
export function EstadoDoCatalogo({ estado, appAberto, agora }: { estado: CatalogoDoChat; appAberto: boolean; agora: number }) {
  const { catalogo, carregado, erro, falhaNoPedido, pedido, atualizar, podeAtualizar } = estado;
  const atualizando = pedido.estado === 'enviando' || pedido.estado === 'aguardando';
  return <div className="br-catalogo">
    <p role="status">
      {!carregado ? 'Carregando o catálogo do dev.kit…'
        : catalogo ? <>Catálogo do dev.kit · atualizado <time dateTime={catalogo.recebidoEm} title={new Date(catalogo.recebidoEm).toLocaleString('pt-BR')}>
          {idade(catalogo.recebidoEm, agora)} ({quando(catalogo.recebidoEm)})</time>{!appAberto && ' · dev.kit fechado: último catálogo recebido'}</>
        : 'Atalhos sem catálogo: o dev.kit desta máquina ainda não enviou work items, repositórios e fluxos.'}
      {pedido.estado === 'aguardando' && <span> · atualização pedida às {hora(pedido.desde)}{appAberto ? ', aguardando o dev.kit' : ': o dev.kit responde quando abrir'}</span>}
    </p>
    <button type="button" className="br-btn" disabled={!podeAtualizar} aria-label="Atualizar catálogo do dev.kit"
      title={podeAtualizar ? 'Pede ao dev.kit os work items, repositórios, fluxos, projetos e designs de agora' : 'Disponível com a máquina conectada e sem outro pedido em curso'}
      onClick={() => void atualizar()}>{atualizando ? 'Atualizando…' : 'Atualizar'}</button>
    {erro && <p className="br-catalogo-aviso" role="alert">{erro}</p>}
    {falhaNoPedido && <p className="br-catalogo-aviso" role="alert">{falhaNoPedido}</p>}
    {(pedido.estado === 'recusado' || pedido.estado === 'vencido' || pedido.estado === 'falhou') && <p className="br-catalogo-aviso" role="alert">
      O catálogo não foi atualizado: {pedido.motivo}.{pedido.motivo.includes('não é conhecido') && ' Atualize o dev.kit desta máquina; enquanto isso, digite o contexto nos atalhos.'}
    </p>}
  </div>;
}
