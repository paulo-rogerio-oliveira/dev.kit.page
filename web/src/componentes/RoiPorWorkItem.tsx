import { useEffect, useState } from 'react';
import { api } from '../api/cliente';
import type { Filtro, RoiDoWorkItem, RoiResposta } from '../api/tipos';
import { formatar, SEM_VALOR } from '../formatar';
import { BarrasLadoALado, Kpi } from './Graficos';

/** Quantos work items o gráfico mostra (os mais recentes); a tabela mostra todos os que a API devolveu. */
export const ITENS_NO_GRAFICO = 12;

/** O lead time em dias, ou o traço do item ainda aberto. */
const dias = (valor: number | null) => (valor === null ? SEM_VALOR : `${formatar.decimal(valor)} dias`);

/** Quem trabalhou no item: o nome que o colaborador informou ou, sem ele, o apelido da máquina. */
const quem = (r: RoiDoWorkItem) => r.colaborador || r.apelido;

/** A dica do gráfico: o que a barra não mostra (o tipo, o estado, o lead time e as PRs). */
function dicaDo(r: RoiDoWorkItem): string {
  return [
    `#${r.workItem} ${r.tipo}`.trim(),
    r.estado,
    `${formatar.decimal(r.horas)} h`,
    `${formatar.inteiro(r.turnosDoAgente)} turnos`,
    `lead time ${r.aberto ? 'em aberto' : dias(r.leadTimeDias)}`,
    `PRs ${r.pullRequestsMergeadas}/${r.pullRequests}`,
  ].join(' · ');
}

/**
 * A seção "ROI por work item" do dashboard (US #387): a foto MAIS RECENTE de cada (máquina, item) que
 * o dev.kit calculou com `devcli roi` e mandou na telemetria. Segue os filtros da página (período e
 * máquina/colaborador): os KPIs são os totais de todas as fotos do período, o gráfico compara horas
 * lançadas e turnos do agente dos itens mais recentes, e a tabela traz o detalhe de cada um.
 */
export function RoiPorWorkItem({ token, filtro, aoFalhar }: { token: string; filtro: Filtro; aoFalhar: (falha: unknown) => void }) {
  const [roi, setRoi] = useState<RoiResposta | null>(null);

  useEffect(() => {
    let vivo = true;
    setRoi(null);
    api.roi(token, filtro).then((r) => vivo && setRoi(r)).catch((falha) => vivo && aoFalhar(falha));
    return () => {
      vivo = false;
    };
  }, [token, filtro, aoFalhar]);

  const t = roi?.totais;

  return (
    <section aria-labelledby="titulo-roi">
      <h2 id="titulo-roi">ROI por work item</h2>
      <p className="nota-da-secao">
        A foto mais recente que o dev.kit calculou de cada work item: horas lançadas, turnos do agente, lead time e pull requests.
      </p>
      {!roi || !t ? (
        <p className="carregando" role="status">Carregando…</p>
      ) : roi.itens.length === 0 ? (
        <p className="vazio">Nenhum ROI calculado — rode <code>devcli roi --id N</code> no dev.kit.</p>
      ) : (
        <>
          <div className="kpis">
            <Kpi rotulo="Work items" valor={formatar.inteiro(t.itens)} nota="com ROI no período" />
            <Kpi rotulo="Turnos do agente" valor={formatar.inteiro(t.turnos)} />
            <Kpi rotulo="Horas lançadas" valor={formatar.decimal(t.horas)} nota={`${formatar.decimal(t.turnos === 0 ? null : t.horas / t.turnos)} h por turno`} />
            <Kpi rotulo="Lead time médio" valor={dias(t.leadTimeMedioDias)} nota="dos encerrados" />
            <Kpi rotulo="PRs mergeadas" valor={`${formatar.inteiro(t.pullRequestsMergeadas)} / ${formatar.inteiro(t.pullRequests)}`} />
          </div>

          <BarrasLadoALado
            titulo={`Horas lançadas × turnos do agente (${Math.min(ITENS_NO_GRAFICO, roi.itens.length)} mais recentes)`}
            vazio="Nenhum ROI no período."
            medidas={[
              { rotulo: 'Horas lançadas', formatar: (v) => `${formatar.decimal(v)} h` },
              { rotulo: 'Turnos do agente', formatar: (v) => formatar.inteiro(v) },
            ]}
            linhas={roi.itens.slice(0, ITENS_NO_GRAFICO).map((r) => ({
              chave: `${r.maquinaId}-${r.workItem}`,
              rotulo: `#${r.workItem}`,
              apoio: `${r.tipo} · lead time ${r.aberto ? 'em aberto' : dias(r.leadTimeDias)}`,
              valores: [r.horas, r.turnosDoAgente],
              dica: dicaDo(r),
            }))}
          />

          <table className="tabela" aria-label="ROI por work item">
            <thead>
              <tr>
                <th>Work item</th><th>Tipo</th><th>Estado</th><th>Quem</th><th className="numero">Turnos</th><th className="numero">Horas</th>
                <th className="numero">Horas/turno</th><th className="numero">Lead time</th><th className="numero">PRs mergeadas/total</th><th>Calculado em</th>
              </tr>
            </thead>
            <tbody>
              {roi.itens.map((r) => (
                <tr key={`${r.maquinaId}-${r.workItem}`}>
                  <td>#{r.workItem}</td>
                  <td>{r.tipo || SEM_VALOR}</td>
                  <td><span className={r.aberto ? 'estado-aberto' : 'estado-encerrado'}>{r.estado || SEM_VALOR}</span></td>
                  <td>{quem(r)}</td>
                  <td className="numero">{formatar.inteiro(r.turnosDoAgente)}</td>
                  <td className="numero">{formatar.decimal(r.horas)}</td>
                  <td className="numero">{formatar.decimal(r.horasPorTurno)}</td>
                  <td className="numero">{r.aberto ? 'em aberto' : dias(r.leadTimeDias)}</td>
                  <td className="numero">{r.pullRequestsMergeadas}/{r.pullRequests}</td>
                  <td>{formatar.dataHora(r.atualizadoEm)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </section>
  );
}
