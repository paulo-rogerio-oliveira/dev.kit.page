import { useEffect, useRef, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { DecisaoPendente, PendenciaRemota, ResponderDecisao, RespostaDaDecisao } from '../api/remoto';
import { useSessao } from '../sessao';
import './DecisoesRemotas.css';

export type AoDecidir = (decisao: DecisaoPendente, pedido: ResponderDecisao) => Promise<RespostaDaDecisao>;

export function RegistroDaDecisao({ resposta }: { resposta: RespostaDaDecisao }) {
  return <p role="status" className="br-note br-registro">{resposta.acao === 'aprovar' ? 'Aprovação' : resposta.acao === 'autorizar' ? 'Autorização' : resposta.acao === 'ajuste' ? 'Pedido de ajuste' : `Resposta: ${resposta.opcao}`} registrada em {new Date(resposta.em).toLocaleString('pt-BR')} · {resposta.estado === 'recebida' ? 'recebida pela máquina' : resposta.estado === 'vencida' ? 'autorização vencida' : 'aguardando a máquina'} · {resposta.segundoFator ? 'segundo fator confirmado' : 'resposta auditada'}{resposta.dispositivo ? ` · pelo ${resposta.dispositivo}` : ''}. {resposta.acao === 'ajuste' && 'Nenhum ato foi autorizado. '}{resposta.sha && `SHA ${resposta.sha}`}</p>;
}

/** O projeto e o item da pendência, e o tipo dela: a linha do cartão recolhido do painel. */
export function OrigemDaPendencia({ pendencia: p }: { pendencia: PendenciaRemota }) {
  const origem = [p.projeto, p.item ? `item ${p.item}` : ''].filter(Boolean).join(' · ');
  const tipo = p.tipo === 'aprovacao' ? 'Aprovação' : p.tipo === 'autorizacao' ? 'Autorização' : 'Pergunta';
  return <p className="br-origem">{origem && <span>{origem}</span>}<span className={`br-tipo${p.tipo === 'aprovacao' || p.tipo === 'autorizacao' ? ' br-tipo-aprovacao' : ''}`}>{tipo}</span></p>;
}

/** (ajuste 9) O ato da autorização na palavra do dev. */
const nomeDoAto: Record<string, string> = {
  'concluir-projeto': 'Concluir o projeto', 'integrar-item': 'Integrar o item', 'arquivar-tasks': 'Arquivar as tasks', 'executar-comando': 'Executar o comando',
};

/** (ajuste 9) O escopo EXATO que o segundo fator autoriza: o ato, o alvo, o SHA, os itens e o comando. */
export function EscopoDaAutorizacao({ pendencia: p }: { pendencia: PendenciaRemota }) {
  const e = p.escopo;
  if (!e) return null;
  return <dl className="br-card br-snapshot br-escopo" aria-label="Escopo da autorização">
    <dt>Ato</dt><dd>{nomeDoAto[e.ato] ?? e.ato}</dd>
    <dt>Alvo</dt><dd>{e.alvo}</dd>
    {e.sha && <><dt>SHA</dt><dd><code>{e.sha}</code></dd></>}
    {!!e.itens?.length && <><dt>Itens</dt><dd>{e.itens.join(', ')}</dd></>}
    {e.comando && <><dt>Comando</dt><dd><code>{e.comando}</code></dd></>}
  </dl>;
}

/**
 * (correções do board remoto) O × que fecha o card no SITE — ou o Restaurar, no filtro "Dispensadas". Não decide nada e não
 * vai à máquina: a task continua como está.
 */
export type AoDispensar = (pendencia: PendenciaRemota, dispensar: boolean) => Promise<void>;
export function BotaoDeDispensar({ pendencia, dispensada, dispensar }: { pendencia: PendenciaRemota; dispensada: boolean; dispensar: AoDispensar }) {
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState('');
  async function clicar() {
    setEnviando(true); setErro('');
    try { await dispensar(pendencia, !dispensada); } catch (e) { setErro(mensagemDaFalha(e)); } finally { setEnviando(false); }
  }
  return <>
    {dispensada
      ? <button type="button" className="br-btn br-restaurar" disabled={enviando} onClick={() => void clicar()}>Restaurar</button>
      : <button type="button" className="br-fechar" disabled={enviando} aria-label={`Dispensar ${pendencia.titulo}`}
        title="Fechar este card no site (a task no dev.kit não muda)" onClick={() => void clicar()}>×</button>}
    {erro && <p role="alert" className="br-note">{erro}</p>}
  </>;
}

/** O Ver detalhes do cartão recolhido (US #858): o painel mostra o título e a ação principal, e o resto sob pedido. */
export function BotaoDeDetalhes({ aberto, alternar, controla, principal = false }: { aberto: boolean; alternar: () => void; controla: string; principal?: boolean }) {
  return <button className={`br-btn${principal && !aberto ? ' br-primary' : ''}`} aria-expanded={aberto} aria-controls={controla} onClick={alternar}>{aberto ? 'Ocultar detalhes' : 'Ver detalhes'}</button>;
}

/**
 * O cartão da decisão no PAINEL de decisões, o único lugar dela (remote-control-v3, item 3: o chat não renderiza aprovação).
 * A aprovação abre a revisão, com o segundo fator e o SHA conferido; a pergunta responde com a opção, auditada.
 */
export function CartaoDaDecisao({ decisao: d, destacada = false, desabilitado, decidir, revisar, dispensar }: {
  decisao: DecisaoPendente; destacada?: boolean; desabilitado: boolean; decidir: AoDecidir; revisar: (d: DecisaoPendente) => void;
  /** O × do card (correções do board remoto); ausente, o card não fecha. */
  dispensar?: AoDispensar;
}) {
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState('');
  const [resposta, setResposta] = useState<RespostaDaDecisao | null>(null);
  // No painel o cartão nasce recolhido; a notificação que aponta para ele o abre.
  const [aberto, setAberto] = useState(destacada);
  useEffect(() => { if (destacada) setAberto(true); }, [destacada]);
  const p = d.pendencia;
  const idDoCartao = `board-decisao-${encodeURIComponent(p.id)}`;
  async function responder(opcao: string) {
    if (enviando) return;
    setEnviando(true); setErro('');
    try { setResposta(await decidir(d, { snapshotId: d.snapshotId, acao: 'responder', opcao })); }
    catch (e) { setErro(mensagemDaFalha(e)); }
    finally { setEnviando(false); }
  }
  const registro = resposta && (!d.resposta || resposta.id > d.resposta.id) ? resposta : d.resposta;
  const snapshot = p.snapshot;
  const resumo = p.tipo === 'aprovacao' && snapshot
    ? `${snapshot.notas.length ? `Nota ${Math.min(...snapshot.notas.map(n => n.nota))}` : 'Aguardando notas'} · ${!snapshot.regressaoConcluida ? 'regressão pendente' : snapshot.testesFalhos ? `regressão com ${snapshot.testesFalhos} falhas` : 'regressão sem falhas'}`
    : p.jaTentei;
  const pendente = !registro || registro.estado === 'vencida';
  const detalhes = `${idDoCartao}-detalhes`;
  // (ajuste 9) A autorização decide como a aprovação: na revisão, com o escopo exato e o segundo fator.
  const comRevisao = p.tipo === 'aprovacao' || p.tipo === 'autorizacao';
  return <section id={idDoCartao} tabIndex={-1} className={`br-card br-decisao-card br-need br-compacta${d.dispensada ? ' br-dispensada' : ''}`} aria-label={p.titulo}>
    <div className="br-decisao-titulo"><b>{p.titulo}</b>{dispensar && <BotaoDeDispensar pendencia={p} dispensada={!!d.dispensada} dispensar={dispensar} />}</div>
    <OrigemDaPendencia pendencia={p} />
    {p.tipo === 'aprovacao' && resumo && <p>{resumo}</p>}
    {registro && <RegistroDaDecisao resposta={registro} />}
    <div id={detalhes} className="br-detalhes">{aberto && <>
      {p.pergunta && <p>{p.pergunta}</p>}
      {p.tipo !== 'aprovacao' && resumo && <p>{resumo}</p>}
      {pendente && <>
        {registro?.estado === 'vencida' && <p>Revise novamente e registre uma nova decisão.</p>}
        {p.tipo === 'pergunta' && <div className="br-row">{p.opcoes?.map((opcao, i) => <button key={opcao} className={`br-btn${i === 0 ? ' br-primary' : ''}`} disabled={desabilitado || enviando || !!d.bloqueio} onClick={() => void responder(opcao)}>{opcao}</button>)}</div>}
        <small>{p.tipo === 'pergunta' ? 'Login confirmado · sua resposta será auditada.' : 'Ato irreversível · exige segundo fator e SHA conferido.'}</small>
        {d.bloqueio && <p role="status">{d.bloqueio}</p>}
      </>}
    </>}</div>
    <div className="br-row">
      <BotaoDeDetalhes aberto={aberto} alternar={() => setAberto(a => !a)} controla={detalhes} principal={!comRevisao && pendente} />
      {comRevisao && pendente && <button className="br-btn br-primary" disabled={desabilitado} onClick={() => revisar(d)}>{p.tipo === 'autorizacao' ? 'Autorizar' : 'Decidir'}</button>}
    </div>
    {erro && <p role="alert">{erro}</p>}
  </section>;
}

function Captura({ titulo, url }: { titulo: string; url: string }) {
  const { sessao } = useSessao();
  const [imagem, setImagem] = useState('');
  const [erro, setErro] = useState(false);
  useEffect(() => {
    const ctrl = new AbortController(); let blob = '';
    setImagem(''); setErro(false);
    if (url.startsWith('/api/design/') && !url.includes('..') && !url.includes('\\') && sessao) {
      void api.capturaDaDecisao(sessao.token, url, ctrl.signal).then(b => { if (!ctrl.signal.aborted) { blob = URL.createObjectURL(b); setImagem(blob); } }).catch(() => { if (!ctrl.signal.aborted) setErro(true); });
    } else {
      try { const u = new URL(url); if (u.protocol === 'https:' && !u.username && !u.password) setImagem(url); else setErro(true); } catch { setErro(true); }
    }
    return () => { ctrl.abort(); if (blob) URL.revokeObjectURL(blob); };
  }, [url, sessao]);
  return <figure>{imagem && !erro ? <a href={imagem} target="_blank" rel="noreferrer"><img src={imagem} alt={titulo} onError={() => setErro(true)} /></a> : <p>{erro ? 'Captura indisponível. Consulte a entrega antes de aprovar.' : 'Carregando captura…'}</p>}<figcaption>{titulo} · ampliar</figcaption></figure>;
}

export function RevisaoDaDecisao({ decisao: d, decidir, voltar, desabilitado }: { decisao: DecisaoPendente; decidir: AoDecidir; voltar: () => void; desabilitado: boolean }) {
  const { sessao, sair } = useSessao();
  const [confirmar, setConfirmar] = useState(false);
  const [ativo, setAtivo] = useState<boolean | null>(null);
  const [senha, setSenha] = useState('');
  const [chave, setChave] = useState('');
  const [codigo, setCodigo] = useState('');
  const [erro, setErro] = useState('');
  const [ocupado, setOcupado] = useState(false);
  const [resposta, setResposta] = useState(d.resposta?.estado === 'vencida' ? null : d.resposta);
  const caixa = useRef<HTMLInputElement>(null);
  const p = d.pendencia, s = p.snapshot;
  useEffect(() => {
    const ctrl = new AbortController();
    if (sessao) void api.estadoDoSegundoFator(sessao.token, ctrl.signal).then(r => { if (!ctrl.signal.aborted) setAtivo(r.ativo); }).catch(e => { if (!ctrl.signal.aborted) setErro(mensagemDaFalha(e)); });
    return () => ctrl.abort();
  }, [sessao]);
  useEffect(() => { if (confirmar && ativo) caixa.current?.focus(); }, [confirmar, ativo]);
  async function executar(acao: () => Promise<void>) {
    if (ocupado) return;
    setOcupado(true); setErro('');
    try { await acao(); } catch (e) { if (e instanceof ErroDaApi && e.status === 401) sair('expirou'); else setErro(mensagemDaFalha(e)); }
    finally { setCodigo(''); setOcupado(false); }
  }
  const bloqueado = desabilitado || ocupado || !!d.bloqueio || !!resposta;
  // (ajuste 9) A autorização revisa o ESCOPO exato no lugar do snapshot, com o mesmo segundo fator.
  const autorizacao = p.tipo === 'autorizacao';
  return <main className="br-review" aria-label="Revisão da decisão" data-confirmar={confirmar}>
    <button className="br-voltar" onClick={voltar}>‹ Voltar ao Board</button>
    <header className="br-review-head"><h1>{p.titulo}</h1><p>Decisões no site e segundo fator</p><span className="br-chip">Login confirmado</span> <span className="br-chip">Ato irreversível · segundo fator</span></header>
    <div className="br-review-grid"><div>
      {autorizacao ? <><EscopoDaAutorizacao pendencia={p} /><p className="br-review-limite"><strong>Vale só para este escopo.</strong> O dev.kit confere o pedido de agora antes de executar; se ele mudar, autorize de novo.</p></> : s ? <><dl className="br-card br-snapshot">
        <dt>Nota</dt><dd>{s.notas.length ? s.notas.map(n => <div key={n.avaliador}><mark>{n.nota} de 100</mark> · mínima {n.minima} · {n.avaliador}</div>) : 'Aguardando avaliadores'}</dd>
        <dt>Regressão</dt><dd>{s.regressaoConcluida ? `${s.testesAprovados} aprovados, ${s.testesFalhos} falhas` : 'Pendente'}</dd>
        <dt>Alterações</dt><dd>{s.arquivos} arquivos em {s.commits} commits · <a href="#alteracoes-decisao" onClick={e => { const detalhes = e.currentTarget.closest('main')?.querySelector('details'); if (detalhes) detalhes.open = true; }}>Ver alterações</a></dd>
        <dt>Origem</dt><dd><code>{s.origem}</code></dd><dt>Destino</dt><dd><code>{s.destino}</code></dd><dt>SHA</dt><dd><code>{s.sha}</code></dd>
      </dl><h2>Capturas</h2><div className="br-capturas">{s.capturas.map(c => <Captura key={c.url} {...c} />)}</div>{!s.capturas.length && <p className="br-note">Nenhuma captura recebida neste snapshot.</p>}
      <p className="br-review-limite"><strong>Vale só para este SHA e este destino.</strong> Não autoriza PR, merge na semana nem fechamento do projeto. Se o snapshot mudar, revise novamente.</p>
      <details id="alteracoes-decisao" className="br-card"><summary>Alterações do snapshot</summary><pre>{s.alteracoes}</pre></details></> : <p className="br-card">A máquina ainda não enviou as evidências deste snapshot.</p>}
      <div className="br-card br-review-info">{autorizacao ? <><strong>Confira o escopo antes de autorizar</strong><p>A autorização vale só para o ato, o alvo e o comando apresentados. Mensagens enviadas pelo chat não autorizam este ato.</p></> : <><strong>Confira as evidências antes de aprovar</strong><p>A aprovação fica vinculada à origem, ao destino e ao SHA apresentados. Mensagens enviadas pelo chat não autorizam este ato.</p></>}</div>
    </div><aside className="br-review-actions">
      {resposta ? <div className="br-card"><RegistroDaDecisao resposta={resposta} /></div> : <>
        <div className="br-review-controls"><section className="br-card br-confirm-panel"><h2>{confirmar ? 'Confirme com segundo fator' : autorizacao ? 'Autorizar este pedido' : 'Aprovar este snapshot'}</h2>
          {!confirmar && <><p>O próximo passo pede o segundo fator. Nenhum ato será autorizado pelo texto da conversa.</p><p className="br-etapas">1. Revisar evidências → 2. Confirmar identidade</p></>}
          {confirmar && ativo === null && <p role="status">Conferindo autenticador…</p>}
          {confirmar && ativo === false && <form onSubmit={e => { e.preventDefault(); void executar(async () => {
            if (!sessao) return;
            if (!chave) { const r = await api.prepararSegundoFator(sessao.token, senha); setChave(r.chave); setSenha(''); }
            else { await api.confirmarSegundoFator(sessao.token, codigo); setChave(''); setAtivo(true); }
          }); }}>
            <p>Configure seu aplicativo autenticador para confirmar decisões.</p>
            {!chave ? <label>Senha atual<input type="password" autoComplete="current-password" value={senha} onChange={e => setSenha(e.target.value)} /></label> : <><p>Adicione esta chave ao autenticador (TOTP, seis dígitos, 30 segundos). Ela expira para configuração em dez minutos.</p><code className="br-chave">{chave}</code><label>Código de 6 dígitos<input inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={codigo} onChange={e => setCodigo(e.target.value.replace(/\D/g, ''))} /></label></>}
            <button className="br-btn" disabled={ocupado || (chave ? codigo.length !== 6 : !senha)}>{chave ? 'Ativar autenticador' : 'Configurar autenticador'}</button>
          </form>}
          {confirmar && ativo && <><p>Use o código do seu aplicativo autenticador para autorizar este SHA.</p><label>Código de 6 dígitos<input ref={caixa} className="br-codigo" aria-label="Código de 6 dígitos" placeholder="○○○○○○" inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={codigo} onChange={e => setCodigo(e.target.value.replace(/\D/g, ''))} /></label><p>Aprovação registrada com data, origem e snapshot.</p></>}
        </section>
          {confirmar && <p className="br-confirmar-limite"><strong>Vale só para este SHA e este destino.</strong> Não autoriza PR, merge na semana nem fechamento do projeto. Se o snapshot mudar, revise novamente.</p>}
          {d.bloqueio && <p className="br-decisao-bloqueio" role="alert">{d.bloqueio}</p>}
          {!resposta && d.resposta?.estado === 'vencida' && <p className="br-decisao-bloqueio" role="status">A autorização anterior venceu. Confira as evidências e confirme uma nova decisão com um código novo.</p>}
          {erro && <p className="br-decisao-erro" role="alert">{erro}</p>}
          <div className="br-review-buttons"><button className="br-btn br-primary" disabled={bloqueado || (confirmar && (!ativo || codigo.length !== 6))} onClick={() => {
            if (!confirmar) setConfirmar(true);
            else void executar(async () => setResposta(await decidir(d, { snapshotId: d.snapshotId, acao: autorizacao ? 'autorizar' : 'aprovar', codigo })));
          }}>{autorizacao ? (confirmar ? 'Confirmar e autorizar' : 'Autorizar com segundo fator') : confirmar ? 'Confirmar e aprovar este SHA' : 'Aprovar com segundo fator'}</button>
          <button className="br-btn" disabled={desabilitado || ocupado} onClick={() => void executar(async () => setResposta(await decidir(d, { snapshotId: d.snapshotId, acao: 'ajuste' })))}>{autorizacao ? 'Recusar' : 'Pedir ajuste'}</button></div>
        </div>
        <section className="br-card br-review-info"><strong>Quando a decisão não pode seguir</strong><p>Nota insuficiente, regressão com falhas ou snapshot alterado bloqueiam a aprovação e pedem nova revisão.</p></section>
        <p>Decisões comuns usam os botões da pergunta, com login e auditoria. Este ato exige confirmação adicional.</p>
      </>}
    </aside></div>
  </main>;
}
