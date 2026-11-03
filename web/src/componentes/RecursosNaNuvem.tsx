import { useCallback, useEffect, useRef, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import { emAndamento, type EventoDaProvisao, type RecursoNaNuvem, type SituacaoDaProvisao } from '../api/provisao';
import { formatar } from '../formatar';
import { Selo, type VarianteDoSelo } from './Selo';

/** De quanto em quanto tempo a tela relê enquanto o trabalhador ainda mexe num recurso. */
export const ACOMPANHAMENTO_MS = 5000;

/** O estado do recurso como a pessoa lê, com a cor do selo: a remoção tem os próprios nomes. */
export function estadoDoRecurso(recurso: RecursoNaNuvem): { texto: string; variante: VarianteDoSelo } {
  const remocao = recurso.operacao === 'remover';
  switch (recurso.estado) {
    case 'pedido': return { texto: remocao ? 'Remoção pedida' : 'Pedida', variante: 'info' };
    case 'em-provisao': return { texto: remocao ? 'Removendo' : 'Em provisão', variante: 'info' };
    case 'pronto': return { texto: 'Pronta', variante: 'sucesso' };
    case 'falhou': return { texto: remocao ? 'A remoção falhou' : 'Falhou', variante: 'marca' };
    default: return { texto: 'Removida', variante: 'neutro' };
  }
}

/** Uma chave por pedido: o clique em dobro (ou o reenvio) devolve o MESMO recurso. */
function novaChave(): string {
  return `site-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
}

/**
 * A fila do usuário no Azure (projeto controle-remoto, item 4), na área do usuário (/conta). Só aparece com a provisão
 * liberada para ele — ou, sem ela, com recursos que ele ainda tem, para ver e remover. A API recusa do mesmo jeito: o que a
 * tela esconde não é a proteção. O pedido é assíncrono: a tela mostra pedida, em provisão, pronta ou falhou, e relê sozinha
 * enquanto o trabalhador mexe.
 */
export function RecursosNaNuvem({ token, sair }: { token: string; sair: (motivo?: 'expirou') => void }) {
  const [situacao, setSituacao] = useState<SituacaoDaProvisao | null>(null);
  const [regiao, setRegiao] = useState('');
  const [enviando, setEnviando] = useState(false);
  const [historicos, setHistoricos] = useState<Record<number, EventoDaProvisao[]>>({});
  const [erro, setErro] = useState('');
  const [aviso, setAviso] = useState('');
  const chave = useRef(novaChave());

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) sair('expirou');
    else setErro(mensagemDaFalha(falha));
  }, [sair]);

  const carregar = useCallback(() => api.provisao(token).then((lida) => {
    setSituacao(lida);
    setRegiao((atual) => atual || lida.regiaoPadrao);
  }).catch(tratar), [token, tratar]);

  useEffect(() => { void carregar(); }, [carregar]);

  const andando = situacao?.recursos.some(emAndamento) ?? false;
  useEffect(() => {
    if (!andando) return undefined;
    const relogio = window.setInterval(() => void carregar(), ACOMPANHAMENTO_MS);
    return () => window.clearInterval(relogio);
  }, [andando, carregar]);

  async function agir(acao: () => Promise<RecursoNaNuvem>, mensagem: (r: RecursoNaNuvem) => string) {
    setErro('');
    setAviso('');
    setEnviando(true);
    try {
      const recurso = await acao();
      setAviso(mensagem(recurso));
      setHistoricos((h) => {
        const sem = { ...h };
        delete sem[recurso.id];
        return sem;
      });
    } catch (falha) {
      tratar(falha);
    } finally {
      setEnviando(false);
      await carregar();
    }
  }

  function pedir() {
    void agir(() => api.pedirFila(token, regiao, chave.current), (r) => {
      chave.current = novaChave();
      return `Fila pedida em ${r.regiao}: o trabalhador da provisão cria a conta ${r.conta} no grupo ${r.grupoDeRecursos}.`;
    });
  }

  function remover(recurso: RecursoNaNuvem) {
    if (!window.confirm(`Remover a fila ${recurso.fila} e a conta ${recurso.conta} do Azure? As mensagens nela se perdem e não há como desfazer.`)) return;
    void agir(() => api.removerRecurso(token, recurso.id), () => 'Remoção pedida: o trabalhador apaga a conta e, vazio, o grupo de recursos.');
  }

  async function alternarHistorico(recurso: RecursoNaNuvem) {
    if (historicos[recurso.id]) {
      const sem = { ...historicos };
      delete sem[recurso.id];
      setHistoricos(sem);
      return;
    }
    try {
      const lido = await api.recursoNaNuvem(token, recurso.id);
      setHistoricos((h) => ({ ...h, [recurso.id]: lido.historico ?? [] }));
    } catch (falha) {
      tratar(falha);
    }
  }

  if (!situacao || (!situacao.liberada && situacao.recursos.length === 0)) return null;

  const ativa = situacao.recursos.find((r) => r.estado !== 'removido');
  return (
    <section aria-labelledby="titulo-da-nuvem" className="cartao secao-da-nuvem">
      <p className="rotulo-da-pagina">Provisão</p>
      <h2 id="titulo-da-nuvem">Recursos na nuvem</h2>
      <p className="nota-da-secao">
        A fila de comandos do seu dev.kit no Azure: uma conta de armazenamento com a fila, num grupo de recursos só seu, na
        assinatura do dev.kit. O pedido é assíncrono: um trabalhador separado, com identidade própria, cria e remove.
      </p>
      {erro && <p className="erro" role="alert">{erro}</p>}
      {aviso && <p className="aviso" role="status">{aviso}</p>}
      {!situacao.liberada && (
        <p className="aviso-da-nuvem">A provisão não está liberada para o seu usuário: dá para acompanhar e remover o que já existe.</p>
      )}

      {situacao.liberada && !ativa && (
        <form className="pedido-da-fila" aria-label="Pedir a fila" onSubmit={(e) => { e.preventDefault(); pedir(); }}>
          <label>
            Região
            <select value={regiao} onChange={(e) => setRegiao(e.target.value)}>
              {situacao.regioes.map((r) => <option key={r} value={r}>{r}</option>)}
            </select>
          </label>
          <button className="botao botao-primario" type="submit" disabled={enviando}>{enviando ? 'Pedindo…' : 'Criar a fila'}</button>
        </form>
      )}

      {situacao.recursos.length === 0 ? (
        <p className="vazio">Nenhum recurso ainda: a fila é o primeiro, e é por ela que o site manda comandos ao seu dev.kit.</p>
      ) : (
        <ul className="lista-da-nuvem" aria-label="Seus recursos na nuvem">
          {situacao.recursos.map((r) => {
            const estado = estadoDoRecurso(r);
            const historico = historicos[r.id];
            return (
              <li key={r.id} className="recurso-na-nuvem">
                <div className="recurso-cabecalho">
                  <strong>Fila <code>{r.fila}</code></strong>
                  <Selo variante={estado.variante} ponto={emAndamento(r) || r.estado === 'pronto'}>{estado.texto}</Selo>
                </div>
                <dl className="recurso-dados">
                  <dt>Conta</dt><dd><code>{r.conta}</code></dd>
                  <dt>Grupo</dt><dd><code>{r.grupoDeRecursos}</code></dd>
                  <dt>Região</dt><dd>{r.regiao}</dd>
                  <dt>{r.prontoEm ? 'Pronta em' : 'Pedida em'}</dt><dd>{formatar.dataHora(r.prontoEm ?? r.pedidoEm)}</dd>
                </dl>
                {r.ultimoErro && (
                  <p className="erro-da-nuvem">
                    {r.estado === 'falhou' ? 'Motivo' : `Tentativa ${r.tentativas}`}: {r.ultimoErro}
                  </p>
                )}
                <div className="acoes">
                  {r.estado === 'falhou' && (situacao.liberada || r.operacao === 'remover') && (
                    <button className="botao botao-fantasma botao-pequeno" type="button" disabled={enviando}
                      onClick={() => void agir(() => api.repetirProvisao(token, r.id), () => 'Pedido repetido: o trabalhador tenta de novo.')}>
                      Repetir
                    </button>
                  )}
                  {(r.estado === 'pronto' || (r.estado === 'falhou' && r.operacao === 'criar')) && (
                    <button className="botao botao-fantasma botao-pequeno" type="button" disabled={enviando} onClick={() => remover(r)}>Remover</button>
                  )}
                  <button className="botao botao-fantasma botao-pequeno" type="button" aria-expanded={!!historico}
                    aria-label={`Histórico da fila ${r.conta}`} onClick={() => void alternarHistorico(r)}>
                    {historico ? 'Esconder o histórico' : 'Histórico'}
                  </button>
                </div>
                {historico && (
                  <ol className="historico-da-nuvem" aria-label={`Auditoria da fila ${r.conta}`}>
                    {historico.map((e, i) => (
                      <li key={i}>
                        <time dateTime={e.em}>{formatar.dataHora(e.em)}</time> · <span>{e.autor === 'usuario' ? 'você' : e.autor}</span> · {e.mensagem}
                      </li>
                    ))}
                  </ol>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
