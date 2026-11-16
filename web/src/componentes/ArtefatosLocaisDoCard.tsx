import type { ArtefatoLocal, ArtefatosLocais, OperacaoDoPreviewRemoto, PreviewLocal } from '../api/artefatosLocais';
import type { MaquinaRemota } from '../api/remoto';
import './ArtefatosLocaisDoCard.css';

export interface AcoesDePreviewNoCard {
  maquina?: MaquinaRemota; atualizado?: boolean;
  operarPreview?: (pedido: OperacaoDoPreviewRemoto) => void | Promise<void>;
  consultar?: () => void;
}

/** Defesa de apresentação: URL de loopback nunca vira link no dispositivo remoto, mesmo em payload incompleto. */
function urlConfirmada(a: ArtefatoLocal, p: PreviewLocal): string | null {
  if (a.situacao !== 'pronto' || p.situacao !== 'pronto' || p.pedido !== 'iniciar' || !p.nestaMaquina
    || !p.prontidao.front || !p.prontidao.api || !p.prontidao.dependencias || !p.prontidao.jornada
    || p.componentes.length < 2 || p.componentes.some(c => c.situacao !== 'pronto' || !c.prontoEm || !c.porta)) return null;
  try {
    const u = new URL(p.url ?? '');
    return u.protocol === 'http:' && (u.hostname === '127.0.0.1' || u.hostname === '[::1]') && !!u.port
      && u.pathname === '/' && !u.username && !u.password && !u.search && !u.hash ? u.href : null;
  } catch { return null; }
}

function AplicacaoLocal({ a, p, slug, podeOperar, motivo, maquina, operarPreview, consultar }: AcoesDePreviewNoCard & {
  a: ArtefatoLocal; p: PreviewLocal; slug: string; podeOperar: boolean; motivo: string;
}) {
  const host = maquina?.nome || maquina?.maquinaId || 'host não identificado';
  const url = urlConfirmada(a, p);
  const iniciar = a.situacao === 'pronto' && ['nao-solicitado', 'parado', 'falhou', 'interrompido'].includes(p.situacao);
  const parar = p.versao > 0 && !['parado', 'nao-solicitado', 'parando'].includes(p.situacao);
  const situacao = p.situacao === 'pronto' && !url ? 'prontidão não confirmada' : p.situacao === 'nao-solicitado' ? 'não solicitado' : p.situacao;
  function pedir(operacao: OperacaoDoPreviewRemoto['operacao']) {
    if (!podeOperar || !a.conjuntoHash) return;
    void operarPreview?.({ acao: 'preview-local', projeto: slug, artefato: a.id, receita: p.receita,
      aplicacao: p.aplicacao, operacao, versao: p.versao, conjunto: a.conjuntoHash });
  }
  return <section className="br-preview-local" aria-label={`Preview local ${p.aplicacao}, geração ${a.id}`}>
    <div className="br-local-titulo"><b>Preview local · {host}</b><span className="br-chip">{situacao}</span></div>
    <small>Aplicação {p.aplicacao} · versão {p.versao}</small>
    {url ? <>
      <p className="br-local-pronto">✓ Front · ✓ API · ✓ Dependências<br />Chamada ao backend conferida</p>
      <code className="br-local-url">{url}</code>
      <p>Este endereço abre na máquina {host}.</p>
    </> : <p>{p.diagnostico}</p>}
    <div className="br-local-acoes">
      <button className="br-btn" disabled={!podeOperar || !url} title={motivo || 'Solicitar abertura no navegador do host.'} onClick={() => pedir('abrir')}>Abrir no host</button>
      <button className="br-btn" disabled={!consultar} onClick={consultar} title="Reler o resultado persistido recebido da máquina, sem iniciar processos.">Consultar</button>
      <button className="br-btn" disabled={!podeOperar || !parar} title={motivo} onClick={() => pedir('parar')}>Parar</button>
    </div>
    <p>Abrir no host pede ao dev.kit para abrir o navegador em {host}. Pedido aceito não confirma prontidão ou parada.</p>
    <button className="br-btn br-local-iniciar" disabled={!podeOperar || !iniciar} title={motivo} onClick={() => pedir('iniciar')}>Iniciar preview no host</button>
    {p.abertura && <p>Abertura · {p.abertura.situacao}: {p.abertura.diagnostico || 'Consulte a confirmação do host.'}</p>}
    <details className="br-local-detalhes"><summary>Componentes e logs · {p.componentes.length}</summary>
      <p>{p.diagnostico}</p>
      {p.componentes.map(c => <div key={c.id}><b>{c.id} · {c.situacao}{c.porta ? ` · porta ${c.porta}` : ''}</b>
        {c.diagnostico && <p>{c.diagnostico}</p>}<pre>{c.logParcial || 'Sem saída parcial recebida.'}</pre></div>)}
    </details>
  </section>;
}

function GeracaoLocal({ a, slug, locais, maquina, atualizado, operarPreview, consultar, desabilitado }: AcoesDePreviewNoCard & {
  a: ArtefatoLocal; slug: string; locais: ArtefatosLocais; desabilitado: boolean;
}) {
  const podeOperar = !!(locais.operacoesDisponiveis && maquina?.conectada && maquina.appAberto && atualizado
    && operarPreview && !desabilitado && a.conjuntoHash);
  const motivo = !atualizado || !maquina?.conectada ? 'Último retrato: atualize e confira a conexão da máquina.'
    : !locais.operacoesDisponiveis || !maquina.appAberto ? 'Esta ponte/versão não anunciou operações locais disponíveis para este acesso.'
      : desabilitado ? 'Aguardando a resposta do pedido anterior.' : '';
  return <section className="br-geracao-local" aria-label={`Geração ${a.id}`}>
    <div className="br-local-titulo"><b>Geração nº {a.id}</b><span className={`br-chip${a.situacao === 'falhou' ? ' br-warn' : ''}`}>{a.situacao === 'pronto' ? 'conferido' : a.situacao}</span></div>
    {a.situacao !== 'pronto' && <p>{a.diagnostico}</p>}
    <details className="br-local-detalhes"><summary>{a.fontes.length} repositórios · ver conjunto e hashes</summary>
      {a.conjuntoHash ? <><p>Conjunto <code>{a.conjuntoHash}</code></p>
        {a.fontes.map(f => <p key={f.id}><b>{f.nome}</b><br />{f.branch}<br /><code>{f.sha}</code><br />Base {f.branchBase || 'não registrada'} · <code>{f.shaBase || 'não registrada'}</code></p>)}
        <p>Alvos · {a.alvos.length}</p>{a.alvos.map((t, i) => <p key={`${t.repositorio}-${t.nome}-${i}`}>{t.nome} · {t.receita}{t.framework ? ` · ${t.framework}` : ''}{t.runtime ? ` · ${t.runtime}` : ''}
          {t.saida && <><br />Saída <code>{t.saida}</code></>}{t.assembly && <> · assembly {t.assembly}</>}</p>)}
        {a.detalhes && <>
          <p>Política de pins · {a.detalhes.politicaDePins}</p>
          {a.detalhes.pins.map((p, i) => <p key={`pin-${i}`}>Pin {p.papel} · consumidor {p.consumidor.slice(0, 12)} · fonte {p.repositorio.slice(0, 12)}<br />
            Declarado <code>{p.declarado}</code><br />Capturado <code>{p.capturado}</code><br />
            Árvores <code>{p.arvoreDeclarada}</code> / <code>{p.arvoreCapturada}</code><br />
            Raiz <code>{p.raiz}</code> · {p.politica}</p>)}
          <p>Exclusões · {a.detalhes.exclusoes.length}</p>
          {a.detalhes.exclusoes.map((e, i) => <p key={`exclusao-${i}`}>{e.repositorio.slice(0, 12)} · {e.caminho} · {e.motivo}</p>)}
          {a.detalhes.opcoes && <p>Formato solicitado · {a.detalhes.opcoes.formatoSolicitado || 'automático'} · referência {a.detalhes.opcoes.repositorioPrincipal.slice(0, 12)}<br />
            Destino SWA registrado · {a.detalhes.opcoes.previewRemoto.app || 'não configurado'} · grupo {a.detalhes.opcoes.previewRemoto.grupo || 'não configurado'}<br />
            Assinatura {a.detalhes.opcoes.previewRemoto.assinatura || 'não configurada'} · tenant {a.detalhes.opcoes.previewRemoto.tenant || 'não configurado'}</p>}
        </>}
      </> : <p>Registro antigo sem snapshot completo; nenhuma fonte histórica foi inventada.</p>}
      {a.hash && <p>SHA-256 do pacote <code>{a.hash}</code></p>}
      {a.pacote && <p>Pacote conferido · versão {a.pacote.versao} · {a.pacote.tamanho} bytes<br />
        SHA-256 <code>{a.pacote.hash}</code><br />
        {a.pacote.idRemoto > 0 ? `Publicação nº ${a.pacote.idRemoto} · link pela consulta autorizada de downloads` : 'Arquivo somente no host'}
      </p>}
      <p>{a.diagnostico}</p>
    </details>
    {a.comando && <details className="br-local-detalhes"><summary>Comando {a.comando.id ?? 'aguardando vínculo'} · diagnóstico e log</summary>
      <p>Recibo <code>{a.comando.recibo}</code></p>
      <p>{a.comando.motivoTermino || 'Causa de término não registrada'}{a.comando.exit !== null ? ` · exit ${a.comando.exit}` : ''}</p>
      <pre>{a.comando.logParcial || 'Sem saída parcial recebida.'}</pre>
    </details>}
    {a.previews.map(p => <AplicacaoLocal key={`${p.receita}-${p.aplicacao}`} a={a} p={p} slug={slug} podeOperar={podeOperar}
      motivo={motivo} maquina={maquina} operarPreview={operarPreview} consultar={consultar} />)}
  </section>;
}

export function ArtefatosLocaisDoCard(props: AcoesDePreviewNoCard & { slug: string; locais: ArtefatosLocais; desabilitado: boolean }) {
  if (props.locais.contrato !== 1) return <p className="br-artefatos-aviso">Contrato de artefatos locais não suportado. Consulte o host.</p>;
  const [atual, ...historico] = props.locais.artefatos;
  return <div className="br-conjuntos-locais">
    {atual && <GeracaoLocal {...props} a={atual} />}
    {!!historico.length && <details className="br-local-detalhes"><summary>Histórico das gerações · {historico.length}</summary>
      {historico.map(a => <GeracaoLocal {...props} key={a.id} a={a} />)}
    </details>}
    {props.locais.aviso && <p className="br-artefatos-aviso">{props.locais.aviso}</p>}
  </div>;
}
