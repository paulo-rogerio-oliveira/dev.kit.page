import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { NivelDeConvencao, ProjetoDeDesign, TelaDeDesign, UsoDoDesign, VersaoDeDesign } from '../api/tipos';
import { copiarTexto } from '../areaDeTransferencia';
import { formatar } from '../formatar';

/** O escopo do filtro: o nível dos projetos (a mesma regra do contexto — empresa, equipe, os meus). */
type Escopo = NivelDeConvencao;

type Modo = 'uma' | 'lado-a-lado' | 'sobrepor';

const ROTULO_DO_ESCOPO: Record<Escopo, string> = { empresa: 'Empresa', time: 'Equipe', usuario: 'Minhas' };

/** O papel de uma versão na comparação: a tela de antes, a proposta e a recaptura (a tela real depois dela). */
export type PapelNaComparacao = 'captura' | 'proposta' | 'recaptura';

export interface ColunaDaComparacao {
  versao: VersaoDeDesign;
  papel: PapelNaComparacao;
  /** O canto direito do cabeçalho da coluna: "antes", "base v001", "depois". */
  legenda: string;
}

const tresDigitos = (n: number) => `v${String(n).padStart(3, '0')}`;

/** O branch curto (o último segmento: feature/dev/semana5 → semana5), como a faixa do dev.kit mostra. */
export const branchCurto = (branch: string) => branch.split('/').filter(Boolean).pop() ?? branch;

/** As versões de uma tela no branch escolhido ('' = todos). */
export const versoesNoBranch = (tela: TelaDeDesign, branch: string) => tela.versoes.filter((v) => !branch || v.branch === branch);

/**
 * A comparação captura | proposta | recaptura de uma tela: a última PROPOSTA, a captura que ela tem por base (ou a
 * última antes dela) e a última captura DEPOIS dela — a recaptura, a tela já implementada. Sem proposta, a última captura.
 */
export function comparacao(versoes: VersaoDeDesign[]): ColunaDaComparacao[] {
  const proposta = [...versoes].reverse().find((v) => v.tipo === 'proposta');
  if (!proposta) {
    const ultima = versoes[versoes.length - 1];
    return ultima ? [{ versao: ultima, papel: 'captura', legenda: 'atual' }] : [];
  }
  const capturas = versoes.filter((v) => v.tipo === 'captura');
  const antes = capturas.find((v) => v.numero === proposta.base) ?? [...capturas].reverse().find((v) => v.numero < proposta.numero);
  const depois = [...capturas].reverse().find((v) => v.numero > proposta.numero);
  return [
    ...(antes ? [{ versao: antes, papel: 'captura' as const, legenda: 'antes' }] : []),
    { versao: proposta, papel: 'proposta', legenda: proposta.base ? `base ${tresDigitos(proposta.base)}` : 'tela nova' },
    ...(depois ? [{ versao: depois, papel: 'recaptura' as const, legenda: 'depois' }] : []),
  ];
}

/** O papel da última versão da tela, para o selo da lista: a captura depois de uma proposta é a recaptura. */
export function papelDaUltima(versoes: VersaoDeDesign[]): PapelNaComparacao | null {
  const ultima = versoes[versoes.length - 1];
  if (!ultima) return null;
  if (ultima.tipo === 'proposta') return 'proposta';
  return versoes.some((v) => v.tipo === 'proposta' && v.numero < ultima.numero) ? 'recaptura' : 'captura';
}

/** A página estável da versão no site — o link que a PR da semana cita (o caminho da imagem sem o /api). */
export const paginaDaVersao = (versao: VersaoDeDesign) => `${window.location.origin}${versao.link.replace(/^\/api/, '')}`;

/** O repositório curto (github.com/acme/portal → acme/portal). */
const repositorioCurto = (chave: string) => chave.split('/').slice(-2).join('/');

/**
 * O DESIGN compartilhado (US #669) — a aba Design de /empresa/conhecimento e a página /design de quem não tem empresa:
 * os projetos por REPOSITÓRIO (o branch é filtro), as telas e, de cada uma, a captura | proposta | recaptura lado a lado
 * (ou uma, ou sobrepostas), com o link estável por versão e o Apagar de quem pode (o autor ou o gestor).
 */
export function DesignCompartilhado({ token, comEmpresa, tratar }: {
  token: string;
  /** Sem empresa só existe o nível usuário: os chips de escopo não aparecem. */
  comEmpresa: boolean;
  tratar: (falha: unknown) => void;
}) {
  const [projetos, setProjetos] = useState<ProjetoDeDesign[] | null>(null);
  const [uso, setUso] = useState<UsoDoDesign | null>(null);
  const [escopo, setEscopo] = useState<Escopo>(comEmpresa ? 'empresa' : 'usuario');
  const [chave, setChave] = useState('');
  const [branch, setBranch] = useState('');
  const [idDaTela, setIdDaTela] = useState('');
  const [modo, setModo] = useState<Modo>('lado-a-lado');
  const [escolhida, setEscolhida] = useState<number | null>(null);
  const [aviso, setAviso] = useState('');

  const carregar = useCallback(async () => {
    try {
      const [lista, doPlano] = await Promise.all([api.projetosDeDesign(token), api.usoDoDesign(token)]);
      setProjetos(lista);
      setUso(doPlano);
    } catch (falha) {
      tratar(falha);
    }
  }, [token, tratar]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  const doEscopo = useMemo(() => (projetos ?? []).filter((p) => p.nivel === escopo), [projetos, escopo]);
  const projeto = doEscopo.find((p) => p.chave === chave) ?? doEscopo[0];
  const branches = useMemo(
    () => [...new Set((projeto?.telas ?? []).flatMap((t) => t.versoes.map((v) => v.branch)).filter(Boolean))].sort(),
    [projeto],
  );
  const telas = (projeto?.telas ?? []).filter((t) => versoesNoBranch(t, branch).length > 0);
  const tela = telas.find((t) => t.id === idDaTela) ?? telas[0];
  const versoes = tela ? versoesNoBranch(tela, branch) : [];
  const colunas = comparacao(versoes);
  const emFoco = modo === 'uma'
    ? versoes.find((v) => v.numero === escolhida) ?? versoes[versoes.length - 1]
    : colunas.find((c) => c.papel === 'proposta')?.versao ?? colunas[colunas.length - 1]?.versao;
  const times = [...new Set((projetos ?? []).filter((p) => p.nivel === 'time').map((p) => p.time))];

  async function apagar(versao: VersaoDeDesign) {
    if (!tela || !window.confirm(`Apagar a ${tresDigitos(versao.numero)} da tela ${tela.nome}? O link dela deixa de valer.`)) return;
    setAviso('');
    try {
      await api.apagarVersaoDeDesign(token, versao.link);
      setAviso(`${tresDigitos(versao.numero)} da tela ${tela.nome} apagada.`);
      await carregar();
    } catch (falha) {
      tratar(falha);
    }
  }

  if (!projetos) return <p className="carregando" role="status">Carregando…</p>;

  return (
    <div className="design">
      <p className="nota-da-secao">
        As telas que o dev.kit capturou e propôs nos repositórios{comEmpresa ? ' da empresa' : ''}. Cada versão tem um link estável — é ele que a PR da semana cita.
      </p>
      {aviso && <p className="aviso" role="status">{aviso}</p>}

      <div className="design-filtros">
        <label className="campo-embutido">
          Repositório
          <select value={projeto?.chave ?? ''} onChange={(e) => { setChave(e.target.value); setIdDaTela(''); setBranch(''); }} disabled={doEscopo.length === 0}>
            {doEscopo.length === 0 && <option value="">—</option>}
            {doEscopo.map((p) => <option key={p.id} value={p.chave}>{p.chave}{p.nivel === 'time' ? ` · ${p.time}` : ''}</option>)}
          </select>
        </label>
        <label className="campo-embutido">
          Branch
          <select value={branch} onChange={(e) => { setBranch(e.target.value); setIdDaTela(''); }}>
            <option value="">todos</option>
            {branches.map((b) => <option key={b} value={b}>{branchCurto(b)}</option>)}
          </select>
        </label>
        {comEmpresa && (
          <div className="segmentado" role="group" aria-label="Escopo">
            {(['empresa', 'time', 'usuario'] as Escopo[]).map((n) => (
              <button key={n} type="button" aria-pressed={escopo === n} onClick={() => { setEscopo(n); setChave(''); setIdDaTela(''); setBranch(''); }}>
                {n === 'time' && times.length === 1 ? `Equipe ${times[0]}` : ROTULO_DO_ESCOPO[n]}
              </button>
            ))}
          </div>
        )}
        {uso && <UsoDoTeto uso={uso} />}
      </div>

      {doEscopo.length === 0 ? (
        <p className="vazio cartao">
          Nenhuma tela neste escopo ainda — o dev.kit envia cada captura e proposta do <code className="codigo">devcli design</code> e da aba Design.
        </p>
      ) : (
        <div className="design-corpo">
          <section className="cartao design-telas" aria-label="Telas">
            <h3 className="design-rotulo">Telas · {telas.length}</h3>
            <ul>
              {telas.map((t) => {
                const dela = versoesNoBranch(t, branch);
                const ultima = dela[dela.length - 1];
                const papel = papelDaUltima(dela);
                return (
                  <li key={t.id}>
                    <button type="button" aria-current={t.id === tela?.id} onClick={() => { setIdDaTela(t.id); setEscolhida(null); }}>
                      <strong>{t.nome}{t.estado ? ` — ${t.estado}` : ''}</strong>
                      <span>
                        {papel && <SeloDoPapel papel={papel} />} {tresDigitos(ultima.numero)} · {dela.length} {dela.length === 1 ? 'versão' : 'versões'} · {branchCurto(ultima.branch)}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          </section>

          {tela && (
            <section className="cartao design-comparacao" aria-label={`Versões de ${tela.nome}`}>
              <div className="design-cabecalho">
                <h3>{tela.nome} <small>{repositorioCurto(projeto!.chave)} · {versoes.length} {versoes.length === 1 ? 'versão' : 'versões'}</small></h3>
                <div className="segmentado" role="group" aria-label="Modo de comparação">
                  {([['uma', 'Uma'], ['lado-a-lado', 'Lado a lado'], ['sobrepor', 'Sobrepor']] as [Modo, string][]).map(([m, rotulo]) => (
                    <button key={m} type="button" aria-pressed={modo === m} onClick={() => setModo(m)}>{rotulo}</button>
                  ))}
                </div>
              </div>

              {modo === 'lado-a-lado' && (
                <div className="design-colunas" style={{ gridTemplateColumns: `repeat(${colunas.length}, minmax(0, 1fr))` }}>
                  {colunas.map((c) => (
                    <CartaoDaVersao key={c.versao.numero} token={token} coluna={c} destaque={c.papel === 'proposta'} aoApagar={() => void apagar(c.versao)} tratar={tratar} />
                  ))}
                </div>
              )}

              {modo === 'uma' && emFoco && (
                <div className="design-uma">
                  <label className="campo-embutido">
                    Versão
                    <select value={emFoco.numero} onChange={(e) => setEscolhida(Number(e.target.value))}>
                      {[...versoes].reverse().map((v) => <option key={v.numero} value={v.numero}>{tresDigitos(v.numero)} · {v.tipo}</option>)}
                    </select>
                  </label>
                  <CartaoDaVersao token={token} coluna={{ versao: emFoco, papel: papelNaTela(versoes, emFoco), legenda: emFoco.base ? `base ${tresDigitos(emFoco.base)}` : '' }}
                    destaque={false} aoApagar={() => void apagar(emFoco)} tratar={tratar} />
                </div>
              )}

              {modo === 'sobrepor' && <Sobrepostas token={token} colunas={colunas} tratar={tratar} />}

              {emFoco && (
                <div className="design-link">
                  <span className="design-rotulo">Link estável</span>
                  <code>{paginaDaVersao(emFoco).replace(/^https?:\/\//, '')}</code>
                  <small>· o que a PR cita</small>
                  <a className="botao botao-fantasma botao-pequeno" href={paginaDaVersao(emFoco)} target="_blank" rel="noreferrer">Abrir</a>
                </div>
              )}
            </section>
          )}
        </div>
      )}
    </div>
  );
}

/** O papel de uma versão qualquer na tela: captura, proposta ou recaptura (a captura depois de uma proposta). */
function papelNaTela(versoes: VersaoDeDesign[], versao: VersaoDeDesign): PapelNaComparacao {
  if (versao.tipo === 'proposta') return 'proposta';
  return versoes.some((v) => v.tipo === 'proposta' && v.numero < versao.numero) ? 'recaptura' : 'captura';
}

/** "37 de 500 versões · 48 MB de 2 GB — plano Empresarial", com a barra do mais usado dos dois. */
function UsoDoTeto({ uso }: { uso: UsoDoDesign }) {
  const fracao = Math.min(1, Math.max(uso.versoes / Math.max(1, uso.tetoDeVersoes), uso.bytes / Math.max(1, uso.tetoDeBytes)));
  const tamanho = (bytes: number) => bytes >= 1024 ** 3 ? `${formatar.decimal(bytes / 1024 ** 3).replace(',0', '')} GB` : formatar.megabytes(bytes).replace(',0', '');
  return (
    <div className="design-uso">
      <span>{uso.versoes} de {uso.tetoDeVersoes} versões · {tamanho(uso.bytes)} de {tamanho(uso.tetoDeBytes)} — plano {uso.plano}</span>
      <span className="trilho" role="progressbar" aria-label="Uso do teto do plano" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(fracao * 100)}>
        <span className={`barra${fracao >= 1 ? ' barra-cheia' : ''}`} style={{ width: `${fracao * 100}%` }} />
      </span>
    </div>
  );
}

function SeloDoPapel({ papel }: { papel: PapelNaComparacao }) {
  return <span className={`selo-do-design selo-do-design-${papel}`}>{papel}</span>;
}

/** Uma versão na comparação: o cabeçalho (número, papel, legenda), a imagem, o branch @ commit e as ações. */
function CartaoDaVersao({ token, coluna, destaque, aoApagar, tratar }: {
  token: string; coluna: ColunaDaComparacao; destaque: boolean; aoApagar: () => void; tratar: (falha: unknown) => void;
}) {
  const { versao, papel, legenda } = coluna;
  const [copiado, setCopiado] = useState('');
  const relogio = useRef<number | undefined>(undefined);
  useEffect(() => () => window.clearTimeout(relogio.current), []);

  async function copiar() {
    setCopiado(await copiarTexto(paginaDaVersao(versao)) ? 'Link copiado' : 'O navegador não deixou copiar');
    window.clearTimeout(relogio.current);
    relogio.current = window.setTimeout(() => setCopiado(''), 2000);
  }

  return (
    <article className={`design-versao${destaque ? ' design-versao-destaque' : ''}`} aria-label={`${tresDigitos(versao.numero)} ${papel}`}>
      <header>
        <strong>{tresDigitos(versao.numero)}</strong> <SeloDoPapel papel={papel} />
        <span className="design-legenda">{legenda}</span>
      </header>
      <ImagemDaVersao token={token} versao={versao} tratar={tratar} />
      <p className="design-meta">
        <code>{branchCurto(versao.branch) || '—'}{versao.commit ? ` @ ${versao.commit}` : ''}</code> · {versao.origem} · {versao.tema}
        <br />
        {versao.autorTipo === 'agente' ? 'agente' : versao.autor || 'usuário'} · {formatar.dataHora(versao.criadaEm)}
      </p>
      {versao.descricao && <p className="design-descricao">{versao.descricao}</p>}
      <div className="acoes">
        <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Copiar link da ${tresDigitos(versao.numero)}`} onClick={() => void copiar()}>Copiar link</button>
        {versao.podeApagar && (
          <button className="botao botao-fantasma botao-pequeno botao-perigo" type="button" aria-label={`Apagar a ${tresDigitos(versao.numero)}`} onClick={aoApagar}>Apagar</button>
        )}
        <span className="copiar-retorno" aria-live="polite">{copiado}</span>
      </div>
    </article>
  );
}

/** A imagem da versão: baixada com o token (a rota é autenticada) e mostrada por um blob: URL. */
export function ImagemDaVersao({ token, versao, tratar }: { token: string; versao: VersaoDeDesign; tratar: (falha: unknown) => void }) {
  const [url, setUrl] = useState('');
  const [falhou, setFalhou] = useState(false);

  useEffect(() => {
    let vivo = true;
    let criada = '';
    setFalhou(false);
    api.imagemDeDesign(token, versao.link).then((blob) => {
      if (!vivo) return;
      criada = URL.createObjectURL(blob);
      setUrl(criada);
    }).catch((falha: unknown) => {
      if (!vivo) return;
      setFalhou(true);
      if (falha instanceof ErroDaApi && falha.status === 401) tratar(falha);
    });
    return () => {
      vivo = false;
      if (criada) URL.revokeObjectURL(criada);
    };
  }, [token, versao.link, tratar]);

  const alt = `${tresDigitos(versao.numero)} ${versao.tipo} (${versao.largura}x${versao.altura})`;
  if (falhou) return <p className="design-imagem design-imagem-falhou" role="img" aria-label={alt}>Imagem indisponível</p>;
  return url
    ? <img className="design-imagem" src={url} alt={alt} width={versao.largura} height={versao.altura} />
    : <span className="design-imagem design-imagem-carregando" role="img" aria-label={`${alt} — carregando`} />;
}

/** Sobrepor: a de antes embaixo e a proposta (ou a recaptura) por cima, com a opacidade no controle deslizante. */
function Sobrepostas({ token, colunas, tratar }: { token: string; colunas: ColunaDaComparacao[]; tratar: (falha: unknown) => void }) {
  const [opacidade, setOpacidade] = useState(50);
  const embaixo = colunas[0];
  const emCima = colunas.find((c) => c.papel === 'proposta') ?? colunas[colunas.length - 1];
  if (!embaixo || !emCima || embaixo === emCima) return <p className="vazio">Só há uma versão para mostrar: escolha Uma.</p>;
  return (
    <div className="design-sobrepor">
      <label className="campo-embutido">
        {tresDigitos(embaixo.versao.numero)} ⟷ {tresDigitos(emCima.versao.numero)}
        <input type="range" min={0} max={100} value={opacidade} onChange={(e) => setOpacidade(Number(e.target.value))} aria-label="Opacidade da versão de cima" />
      </label>
      <div className="design-pilha">
        <ImagemDaVersao token={token} versao={embaixo.versao} tratar={tratar} />
        <div className="design-por-cima" style={{ opacity: opacidade / 100 }}>
          <ImagemDaVersao token={token} versao={emCima.versao} tratar={tratar} />
        </div>
      </div>
    </div>
  );
}

/** A falha da tela de design no texto de quem a lê (o 404 do link que não vale mais, inclusive). */
export const falhaDoDesign = (falha: unknown) => falha instanceof ErroDaApi && falha.status === 404
  ? 'Esta versão de design não existe mais, ou não é do seu escopo.'
  : mensagemDaFalha(falha);
