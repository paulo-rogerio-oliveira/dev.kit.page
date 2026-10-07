import { useCallback, useEffect, useState } from 'react';
import { api } from '../api/cliente';
import type { EstadoDoGrupo, Filtro, GrupoDeErroDetalhe, GrupoDeErroResumo, Pagina } from '../api/tipos';
import { salvarArquivo } from '../arquivos';
import { formatar } from '../formatar';
import { GraficoDeColunas } from './Graficos';
import { TraceDoErro } from './TraceDoErro';

/** Quantos grupos por página. */
export const GRUPOS_POR_PAGINA = 10;

/** O texto pronto para abrir o Bug no Azure DevOps — o dado da reação, sem nada a reescrever. */
export function textoDoBug(detalhe: GrupoDeErroDetalhe): string {
  const g = detalhe.grupo;
  return [
    `[dev.kit] Exceção não classificada: ${g.tipo}`,
    '',
    `Assinatura: ${g.assinatura}`,
    `Ocorrências no período: ${g.ocorrencias} em ${g.maquinas} máquina(s)`,
    `Versões: ${detalhe.versoes.join(', ') || '—'}`,
    `Primeiro visto: ${formatar.dataHora(g.primeiroVistoEm)} · Último visto: ${formatar.dataHora(g.ultimoVistoEm)}`,
    '',
    'Trace (sanitizado no dev.kit):',
    detalhe.trace || '(sem trace guardado)',
    '',
    'Reação: escrever o detector desta causa em TurnFailures.Padrao (git.kit) — ela deixa de ser "não classificada".',
  ].join('\n');
}

const ROTULO_DO_ESTADO: Record<EstadoDoGrupo, string> = {
  Novo: 'Novo',
  Visto: 'Visto',
  Resolvido: 'Resolvido',
  Ignorado: 'Ignorado',
  Regrediu: 'Regrediu',
};

/**
 * A seção "Exceções não classificadas" do dashboard (US #381): os grupos do período (uma linha por
 * assinatura, com ocorrências, máquinas, versões e quando foram vistos) e o detalhe do escolhido — o
 * trace, as ocorrências por dia e a REAÇÃO. Marcar visto, resolver na versão e ignorar são do admin
 * (a API os recusa ao gestor); exportar o JSON e copiar o texto do Bug são de quem vê o grupo.
 */
export function ExcecoesNaoClassificadas({ token, filtro, podeReagir, aoFalhar }: {
  token: string;
  filtro: Filtro;
  podeReagir: boolean;
  aoFalhar: (falha: unknown) => void;
}) {
  const [pagina, setPagina] = useState(1);
  const [lista, setLista] = useState<Pagina<GrupoDeErroResumo> | null>(null);
  const [aberto, setAberto] = useState<number | null>(null);
  const [detalhe, setDetalhe] = useState<GrupoDeErroDetalhe | null>(null);
  const [versao, setVersao] = useState('');
  const [aviso, setAviso] = useState('');

  // O filtro do dashboard mudou: volta à primeira página e fecha o detalhe.
  useEffect(() => {
    setPagina(1);
    setAberto(null);
  }, [filtro.de, filtro.ate, filtro.maquina]);

  const carregarLista = useCallback(
    () => api.erros(token, filtro, pagina, GRUPOS_POR_PAGINA).then(setLista).catch(aoFalhar),
    [token, filtro, pagina, aoFalhar],
  );

  const carregarDetalhe = useCallback(
    (id: number) => api.erro(token, id, filtro).then(setDetalhe).catch(aoFalhar),
    [token, filtro, aoFalhar],
  );

  useEffect(() => {
    void carregarLista();
  }, [carregarLista]);

  useEffect(() => {
    setDetalhe(null);
    setAviso('');
    setVersao('');
    if (aberto !== null) void carregarDetalhe(aberto);
  }, [aberto, carregarDetalhe]);

  async function reagir(estado: Exclude<EstadoDoGrupo, 'Regrediu'>, versaoDaCorrecao: string | null = null) {
    if (aberto === null) return;
    try {
      await api.alterarEstado(token, aberto, estado, versaoDaCorrecao);
      setAviso(estado === 'Resolvido' && versaoDaCorrecao ? `Marcado como resolvido na versão ${versaoDaCorrecao}.` : `Marcado como ${ROTULO_DO_ESTADO[estado].toLowerCase()}.`);
      await Promise.all([carregarLista(), carregarDetalhe(aberto)]);
    } catch (falha) {
      aoFalhar(falha);
    }
  }

  async function exportar() {
    if (aberto === null) return;
    try {
      salvarArquivo(await api.exportarErro(token, aberto, filtro));
      setAviso('Exportação em JSON baixada.');
    } catch (falha) {
      aoFalhar(falha);
    }
  }

  async function copiar() {
    if (!detalhe) return;
    try {
      await navigator.clipboard.writeText(textoDoBug(detalhe));
      setAviso('Texto do Bug copiado para a área de transferência.');
    } catch {
      setAviso('O navegador não deixou copiar: use a exportação em JSON.');
    }
  }

  const totalDePaginas = lista ? Math.max(1, Math.ceil(lista.total / lista.tamanho)) : 1;

  return (
    <section aria-labelledby="titulo-excecoes">
      <h2 id="titulo-excecoes">Exceções não classificadas</h2>
      <p className="nota-da-secao">
        Falhas que nenhum detector do dev.kit reconheceu, agrupadas pela assinatura. O trace vem sem caminhos, e-mails nem URLs.
      </p>
      {!lista ? (
        <p className="carregando" role="status">Carregando…</p>
      ) : lista.total === 0 ? (
        <p className="vazio">Nenhuma exceção não classificada no período.</p>
      ) : (
        <>
          <table className="tabela">
            <thead>
              <tr>
                <th>Exceção</th><th>Estado</th><th className="numero">Ocorrências</th><th className="numero">Máquinas</th>
                <th>Versões</th><th>Primeiro visto</th><th>Último visto</th>
              </tr>
            </thead>
            <tbody>
              {lista.itens.map((g) => (
                <tr key={g.id} className={g.id === aberto ? 'linha-aberta' : undefined}>
                  <td>
                    <button className="link" type="button" aria-expanded={g.id === aberto} onClick={() => setAberto(g.id === aberto ? null : g.id)}>
                      {g.tipo}
                    </button>
                    <small className="assinatura">{g.assinatura}</small>
                  </td>
                  <td><span className={`estado estado-${g.estado.toLowerCase()}`}>{ROTULO_DO_ESTADO[g.estado]}</span></td>
                  <td className="numero">{formatar.inteiro(g.ocorrencias)}</td>
                  <td className="numero">{formatar.inteiro(g.maquinas)}</td>
                  <td>{g.primeiraVersao === g.ultimaVersao ? g.ultimaVersao || '—' : `${g.primeiraVersao} → ${g.ultimaVersao}`}</td>
                  <td>{formatar.dataHora(g.primeiroVistoEm)}</td>
                  <td>{formatar.dataHora(g.ultimoVistoEm)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <div className="paginacao">
            <button className="botao botao-fantasma" type="button" disabled={pagina <= 1} onClick={() => setPagina(pagina - 1)}>Anterior</button>
            <span>Página {pagina} de {totalDePaginas} · {lista.total} grupo(s)</span>
            <button className="botao botao-fantasma" type="button" disabled={pagina >= totalDePaginas} onClick={() => setPagina(pagina + 1)}>Próxima</button>
          </div>
        </>
      )}

      {aberto !== null && (
        <article className="detalhe-do-erro" aria-label="Detalhe da exceção">
          {!detalhe ? (
            <p className="carregando" role="status">Carregando o detalhe…</p>
          ) : (
            <>
              <header>
                <h3>{detalhe.grupo.tipo}</h3>
                <p>
                  {ROTULO_DO_ESTADO[detalhe.grupo.estado]}
                  {detalhe.grupo.resolvidoNaVersao ? ` na versão ${detalhe.grupo.resolvidoNaVersao}` : ''} · assinatura {detalhe.grupo.assinatura} ·
                  {' '}{detalhe.grupo.ocorrencias} ocorrência(s) em {detalhe.grupo.maquinas} máquina(s)
                </p>
              </header>

              <TraceDoErro trace={detalhe.trace} />

              <div className="acoes" role="group" aria-label="Reagir à exceção">
                {podeReagir && (
                  <>
                    <button className="botao botao-fantasma" type="button" onClick={() => void reagir('Visto')}>Marcar visto</button>
                    <label className="resolver">
                      Versão da correção
                      <input value={versao} onChange={(e) => setVersao(e.target.value)} placeholder="ex.: 1.5.0" maxLength={50} />
                    </label>
                    <button className="botao botao-fantasma" type="button" disabled={versao.trim().length === 0} onClick={() => void reagir('Resolvido', versao.trim())}>
                      Resolver na versão
                    </button>
                    <button className="botao botao-fantasma" type="button" onClick={() => void reagir('Ignorado')}>Ignorar</button>
                    {detalhe.grupo.estado !== 'Novo' && (
                      <button className="botao botao-fantasma" type="button" onClick={() => void reagir('Novo')}>Reabrir</button>
                    )}
                  </>
                )}
                <button className="botao botao-fantasma" type="button" onClick={() => void exportar()}>Exportar JSON</button>
                <button className="botao botao-fantasma" type="button" onClick={() => void copiar()}>Copiar para Bug</button>
              </div>
              {aviso && <p className="aviso" role="status">{aviso}</p>}

              <GraficoDeColunas
                titulo="Ocorrências por dia"
                unidade="ocorrências"
                pontos={detalhe.porDia.map((d) => ({ rotulo: formatar.dia(d.dia), valor: d.quantidade }))}
              />

              <h4>Últimas ocorrências</h4>
              {detalhe.ocorrencias.length === 0 ? (
                <p className="vazio">Nenhuma ocorrência guardada no seu escopo.</p>
              ) : (
                <table className="tabela">
                  <thead><tr><th>Quando</th><th>Máquina</th><th>Versão</th></tr></thead>
                  <tbody>
                    {detalhe.ocorrencias.map((o) => (
                      <tr key={o.eventId}><td>{formatar.dataHora(o.em)}</td><td>{o.apelido}</td><td>{o.versaoDevKit || '—'}</td></tr>
                    ))}
                  </tbody>
                </table>
              )}
            </>
          )}
        </article>
      )}
    </section>
  );
}
