import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { ConvencaoDaEmpresa, NivelDeConvencao, RepositorioDaEmpresa } from '../api/tipos';
import { Abas } from '../componentes/Abas';
import { Chips } from '../componentes/Chips';
import { ContextoPorNivel } from '../componentes/ContextoPorNivel';
import { DesignCompartilhado } from '../componentes/DesignCompartilhado';
import { ehContexto, temaDa } from '../componentes/EditorDeContexto';
import { Pagina } from '../componentes/Pagina';
import { PainelLateral } from '../componentes/PainelLateral';
import { Selo } from '../componentes/Selo';
import { MenuDaEmpresa, Topo } from '../componentes/Topo';
import { formatar } from '../formatar';
import { papelDa, useSessao } from '../sessao';

/**
 * As abas da página: o catálogo de repositórios, as convenções, o contexto (US #550) e o design compartilhado (US #669).
 * A ativa fica na URL (?aba=).
 */
const ABAS = [
  { id: 'repositorios', rotulo: 'Repositórios' },
  { id: 'convencoes', rotulo: 'Convenções' },
  { id: 'contexto', rotulo: 'Contexto' },
  { id: 'design', rotulo: 'Design', novo: true },
];

type IdDaAba = 'repositorios' | 'convencoes' | 'contexto' | 'design';

const NOME_DO_NIVEL: Record<NivelDeConvencao, string> = { empresa: 'Empresa', time: 'Time', usuario: 'Usuário' };

/** Os tipos de branch com padrão no dev.kit; outro tipo (o do work item) é digitado como branch.<tipo>. */
const TIPOS_DE_BRANCH = ['feature', 'bug', 'hotfix'];

type FiltroDeNivel = 'todas' | NivelDeConvencao;

/**
 * O que a empresa compartilha com o dev.kit (US #491, /empresa/conhecimento) — para AUDITAR (quem gravou e quando) e
 * EDITAR: o catálogo de repositórios (o caminho e a descrição que o agente grava ao terminar cada task) e as
 * convenções em seus níveis (empresa, time e usuário; no dev.kit vale usuário > time > empresa). É do gestor e do
 * dev: o gestor vê todos os níveis e grava a convenção da empresa; o dev vê a da empresa, as dos times e as dele.
 */
export function EmpresaConhecimento() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const ehGestor = papelDa(sessao!) === 'gestor';
  const [busca, setBusca] = useSearchParams();
  const pedida = busca.get('aba');
  const aba: IdDaAba = pedida === 'convencoes' || pedida === 'contexto' || pedida === 'design' ? pedida : 'repositorios';

  const [repositorios, setRepositorios] = useState<RepositorioDaEmpresa[] | null>(null);
  const [convencoes, setConvencoes] = useState<ConvencaoDaEmpresa[] | null>(null);
  const [aviso, setAviso] = useState('');
  const [erro, setErro] = useState('');

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) {
      sair('expirou');
      return;
    }
    setErro(mensagemDaFalha(falha));
  }, [sair]);

  const carregar = useCallback(async () => {
    try {
      const [repos, convs] = await Promise.all([api.repositoriosDaEmpresa(token), api.convencoesDaEmpresa(token)]);
      setRepositorios(repos);
      setConvencoes(convs);
    } catch (falha) {
      tratar(falha);
    }
  }, [token, tratar]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  /** Roda uma escrita: limpa as mensagens, grava, avisa e recarrega. */
  async function escrever(acao: () => Promise<unknown>, mensagem: string) {
    setErro('');
    setAviso('');
    try {
      await acao();
      setAviso(mensagem);
      await carregar();
      return true;
    } catch (falha) {
      tratar(falha);
      return false;
    }
  }

  return (
    <div className="pagina">
      <Topo secao="conhecimento da empresa">
        {ehGestor ? <MenuDaEmpresa /> : <Link className="botao botao-fantasma" to="/conta">Conta</Link>}
      </Topo>
      <Pagina rotulo={sessao!.empresa ?? 'Empresa'} titulo="Repositórios e convenções"subtitulo="O que a empresa compartilha com o dev.kit: auditado aqui, com quem gravou e quando, e editável.">
        {erro && <p className="erro" role="alert">{erro}</p>}
        {aviso && <p className="aviso" role="status">{aviso}</p>}
        <Abas rotulo="Conhecimento da empresa" abas={ABAS} ativa={aba} aoMudar={(id) => { setAviso(''); setErro(''); setBusca({ aba: id }, { replace: true }); }} prefixo="conhecimento">
          {aba === 'repositorios' && <AbaRepositorios repositorios={repositorios} ehGestor={ehGestor} token={token} escrever={escrever} />}
          {aba === 'convencoes' && <AbaConvencoes convencoes={convencoes} ehGestor={ehGestor} token={token} escrever={escrever} />}
          {aba === 'contexto' && <AbaContexto convencoes={convencoes} ehGestor={ehGestor} token={token} escrever={escrever} tratar={tratar} />}
          {aba === 'design' && <DesignCompartilhado token={token} comEmpresa={Boolean(sessao!.empresa)} tratar={tratar} />}
        </Abas>
      </Pagina>
    </div>
  );
}

type Escrever = (acao: () => Promise<unknown>, mensagem: string) => Promise<boolean>;

/** O catálogo de repositórios: a lista com o autor e a data, e o formulário que grava (ou corrige) uma descrição. */
function AbaRepositorios({ repositorios, ehGestor, token, escrever }: {
  repositorios: RepositorioDaEmpresa[] | null; ehGestor: boolean; token: string; escrever: Escrever;
}) {
  const [origem, setOrigem] = useState('');
  const [descricao, setDescricao] = useState('');

  async function gravar(evento: FormEvent) {
    evento.preventDefault();
    if (await escrever(() => api.gravarRepositorio(token, origem.trim(), descricao.trim()), `${origem.trim()} gravado no catálogo da empresa.`)) {
      setOrigem('');
      setDescricao('');
    }
  }

  async function remover(r: RepositorioDaEmpresa) {
    if (!window.confirm(`Remover ${r.origem} do catálogo da empresa?`)) return;
    await escrever(() => api.removerRepositorio(token, r.origem), `${r.origem} removido do catálogo da empresa.`);
  }

  return (
    <div className="com-lateral">
      <section className="cartao cartao-de-tabela" aria-label="Repositórios da empresa">
        {!repositorios ? (
          <p className="carregando" role="status">Carregando…</p>
        ) : repositorios.length === 0 ? (
          <p className="vazio">Nenhum repositório descrito ainda — o agente do dev.kit grava cada um ao terminar a task.</p>
        ) : (
          <div className="tabela-rolavel-simples">
            <table className="tabela tabela-do-cartao">
              <thead>
                <tr><th>Repositório</th><th>Descrição</th><th>Autor</th><th>Atualizado em</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {repositorios.map((r) => (
                  <tr key={r.origem}>
                    <td><code className="codigo">{r.origem}</code></td>
                    <td>{r.descricao}</td>
                    <td>{r.autor || '—'}</td>
                    <td>{formatar.dataHora(r.atualizadoEm)}</td>
                    <td>
                      <span className="acoes">
                        <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Editar ${r.origem}`}
                          onClick={() => { setOrigem(r.origem); setDescricao(r.descricao); }}>Editar</button>
                        {ehGestor && (
                          <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Remover ${r.origem}`} onClick={() => void remover(r)}>Remover</button>
                        )}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <PainelLateral id="titulo-gravar-repositorio" titulo="Descrever um repositório">
        <form className="formulario-lateral" onSubmit={(e) => void gravar(e)} aria-label="Descrever um repositório">
          <label>
            Caminho
            <input value={origem} onChange={(e) => setOrigem(e.target.value)} maxLength={400} placeholder="https://github.com/empresa/repositorio" required />
          </label>
          <label>
            Descrição
            <textarea value={descricao} onChange={(e) => setDescricao(e.target.value)} maxLength={1000} rows={4} placeholder="Para que serve, a tecnologia, o que mora nele" required />
          </label>
          <p className="nota-do-formulario">O mesmo caminho troca a descrição. O dev.kit consulta este catálogo para resolver o repositório de um pedido.</p>
          <button className="botao botao-primario botao-largo" type="submit" disabled={!origem.trim() || !descricao.trim()}>Gravar</button>
        </form>
      </PainelLateral>
    </div>
  );
}

/** As convenções em seus níveis: o filtro por nível, a lista com o autor e a data, e o formulário que grava. */
function AbaConvencoes({ convencoes, ehGestor, token, escrever }: {
  convencoes: ConvencaoDaEmpresa[] | null; ehGestor: boolean; token: string; escrever: Escrever;
}) {
  const [filtro, setFiltro] = useState<FiltroDeNivel>('todas');
  const [nivel, setNivel] = useState<NivelDeConvencao>(ehGestor ? 'empresa' : 'time');
  const [time, setTime] = useState('');
  const [chave, setChave] = useState('branch.feature');
  const [valor, setValor] = useState('');

  // O contexto (contexto.<tema>) tem a aba dele: aqui ficam as convenções de branch.
  const lista = (convencoes ?? []).filter((c) => !ehContexto(c.chave));
  const visiveis = filtro === 'todas' ? lista : lista.filter((c) => c.nivel === filtro);
  const contagem = (n: NivelDeConvencao) => lista.filter((c) => c.nivel === n).length;

  async function gravar(evento: FormEvent) {
    evento.preventDefault();
    if (await escrever(() => api.gravarConvencao(token, nivel, chave.trim(), valor.trim(), nivel === 'time' ? time.trim() : ''),
      `${chave.trim()} gravada no nível ${NOME_DO_NIVEL[nivel].toLowerCase()}.`)) {
      setValor('');
    }
  }

  function editar(c: ConvencaoDaEmpresa) {
    setNivel(c.nivel);
    setTime(c.time);
    setChave(c.chave);
    setValor(c.valor);
  }

  async function apagar(c: ConvencaoDaEmpresa) {
    if (!window.confirm(`Apagar ${c.chave} do nível ${NOME_DO_NIVEL[c.nivel].toLowerCase()}? Volta a valer a do nível de baixo.`)) return;
    await escrever(() => api.gravarConvencao(token, c.nivel, c.chave, '', c.time), `${c.chave} apagada do nível ${NOME_DO_NIVEL[c.nivel].toLowerCase()}.`);
  }

  /** O que a linha pode editar: a da empresa só o gestor; a de usuário, a de quem a vê (o dev só vê a dele). */
  const editavel = (c: ConvencaoDaEmpresa) => c.nivel !== 'empresa' || ehGestor;

  return (
    <div className="com-lateral">
      <section className="cartao cartao-de-tabela" aria-label="Convenções da empresa">
        <Chips<FiltroDeNivel>
          rotulo="Filtrar por nível"
          valor={filtro}
          aoMudar={setFiltro}
          opcoes={[
            { valor: 'todas', rotulo: 'Todas', contagem: lista.length },
            { valor: 'empresa', rotulo: 'Empresa', contagem: contagem('empresa') },
            { valor: 'time', rotulo: 'Time', contagem: contagem('time') },
            { valor: 'usuario', rotulo: 'Usuário', contagem: contagem('usuario') },
          ]}
        />
        {!convencoes ? (
          <p className="carregando" role="status">Carregando…</p>
        ) : visiveis.length === 0 ? (
          <p className="vazio">Nenhuma convenção neste nível — vale o padrão do dev.kit (feature/{'{equipe}'}/{'{id}'}, hotfix/ no Bug).</p>
        ) : (
          <div className="tabela-rolavel-simples">
            <table className="tabela tabela-do-cartao">
              <thead>
                <tr><th>Nível</th><th>Convenção</th><th>Valor</th><th>Gravada por</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {visiveis.map((c) => (
                  <tr key={`${c.nivel}|${c.time}|${c.usuario}|${c.chave}`}>
                    <td>
                      {/* O nível e, embaixo, de quem é: o time ou o usuário (a da empresa é de todos). */}
                      <span className="nome-e-login">
                        <Selo variante={c.nivel === 'empresa' ? 'papel' : c.nivel === 'time' ? 'info' : 'neutro'}>{NOME_DO_NIVEL[c.nivel]}</Selo>
                        {c.nivel !== 'empresa' && <small>{c.nivel === 'time' ? c.time : c.usuario}</small>}
                      </span>
                    </td>
                    <td><code className="codigo">{c.chave}</code></td>
                    <td><code className="codigo">{c.valor}</code></td>
                    <td>
                      <span className="nome-e-login">
                        {c.autor || '—'}
                        {c.atualizadoEm && <small>{formatar.dataHora(c.atualizadoEm)}</small>}
                      </span>
                    </td>
                    <td>
                      {editavel(c) && (
                        <span className="acoes">
                          <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Editar ${c.chave} (${NOME_DO_NIVEL[c.nivel]})`} onClick={() => editar(c)}>Editar</button>
                          <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Apagar ${c.chave} (${NOME_DO_NIVEL[c.nivel]})`} onClick={() => void apagar(c)}>Apagar</button>
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <PainelLateral id="titulo-gravar-convencao" titulo="Gravar uma convenção">
        <form className="formulario-lateral" onSubmit={(e) => void gravar(e)} aria-label="Gravar uma convenção">
          <div className="campos-lado-a-lado">
            <label>
              Nível
              <select value={nivel} onChange={(e) => setNivel(e.target.value as NivelDeConvencao)}>
                {ehGestor && <option value="empresa">Empresa</option>}
                <option value="time">Time</option>
                <option value="usuario">Usuário (a sua)</option>
              </select>
            </label>
            {/* Uma convenção por TIPO DE BRANCH: as três de sempre na lista, e qualquer branch.<tipo do item>
                (branch.task, branch.solicitacao) digitada. */}
            <label>
              Convenção
              <input value={chave} onChange={(e) => setChave(e.target.value.trim().toLowerCase())} list="tipos-de-branch"
                maxLength={60} placeholder="branch.hotfix" required />
              <datalist id="tipos-de-branch">
                {TIPOS_DE_BRANCH.map((t) => <option key={t} value={`branch.${t}`} />)}
              </datalist>
            </label>
          </div>
          {nivel === 'time' && (
            <label>
              Time
              <input value={time} onChange={(e) => setTime(e.target.value)} maxLength={80} placeholder="A Equipe das Configurações do dev.kit" required />
            </label>
          )}
          <label>
            Valor
            <input value={valor} onChange={(e) => setValor(e.target.value)} maxLength={200} placeholder="feature/{equipe}/{id}" required />
          </label>
          <p className="nota-do-formulario">
            Marcadores: {'{equipe}'}, {'{id}'} e {'{tipo}'}. O dev.kit escolhe a convenção pelo tipo: o tipo= do comentário; senão a
            branch.&lt;tipo do work item&gt;, se existir; senão branch.bug no Bug e branch.feature no resto. A do usuário vence a do
            time, e a do time vence a da empresa.
          </p>
          <button className="botao botao-primario botao-largo" type="submit" disabled={!chave.trim() || !valor.trim() || (nivel === 'time' && !time.trim())}>Gravar</button>
        </form>
      </PainelLateral>
    </div>
  );
}

/**
 * O contexto do dev.kit da empresa (US #550): o gestor grava o da empresa e o de cada equipe e lê o dos usuários; o
 * dev lê o da empresa e o das equipes (o dele, ele edita em /conta).
 */
function AbaContexto({ convencoes, ehGestor, token, escrever, tratar }: {
  convencoes: ConvencaoDaEmpresa[] | null; ehGestor: boolean; token: string; escrever: Escrever; tratar: (falha: unknown) => void;
}) {
  const [equipes, setEquipes] = useState<string[] | null>(null);

  useEffect(() => {
    api.equipesDaEmpresa(token).then(setEquipes).catch(tratar);
  }, [token, tratar]);

  if (!convencoes || !equipes) return <p className="carregando" role="status">Carregando…</p>;
  return (
    <section className="cartao" aria-label="Contexto da empresa por nível">
      <p className="nota-do-formulario">
        O que os agentes do dev.kit recebem em toda task, composto empresa → equipe → usuário: num tema em conflito, vale o
        nível mais específico. A regra (do usuário) é obrigatória para o agente; a sugestão (do agente) é só consulta.
        {!ehGestor && ' Só o gestor edita o da empresa e o das equipes. Ele promove as sugestões delas; você descarta as que o seu agente registrou.'}
      </p>
      <ContextoPorNivel convencoes={convencoes} equipes={equipes} podeGerir={ehGestor}
        aoGravar={(nivel, chave, valor, time) => escrever(() => api.gravarConvencao(token, nivel, chave, valor, time),
          valor ? `Tema ${temaDa(chave)} gravado no contexto.` : `Tema ${temaDa(chave)} removido do contexto.`)}
        aoPromover={(c) => escrever(() => api.promoverContexto(token, c.id!, c.versao), `Sugestão ${temaDa(c.chave)} promovida a regra.`)}
        aoDescartar={(c) => escrever(() => api.descartarContexto(token, c.id!), `Sugestão ${temaDa(c.chave)} descartada.`)} />
    </section>
  );
}
