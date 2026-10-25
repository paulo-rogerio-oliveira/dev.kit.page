import { useEffect, useState } from 'react';
import { api } from '../api/cliente';
import type { DefinicaoDeLicenca, DetalheDaLicenca, LicencaDoUsuario, ModoDaValidade, Papel, PreviaDeLicenca, TipoDaLicenca } from '../api/tipos';
import { formatar } from '../formatar';
import { PainelLateral } from './PainelLateral';

/** Os atalhos de "somar à validade atual", em dias. */
export const ATALHOS_DE_DIAS: { rotulo: string; dias: number }[] = [
  { rotulo: '+7 dias', dias: 7 }, { rotulo: '+30 dias', dias: 30 }, { rotulo: '+90 dias', dias: 90 },
  { rotulo: '+6 meses', dias: 182 }, { rotulo: '+1 ano', dias: 365 },
];

/** Os tipos que o admin escolhe (a conta de administração não tem o que definir). */
export const TIPOS_DA_LICENCA: { valor: Exclude<TipoDaLicenca, 'admin'>; rotulo: string }[] = [
  { valor: 'individual', rotulo: 'individual' }, { valor: 'empresa', rotulo: 'empresa' }, { valor: 'trial', rotulo: 'trial' },
];

const NOME_DO_PAPEL: Record<Papel, string> = { dev: 'Dev', gestor: 'Gestor', admin: 'Admin' };

/** O que a licença diz na coluna e no painel: a frase curta e a cor (a do aviso dos 7 dias e a da vencida). */
export function descreverLicenca(l: LicencaDoUsuario | null | undefined): { texto: string; cor: 'boa' | 'alerta' | 'ruim' | 'neutra' } {
  if (!l) return { texto: '—', cor: 'neutra' };
  switch (l.situacao) {
    case 'sem-prazo': return { texto: 'Sem prazo', cor: 'neutra' };
    case 'automatica': return { texto: 'Renovação automática', cor: 'neutra' };
    case 'revogada': return { texto: 'revogada', cor: 'ruim' };
    case 'vencida': {
      const dias = Math.abs(l.diasRestantes ?? 0);
      return { texto: dias === 0 ? 'venceu hoje' : `vencida há ${dias} ${dias === 1 ? 'dia' : 'dias'}`, cor: 'ruim' };
    }
    case 'vencendo': {
      const dias = l.diasRestantes ?? 0;
      return { texto: dias <= 0 ? 'vence hoje' : dias === 1 ? 'vence amanhã' : `vence em ${dias} dias`, cor: 'alerta' };
    }
    default: return { texto: `${l.diasRestantes} dias`, cor: 'boa' };
  }
}

/** A linha de apoio da licença ("até 28/10/2026 · trial", "conta de administração"). */
export function apoioDaLicenca(l: LicencaDoUsuario | null | undefined): string {
  if (!l) return '';
  if (l.tipo === 'admin') return 'conta de administração';
  if (l.situacao === 'vencida' && l.validaAte) return `venceu em ${formatar.data(l.validaAte)} · ${l.tipo}`;
  if (l.validaAte) return `até ${formatar.data(l.validaAte)} · ${l.tipo}`;
  if (l.situacao === 'revogada') return `renovação parada · ${l.tipo}`;
  return l.situacao === 'automatica' ? `sem validade definida · ${l.tipo}` : l.tipo;
}

/** Quanto da barra está cheio: os dias que faltam sobre o ano (a vencida e a revogada, cheia de vermelho). */
export function fracaoDaLicenca(l: LicencaDoUsuario | null | undefined): number {
  if (!l) return 0;
  if (l.situacao === 'vencida' || l.situacao === 'revogada' || l.situacao === 'sem-prazo') return 1;
  if (l.diasRestantes === null) return 0;
  return Math.max(0.04, Math.min(1, l.diasRestantes / 180));
}

/** A barra da licença: a cor da situação, no trilho de sempre. */
export function BarraDaLicenca({ licenca, rotulo }: { licenca: LicencaDoUsuario | null | undefined; rotulo: string }) {
  const { cor } = descreverLicenca(licenca);
  return (
    <span className="trilho trilho-da-licenca" role="img" aria-label={rotulo}>
      <span className={`barra barra-${cor}`} style={{ width: `${fracaoDaLicenca(licenca) * 100}%` }} />
    </span>
  );
}

/**
 * O painel "Licença de &lt;usuário&gt;" (US #687, proposta admin-usuarios v005): a validade atual, a DEFINIÇÃO da validade
 * que o admin quiser — somar dias à atual (os atalhos ou outro número), até uma data ou sem prazo —, o tipo, o motivo
 * (que vai para o histórico), a prévia da nova data, o Revogar e o Aplicar, e o histórico. A renovação do dev.kit
 * respeita o que for aplicado aqui, no próximo login.
 */
export function PainelDaLicenca({ token, usuarioId, aoMudar, aoFechar, tratar }: {
  token: string;
  usuarioId: number;
  aoMudar: (mensagem: string) => void;
  aoFechar: () => void;
  tratar: (falha: unknown) => void;
}) {
  const [detalhe, setDetalhe] = useState<DetalheDaLicenca | null>(null);
  const [modo, setModo] = useState<ModoDaValidade>('somar');
  const [dias, setDias] = useState(30);
  const [outro, setOutro] = useState(false);
  const [ate, setAte] = useState('');
  const [tipo, setTipo] = useState<TipoDaLicenca>('individual');
  const [motivo, setMotivo] = useState('');
  const [previa, setPrevia] = useState<PreviaDeLicenca | null>(null);
  const [problema, setProblema] = useState('');
  const [enviando, setEnviando] = useState(false);

  useEffect(() => {
    let vivo = true;
    setDetalhe(null);
    api.licencaDoUsuario(token, usuarioId)
      .then((d) => {
        if (!vivo) return;
        setDetalhe(d);
        setTipo(d.licenca.tipo === 'admin' ? 'individual' : d.licenca.tipo);
      })
      .catch((falha) => vivo && tratar(falha));
    return () => {
      vivo = false;
    };
  }, [token, usuarioId, tratar]);

  const definicao: DefinicaoDeLicenca = modo === 'somar' ? { modo, dias, tipo } : modo === 'ate-data' ? { modo, ate: ate || null, tipo } : { modo, tipo };
  const chaveDaPrevia = JSON.stringify(definicao);

  // A prévia acompanha o que o admin escolhe (a API calcula: a mesma regra do aplicar).
  useEffect(() => {
    if (!detalhe || detalhe.licenca.tipo === 'admin' || (modo === 'ate-data' && !ate) || (modo === 'somar' && !(dias >= 1))) {
      setPrevia(null);
      return;
    }
    let vivo = true;
    const espera = setTimeout(() => {
      api.previaDaLicenca(token, usuarioId, JSON.parse(chaveDaPrevia) as DefinicaoDeLicenca)
        .then((p) => { if (vivo) { setPrevia(p); setProblema(''); } })
        .catch((falha) => { if (vivo) { setPrevia(null); setProblema(falha instanceof Error ? falha.message : 'A prévia falhou.'); } });
    }, 250);
    return () => {
      vivo = false;
      clearTimeout(espera);
    };
  }, [token, usuarioId, chaveDaPrevia, detalhe, modo, ate, dias]);

  async function aplicar() {
    if (!motivo.trim()) {
      setProblema('Informe o motivo: ele vai para o histórico da licença.');
      return;
    }
    setEnviando(true);
    try {
      const novo = await api.definirLicenca(token, usuarioId, { ...definicao, motivo: motivo.trim() });
      setDetalhe(novo);
      setMotivo('');
      aoMudar(`Licença de ${novo.nome || novo.login} aplicada: ${descreverLicenca(novo.licenca).texto}.`);
    } catch (falha) {
      tratar(falha);
    } finally {
      setEnviando(false);
    }
  }

  async function revogar() {
    if (!motivo.trim()) {
      setProblema('Informe o motivo da revogação: ele vai para o histórico.');
      return;
    }
    if (!window.confirm(`Revogar a licença de ${detalhe?.nome || detalhe?.login}? A renovação do dev.kit para até uma validade nova.`)) return;
    setEnviando(true);
    try {
      const novo = await api.revogarLicenca(token, usuarioId, motivo.trim());
      setDetalhe(novo);
      setMotivo('');
      aoMudar(`Licença de ${novo.nome || novo.login} revogada.`);
    } catch (falha) {
      tratar(falha);
    } finally {
      setEnviando(false);
    }
  }

  const nome = detalhe ? detalhe.nome || detalhe.login : '';
  const l = detalhe?.licenca;
  const descricao = descreverLicenca(l);

  return (
    <PainelLateral id="titulo-licenca" titulo={detalhe ? `Licença de ${nome}` : 'Licença'}>
      <button className="botao-icone fechar-painel" type="button" aria-label="Fechar o painel da licença" onClick={aoFechar}>×</button>
      {!detalhe || !l ? (
        <p className="carregando" role="status">Carregando…</p>
      ) : (
        <div className="painel-da-licenca">
          <p className="texto-mudo apoio-do-painel">{detalhe.login} · {NOME_DO_PAPEL[detalhe.papel] ?? detalhe.papel}{detalhe.empresa ? ` · ${detalhe.empresa}` : ''}</p>

          <div className="validade-atual">
            <div className="validade-atual-topo">
              <span>Validade atual</span>
              <span className={`licenca-${descricao.cor}`}>{l.tipo} · {descricao.texto}</span>
            </div>
            <strong className="validade-atual-data">{l.validaAte ? formatar.data(l.validaAte) : descricao.texto}</strong>
            <BarraDaLicenca licenca={l} rotulo={`Validade de ${nome}`} />
            <div className="validade-atual-rodape texto-mudo">
              <span>início {formatar.data(l.inicio)}</span>
              <span>último uso: {l.ultimoUso ? formatar.dataHora(l.ultimoUso) : 'nenhum ainda'}</span>
            </div>
          </div>

          {l.tipo === 'admin' ? (
            <p className="nota-informativa">A conta de administração não tem prazo de licença para definir.</p>
          ) : (
            <>
              <h3 className="subtitulo-do-painel">Definir a validade</h3>
              <div className="segmentos" role="radiogroup" aria-label="Como definir a validade">
                {([['somar', 'Somar à validade atual'], ['ate-data', 'Até uma data'], ['sem-prazo', 'Sem prazo']] as [ModoDaValidade, string][]).map(([valor, rotulo]) => (
                  <button key={valor} type="button" role="radio" aria-checked={modo === valor} className="segmento" onClick={() => setModo(valor)}>{rotulo}</button>
                ))}
              </div>

              {modo === 'somar' && (
                <div className="chips atalhos-de-dias" role="group" aria-label="Somar dias">
                  {ATALHOS_DE_DIAS.map((a) => (
                    <button key={a.dias} className="chip" type="button" aria-pressed={!outro && dias === a.dias} onClick={() => { setOutro(false); setDias(a.dias); }}>{a.rotulo}</button>
                  ))}
                  <button className="chip" type="button" aria-pressed={outro} onClick={() => setOutro(true)}>Outro…</button>
                </div>
              )}

              <div className="campos-lado-a-lado">
                {modo === 'somar' && (
                  <label className="campo-com-sufixo">
                    <span className="visualmente-oculto">Dias a somar</span>
                    <input type="number" min={1} max={3650} value={dias} onChange={(e) => { setOutro(true); setDias(Number(e.target.value)); }} aria-label="Dias a somar" />
                    <span className="sufixo">dias</span>
                  </label>
                )}
                {modo === 'ate-data' && (
                  <label>
                    <span className="visualmente-oculto">Até a data</span>
                    <input type="date" value={ate} onChange={(e) => setAte(e.target.value)} aria-label="Até a data" />
                  </label>
                )}
                <label>
                  <span className="visualmente-oculto">Tipo da licença</span>
                  <select aria-label="Tipo da licença" value={tipo} onChange={(e) => setTipo(e.target.value as TipoDaLicenca)}>
                    {TIPOS_DA_LICENCA.map((t) => <option key={t.valor} value={t.valor}>Tipo: {t.rotulo}</option>)}
                  </select>
                </label>
              </div>

              <input
                aria-label="Motivo"
                value={motivo}
                maxLength={300}
                onChange={(e) => { setMotivo(e.target.value); setProblema(''); }}
                placeholder="Motivo (vai para o histórico) — ex.: piloto Acme estendido"
              />

              {previa && <p className="previa-da-licenca" role="status">{previa.mensagem}</p>}
              {problema && <p className="erro" role="alert">{problema}</p>}

              <div className="acoes acoes-da-licenca">
                <button className="botao botao-fantasma" type="button" disabled={enviando || l.situacao === 'revogada'} onClick={() => void revogar()}>Revogar</button>
                <button className="botao botao-primario" type="button" disabled={enviando} onClick={() => void aplicar()}>Aplicar validade</button>
              </div>
            </>
          )}

          <div className="historico-da-licenca">
            <h3 className="subtitulo-do-painel">Histórico</h3>
            <ul>
              {detalhe.historico.map((h) => (
                <li key={`${h.em}-${h.acao}-${h.descricao}`}>
                  {formatar.data(h.em).slice(0, 5)} · {h.descricao}{h.motivo ? ` — ${h.motivo}` : ''}{h.autor ? ` · ${h.autor}` : ''}
                </li>
              ))}
            </ul>
          </div>
        </div>
      )}
    </PainelLateral>
  );
}
