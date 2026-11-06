import { useCallback, useEffect, useState } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { CotaDaProvisao, DetalheDoRecurso, PermissaoDeProvisao, RecursoProvisionado, TipoDeAplicativo, TipoDoAplicativo } from '../api/tipos';
import { formatar } from '../formatar';
import { emCurso, INTERVALO_DE_ACOMPANHAMENTO_MS, LinhaDoTempo, rotuloDaRegiao } from './FilaDoControleRemoto';
import { Selo, type VarianteDoSelo } from './Selo';

const NOME_DO_TIPO: Record<TipoDoAplicativo, string> = { 'app-service': 'App Service', 'static-web-app': 'Static Web App' };
const PLURAL_DO_TIPO: Record<TipoDoAplicativo, string> = { 'app-service': 'App Services', 'static-web-app': 'Static Web Apps' };
const OPCAO_DO_TIPO: Record<TipoDoAplicativo, string> = { 'app-service': 'App Service · API ou site', 'static-web-app': 'Static Web App · site estático' };

const moeda = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });

/** "R$ 123,40" (com o espaço comum: o Intl põe o não separável). */
export const reais = (valor: number) => moeda.format(valor).replace(/ /g, ' ');

const ehAplicativo = (r: RecursoProvisionado) => r.tipo === 'app-service' || r.tipo === 'static-web-app';

/** O estado do aplicativo em palavras, com a cor do selo: a falha depois do plano criado é a "Falhou em parte". */
export function situacaoDoAplicativo(r: RecursoProvisionado): { texto: string; variante: VarianteDoSelo; ponto: boolean } {
  const remocao = r.operacao === 'remover';
  switch (r.estado) {
    case 'pedido': return { texto: remocao ? 'Remoção pedida' : r.proximaTentativaEm ? 'Aguardando nova tentativa' : 'Pedida', variante: 'info', ponto: false };
    case 'em-provisao': return { texto: remocao ? 'Removendo' : 'Em provisão', variante: 'aviso', ponto: true };
    case 'pronto': return { texto: 'Pronto', variante: 'sucesso', ponto: true };
    case 'falhou': return { texto: remocao ? 'Remoção falhou' : r.parcial ? 'Falhou em parte' : 'Falhou', variante: 'marca', ponto: false };
    default: return { texto: 'Removido', variante: 'neutro', ponto: false };
  }
}

/** "um", "dois"... só até onde a frase pede: "cabe mais um", "cabe mais 2". */
const quantos = (n: number) => (n === 1 ? 'um' : String(n));

/**
 * A confirmação da remoção com o SEGUNDO FATOR: o código do aplicativo autenticador — o mesmo do Aprovar remoto. Quem ainda
 * não tem o autenticador o configura aqui (a senha, a chave e o primeiro código), e então confirma com um código novo.
 */
function ConfirmacaoDaRemocao({ recurso, rotuloDoPlano, token, aoRemover, aoCancelar, sair }: {
  recurso: RecursoProvisionado;
  rotuloDoPlano: string;
  token: string;
  aoRemover: (mensagem: string) => void;
  aoCancelar: () => void;
  sair: (motivo?: 'expirou') => void;
}) {
  const [ativo, setAtivo] = useState<boolean | null>(null);
  const [codigo, setCodigo] = useState('');
  const [senha, setSenha] = useState('');
  const [chave, setChave] = useState('');
  const [erro, setErro] = useState('');
  const [ocupado, setOcupado] = useState(false);
  const servico = recurso.tipo === 'app-service';

  useEffect(() => {
    let desmontado = false;
    api.estadoDoSegundoFator(token)
      .then((r) => { if (!desmontado) setAtivo(r.ativo); })
      .catch((falha) => { if (!desmontado) setErro(mensagemDaFalha(falha)); });
    return () => { desmontado = true; };
  }, [token]);

  async function executar(acao: () => Promise<void>) {
    if (ocupado) return;
    setOcupado(true);
    setErro('');
    try {
      await acao();
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.status === 401) sair('expirou');
      else setErro(mensagemDaFalha(falha));
    } finally {
      setCodigo('');
      setOcupado(false);
    }
  }

  const remover = () => void executar(async () => {
    await api.removerComSegundoFator(token, recurso.id, codigo);
    aoRemover(`Remoção pedida: o worker apaga ${servico ? `o site ${recurso.nome} e o plano ${recurso.nomeDoPlano}` : `o site ${recurso.nome}`} em instantes.`);
  });

  const configurar = () => void executar(async () => {
    if (!chave) {
      const preparado = await api.prepararSegundoFator(token, senha);
      setChave(preparado.chave);
      setSenha('');
    } else {
      await api.confirmarSegundoFator(token, codigo);
      setChave('');
      setAtivo(true);
    }
  });

  return (
    <div className="confirmacao-da-remocao" role="group" aria-label="Confirmar a remoção">
      <p>
        <strong>Remover o {NOME_DO_TIPO[recurso.tipo as TipoDoAplicativo]} {recurso.rotulo}?</strong>{' '}
        {servico ? `O site sai do ar e o plano ${rotuloDoPlano.split(' · ')[0]} é apagado do Azure.` : 'O site sai do ar e é apagado do Azure.'} Não tem volta.
      </p>
      {ativo === false ? (
        <form className="linha-da-confirmacao" aria-label="Configurar o autenticador" onSubmit={(e) => { e.preventDefault(); configurar(); }}>
          <p>
            {chave
              ? 'Adicione esta chave ao aplicativo autenticador (TOTP, seis dígitos, 30 segundos) e confirme com o primeiro código. Ela expira em dez minutos.'
              : 'Remover pede o segundo fator, e o seu aplicativo autenticador ainda não está configurado. É o mesmo do Aprovar remoto: confirme a senha para gerar a chave.'}
          </p>
          {chave && <code className="chave-do-autenticador">{chave}</code>}
          {chave ? (
            <label>Código de 6 dígitos
              <input className="codigo-do-segundo-fator" aria-label="Código de 6 dígitos" placeholder="○○○○○○" inputMode="numeric" autoComplete="one-time-code"
                maxLength={6} value={codigo} onChange={(e) => setCodigo(e.target.value.replace(/\D/g, ''))} />
            </label>
          ) : (
            <label>Senha atual
              <input type="password" aria-label="Senha atual" autoComplete="current-password" value={senha} onChange={(e) => setSenha(e.target.value)} />
            </label>
          )}
          <button className="botao botao-primario" type="submit" disabled={ocupado || (chave ? codigo.length !== 6 : !senha)}>
            {chave ? 'Ativar autenticador' : 'Configurar autenticador'}
          </button>
          <button className="botao botao-fantasma" type="button" onClick={aoCancelar}>Cancelar</button>
        </form>
      ) : (
        <>
          <p>Confirme com o código do seu aplicativo autenticador. A remoção fica na auditoria, com data e origem.</p>
          <form className="linha-da-confirmacao" aria-label="Remover com segundo fator" onSubmit={(e) => { e.preventDefault(); remover(); }}>
            <label>Código de 6 dígitos
              <input className="codigo-do-segundo-fator" aria-label="Código de 6 dígitos" placeholder="○○○○○○" inputMode="numeric" autoComplete="one-time-code"
                maxLength={6} value={codigo} disabled={ativo === null} onChange={(e) => setCodigo(e.target.value.replace(/\D/g, ''))} />
            </label>
            <button className="botao botao-primario" type="submit" disabled={ocupado || !ativo || codigo.length !== 6}>Remover com segundo fator</button>
            <button className="botao botao-fantasma" type="button" onClick={aoCancelar}>Cancelar</button>
          </form>
        </>
      )}
      {erro && <p className="erro" role="alert">{erro}</p>}
    </div>
  );
}

/**
 * Um aplicativo da conta: o estado, os nomes no Azure, o plano e o custo, o endereço quando pronto, a falha (a PARCIAL diz o
 * que ficou no Azure e o que faltou), as ações que o estado permite e o histórico da auditoria sob demanda. Remover abre a
 * confirmação com o segundo fator, no próprio cartão.
 */
function AplicativoDaConta({ recurso: r, rotuloDoPlano, token, aoMudar, tratar, sair }: {
  recurso: RecursoProvisionado;
  rotuloDoPlano: string;
  token: string;
  aoMudar: (mensagem: string) => void;
  tratar: (falha: unknown) => void;
  sair: (motivo?: 'expirou') => void;
}) {
  const [detalhe, setDetalhe] = useState<DetalheDoRecurso | null>(null);
  const [aberto, setAberto] = useState(false);
  const [removendo, setRemovendo] = useState(false);
  const situacao = situacaoDoAplicativo(r);
  const removido = r.estado === 'removido';
  const pronto = r.estado === 'pronto';
  const nomeDoTipo = NOME_DO_TIPO[r.tipo as TipoDoAplicativo];

  useEffect(() => {
    if (aberto) api.detalheDoRecurso(token, r.id).then(setDetalhe).catch(tratar);
  }, [aberto, r, token, tratar]);

  async function repetir() {
    try {
      await api.repetirProvisao(token, r.id);
      aoMudar(r.parcial ? 'Pedido devolvido ao worker: ele retoma do aplicativo, sem criar o plano de novo.' : 'Pedido devolvido ao worker: ele tenta de novo do começo.');
    } catch (falha) {
      tratar(falha);
    }
  }

  return (
    <article className={`recurso-provisionado${removido ? ' recurso-removido' : ''}`} aria-label={`${nomeDoTipo} ${r.rotulo}`}>
      <header className="recurso-cabecalho">
        <h3>{nomeDoTipo} <code>{r.rotulo}</code></h3>
        <Selo variante={situacao.variante} ponto={situacao.ponto}>{situacao.texto}</Selo>
      </header>
      <dl className="recurso-dados">
        <dt>Nome no Azure</dt><dd><code>{r.nome}</code></dd>
        <dt>Grupo</dt><dd><code>{r.grupoDeRecursos}</code></dd>
        <dt>Região</dt><dd>{r.regiao}</dd>
        <dt>Plano</dt><dd>{rotuloDoPlano}{r.parcial ? ' (criado)' : ''}</dd>
        {pronto ? (
          <>
            <dt>Custo do mês</dt><dd>{reais(r.custoMensal ?? 0)} (estimado)</dd>
            <dt>Pronto em</dt><dd>{formatar.dataHora(r.prontoEm)}</dd>
          </>
        ) : (
          <>
            <dt>{removido ? 'Removido em' : 'Pedida em'}</dt><dd>{formatar.dataHora(removido ? r.atualizadoEm : r.criadoEm)}</dd>
            {r.estado === 'em-provisao' && <><dt>Custo do mês</dt><dd>{reais(r.custoMensal ?? 0)}</dd></>}
          </>
        )}
      </dl>
      {pronto && r.endpoint && (
        <p className="endereco-do-recurso">Endereço <a href={r.endpoint} target="_blank" rel="noreferrer">{r.endpoint}</a></p>
      )}
      {r.parcial ? (
        <p className="erro" role="alert">
          Falha parcial: o plano <code>{r.nomeDoPlano}</code> foi criado e o aplicativo não — {r.erro.replace(/\.$/, '')}. Repetir retoma do
          aplicativo; Remover apaga também o plano que ficou.
        </p>
      ) : r.erro && !removido && (
        <p className={r.estado === 'falhou' ? 'erro' : 'nota-da-cota'} role={r.estado === 'falhou' ? 'alert' : undefined}>{r.erro}</p>
      )}
      <div className="recurso-acoes">
        <button className="botao botao-fantasma botao-pequeno" type="button" aria-expanded={aberto} onClick={() => setAberto(!aberto)}>
          {aberto ? 'Ocultar histórico' : 'Histórico'}
        </button>
        {r.estado === 'falhou' && (
          <button className="botao botao-fantasma botao-pequeno" type="button" onClick={() => void repetir()}>Repetir</button>
        )}
        {(pronto || r.estado === 'falhou') && (
          <button className="botao botao-fantasma botao-pequeno" type="button" aria-expanded={removendo} onClick={() => setRemovendo(!removendo)}>Remover</button>
        )}
      </div>
      {removendo && (pronto || r.estado === 'falhou') && (
        <ConfirmacaoDaRemocao recurso={r} rotuloDoPlano={rotuloDoPlano} token={token} sair={sair} aoCancelar={() => setRemovendo(false)}
          aoRemover={(mensagem) => { setRemovendo(false); aoMudar(mensagem); }} />
      )}
      {aberto && (detalhe ? <LinhaDoTempo eventos={detalhe.eventos} /> : <p className="carregando" role="status">Carregando…</p>)}
    </article>
  );
}

/** Um cartão da cota: quantos do tipo estão ativos, e se cabe mais. */
function CotaDoTipo({ tipo, cota }: { tipo: TipoDeAplicativo; cota: CotaDaProvisao }) {
  const uso = cota.tipos.find((t) => t.tipo === tipo.tipo) ?? { ativos: 0, maximo: tipo.maximo };
  const restante = Math.max(0, uso.maximo - uso.ativos);
  const maisBarato = [...tipo.planos].sort((a, b) => a.custoMensal - b.custoMensal)[0];
  return (
    <div className="cota-item">
      <span className="cota-rotulo">{NOME_DO_TIPO[tipo.tipo]}</span>
      <strong className="cota-valor">{uso.ativos} de {uso.maximo}</strong>
      <span className="nota-da-cota">
        {restante === 0 ? 'Cota cheia: remova um para criar outro.' : `${maisBarato ? `Plano ${maisBarato.rotulo} · c` : 'C'}abe mais ${quantos(restante)}.`}
      </span>
    </div>
  );
}

/** O custo estimado do mês contra o teto: o medidor com a marca do alerta. */
function CustoDoMes({ cota }: { cota: CotaDaProvisao }) {
  const temTeto = cota.limiteDeCusto > 0;
  const preenchido = temTeto ? Math.min(100, (cota.custoDoMes / cota.limiteDeCusto) * 100) : 0;
  const estado = cota.bloqueado ? ' medidor-no-teto' : cota.alerta ? ' medidor-em-alerta' : '';
  return (
    <div className="cota-item cota-do-custo">
      <span className="cota-rotulo">Custo estimado do mês</span>
      <strong className="cota-valor">{reais(cota.custoDoMes)} {temTeto && <small>de {reais(cota.limiteDeCusto)}</small>}</strong>
      {temTeto && (
        <div className="medidor" role="meter" aria-label="Custo do mês" aria-valuemin={0} aria-valuemax={cota.limiteDeCusto} aria-valuenow={cota.custoDoMes}>
          <span className={`medidor-preenchido${estado}`} style={{ width: `${preenchido}%` }} />
          <span className="medidor-marca" style={{ left: `${cota.alertaDeCustoPercentual}%` }} title={`Alerta em ${cota.alertaDeCustoPercentual}%`} />
        </div>
      )}
      <span className="nota-da-cota">
        {temTeto ? `Alerta em ${cota.alertaDeCustoPercentual}% · novos pedidos param em 100%.` : 'Sem limite de custo configurado.'}
      </span>
    </div>
  );
}

/**
 * SITES E APIS NO AZURE na conta (controle remoto v2, item 12; proposta `conta` v17/v18): o App Service e o Static Web App
 * do usuário, pelo MESMO motor da fila — o pedido é assíncrono e o worker cria e remove. Só aparece com a provisão liberada
 * e numa API que publica os aplicativos. Mostra a cota de cada tipo e o custo estimado do mês com o alerta; o tipo de cota
 * cheia fica desabilitado no pedido, e o custo no teto bloqueia novos pedidos (a API recusa do mesmo jeito). Remover pede o
 * segundo fator.
 */
export function SitesNoAzure({ token, sair }: { token: string; sair: (motivo?: 'expirou') => void }) {
  const [permissao, setPermissao] = useState<PermissaoDeProvisao | null>(null);
  const [recursos, setRecursos] = useState<RecursoProvisionado[] | null>(null);
  const [cota, setCota] = useState<CotaDaProvisao | null>(null);
  const [tipo, setTipo] = useState<TipoDoAplicativo | ''>('');
  const [nome, setNome] = useState('');
  const [regiao, setRegiao] = useState('');
  const [plano, setPlano] = useState('');
  const [enviando, setEnviando] = useState(false);
  const [erro, setErro] = useState('');
  const [aviso, setAviso] = useState('');

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) sair('expirou');
    else setErro(mensagemDaFalha(falha));
  }, [sair]);

  const carregar = useCallback(() => Promise.all([api.recursosProvisionados(token), api.cotaDaProvisao(token)])
    .then(([lidos, lida]) => { setRecursos(lidos.filter(ehAplicativo)); setCota(lida); })
    .catch(tratar), [token, tratar]);

  useEffect(() => {
    api.permissaoDeProvisao(token).then((lida) => {
      setPermissao(lida);
      if (lida.liberada && lida.aplicativos?.length) void carregar();
    }).catch((falha) => {
      if (falha instanceof ErroDaApi && falha.status === 401) sair('expirou');
    });
  }, [token, carregar, sair]);

  const tipos = permissao?.aplicativos ?? [];
  const cheio = (t: TipoDoAplicativo) => {
    const uso = cota?.tipos.find((u) => u.tipo === t);
    return !!uso && uso.ativos >= uso.maximo;
  };

  // O tipo do pedido: o escolhido, enquanto couber; senão o primeiro que cabe. A região e o plano valem se o tipo os
  // aceita; senão, os padrão dele.
  const pedido = tipos.find((t) => t.tipo === tipo);
  const escolhido = pedido && !cheio(pedido.tipo) ? pedido : tipos.find((t) => !cheio(t.tipo)) ?? pedido ?? tipos[0];
  const regiaoDoPedido = escolhido?.regioes.includes(regiao) ? regiao : escolhido?.regiaoPadrao ?? '';
  const planoDoPedido = escolhido?.planos.some((p) => p.id === plano) ? plano : escolhido?.planos[0]?.id ?? '';

  const acompanhar = (recursos ?? []).some(emCurso);
  useEffect(() => {
    if (!acompanhar) return;
    const relogio = window.setInterval(() => void carregar(), INTERVALO_DE_ACOMPANHAMENTO_MS);
    return () => window.clearInterval(relogio);
  }, [acompanhar, carregar]);

  if (!permissao?.liberada || tipos.length === 0) return null;

  const rotuloDoPlano = (r: RecursoProvisionado) =>
    tipos.find((t) => t.tipo === r.tipo)?.planos.find((p) => p.id === r.plano)?.rotulo ?? r.plano ?? '';

  async function criar() {
    if (!escolhido) return;
    setErro('');
    setAviso('');
    setEnviando(true);
    try {
      const aceito = await api.pedirAplicativo(token, { tipo: escolhido.tipo, nome: nome.trim(), regiao: regiaoDoPedido, plano: planoDoPedido });
      setAviso(`Pedido aceito: o worker cria ${escolhido.tipo === 'app-service' ? 'o plano e o site' : 'o site'} ${aceito.nome} em instantes. Esta tela acompanha.`);
      setNome('');
      await carregar();
    } catch (falha) {
      tratar(falha);
      await carregar();
    } finally {
      setEnviando(false);
    }
  }

  async function mudou(mensagem: string) {
    setErro('');
    setAviso(mensagem);
    await carregar();
  }

  const bloqueado = !!cota?.bloqueado;
  const semVaga = !escolhido || cheio(escolhido.tipo);
  return (
    <section aria-labelledby="titulo-sites" className="cartao secao-da-provisao secao-dos-sites">
      <p className="rotulo-da-pagina">Provisão</p>
      <h2 id="titulo-sites">Sites e APIs no Azure</h2>
      <p className="nota-da-secao">
        App Service e Static Web App no mesmo grupo de recursos só seu, na assinatura do dev.kit. É o mesmo motor da fila: o
        pedido é assíncrono, um processo separado com identidade própria cria e remove, e esta tela acompanha. Cada tipo tem uma
        cota por usuário, o custo do mês tem limite com alerta, e remover pede o segundo fator.
      </p>
      {cota && (
        <div className="cota-da-provisao" aria-label="Cota e custo">
          {tipos.map((t) => <CotaDoTipo key={t.tipo} tipo={t} cota={cota} />)}
          <CustoDoMes cota={cota} />
        </div>
      )}
      {cota?.bloqueado ? (
        <p className="aviso aviso-de-custo" role="status">
          O custo estimado do mês chegou ao seu limite ({reais(cota.custoDoMes)} de {reais(cota.limiteDeCusto)}). Novos pedidos ficam
          bloqueados até você remover um aplicativo; o que já existe continua no ar.
        </p>
      ) : cota?.alerta && (
        <p className="aviso aviso-de-custo" role="status">
          O custo estimado do mês passou de {cota.alertaDeCustoPercentual}% do seu limite ({reais(cota.custoDoMes)} de {reais(cota.limiteDeCusto)}). Em
          100%, novos pedidos ficam bloqueados; o que já existe continua no ar.
        </p>
      )}
      {erro && <p className="erro" role="alert">{erro}</p>}
      {aviso && <p className="aviso" role="status">{aviso}</p>}
      <form className="formulario-da-provisao" aria-label="Criar um recurso" onSubmit={(e) => { e.preventDefault(); void criar(); }}>
        <label>
          Tipo
          <select aria-label="Tipo" value={escolhido?.tipo ?? ''} disabled={enviando || bloqueado}
            onChange={(e) => setTipo(e.target.value as TipoDoAplicativo)}>
            {tipos.map((t) => (
              <option key={t.tipo} value={t.tipo} disabled={cheio(t.tipo)}>
                {cheio(t.tipo) ? `${NOME_DO_TIPO[t.tipo]} · cota cheia` : OPCAO_DO_TIPO[t.tipo]}
              </option>
            ))}
          </select>
        </label>
        <label>
          Nome
          <input aria-label="Nome" placeholder="site-novo" required minLength={3} maxLength={20} pattern="[a-z][a-z0-9\-]*[a-z0-9]"
            title="3 a 20 caracteres: letras minúsculas, dígitos e hífen, começando por letra." autoComplete="off" spellCheck={false} value={nome}
            disabled={enviando || bloqueado || semVaga} onChange={(e) => setNome(e.target.value.toLowerCase())} />
        </label>
        <label>
          Região
          <select aria-label="Região" value={regiaoDoPedido} disabled={enviando || bloqueado || semVaga} onChange={(e) => setRegiao(e.target.value)}>
            {(escolhido?.regioes ?? []).map((r) => <option key={r} value={r}>{rotuloDaRegiao(r)}</option>)}
          </select>
        </label>
        <label>
          Plano
          <select aria-label="Plano" value={planoDoPedido} disabled={enviando || bloqueado || semVaga} onChange={(e) => setPlano(e.target.value)}>
            {(escolhido?.planos ?? []).map((p) => <option key={p.id} value={p.id}>{p.rotulo} · {reais(p.custoMensal)}/mês</option>)}
          </select>
        </label>
        <button className="botao botao-primario" type="submit" disabled={enviando || bloqueado || semVaga || !recursos}>Criar</button>
        <p className="nota-da-cota">
          Até {tipos.map((t) => `${t.maximo} ${t.maximo === 1 ? NOME_DO_TIPO[t.tipo] : PLURAL_DO_TIPO[t.tipo]}`).join(' e ')} por usuário. O nome no
          Azure ganha o prefixo e um sufixo únicos.
        </p>
      </form>
      {!recursos ? (
        !erro && <p className="carregando" role="status">Carregando…</p>
      ) : recursos.length === 0 ? (
        <p className="vazio">Nenhum site ou API ainda: escolha o tipo, dê um nome e crie.</p>
      ) : (
        <div className="recursos-da-conta">
          {recursos.map((r) => (
            <AplicativoDaConta key={r.id} recurso={r} rotuloDoPlano={rotuloDoPlano(r)} token={token} tratar={tratar} sair={sair}
              aoMudar={(m) => void mudou(m)} />
          ))}
        </div>
      )}
    </section>
  );
}
