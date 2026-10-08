import { useState } from 'react';

/** Um ponto de uma série: o rótulo do eixo e o valor. */
export interface Ponto {
  rotulo: string;
  valor: number;
}

const ALTURA = 180;
const MARGEM = { topo: 12, base: 24, esquerda: 36, direita: 8 };

/** Um teto "redondo" para o eixo (1, 2, 5 × 10^n), para as linhas de grade caírem em números legíveis. */
function teto(maximo: number): number {
  if (maximo <= 0) return 1;
  const ordem = 10 ** Math.floor(Math.log10(maximo));
  return [1, 2, 5, 10].map((m) => m * ordem).find((v) => v >= maximo) ?? maximo;
}

/** A barra com as pontas de DADO arredondadas (4px) e a base reta, ancorada na linha de base. */
function caminhoDaBarra(x: number, y: number, largura: number, altura: number, raio = 4): string {
  if (altura <= 0) return '';
  const r = Math.min(raio, largura / 2, altura);
  return `M${x},${y + altura} V${y + r} Q${x},${y} ${x + r},${y} H${x + largura - r} Q${x + largura},${y} ${x + largura},${y + r} V${y + altura} Z`;
}

/**
 * Colunas de UMA série ao longo do tempo (a série diária). Sem legenda — o título nomeia a série;
 * tooltip por coluna (o alvo é a coluna inteira, maior que a barra) e a tabela equivalente logo abaixo.
 */
export function GraficoDeColunas({ titulo, unidade, pontos }: { titulo: string; unidade: string; pontos: Ponto[] }) {
  const [foco, setFoco] = useState<number | null>(null);
  // Largura do viewBox próxima da do card: o SVG escala com 100%, e um viewBox estreito ampliaria o texto dos eixos.
  const largura = Math.max(960, pontos.length * 14 + MARGEM.esquerda + MARGEM.direita);
  const area = { largura: largura - MARGEM.esquerda - MARGEM.direita, altura: ALTURA - MARGEM.topo - MARGEM.base };
  const maximo = teto(Math.max(0, ...pontos.map((p) => p.valor)));
  const passo = pontos.length === 0 ? 0 : area.largura / pontos.length;
  const barra = Math.max(2, passo - 2); // 2px de superfície entre as barras
  const y = (valor: number) => MARGEM.topo + area.altura - (valor / maximo) * area.altura;
  const cadaQuantos = Math.max(1, Math.ceil(pontos.length / 8)); // rótulos seletivos no eixo

  return (
    <figure className="grafico" aria-label={titulo}>
      <figcaption>{titulo}</figcaption>
      <div className="grafico-area">
        <svg viewBox={`0 0 ${largura} ${ALTURA}`} role="img" aria-label={`${titulo} — ${pontos.length} dias`}>
          {/* A linha do meio só quando cai num inteiro: "3" no lugar de 2,5 seria um eixo mentindo. */}
          {[0, 0.5, 1].filter((fracao) => Number.isInteger(maximo * fracao)).map((fracao) => (
            <g key={fracao}>
              <line className="grade" x1={MARGEM.esquerda} x2={largura - MARGEM.direita} y1={y(maximo * fracao)} y2={y(maximo * fracao)} />
              <text className="eixo" x={MARGEM.esquerda - 6} y={y(maximo * fracao) + 4} textAnchor="end">{maximo * fracao}</text>
            </g>
          ))}
          {pontos.map((ponto, i) => {
            const x = MARGEM.esquerda + i * passo + 1;
            return (
              <g key={ponto.rotulo} onMouseEnter={() => setFoco(i)} onMouseLeave={() => setFoco(null)}>
                <rect className="alvo" x={x - 1} y={MARGEM.topo} width={passo} height={area.altura} />
                <path className={`marca-serie${foco === i ? ' em-foco' : ''}`} d={caminhoDaBarra(x, y(ponto.valor), barra, MARGEM.topo + area.altura - y(ponto.valor))} />
                {i % cadaQuantos === 0 && (
                  <text className="eixo" x={x + barra / 2} y={ALTURA - 6} textAnchor="middle">{ponto.rotulo}</text>
                )}
              </g>
            );
          })}
          <line className="base" x1={MARGEM.esquerda} x2={largura - MARGEM.direita} y1={MARGEM.topo + area.altura} y2={MARGEM.topo + area.altura} />
        </svg>
        {foco !== null && pontos[foco] && (
          <div className="dica" role="tooltip" style={{ left: `${((MARGEM.esquerda + foco * passo + passo / 2) / largura) * 100}%` }}>
            <strong>{pontos[foco].rotulo}</strong> · {pontos[foco].valor} {unidade}
          </div>
        )}
      </div>
      <details>
        <summary>Ver como tabela</summary>
        <table className="tabela">
          <thead><tr><th>Dia</th><th>{unidade}</th></tr></thead>
          <tbody>{pontos.map((p) => <tr key={p.rotulo}><td>{p.rotulo}</td><td className="numero">{p.valor}</td></tr>)}</tbody>
        </table>
      </details>
    </figure>
  );
}

/** Barras horizontais de UMA série por categoria (as falhas por causa), com o valor rotulado em cada barra. */
export function BarrasHorizontais({ titulo, pontos, vazio }: { titulo: string; pontos: Ponto[]; vazio: string }) {
  const maximo = Math.max(1, ...pontos.map((p) => p.valor));
  return (
    <figure className="grafico" aria-label={titulo}>
      <figcaption>{titulo}</figcaption>
      {pontos.length === 0 ? (
        <p className="vazio">{vazio}</p>
      ) : (
        <ul className="barras-horizontais">
          {pontos.map((p) => (
            <li key={p.rotulo} title={`${p.rotulo}: ${p.valor}`}>
              <span className="rotulo">{p.rotulo}</span>
              <span className="trilho"><span className="barra" style={{ width: `${(p.valor / maximo) * 100}%` }} /></span>
              <span className="numero">{p.valor}</span>
            </li>
          ))}
        </ul>
      )}
    </figure>
  );
}

/** Uma medida das barras lado a lado: o cabeçalho da coluna e como o valor é escrito. */
export interface Medida {
  rotulo: string;
  formatar: (valor: number) => string;
}

/** Uma categoria das barras lado a lado: o rótulo, a linha de apoio, um valor por medida e a dica do hover. */
export interface LinhaDeBarras {
  chave: string;
  rotulo: string;
  apoio?: string;
  valores: number[];
  dica: string;
}

/**
 * Barras horizontais de DUAS (ou mais) medidas por categoria, em colunas lado a lado — pequenos múltiplos
 * com a MESMA ordem de linhas (o ROI: horas lançadas × turnos do agente por work item). Cada coluna tem a
 * sua escala e o seu cabeçalho: horas e turnos não dividem eixo (nada de eixo duplo), e a comparação é
 * entre os itens de uma coluna. Uma cor só (a série de dado): o cabeçalho nomeia a medida, o valor vai
 * rotulado na barra, e o hover da linha inteira mostra a dica (com o que não cabe na barra, como o lead time).
 */
export function BarrasLadoALado({ titulo, medidas, linhas, vazio }: { titulo: string; medidas: Medida[]; linhas: LinhaDeBarras[]; vazio: string }) {
  const [foco, setFoco] = useState<string | null>(null);
  const maximos = medidas.map((_, i) => Math.max(1, ...linhas.map((l) => l.valores[i] ?? 0)));
  const colunas = `minmax(140px, 220px) ${medidas.map(() => 'minmax(0, 1fr)').join(' ')}`;

  return (
    <figure className="grafico" aria-label={titulo}>
      <figcaption>{titulo}</figcaption>
      {linhas.length === 0 ? (
        <p className="vazio">{vazio}</p>
      ) : (
        <div className="barras-lado-a-lado" role="list">
          <div className="cabecalho" aria-hidden="true" style={{ gridTemplateColumns: colunas }}>
            <span />
            {medidas.map((m) => <span key={m.rotulo}>{m.rotulo}</span>)}
          </div>
          {linhas.map((linha) => (
            <div
              key={linha.chave}
              role="listitem"
              aria-label={linha.dica}
              className={`linha${foco === linha.chave ? ' em-foco' : ''}`}
              style={{ gridTemplateColumns: colunas }}
              onMouseEnter={() => setFoco(linha.chave)}
              onMouseLeave={() => setFoco(null)}
            >
              <span className="rotulo">
                {linha.rotulo}
                {linha.apoio && <small>{linha.apoio}</small>}
              </span>
              {medidas.map((m, i) => (
                <span key={m.rotulo} className="celula">
                  <span className="trilho"><span className="barra" style={{ width: `${((linha.valores[i] ?? 0) / maximos[i]) * 100}%` }} /></span>
                  <span className="numero">{m.formatar(linha.valores[i] ?? 0)}</span>
                </span>
              ))}
              {foco === linha.chave && <span className="dica" role="tooltip">{linha.dica}</span>}
            </div>
          ))}
        </div>
      )}
    </figure>
  );
}

/** Um bloco de número (KPI): o rótulo, o valor e, quando há, a nota de rodapé. */
export function Kpi({ rotulo, valor, nota }: { rotulo: string; valor: string; nota?: string }) {
  return (
    <div className="kpi" role="group" aria-label={rotulo}>
      <span className="kpi-rotulo">{rotulo}</span>
      <span className="kpi-valor" data-testid={`kpi-${rotulo}`}>{valor}</span>
      {nota && <span className="kpi-nota">{nota}</span>}
    </div>
  );
}
