import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import type { ColaboradorResumo, EventoDoLog, Filtro, MaquinaResumo, Pagina, QualidadeResposta, QuantidadeResposta } from '../api/tipos';
import { salvarArquivo } from '../arquivos';
import { ExcecoesNaoClassificadas } from '../componentes/ExcecoesNaoClassificadas';
import { CartoesDoFluxo } from '../componentes/CartoesDoFluxo';
import { BarrasHorizontais, GraficoDeColunas, Kpi } from '../componentes/Graficos';
import { PedidosDeDemonstracao } from '../componentes/PedidosDeDemonstracao';
import { RoiPorWorkItem } from '../componentes/RoiPorWorkItem';
import { formatar, ultimosDias } from '../formatar';
import { papelDa, useSessao } from '../sessao';

/** Os períodos oferecidos no filtro. */
export const PERIODOS = [7, 30, 90] as const;

interface Dados {
  quantidade: QuantidadeResposta;
  qualidade: QualidadeResposta;
  eventos: Pagina<EventoDoLog>;
}

/** Uma opção do filtro: a máquina (admin) ou o colaborador (gestor) — o valor é o id interno da máquina. */
interface OpcaoDoFiltro {
  id: number;
  rotulo: string;
}

/**
 * O dashboard de uso por máquina: filtros (máquina e período) numa linha acima de tudo, os KPIs
 * de QUANTIDADE (com as máquinas ativas e registradas) e de QUALIDADE, a série diária, as falhas por
 * causa, o ROI por work item (US #387), as exceções não classificadas com o trace e a reação (US #381), o log paginado e os pedidos
 * de demonstração da landing (fora do filtro: não são telemetria). Um 401 da
 * API (token vencido) encerra a sessão e volta ao login.
 */
export function Dashboard() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const ehAdmin = papelDa(sessao!) === 'admin';
  const [dias, setDias] = useState<number>(30);
  const [maquina, setMaquina] = useState<number | null>(null);
  const filtro = useMemo<Filtro>(() => ({ ...ultimosDias(dias), maquina }), [dias, maquina]);
  const [pagina, setPagina] = useState(1);
  const [opcoes, setOpcoes] = useState<OpcaoDoFiltro[] | null>(null);
  const [dados, setDados] = useState<Dados | null>(null);
  const [erro, setErro] = useState('');
  const [aviso, setAviso] = useState('');
  const [carregando, setCarregando] = useState(true);

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) {
      sair('expirou');
      return;
    }
    setErro(falha instanceof Error ? falha.message : 'Falha ao consultar a API.');
  }, [sair]);

  // O filtro do admin são as máquinas; o do gestor, os colaboradores que consentiram (US #381) —
  // a API devolve ao gestor só a empresa dele, e o nome vem do que o colaborador informou no dev.kit.
  useEffect(() => {
    const carregar: Promise<OpcaoDoFiltro[]> = ehAdmin
      ? api.maquinas(token).then((lista: MaquinaResumo[]) => lista.map((m) => ({ id: m.id, rotulo: `${m.apelido} (dev.kit ${m.versaoDevKit || '?'})` })))
      : api.colaboradores(token).then((lista: ColaboradorResumo[]) => lista.map((c) => ({ id: c.maquinaId, rotulo: c.colaborador || c.apelido })));
    carregar.then(setOpcoes).catch(tratar);
  }, [token, ehAdmin, tratar]);

  async function exportar() {
    setAviso('');
    try {
      salvarArquivo(await api.exportar(token, filtro, 'csv'));
      setAviso('Exportação baixada (CSV). O acesso fica registrado na trilha de auditoria.');
    } catch (falha) {
      tratar(falha);
    }
  }

  useEffect(() => {
    let vivo = true;
    setCarregando(true);
    setErro('');
    Promise.all([api.quantidade(token, filtro), api.qualidade(token, filtro), api.eventos(token, filtro, pagina)])
      .then(([quantidade, qualidade, eventos]) => vivo && setDados({ quantidade, qualidade, eventos }))
      .catch((falha) => vivo && tratar(falha))
      .finally(() => vivo && setCarregando(false));
    return () => {
      vivo = false;
    };
  }, [token, filtro, pagina, tratar]);

  const q = dados?.quantidade;
  const ql = dados?.qualidade;
  const totalDePaginas = dados ? Math.max(1, Math.ceil(dados.eventos.total / dados.eventos.tamanho)) : 1;

  return (
    <div className="pagina">
      <header className="topo">
        <span className="marca">dev<span className="marca-ponto">.</span>kit <small>uso</small></span>
        <nav>
          {ehAdmin && <Link className="botao botao-fantasma" to="/empresas">Empresas</Link>}
          {ehAdmin && <Link className="botao botao-fantasma" to="/usuarios">Usuários</Link>}
          <span className="usuario">{sessao!.login}{sessao!.empresa ? ` · ${sessao!.empresa}` : ''}</span>
          <button className="botao botao-fantasma" type="button" onClick={() => sair()}>Sair</button>
        </nav>
      </header>

      <main className="painel">
        <div className="filtros" role="search">
          <label>
            {ehAdmin ? 'Máquina' : 'Colaborador'}
            <select value={maquina ?? ''} onChange={(e) => { setPagina(1); setMaquina(e.target.value ? Number(e.target.value) : null); }}>
              <option value="">{ehAdmin ? 'Todas as máquinas' : 'Todos os colaboradores'}</option>
              {(opcoes ?? []).map((o) => <option key={o.id} value={o.id}>{o.rotulo}</option>)}
            </select>
          </label>
          <label>
            Período
            <select value={dias} onChange={(e) => { setPagina(1); setDias(Number(e.target.value)); }}>
              {PERIODOS.map((p) => <option key={p} value={p}>Últimos {p} dias</option>)}
            </select>
          </label>
          <button className="botao botao-fantasma" type="button" onClick={() => void exportar()}>Exportar uso (CSV)</button>
          {carregando && <span className="carregando" role="status">Carregando…</span>}
        </div>

        {erro && <p className="erro" role="alert">{erro}</p>}
        {aviso && <p className="aviso" role="status">{aviso}</p>}

        {!carregando && !erro && opcoes?.length === 0 && (
          <p className="vazio" role="status">
            {ehAdmin
              ? 'Nenhuma máquina enviou telemetria ainda. No dev.kit, confira em Configurações → Telemetria de uso que o envio está ligado, com a URL desta API e o código de registro.'
              : 'Nenhum colaborador aderiu ainda. Entregue ao time o código de adesão da empresa: no dev.kit, Configurações → Telemetria de uso → Código da empresa, com o aceite do aviso de coleta.'}
          </p>
        )}

        {q && ql && (
          <>
            <section aria-labelledby="titulo-quantidade">
              <h2 id="titulo-quantidade">Quantidade de uso</h2>
              <div className="kpis">
                <Kpi rotulo="Máquinas ativas" valor={formatar.inteiro(q.maquinasAtivas)} nota="com uso no período" />
                <Kpi rotulo="Máquinas registradas" valor={formatar.inteiro(q.maquinasRegistradas)} />
                <Kpi rotulo="Sessões" valor={formatar.inteiro(q.sessoes)} />
                <Kpi rotulo="Turnos" valor={formatar.inteiro(q.turnos)} />
                <Kpi rotulo="Fluxos" valor={formatar.inteiro(q.fluxos)} />
                <Kpi rotulo="Ferramentas acionadas" valor={formatar.inteiro(q.ferramentas)} />
                <Kpi rotulo="Comandos delegados" valor={formatar.inteiro(q.comandosDelegados)} />
                <Kpi rotulo="Arquivos alterados" valor={formatar.inteiro(q.arquivosAlterados)} />
              </div>
              <GraficoDeColunas
                titulo="Turnos por dia"
                unidade="turnos"
                pontos={q.serieDiaria.map((d) => ({ rotulo: formatar.dia(d.dia), valor: d.turnos }))}
              />
            </section>

            <section aria-labelledby="titulo-qualidade">
              <h2 id="titulo-qualidade">Qualidade de uso</h2>
              <div className="kpis">
                <Kpi rotulo="Taxa de falha de turno" valor={formatar.percentual(ql.taxaDeFalha)} nota={`${ql.turnosComFalha} de ${ql.turnos} turnos`} />
                <Kpi rotulo="Duração média do turno" valor={formatar.segundos(ql.duracaoMediaDoTurnoMs)} />
                <Kpi rotulo="Nota média dos avaliadores" valor={formatar.decimal(ql.notaMedia)} nota={`${ql.avaliacoes} avaliação(ões)`} />
                <Kpi rotulo="Objetivos cumpridos / recusados" valor={`${ql.objetivosCumpridos} / ${ql.objetivosRecusados}`} nota={`razão ${formatar.decimal(ql.razaoCumpridosRecusados)}`} />
                <Kpi rotulo="Turnos por objetivo cumprido" valor={formatar.decimal(ql.turnosPorObjetivoCumprido)} nota="retrabalho" />
              </div>
              <BarrasHorizontais
                titulo="Falhas de turno por causa"
                vazio="Nenhuma falha de turno no período."
                pontos={ql.falhasPorCausa.map((c) => ({ rotulo: c.causa, valor: c.quantidade }))}
              />
              <CartoesDoFluxo impasses={ql.impasses} arbitro={ql.arbitro} />
            </section>

            <RoiPorWorkItem token={token} filtro={filtro} aoFalhar={tratar} />

            <ExcecoesNaoClassificadas token={token} filtro={filtro} podeReagir={ehAdmin} aoFalhar={tratar} />

            <section aria-labelledby="titulo-eventos">
              <h2 id="titulo-eventos">Eventos recentes</h2>
              {dados.eventos.total === 0 ? (
                <p className="vazio">Nenhum evento no período.</p>
              ) : (
                <>
                  <table className="tabela">
                    <thead>
                      <tr><th>Quando</th><th>Máquina</th><th>Tipo</th><th>Detalhe</th><th className="numero">Qtd.</th><th className="numero">Valor</th></tr>
                    </thead>
                    <tbody>
                      {dados.eventos.itens.map((e) => (
                        <tr key={e.eventId}>
                          <td>{formatar.dataHora(e.em)}</td>
                          <td>{e.apelido}</td>
                          <td>{e.tipo}</td>
                          <td>{e.detalhe || '—'}</td>
                          <td className="numero">{e.quantidade}</td>
                          <td className="numero">{e.valor ?? '—'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  <div className="paginacao">
                    <button className="botao botao-fantasma" type="button" disabled={pagina <= 1} onClick={() => setPagina(pagina - 1)}>Anterior</button>
                    <span>Página {pagina} de {totalDePaginas} · {dados.eventos.total} eventos</span>
                    <button className="botao botao-fantasma" type="button" disabled={pagina >= totalDePaginas} onClick={() => setPagina(pagina + 1)}>Próxima</button>
                  </div>
                </>
              )}
            </section>
          </>
        )}

        {/* Contatos de venda da landing: só do admin (a API recusa ao gestor). */}
        {ehAdmin && <PedidosDeDemonstracao token={token} aoFalhar={tratar} />}
      </main>
    </div>
  );
}
