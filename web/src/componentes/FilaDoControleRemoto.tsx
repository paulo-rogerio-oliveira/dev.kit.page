import { useCallback, useEffect, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { DetalheDoRecurso, EventoDaProvisao, PermissaoDeProvisao, RecursoProvisionado } from '../api/tipos';
import { formatar } from '../formatar';
import { Selo, type VarianteDoSelo } from './Selo';

/** De quanto em quanto tempo a seção relê os recursos enquanto o worker executa um pedido. */
export const INTERVALO_DE_ACOMPANHAMENTO_MS = 5000;

/** Os nomes das regiões que o site conhece; a fora da lista aparece só pelo código. */
const NOME_DA_REGIAO: Record<string, string> = { brazilsouth: 'Brasil (Sul)', eastus2: 'Leste dos EUA 2' };

const rotuloDaRegiao = (regiao: string) => (NOME_DA_REGIAO[regiao] ? `${NOME_DA_REGIAO[regiao]} · ${regiao}` : regiao);

/** O pedido ainda nas mãos do worker: a seção acompanha. */
const emCurso = (r: RecursoProvisionado) => r.estado === 'pedido' || r.estado === 'em-provisao';

/** O estado em palavras, com a cor do selo — a remoção tem as próprias. */
export function situacaoDoRecurso(r: RecursoProvisionado): { texto: string; variante: VarianteDoSelo } {
  const remocao = r.operacao === 'remover';
  switch (r.estado) {
    case 'pedido': return { texto: remocao ? 'Remoção pedida' : r.proximaTentativaEm ? 'Aguardando nova tentativa' : 'Pedida', variante: 'info' };
    case 'em-provisao': return { texto: remocao ? 'Removendo' : 'Em provisão', variante: 'aviso' };
    case 'pronto': return { texto: 'Pronta', variante: 'sucesso' };
    case 'falhou': return { texto: remocao ? 'Remoção falhou' : 'Falhou', variante: 'marca' };
    default: return { texto: 'Removida', variante: 'neutro' };
  }
}

const ROTULO_DO_EVENTO: Record<string, string> = {
  'pedido': 'Pedido', 'inicio': 'Em provisão', 'pronto': 'Pronta', 'falha-transitoria': 'Falhou (transitória)', 'falha': 'Falhou',
  'repeticao': 'Repetição', 'remocao-pedida': 'Remoção pedida', 'removido': 'Removida',
};

/** "03/11 11:40" no fuso de quem lê. */
const horaCurta = (iso: string) => new Date(iso).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }).replace(',', '');

function LinhaDoTempo({ eventos }: { eventos: EventoDaProvisao[] }) {
  return (
    <ol className="linha-do-tempo" aria-label="Histórico">
      {eventos.map((e, i) => (
        <li key={i}>
          <time dateTime={e.em}>{horaCurta(e.em)}</time>
          {[ROTULO_DO_EVENTO[e.evento] ?? e.evento, e.autor !== 'worker' ? e.autor : null, e.detalhe || null].filter(Boolean).join(' · ')}
        </li>
      ))}
    </ol>
  );
}

/**
 * Um recurso da conta: o estado, os nomes no Azure, o erro (com a próxima tentativa), as ações que o estado permite —
 * Repetir só no que falhou, Remover no pronto e no que falhou — e o histórico da auditoria sob demanda.
 */
function RecursoDaConta({ recurso, token, aoMudar, tratar }: {
  recurso: RecursoProvisionado;
  token: string;
  aoMudar: (mensagem: string) => void;
  tratar: (falha: unknown) => void;
}) {
  const [detalhe, setDetalhe] = useState<DetalheDoRecurso | null>(null);
  const [aberto, setAberto] = useState(false);
  const situacao = situacaoDoRecurso(recurso);
  const removido = recurso.estado === 'removido';

  // O histórico aberto acompanha o recurso (a varredura troca o objeto a cada mudança).
  useEffect(() => {
    if (aberto) api.detalheDoRecurso(token, recurso.id).then(setDetalhe).catch(tratar);
  }, [aberto, recurso, token, tratar]);

  async function agir(acao: () => Promise<unknown>, mensagem: string) {
    try {
      await acao();
      aoMudar(mensagem);
    } catch (falha) {
      tratar(falha);
    }
  }

  function remover() {
    if (!window.confirm(`Remover a conta ${recurso.nome} e a fila ${recurso.fila} do Azure? As mensagens que estiverem nela se perdem.`)) return;
    void agir(() => api.removerRecurso(token, recurso.id), `Remoção pedida: o worker apaga a conta ${recurso.nome} em instantes.`);
  }

  return (
    <article className={`recurso-provisionado${removido ? ' recurso-removido' : ''}`} aria-label={`Fila ${recurso.fila} na conta ${recurso.nome}`}>
      <header className="recurso-cabecalho">
        <h3>Fila <code>{recurso.fila}</code></h3>
        <Selo variante={situacao.variante} ponto={recurso.estado === 'pronto'}>{situacao.texto}</Selo>
      </header>
      <dl className="recurso-dados">
        <dt>Conta</dt><dd><code>{recurso.nome}</code></dd>
        <dt>Grupo</dt><dd><code>{recurso.grupoDeRecursos}</code></dd>
        <dt>Região</dt><dd>{recurso.regiao}</dd>
        {recurso.prontoEm && !removido
          ? <><dt>Pronta em</dt><dd>{formatar.dataHora(recurso.prontoEm)}</dd></>
          : <><dt>{removido ? 'Removida em' : 'Pedida em'}</dt><dd>{formatar.dataHora(removido ? recurso.atualizadoEm : recurso.criadoEm)}</dd></>}
      </dl>
      {recurso.erro && !removido && (
        <p className={recurso.estado === 'falhou' ? 'erro' : 'nota-da-cota'} role={recurso.estado === 'falhou' ? 'alert' : undefined}>
          {recurso.estado === 'pedido' && recurso.proximaTentativaEm ? `Nova tentativa às ${horaCurta(recurso.proximaTentativaEm)} (tentativa ${recurso.tentativas}): ` : ''}
          {recurso.erro}
        </p>
      )}
      <div className="recurso-acoes">
        <button className="botao botao-fantasma botao-pequeno" type="button" aria-expanded={aberto} onClick={() => setAberto(!aberto)}>
          {aberto ? 'Ocultar histórico' : 'Histórico'}
        </button>
        {recurso.estado === 'falhou' && (
          <button className="botao botao-fantasma botao-pequeno" type="button"
            onClick={() => void agir(() => api.repetirProvisao(token, recurso.id), 'Pedido devolvido ao worker: ele tenta de novo do começo.')}>
            Repetir
          </button>
        )}
        {(recurso.estado === 'pronto' || recurso.estado === 'falhou') && (
          <button className="botao botao-fantasma botao-pequeno" type="button" onClick={remover}>Remover</button>
        )}
      </div>
      {aberto && (detalhe ? <LinhaDoTempo eventos={detalhe.eventos} /> : <p className="carregando" role="status">Carregando…</p>)}
    </article>
  );
}

/**
 * A FILA DO CONTROLE REMOTO na conta (controle remoto v2, item 4; proposta `conta` v12): a ação nova da área do usuário,
 * que SÓ aparece com a provisão liberada — a permissão do admin hoje, o plano da assinatura depois (`GET
 * /api/provisao/permissao`). Sem ela a seção não existe, e a API recusa do mesmo jeito. O pedido é assíncrono: a seção
 * relê os recursos enquanto o worker executa (pedido, em provisão, pronto ou falhou).
 */
export function FilaDoControleRemoto({ token, sair }: { token: string; sair: (motivo?: 'expirou') => void }) {
  const [permissao, setPermissao] = useState<PermissaoDeProvisao | null>(null);
  const [recursos, setRecursos] = useState<RecursoProvisionado[] | null>(null);
  const [regiao, setRegiao] = useState('');
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState('');
  const [aviso, setAviso] = useState('');

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) sair('expirou');
    else setErro(mensagemDaFalha(falha));
  }, [sair]);

  const carregar = useCallback(() => api.recursosProvisionados(token).then(setRecursos).catch(tratar), [token, tratar]);

  useEffect(() => {
    api.permissaoDeProvisao(token).then((lida) => {
      setPermissao(lida);
      setRegiao(lida.regiaoPadrao);
      if (lida.liberada) void carregar();
    }).catch((falha) => {
      // Sem a rota (uma API anterior à provisão) a seção só não aparece; o 401 encerra a sessão como no resto da conta.
      if (falha instanceof ErroDaApi && falha.status === 401) sair('expirou');
    });
  }, [token, carregar, sair]);

  const acompanhar = (recursos ?? []).some(emCurso);
  useEffect(() => {
    if (!acompanhar) return;
    const relogio = window.setInterval(() => void carregar(), INTERVALO_DE_ACOMPANHAMENTO_MS);
    return () => window.clearInterval(relogio);
  }, [acompanhar, carregar]);

  if (!permissao?.liberada) return null;

  const ativos = (recursos ?? []).filter((r) => r.estado !== 'removido').length;
  const cotaCheia = ativos >= permissao.maximoDeFilas;

  async function criar() {
    setErro('');
    setAviso('');
    setEnviando(true);
    try {
      await api.pedirFila(token, regiao);
      setAviso('Pedido aceito: o worker cria o grupo, a conta e a fila em instantes. Esta tela acompanha.');
      await carregar();
    } catch (falha) {
      tratar(falha);
    } finally {
      setEnviando(false);
    }
  }

  async function mudou(mensagem: string) {
    setErro('');
    setAviso(mensagem);
    await carregar();
  }

  return (
    <section aria-labelledby="titulo-provisao" className="cartao secao-da-provisao">
      <p className="rotulo-da-pagina">Provisão</p>
      <h2 id="titulo-provisao">Fila do controle remoto</h2>
      <p className="nota-da-secao">
        A fila de comandos do seu dev.kit no Azure: uma conta de armazenamento com a fila, num grupo de recursos só seu, na
        assinatura do dev.kit. O pedido é assíncrono — um processo separado, com identidade própria, cria e remove — e esta tela
        acompanha: pedido, em provisão, pronto ou falhou.
      </p>
      {erro && <p className="erro" role="alert">{erro}</p>}
      {aviso && <p className="aviso" role="status">{aviso}</p>}
      <form className="formulario-da-provisao" aria-label="Criar a fila" onSubmit={(e) => { e.preventDefault(); void criar(); }}>
        <label>
          Região
          <select aria-label="Região" value={regiao} disabled={cotaCheia || enviando} onChange={(e) => setRegiao(e.target.value)}>
            {permissao.regioes.map((r) => <option key={r} value={r}>{rotuloDaRegiao(r)}</option>)}
          </select>
        </label>
        <button className="botao botao-primario" type="submit" disabled={cotaCheia || enviando || !recursos}>Criar a fila</button>
        {cotaCheia && (
          <p className="nota-da-cota">
            {permissao.maximoDeFilas === 1 ? 'Uma fila por usuário' : `Até ${permissao.maximoDeFilas} filas por usuário`}: remova a atual para criar outra.
          </p>
        )}
      </form>
      {!recursos ? (
        !erro && <p className="carregando" role="status">Carregando…</p>
      ) : recursos.length === 0 ? (
        <p className="vazio">Nenhuma fila ainda: escolha a região e crie a sua.</p>
      ) : (
        <div className="recursos-da-conta">
          {recursos.map((r) => <RecursoDaConta key={r.id} recurso={r} token={token} aoMudar={(m) => void mudou(m)} tratar={tratar} />)}
        </div>
      )}
    </section>
  );
}
