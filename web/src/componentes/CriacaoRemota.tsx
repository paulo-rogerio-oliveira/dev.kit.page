import { useEffect, useRef, useState, type ReactNode } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { CatalogoRemoto, ComandoRemoto, ConversaRemota, MaquinaRemota, NovoDesignRemoto, NovoProjetoRemoto, TipoDeCriacao } from '../api/remoto';
import type { UsoDoDesign } from '../api/tipos';
import type { ConversaDaMaquina } from './useConversaRemota';
import { tiposDeCriacao } from './useCatalogoRemoto';
import { useSessao } from '../sessao';

const emCurso = ['pendente', 'na-fila', 'entregue'];
/** Quanto tempo o desfecho da última criação fica à vista no painel. */
const vistaDoDesfechoMs = 15 * 60_000;

function hora(data: string) { return new Date(data).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }); }
export function rotuloDaCriacao(tipo: string) { return tipo === 'projeto' ? 'Novo projeto' : 'Novo design'; }

/** Onde a criação está, com o texto do dev.kit quando ele já respondeu (o que criou, ou o motivo da recusa). */
function situacao(comando: ComandoRemoto, conversa: ConversaRemota) {
  const resposta = conversa.respostas.filter(r => r.comandoId === comando.id).at(-1)?.texto ?? '';
  switch (comando.estado) {
    case 'pendente': case 'na-fila': return { classe: '', titulo: 'aguardando o dev.kit', texto: 'O pedido está na fila: quem cria é o dev.kit da máquina, pelo mesmo caminho do devcli.' };
    case 'entregue': return { classe: '', titulo: 'em execução no dev.kit', texto: 'O dev.kit recebeu o pedido e está criando.' };
    case 'concluido': case 'respondido': return { classe: ' br-ok', titulo: 'concluído', texto: resposta || 'Criado pelo dev.kit.' };
    // O dev.kit antigo não conhece o comando e recusa com o motivo (regra de impacto #2): o site o mostra como veio.
    case 'recusado': return { classe: ' br-recusa', titulo: 'recusado', texto: (resposta || comando.erro || 'O dev.kit recusou o pedido.').replace(/^Não executado:\s*/, '') };
    case 'expirado': return { classe: ' br-recusa', titulo: 'expirado', texto: 'O prazo venceu antes de o dev.kit receber o pedido: a máquina pode estar desligada.' };
    default: return { classe: ' br-recusa', titulo: 'falhou', texto: comando.erro || 'A fila não aceitou o pedido. Tente de novo.' };
  }
}

/**
 * (remote-control-v3, item 4) CRIAR projeto e projeto de design pelo site: o formulário vira um comando na fila, e o dev.kit
 * da máquina executa pelo mesmo caminho do devcli (`projeto --criar` e `design --repo`). Uma criação por vez; o desfecho —
 * o que foi criado, ou a recusa com o motivo — é o do dev.kit, que chega pela conversa (SSE). O site não aprova nem dispara.
 */
export function useCriacaoRemota({ maquina, conversa, agora }: { maquina: MaquinaRemota | undefined; conversa: ConversaDaMaquina; agora: number }) {
  const [aberta, setAberta] = useState<TipoDeCriacao | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [falha, setFalha] = useState('');
  const ultima = conversa.conversa.comandos.filter(c => tiposDeCriacao.includes(c.tipo))
    .sort((a, b) => Date.parse(a.criadoEm) - Date.parse(b.criadoEm)).at(-1);
  const ocupado = enviando || (!!ultima && emCurso.includes(ultima.estado) && agora < Date.parse(ultima.expiraEm));
  const motivo = !maquina ? 'Nenhuma máquina conectada.'
    : ocupado ? 'Aguardando o dev.kit terminar a criação anterior.'
      : 'O dev.kit da máquina cria pelo mesmo caminho do devcli.';

  useEffect(() => { setAberta(null); setFalha(''); }, [maquina?.maquinaId]);

  async function criar(tipo: TipoDeCriacao, formulario: NovoProjetoRemoto | NovoDesignRemoto) {
    setEnviando(true); setFalha('');
    try {
      const desfecho = await conversa.criar(tipo, formulario, crypto.randomUUID());
      // Aceito pela fila, o formulário fecha: o desfecho do dev.kit aparece no painel. A recusa da API (a antiga não conhece
      // o tipo, o formulário fora da regra) fica no formulário, com o motivo.
      if (desfecho.ok) setAberta(null);
      else if (desfecho.mensagem) setFalha(desfecho.mensagem);
    } finally { setEnviando(false); }
  }

  return {
    maquina, aberta, enviando, falha, motivo, criar,
    desabilitado: !maquina || ocupado,
    abrir: (tipo: TipoDeCriacao) => { setFalha(''); setAberta(tipo); },
    fechar: () => { setFalha(''); setAberta(null); },
    desfecho: ultima && agora - Date.parse(ultima.criadoEm) < vistaDoDesfechoMs ? { comando: ultima, ...situacao(ultima, conversa.conversa) } : null,
  };
}

export type CriacaoDoBoard = ReturnType<typeof useCriacaoRemota>;

/** A faixa do painel: onde a última criação está e o desfecho que o dev.kit mandou. */
export function DesfechoDaCriacao({ criacao }: { criacao: CriacaoDoBoard }) {
  const d = criacao.desfecho;
  if (!d) return null;
  return <div className={`br-criacao${d.classe}`} role="status" aria-label="Desfecho da criação">
    <strong>{d.comando.estado === 'concluido' ? '✓ ' : ''}{rotuloDaCriacao(d.comando.tipo)} · {hora(d.comando.criadoEm)} · {d.titulo}</strong>
    <span>{d.texto}</span>
  </div>;
}

/** O diálogo comum: o foco entra no primeiro campo, e o Escape cancela. */
function Dialogo({ titulo, id, fechar, children }: { titulo: string; id: string; fechar: () => void; children: ReactNode }) {
  const caixa = useRef<HTMLDivElement>(null);
  useEffect(() => { caixa.current?.querySelector<HTMLElement>('input, select, textarea')?.focus(); }, []);
  return <div className="br-dialogo-fundo" onKeyDown={e => { if (e.key === 'Escape') fechar(); }}>
    <div ref={caixa} className="br-dialogo br-form" role="dialog" aria-modal="true" aria-labelledby={id}>
      <h2 id={id}>{titulo}</h2>
      {children}
    </div>
  </div>;
}

function Rodape({ criacao, pronto }: { criacao: CriacaoDoBoard; pronto: boolean }) {
  return <>
    {criacao.maquina && !criacao.maquina.appAberto && <p role="status">O dev.kit está fechado nesta máquina: o pedido espera ele abrir (use Ligar dev.kit).</p>}
    {criacao.falha && <p role="alert" className="br-recusa">Não enviado. {criacao.falha}</p>}
    <div className="br-row">
      <button type="button" className="br-btn" disabled={criacao.enviando} onClick={criacao.fechar}>Cancelar</button>
      <button type="submit" className="br-btn br-perigo" disabled={!pronto || criacao.enviando}>{criacao.enviando ? 'Enviando…' : 'Criar no dev.kit'}</button>
    </div>
  </>;
}

/**
 * O formulário do NOVO PROJETO: nome, os repositórios do catálogo da máquina (o handshake do item 1), a definição e o
 * compartilhamento. Nasce em rascunho no dev.kit — o plano, a aprovação e o disparo continuam lá.
 */
export function NovoProjetoNoBoard({ criacao, catalogo, recebidoEm }: { criacao: CriacaoDoBoard; catalogo: CatalogoRemoto | null; recebidoEm: string | null }) {
  const { sessao } = useSessao();
  const comEmpresa = Boolean(sessao?.empresa);
  const repositorios = catalogo?.repositorios ?? [];
  const [nome, setNome] = useState('');
  const [escolhidos, setEscolhidos] = useState<string[]>([]);
  const [definicao, setDefinicao] = useState('');
  const [nivel, setNivel] = useState<'usuario' | 'time' | 'empresa'>(comEmpresa ? 'empresa' : 'usuario');
  const pronto = nome.trim().length > 0 && escolhidos.length > 0;
  function alternar(repositorio: string) {
    setEscolhidos(atual => atual.includes(repositorio) ? atual.filter(r => r !== repositorio) : [...atual, repositorio]);
  }
  return <Dialogo titulo="Novo projeto no dev.kit" id="br-novo-projeto" fechar={criacao.fechar}>
    <form onSubmit={e => {
      e.preventDefault();
      if (pronto) void criacao.criar('projeto', { nome: nome.trim(), repositorios: escolhidos, ...(definicao.trim() ? { definicao: definicao.trim() } : {}), nivel });
    }}>
      <p>{criacao.maquina?.nome || 'Máquina'} · o dev.kit cria pelo mesmo caminho do <code>devcli projeto --criar</code>. O projeto nasce em rascunho: o plano, a aprovação e o disparo seguem no dev.kit.</p>
      <label>Nome<input type="text" value={nome} maxLength={200} required onChange={e => setNome(e.target.value)} /></label>
      <fieldset>
        <legend>Repositórios (catálogo da máquina)</legend>
        {repositorios.map(r => <label key={r.nome} className="br-opcao">
          <input type="checkbox" checked={escolhidos.includes(r.nome)} onChange={() => alternar(r.nome)} />{r.nome}
        </label>)}
        <span className="br-dica">{repositorios.length
          ? `Catálogo de ${recebidoEm ? new Date(recebidoEm).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : '—'} · Atualizar no chat traz os repositórios novos.`
          : 'O catálogo da máquina ainda não trouxe repositórios: use Atualizar no chat (o handshake).'}</span>
      </fieldset>
      <label>Definição (opcional)<textarea value={definicao} maxLength={3000} onChange={e => setDefinicao(e.target.value)} /></label>
      <label>Compartilhamento
        <select value={nivel} onChange={e => setNivel(e.target.value as typeof nivel)}>
          <option value="usuario">Só eu</option>
          {comEmpresa && <><option value="time">Meu time</option><option value="empresa">Empresa</option></>}
        </select>
      </label>
      <Rodape criacao={criacao} pronto={pronto} />
    </form>
  </Dialogo>;
}

/**
 * O formulário do NOVO PROJETO DE DESIGN: o repositório do catálogo e o USO do teto de versões do plano (regra de impacto
 * #25). A criação não grava versão, mas com o teto cheio as versões seguintes do projeto serão recusadas no envio — o site avisa.
 */
export function NovoDesignNoBoard({ criacao, catalogo }: { criacao: CriacaoDoBoard; catalogo: CatalogoRemoto | null }) {
  const { sessao, sair } = useSessao();
  const repositorios = catalogo?.repositorios ?? [];
  const comDesign = new Set((catalogo?.designs ?? []).map(d => d.repositorio).filter(Boolean));
  const [repositorio, setRepositorio] = useState(() => (repositorios.find(r => !comDesign.has(r.nome)) ?? repositorios[0])?.nome ?? '');
  const [uso, setUso] = useState<UsoDoDesign | null>(null);
  const [falhaDoUso, setFalhaDoUso] = useState('');
  useEffect(() => {
    if (!sessao) return;
    let vivo = true;
    api.usoDoDesign(sessao.token).then(u => { if (vivo) setUso(u); }).catch(falha => {
      if (!vivo) return;
      if (falha instanceof ErroDaApi && falha.status === 401) sair('expirou');
      else setFalhaDoUso(mensagemDaFalha(falha));
    });
    return () => { vivo = false; };
  }, [sessao, sair]);
  const cheio = !!uso && uso.versoes >= uso.tetoDeVersoes;
  return <Dialogo titulo="Novo projeto de design" id="br-novo-design" fechar={criacao.fechar}>
    <form onSubmit={e => { e.preventDefault(); if (repositorio) void criacao.criar('design', { repositorio }); }}>
      <p>{criacao.maquina?.nome || 'Máquina'} · o dev.kit abre (ou cria) o projeto do repositório, como o <code>devcli design --repo</code>. Nenhuma versão é gravada nem enviada.</p>
      <label>Repositório (catálogo da máquina)
        <select value={repositorio} onChange={e => setRepositorio(e.target.value)} disabled={!repositorios.length}>
          {!repositorios.length && <option value="">—</option>}
          {repositorios.map(r => <option key={r.nome} value={r.nome}>{r.nome}{comDesign.has(r.nome) ? ' · já tem design' : ''}</option>)}
        </select>
      </label>
      {!repositorios.length && <p>O catálogo da máquina ainda não trouxe repositórios: use Atualizar no chat (o handshake).</p>}
      {uso && <div className={`br-uso${cheio ? ' br-cheio' : ''}`} role={cheio ? 'alert' : undefined}>
        <span>Teto do plano {uso.plano}: {uso.versoes} de {uso.tetoDeVersoes} versões de design.</span>
        {cheio && <span> O teto está cheio: o projeto é criado, mas as versões novas dele serão recusadas no envio até liberar espaço (apague versões antigas ou fale com o gestor).</span>}
        <div className="br-bar" role="progressbar" aria-label="Uso do teto de versões" aria-valuenow={uso.versoes} aria-valuemin={0} aria-valuemax={uso.tetoDeVersoes}>
          <i style={{ width: `${Math.min(100, uso.tetoDeVersoes ? uso.versoes / uso.tetoDeVersoes * 100 : 0)}%` }} />
        </div>
      </div>}
      {falhaDoUso && <p>Não foi possível ler o uso do teto de versões. {falhaDoUso}</p>}
      <Rodape criacao={criacao} pronto={!!repositorio} />
    </form>
  </Dialogo>;
}
