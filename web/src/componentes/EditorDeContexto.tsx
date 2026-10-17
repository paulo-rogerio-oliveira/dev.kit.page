import { useState, type FormEvent } from 'react';
import { ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { ConflitoDeContexto, GravacaoDeContexto, ItemDeContexto, NivelDeContexto, OrigemDoContexto } from '../api/tipos';
import { formatar } from '../formatar';
import { Chips } from './Chips';
import { PainelLateral } from './PainelLateral';
import { Selo } from './Selo';

/** O teto do texto de um item (o mesmo LimitesDoContexto.TamanhoMaximoDoValor da API). */
export const TETO_DO_VALOR = 4000;

export const NOME_DO_NIVEL: Record<NivelDeContexto, string> = { usuario: 'Usuário', equipe: 'Equipe', empresa: 'Empresa' };

const NOME_DA_ORIGEM: Record<OrigemDoContexto, string> = { usuario: 'Instrução', agente: 'Sugestão' };

/** Os temas sugeridos no campo Chave: o texto livre (contexto.*) e o nome do branch (branch.*), que segue valendo. */
const CHAVES_SUGERIDAS = ['contexto.geral', 'contexto.testes', 'contexto.arquitetura', 'contexto.build', 'branch.feature', 'branch.bug', 'branch.hotfix'];

type FiltroDeNivel = 'todos' | NivelDeContexto;
type FiltroDeOrigem = 'todas' | OrigemDoContexto;

/** Um usuário que o admin escolhe como dono do item de nível usuário. */
export interface UsuarioDoContexto {
  id: number;
  nome: string;
}

/**
 * O editor do CONTEXTO do dev.kit (US #556), o mesmo nas três visões — a do dev, a do gestor e a do admin do board: a
 * lista com o nível, a ORIGEM (instrução do usuário × sugestão de um agente), quem gravou e quando; o formulário que grava
 * (com o contador do teto); excluir, PROMOVER e DESCARTAR a sugestão; e o conflito (409) mostrado com o valor atual para
 * reaplicar — nada se sobrescreve em silêncio. Quem pode o quê é da API: `podeEditar` e `niveis` só escondem o que ela
 * recusaria.
 */
export function EditorDeContexto({ itens, niveis, equipes, podeEditar, usuarios, aoGravar, aoExcluir, aoPromover, aoMudar, aoExpirar }: {
  itens: ItemDeContexto[] | null;
  /** Os níveis em que o perfil grava (o formulário só oferece estes). */
  niveis: NivelDeContexto[];
  /** As equipes que o perfil vê (sugeridas no campo Equipe). */
  equipes: string[];
  podeEditar: (item: ItemDeContexto) => boolean;
  /** Só o admin: os usuários da empresa, para escolher de quem é o item de nível usuário. */
  usuarios?: UsuarioDoContexto[];
  aoGravar: (gravacao: GravacaoDeContexto) => Promise<unknown>;
  aoExcluir: (item: ItemDeContexto) => Promise<unknown>;
  aoPromover: (item: ItemDeContexto) => Promise<unknown>;
  /** Depois de cada escrita bem-sucedida: a página relê a visão. */
  aoMudar: () => Promise<unknown>;
  /** O 401: a sessão venceu. */
  aoExpirar: () => void;
}) {
  const [filtroDeNivel, setFiltroDeNivel] = useState<FiltroDeNivel>('todos');
  const [filtroDeOrigem, setFiltroDeOrigem] = useState<FiltroDeOrigem>('todas');
  const [nivel, setNivel] = useState<NivelDeContexto>(niveis[0] ?? 'usuario');
  const [equipe, setEquipe] = useState(equipes[0] ?? '');
  const [usuarioId, setUsuarioId] = useState<number | ''>('');
  const [chave, setChave] = useState('contexto.geral');
  const [valor, setValor] = useState('');
  const [versaoEsperada, setVersaoEsperada] = useState<number | undefined>(undefined);
  const [conflito, setConflito] = useState<ConflitoDeContexto | null>(null);
  const [aviso, setAviso] = useState('');
  const [erro, setErro] = useState('');

  const lista = itens ?? [];
  const visiveis = lista.filter((i) => (filtroDeNivel === 'todos' || i.nivel === filtroDeNivel) && (filtroDeOrigem === 'todas' || i.origem === filtroDeOrigem));
  const contagem = (n: NivelDeContexto) => lista.filter((i) => i.nivel === n).length;
  const doAdmin = usuarios !== undefined;

  /** Roda uma escrita: limpa as mensagens, escreve, avisa e relê; o 409 vira o painel do conflito. */
  async function escrever(acao: () => Promise<unknown>, mensagem: string) {
    setErro('');
    setAviso('');
    setConflito(null);
    try {
      await acao();
      setAviso(mensagem);
      await aoMudar();
      return true;
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.status === 401) aoExpirar();
      else if (falha instanceof ErroDaApi && falha.status === 409 && falha.corpo && typeof falha.corpo === 'object' && 'atual' in falha.corpo)
        setConflito(falha.corpo as ConflitoDeContexto);
      else setErro(mensagemDaFalha(falha));
      return false;
    }
  }

  function limpar() {
    setValor('');
    setVersaoEsperada(undefined);
  }

  async function gravar(evento: FormEvent, versao = versaoEsperada) {
    evento.preventDefault();
    const gravacao: GravacaoDeContexto = {
      nivel, chave: chave.trim(), valor: valor.trim(), equipe: nivel === 'equipe' ? equipe.trim() : '',
      ...(versao === undefined ? {} : { versaoEsperada: versao }),
      ...(doAdmin && nivel === 'usuario' && usuarioId !== '' ? { usuarioId } : {}),
    };
    if (await escrever(() => aoGravar(gravacao), `${gravacao.chave} gravado no nível ${NOME_DO_NIVEL[nivel].toLowerCase()}.`)) limpar();
  }

  function editar(item: ItemDeContexto) {
    setNivel(item.nivel);
    setEquipe(item.equipe);
    setChave(item.chave);
    setValor(item.valor);
    setVersaoEsperada(item.versao);
    setConflito(null);
  }

  /** O conflito: fica com o valor atual (descarta a edição) ou reaplica a sua sobre a versão atual. */
  function usarOAtual() {
    if (conflito?.atual) editar(conflito.atual);
    else limpar();
    setConflito(null);
  }

  async function excluir(item: ItemDeContexto) {
    const sugestao = item.origem === 'agente';
    if (!window.confirm(sugestao ? `Descartar a sugestão ${item.chave}?` : `Excluir ${item.chave} do nível ${NOME_DO_NIVEL[item.nivel].toLowerCase()}?`)) return;
    await escrever(() => aoExcluir(item), sugestao ? `Sugestão ${item.chave} descartada.` : `${item.chave} excluído.`);
  }

  async function promover(item: ItemDeContexto) {
    await escrever(() => aoPromover(item), `Sugestão ${item.chave} promovida a instrução.`);
  }

  const semDono = doAdmin && nivel === 'usuario' && usuarioId === '';
  return (
    <div className="com-lateral">
      <section className="cartao cartao-de-tabela" aria-label="Itens do contexto">
        <div className="barra-de-filtros">
          <Chips<FiltroDeNivel>
            rotulo="Filtrar por nível"
            valor={filtroDeNivel}
            aoMudar={setFiltroDeNivel}
            opcoes={[
              { valor: 'todos', rotulo: 'Todos', contagem: lista.length },
              { valor: 'usuario', rotulo: 'Usuário', contagem: contagem('usuario') },
              { valor: 'equipe', rotulo: 'Equipe', contagem: contagem('equipe') },
              { valor: 'empresa', rotulo: 'Empresa', contagem: contagem('empresa') },
            ]}
          />{' '}
          <Chips<FiltroDeOrigem>
            rotulo="Filtrar por origem"
            valor={filtroDeOrigem}
            aoMudar={setFiltroDeOrigem}
            opcoes={[
              { valor: 'todas', rotulo: 'Todas as origens' },
              { valor: 'usuario', rotulo: 'Instruções', contagem: lista.filter((i) => i.origem === 'usuario').length },
              { valor: 'agente', rotulo: 'Sugestões', contagem: lista.filter((i) => i.origem === 'agente').length },
            ]}
          />
        </div>
        {erro && <p className="erro" role="alert">{erro}</p>}
        {aviso && <p className="aviso" role="status">{aviso}</p>}
        {conflito && (
          <div className="conflito-do-contexto" role="alert">
            <p>{conflito.mensagem}</p>
            {conflito.atual && <p>Valor atual (versão {conflito.atual.versao}, de {conflito.atual.autor || '—'}): <code className="codigo">{conflito.atual.valor}</code></p>}
            <span className="acoes">
              <button className="botao botao-fantasma botao-pequeno" type="button" onClick={usarOAtual}>Usar o valor atual</button>
              {conflito.atual && (
                <button className="botao botao-primario botao-pequeno" type="button" onClick={(e) => void gravar(e, conflito.atual!.versao)}>Reaplicar a minha edição</button>
              )}
            </span>
          </div>
        )}
        {!itens ? (
          <p className="carregando" role="status">Carregando…</p>
        ) : visiveis.length === 0 ? (
          <p className="vazio">Nenhum item aqui — o agente segue o pedido e as Rules; o que for gravado vale para as próximas execuções.</p>
        ) : (
          <div className="tabela-rolavel-simples">
            <table className="tabela tabela-do-cartao">
              <thead>
                <tr><th>Nível</th><th>Chave</th><th>Texto</th><th>Origem</th><th>Gravado por</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {visiveis.map((i) => (
                  <tr key={i.id}>
                    <td>
                      {/* O nível e, embaixo, de quem é: a equipe ou o usuário (o da empresa é de todos). */}
                      <span className="nome-e-login">
                        <Selo variante={i.nivel === 'empresa' ? 'papel' : i.nivel === 'equipe' ? 'info' : 'neutro'}>{NOME_DO_NIVEL[i.nivel]}</Selo>
                        {i.nivel !== 'empresa' && <small>{i.nivel === 'equipe' ? i.equipe : i.usuario}</small>}
                      </span>
                    </td>
                    <td><code className="codigo">{i.chave}</code></td>
                    <td className="texto-do-contexto">{i.valor}</td>
                    <td><Selo variante={i.origem === 'usuario' ? 'sucesso' : 'aviso'}>{NOME_DA_ORIGEM[i.origem]}</Selo></td>
                    <td>
                      <span className="nome-e-login">
                        {i.autor || '—'}
                        <small>{formatar.dataHora(i.atualizadoEm)}</small>
                      </span>
                    </td>
                    <td>
                      {podeEditar(i) && (
                        <span className="acoes">
                          {i.origem === 'agente' ? (
                            <>
                              <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Promover ${i.chave} (${NOME_DO_NIVEL[i.nivel]})`} onClick={() => void promover(i)}>Promover</button>
                              <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Descartar ${i.chave} (${NOME_DO_NIVEL[i.nivel]})`} onClick={() => void excluir(i)}>Descartar</button>
                            </>
                          ) : (
                            <>
                              <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Editar ${i.chave} (${NOME_DO_NIVEL[i.nivel]})`} onClick={() => editar(i)}>Editar</button>
                              <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Excluir ${i.chave} (${NOME_DO_NIVEL[i.nivel]})`} onClick={() => void excluir(i)}>Excluir</button>
                            </>
                          )}
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

      <PainelLateral id="titulo-gravar-contexto" titulo={versaoEsperada === undefined ? 'Gravar no contexto' : 'Editar o item'}>
        <form className="formulario-lateral" onSubmit={(e) => void gravar(e)} aria-label="Gravar no contexto">
          <div className="campos-lado-a-lado">
            <label>
              Nível
              <select value={nivel} onChange={(e) => setNivel(e.target.value as NivelDeContexto)} disabled={versaoEsperada !== undefined}>
                {niveis.map((n) => <option key={n} value={n}>{n === 'usuario' && !doAdmin ? 'Usuário (o seu)' : NOME_DO_NIVEL[n]}</option>)}
              </select>
            </label>
            <label>
              Chave
              <input value={chave} onChange={(e) => setChave(e.target.value.trim().toLowerCase())} list="chaves-do-contexto"
                maxLength={60} placeholder="contexto.testes" required disabled={versaoEsperada !== undefined} />
              <datalist id="chaves-do-contexto">
                {CHAVES_SUGERIDAS.map((c) => <option key={c} value={c} />)}
              </datalist>
            </label>
          </div>
          {nivel === 'equipe' && (
            <label>
              Equipe
              <input value={equipe} onChange={(e) => setEquipe(e.target.value)} list="equipes-do-contexto" maxLength={80}
                placeholder="A Equipe das Configurações do dev.kit" required disabled={versaoEsperada !== undefined} />
              <datalist id="equipes-do-contexto">
                {equipes.map((e) => <option key={e} value={e} />)}
              </datalist>
            </label>
          )}
          {doAdmin && nivel === 'usuario' && (
            <label>
              Usuário
              <select value={usuarioId} onChange={(e) => setUsuarioId(e.target.value ? Number(e.target.value) : '')} required>
                <option value="">Escolha o usuário</option>
                {usuarios!.map((u) => <option key={u.id} value={u.id}>{u.nome}</option>)}
              </select>
            </label>
          )}
          <label>
            Texto
            <textarea value={valor} onChange={(e) => setValor(e.target.value)} maxLength={TETO_DO_VALOR} rows={6}
              placeholder="O que o agente deve saber para decidir: convenções, a stack, o que evitar." required />
          </label>
          <p className="nota-do-formulario contador-do-contexto" aria-live="polite">{valor.length}/{TETO_DO_VALOR} caracteres</p>
          <p className="nota-do-formulario">
            O que você grava é INSTRUÇÃO para os agentes (e os avaliadores) do dev.kit; o mais específico vence (usuário &gt; equipe
            &gt; empresa). O que os agentes anotam chega como SUGESTÃO: promova ou descarte. Nunca grave senha, token ou dado de cliente.
          </p>
          <button className="botao botao-primario botao-largo" type="submit"
            disabled={!chave.trim() || !valor.trim() || (nivel === 'equipe' && !equipe.trim()) || semDono}>Gravar</button>
          {versaoEsperada !== undefined && (
            <button className="botao botao-fantasma botao-largo" type="button" onClick={limpar}>Cancelar a edição</button>
          )}
        </form>
      </PainelLateral>
    </div>
  );
}
