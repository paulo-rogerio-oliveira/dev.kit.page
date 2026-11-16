import { useEffect, useState } from 'react';
import { api, ErroDaApi } from '../api/cliente';
import type { Artefato } from '../api/projetos';
import type { ArtefatosLocais } from '../api/artefatosLocais';
import { ArtefatosLocaisDoCard, type AcoesDePreviewNoCard } from './ArtefatosLocaisDoCard';

/** Quanto tempo os links do card valem antes de o Board relê-los (o link de download vence; a API devolve um novo a cada leitura). */
const releituraMs = 60_000;

/**
 * (controle-v4, item 2) Os últimos artefatos de cada projeto, por slug (o do retrato da máquina). Relê a cada minuto e quando
 * `versao` muda — o desfecho de um "Gerar artefato". A API antiga (sem a rota) e a falha deixam o card sem links, sem erro na tela.
 */
export function useArtefatosDoBoard(token: string | undefined, versao: string) {
  const [porSlug, setPorSlug] = useState(new Map<string, Artefato[]>());
  useEffect(() => {
    if (!token) return;
    const controller = new AbortController();
    let timer: number;
    async function carregar() {
      try {
        const lista = await api.artefatosRecentes(token!, controller.signal);
        if (!controller.signal.aborted) setPorSlug(new Map(lista.map(p => [p.slug, p.artefatos])));
      } catch (falha) {
        // 404 é a API de antes do item 2 (regra #2): o card mostra só o botão. O 401 é tratado pelo retrato.
        if (!controller.signal.aborted && falha instanceof ErroDaApi && falha.status === 404) setPorSlug(new Map());
      } finally {
        if (!controller.signal.aborted) timer = window.setTimeout(carregar, releituraMs);
      }
    }
    void carregar();
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [token, versao]);
  return porSlug;
}

function repositorioCurto(repositorio: string) {
  return repositorio.replace(/\.git$/i, '').split(/[/:]/).filter(Boolean).at(-1) ?? repositorio;
}

/**
 * O tamanho do pacote no card. (controle-v4, item 5) Abaixo de 1 MB ele saía "0 MB" — o pacote pequeno parecia vazio —, e
 * agora sai em KB (ou em bytes, abaixo de 1 KB).
 */
export function tamanhoDoArtefato(bytes: number) {
  if (bytes < 1024) return `${bytes} bytes`;
  // O arredondado a 1 KB do teto já é 1 MB: 1.048.575 bytes saem "1,0 MB", e não "1.024 KB".
  if (Math.round(bytes / 102.4) < 10240) return `${(bytes / 1024).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} KB`;
  return `${(bytes / 1048576).toLocaleString('pt-BR', { maximumFractionDigits: 1, minimumFractionDigits: 1 })} MB`;
}

function prazo(expiraEm: string, agora: number) {
  const minutos = Math.floor((Date.parse(expiraEm) - agora) / 60_000);
  if (minutos <= 0) return 'link vencido: gere de novo';
  return minutos < 60 ? `expira em ${minutos} min` : `expira em ${Math.floor(minutos / 60)} h`;
}

/** O retrato identifica; somente a lista autorizada da API fornece o link e o prazo atuais. Ambiguidade não cria vínculo. */
function geracaoDoPublicado(a: Artefato, locais?: ArtefatosLocais | null): number | null {
  if (locais?.contrato !== 1) return null;
  function corresponde(p: { idRemoto: number; tipo: string; sha: string; hash: string; tamanho: number }) {
    return Number.isSafeInteger(p.idRemoto) && p.idRemoto > 0 && p.idRemoto === a.id && p.tipo === a.tipo
      && /^[a-f0-9]{40}([a-f0-9]{24})?$/i.test(p.sha) && p.sha.toLowerCase() === a.sha.toLowerCase()
      && p.hash.toLowerCase() === a.hash.toLowerCase() && p.tamanho === a.tamanho;
  }
  const geracoes = locais.artefatos.filter(g =>
    (g.publicacao && corresponde(g.publicacao))
    || (g.pacote && corresponde({ ...g.pacote, tipo: 'pacote' })));
  return geracoes.length === 1 ? geracoes[0].id : null;
}

/**
 * A seção Artefatos do card do projeto: o "Gerar artefato" (o pedido vai pela fila ao dev.kit da máquina, que gera no
 * executor) e os últimos links — o pacote .NET para baixar e o preview da página estática. Nunca publica em produção.
 */
export function ArtefatosDoCard({ nome, slug, artefatos, gerar, desabilitado, motivo, agora, locais, ...acoes }: AcoesDePreviewNoCard & {
  nome: string; slug: string; artefatos: Artefato[]; gerar: (slug: string) => void; desabilitado: boolean; motivo: string; agora: number;
  locais?: ArtefatosLocais | null;
}) {
  return <div className="br-artefatos" role="group" aria-label={`Artefatos de ${nome}`}>
    <div className="br-artefatos-titulo"><span>Artefatos{locais ? ` · ${locais.artefatos.length} conjunto(s)` : artefatos.length > 0 && ` · ${artefatos.length}`}</span>
      <button className="br-btn br-gerar" disabled={desabilitado} onClick={() => gerar(slug)}
        title={desabilitado ? motivo : 'O dev.kit da máquina gera no executor: pacote .NET para baixar ou preview da página estática. Nunca publica em produção.'}>
        {locais ? 'Gerar artefatos' : 'Gerar artefato'}</button></div>
    {locais && <ArtefatosLocaisDoCard {...acoes} locais={locais} slug={slug} desabilitado={desabilitado} />}
    {locais && artefatos.length > 0 && <p className="br-artefatos-aviso">Downloads e previews publicados · não alteram o pedido local</p>}
    {artefatos.map(a => {
      const preview = a.tipo === 'preview';
      const alvo = `${repositorioCurto(a.repositorio)} · ${a.sha.slice(0, 7)}`;
      const geracao = geracaoDoPublicado(a, locais);
      return <a className="br-artefato" key={a.id} href={a.link} target="_blank" rel="noopener noreferrer"
        aria-label={`${preview ? 'Abrir o preview' : 'Baixar o pacote'} de ${alvo}${geracao !== null ? `, geração ${geracao}` : ''}`}>
        <span className={`br-chip${preview ? ' br-chip-preview' : ''}`}>{preview ? 'preview' : 'pacote .NET'}</span>
        <span>{geracao !== null ? `Geração nº ${geracao} · ` : ''}{alvo}</span>
        <small><b>{preview ? 'Abrir preview' : 'Baixar'}</b> · {preview ? `ambiente ${a.ambiente}` : `${tamanhoDoArtefato(a.tamanho)} · ${a.expiraEm ? prazo(a.expiraEm, agora) : 'sem prazo'}`}</small>
      </a>;
    })}
    <p className="br-artefatos-aviso">{locais ? 'Preview local exige front, API e dependências prontos no host. Downloads publicados mantêm seus prazos.' : artefatos.length
      ? 'Só preview e download, nunca produção. O link de download vence no prazo.'
      : 'Nenhum artefato ainda: o pacote .NET ou o preview aparecem aqui. Nunca produção.'}</p>
  </div>;
}
