import type { CartaoDeSaude, TipoDoSelo, UtilidadeResposta } from '../api/tipos';
import { formatar, SEM_VALOR } from '../formatar';
import { GraficoEmpilhado, type Camada } from './Graficos';
import { Selo, type VarianteDoSelo } from './Selo';

/** As camadas do gráfico de turnos, de baixo para cima — a ordem e as cores da proposta admin-dashboard v008. */
export const CAMADAS_DO_TURNO: Camada[] = [
  { chave: 'trabalhou', rotulo: 'trabalhou' },
  { chave: 'esperou', rotulo: 'esperou o dev' },
  { chave: 'limite', rotulo: 'limite de uso' },
  { chave: 'falhou', rotulo: 'falhou' },
];

/** A cor do selo de cada causa: a sugestão pendente pede atenção, o corrigido é bom, a US é informação. */
const COR_DO_SELO: Record<TipoDoSelo, VarianteDoSelo> = { sugestao: 'marca', corrigido: 'sucesso', us: 'aviso', '': 'neutro' };

/** O que a ação de um cartão faz: recortar o log de eventos lá embaixo pelo tipo (e o recorte) do caso. */
export type FiltrarLog = (tipo: string, detalhe: string) => void;

function Cartao({ titulo, acao, aoAgir, cartao, destaque, complemento, nota }: {
  titulo: string; acao: string; aoAgir: () => void; cartao: CartaoDeSaude; destaque: 'marca' | 'alerta'; complemento?: string; nota: string;
}) {
  const ativo = cartao.quantidade > 0;
  return (
    <div className={`cartao-de-saude${ativo ? ` cartao-de-saude-${destaque}` : ''}`} role="group" aria-label={titulo}>
      <div className="cartao-de-saude-topo">
        <span>{titulo}</span>
        <button className="link" type="button" onClick={aoAgir}>{acao} ›</button>
      </div>
      <p className="cartao-de-saude-valor" data-testid={`saude-${titulo}`}>
        {formatar.inteiro(cartao.quantidade)}
        {complemento && <small className="variacao-ruim">{complemento}</small>}
      </p>
      <p className="cartao-de-saude-nota">{nota}</p>
    </div>
  );
}

function Resultado({ rotulo, valor, variacao, variacaoBoa, nota }: { rotulo: string; valor: string; variacao?: string; variacaoBoa?: boolean; nota: string }) {
  return (
    <div className="kpi kpi-resultado" role="group" aria-label={rotulo}>
      <span className="kpi-rotulo">{rotulo}</span>
      <span className="kpi-valor" data-testid={`kpi-${rotulo}`}>
        {valor}
        {variacao && <small className={variacaoBoa ? 'variacao-boa' : 'variacao-ruim'}>{variacao}</small>}
      </span>
      <span className="kpi-nota">{nota}</span>
    </div>
  );
}

/**
 * O PAINEL DE UTILIDADE do admin (US #687, proposta admin-dashboard v008): o que está travando o trabalho AGORA (cada
 * cartão com a ação dele), o que o uso ENTREGOU no período, os turnos de cada dia pelo desfecho e onde as tasks travam
 * (as causas da retrospectiva do Orquestrador, com o selo da sugestão de impacto ou da US). Os dados vêm da telemetria.
 */
export function PainelDeUtilidade({ dados, aoFiltrarLog, agora = new Date() }: { dados: UtilidadeResposta; aoFiltrarLog: FiltrarLog; agora?: Date }) {
  const { saude, resultado: r } = dados;
  const na1a = r.percentualNaPrimeira;
  const variacao1a = r.variacaoNaPrimeira;
  const variacaoMediana = r.variacaoDaMedianaMinutos;
  const maiorCausa = Math.max(1, ...dados.ondeTravam.map((c) => c.vezes));

  return (
    <>
      <section aria-labelledby="titulo-saude" className="secao-do-painel">
        <div className="titulo-com-nota"><h2 id="titulo-saude">Saúde agora</h2><span>o que está travando o trabalho neste momento</span></div>
        <div className="cartoes-de-saude">
          <Cartao
            titulo="Tasks paradas sem processo" acao="Ver" destaque="marca" cartao={saude.tasksParadas}
            aoAgir={() => aoFiltrarLog('SituacaoDaTask', 'parada-sem-processo')}
            complemento={saude.tasksParadas.novos24h > 0 ? `+${saude.tasksParadas.novos24h} em 24 h` : undefined}
            nota={saude.tasksParadas.quantidade > 0
              ? `"em execução" sem atividade há mais de 1 h · a mais antiga desde ${formatar.data(saude.tasksParadas.maisAntigo)}`
              : 'nenhuma task em execução parada sem processo'}
          />
          <Cartao
            titulo="Perguntas esperando o dev" acao="Responder" destaque="alerta" cartao={saude.perguntas}
            aoAgir={() => aoFiltrarLog('PerguntaAoDev', 'aberta')}
            nota={saude.perguntas.quantidade > 0 ? `a mais antiga ${formatar.idade(saude.perguntas.maisAntigo, agora)}` : 'nenhuma pergunta esperando resposta'}
          />
          <Cartao
            titulo="Fechamentos que falharam" acao="Ver" destaque="alerta" cartao={saude.fechamentos}
            aoAgir={() => aoFiltrarLog('FechamentoDaEntrega', 'falhou')}
            nota={saude.fechamentos.itens.length > 0 ? `${saude.fechamentos.itens.join(' e ')} · o último desfecho do fechamento foi a falha` : 'nenhum fechamento com falha'}
          />
          <Cartao
            titulo="Máquinas com recurso no limite" acao="Detalhe" destaque="alerta" cartao={saude.maquinas}
            aoAgir={() => aoFiltrarLog('AmostraDaMaquina', '')}
            nota={saude.maquinas.itens.length > 0 ? saude.maquinas.itens.join(' · ') : 'disco e memória fora do limite em todas as máquinas'}
          />
        </div>
      </section>

      <section aria-labelledby="titulo-resultado" className="secao-do-painel">
        <div className="titulo-com-nota"><h2 id="titulo-resultado">Resultado</h2><span>o que o uso entregou no período</span></div>
        <div className="kpis kpis-resultado">
          <Resultado
            rotulo="Entregas aprovadas · integradas" valor={`${formatar.inteiro(r.aprovadas)} · ${formatar.inteiro(r.integradas)}`}
            variacao={`${formatar.inteiro(r.aprovadasNaSemana)} na semana`} variacaoBoa
            nota={r.aprovadas > r.integradas ? `${r.aprovadas - r.integradas} aprovadas ainda sem integrar` : 'todas as aprovadas integradas'}
          />
          <Resultado
            rotulo="Aprovadas na 1ª avaliação" valor={na1a === null ? SEM_VALOR : `${Math.round(na1a)}%`}
            variacao={variacao1a === null ? undefined : `${variacao1a >= 0 ? '+' : '−'}${Math.abs(Math.round(variacao1a))} p.p.`} variacaoBoa={(variacao1a ?? 0) >= 0}
            nota={`nota média ${r.notaMedia === null ? SEM_VALOR : Math.round(r.notaMedia)} · ${formatar.decimal(r.rodadasAteANota)} rodadas até a nota mínima`}
          />
          <Resultado
            rotulo="Tempo até a aprovação (mediana)" valor={formatar.duracao(r.medianaAteAprovacaoMinutos)}
            variacao={variacaoMediana === null ? undefined : formatar.duracao(variacaoMediana, true)} variacaoBoa={(variacaoMediana ?? 0) <= 0}
            nota={`do pedido à sua aprovação · ${r.percentualEsperandoDev === null ? SEM_VALOR : `${Math.round(r.percentualEsperandoDev)}%`} esperando o dev`}
          />
          <Resultado
            rotulo="Turnos por entrega" valor={formatar.decimal(r.turnosPorEntrega)}
            nota={`${formatar.inteiro(r.turnos)} turnos · ${formatar.inteiro(r.comandosDelegados)} comandos delegados · ${formatar.inteiro(r.arquivosAlterados)} arquivos`}
          />
        </div>
      </section>

      <div className="painel-em-duas-colunas">
        <GraficoEmpilhado
          titulo="Turnos por dia, pelo desfecho"
          unidade="turnos"
          camadas={CAMADAS_DO_TURNO}
          pontos={dados.turnosPorDesfecho.map((d) => ({ rotulo: formatar.dia(d.dia), valores: [d.trabalhou, d.esperouDev, d.limiteDeUso, d.falhou] }))}
        />

        <figure className="grafico onde-travam" aria-label="Onde as tasks travam">
          <div className="grafico-topo">
            <figcaption>Onde as tasks travam</figcaption>
            <span className="texto-mudo">{formatar.data(dados.de)} a {formatar.data(dados.ate)}</span>
          </div>
          {dados.ondeTravam.length === 0 ? (
            <p className="vazio">Nenhuma causa registrada pela retrospectiva do Orquestrador no período.</p>
          ) : (
            <table className="tabela tabela-das-causas">
              <thead><tr><th>Causa</th><th className="numero">Vezes</th><th><span className="visualmente-oculto">Frequência e selo</span></th></tr></thead>
              <tbody>
                {dados.ondeTravam.map((c) => (
                  <tr key={c.causa}>
                    <td>{c.causa}</td>
                    <td className="numero">{c.vezes}</td>
                    <td>
                      <span className="causa-barra">
                        <span className="trilho"><span className="barra barra-da-causa" style={{ width: `${(c.vezes / maiorCausa) * 100}%` }} /></span>
                        {c.selo && <Selo variante={COR_DO_SELO[c.tipoDoSelo] ?? 'neutro'}>{c.selo}</Selo>}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p className="nota-da-secao">Cada causa vem da retrospectiva do Orquestrador; o selo mostra a sugestão de impacto ou a US que trata.</p>
        </figure>
      </div>
    </>
  );
}
