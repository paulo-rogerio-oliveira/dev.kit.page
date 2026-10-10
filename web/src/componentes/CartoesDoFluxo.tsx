import type { ArbitroResumo, ContagemPorRecorte, ImpassesResumo } from '../api/tipos';
import { formatar } from '../formatar';
import { BarrasHorizontais, Kpi } from './Graficos';

/** Como cada desfecho de impasse e cada ação do árbitro aparecem na tela (o recorte vem do dev.kit). */
const ROTULOS: Record<string, string> = {
  'mensagem-parada': 'mensagem parada',
  'objetivo-parado': 'objetivo parado',
  nota: 'chegou a nota',
  'mensagem-entregue': 'mensagem entregue',
  'objetivo-cumprido': 'objetivo cumprido',
  'dev-falou': 'o dev falou',
  'agente-pediu-avaliacao': 'o agente pediu avaliação',
  cancelado: 'cancelado',
  'arbitro-reagiu': 'o árbitro reagiu',
};

const pontos = (lista: ContagemPorRecorte[] | undefined, vazio: string) =>
  (lista ?? []).filter((c) => c.quantidade > 0).map((c) => ({ rotulo: c.recorte ? (ROTULOS[c.recorte] ?? c.recorte) : vazio, valor: c.quantidade }));

/**
 * Os cartões Impasses e Árbitro da qualidade de uso (US #405, #411): quando o fluxo parou, quanto tempo
 * ficou parado e como destravou; e o que o árbitro cobrou, por regra (a seção do CLAUDE.md), quanto foi
 * corrigido e quantas vezes ele chamou o dev. Os números vêm agregados da API (no escopo e no período do
 * filtro); sem denominador, o traço. Uma API anterior a eles (sem os campos) mostra os cartões vazios.
 */
export function CartoesDoFluxo({ impasses, arbitro }: { impasses?: ImpassesResumo | null; arbitro?: ArbitroResumo | null }) {
  return (
    <div className="cartoes-do-fluxo">
      <article className="cartao-do-painel" aria-labelledby="titulo-impasses">
        <h3 id="titulo-impasses">Impasses</h3>
        <p className="nota-da-secao">O fluxo parado à espera de algo que não chega — e como voltou a andar.</p>
        <div className="kpis">
          <Kpi rotulo="Impasses detectados" valor={formatar.inteiro(impasses?.detectados ?? 0)} nota={`${formatar.inteiro(impasses?.destravados ?? 0)} destravado(s)`} />
          <Kpi rotulo="Tempo médio parado" valor={formatar.minutos(impasses?.minutosParadoNaDeteccao)} nota="até a detecção" />
          <Kpi rotulo="Tempo médio até destravar" valor={formatar.minutos(impasses?.minutosAteDestravar)} nota="da detecção ao desfecho" />
        </div>
        <BarrasHorizontais titulo="Como destravaram" vazio="Nenhum impasse destravado no período." pontos={pontos(impasses?.comoDestravaram, 'não informado')} />
      </article>

      <article className="cartao-do-painel" aria-labelledby="titulo-arbitro">
        <h3 id="titulo-arbitro">Árbitro</h3>
        <p className="nota-da-secao">As regras do time que o árbitro cobrou do agente, e o que virou correção.</p>
        <div className="kpis">
          <Kpi rotulo="Cobranças do árbitro" valor={formatar.inteiro(arbitro?.cobrancas ?? 0)} nota={`${formatar.inteiro(arbitro?.corrigidas ?? 0)} corrigida(s)`} />
          <Kpi rotulo="Taxa de correção" valor={formatar.percentual(arbitro?.taxaDeCorrecao)} nota="corrigidas ÷ cobranças" />
          <Kpi rotulo="Escaladas ao dev" valor={formatar.inteiro(arbitro?.escaladasAoDev ?? 0)} />
        </div>
        <BarrasHorizontais titulo="Cobranças por regra" vazio="Nenhuma cobrança no período." pontos={pontos(arbitro?.cobrancasPorRegra, 'sem seção')} />
      </article>
    </div>
  );
}
