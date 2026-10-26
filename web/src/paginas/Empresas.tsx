import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { useSearchParams } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { ConvencaoDaEmpresa, EmpresaResumo, GestorCriado } from '../api/tipos';
import { Avatar } from '../componentes/Avatar';
import { BarraDeUso } from '../componentes/BarraDeUso';
import { BotaoCopiar } from '../componentes/BotaoCopiar';
import { ContextoPorNivel, type GravarContexto, type UsuarioDoContexto } from '../componentes/ContextoPorNivel';
import { temaDa, type AcaoNaSugestao } from '../componentes/EditorDeContexto';
import { Pagina } from '../componentes/Pagina';
import { formatarCnpj, PainelDaEmpresa } from '../componentes/PainelDaEmpresa';
import { PainelLateral } from '../componentes/PainelLateral';
import { Selo } from '../componentes/Selo';
import { MenuDoAdmin, Topo } from '../componentes/Topo';
import { formatar } from '../formatar';
import { useSessao } from '../sessao';

/** Os planos que o admin escolhe ao criar a empresa (o da compra individual nasce pelo gateway). */
export const PLANOS = ['Empresarial', 'Individual'];

/** "1 empresa · 10 assentos contratados · 2 em uso": o resumo do cabeçalho. */
export function resumoDasEmpresas(empresas: EmpresaResumo[]): string {
  const assentos = empresas.reduce((soma, e) => soma + e.assentos, 0);
  const emUso = empresas.reduce((soma, e) => soma + e.assentosEmUso, 0);
  return `${empresas.length} ${empresas.length === 1 ? 'empresa' : 'empresas'} · ${assentos} ${assentos === 1 ? 'assento contratado' : 'assentos contratados'} · ${emUso} em uso`;
}

/** A linha de apoio da empresa na lista (US #687): o CNPJ e a cidade/UF, ou "sem CNPJ cadastrado". */
export function apoioDaEmpresa(e: EmpresaResumo): string {
  const d = e.dados;
  const local = d?.cidade ? `${d.cidade}${d.uf ? `/${d.uf}` : ''}` : d?.uf ?? '';
  const partes = [d?.cnpj ? formatarCnpj(d.cnpj) : 'sem CNPJ cadastrado', local].filter(Boolean);
  return partes.join(' · ');
}

/**
 * As empresas do plano empresarial (US #381; visual da US #461) — só do admin. A tabela mostra o plano, os
 * assentos EM USO (os devs ativos, a mesma conta do login), o CÓDIGO DE ADESÃO que o gestor entrega ao time (com o
 * copiar) e o gestor — ou "Sem gestor" e o Convidar: a senha inicial do convidado aparece UMA vez, e o primeiro
 * acesso dele exige a troca. O cadastro fica no painel lateral — e, desde a US #550, o Contexto do dev.kit de cada
 * empresa, nos três níveis.
 */
export function Empresas() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const [empresas, setEmpresas] = useState<EmpresaResumo[] | null>(null);
  const [nome, setNome] = useState('');
  const [plano, setPlano] = useState(PLANOS[0]);
  const [assentos, setAssentos] = useState(10);
  const [convidando, setConvidando] = useState<number | null>(null);
  const [loginDoGestor, setLoginDoGestor] = useState('');
  const [criado, setCriado] = useState<(GestorCriado & { empresa: string }) | null>(null);
  const [contextoDe, setContextoDe] = useState<EmpresaResumo | null>(null);
  // O painel à direita (US #687): nenhum, a "Nova empresa" (pelo botão do título) ou a edição de uma empresa.
  // O link direto ?editar=<id> abre a edição da empresa.
  const [parametros] = useSearchParams();
  const [lateral, setLateral] = useState<'nova' | number | null>(() => (Number(parametros.get('editar')) > 0 ? Number(parametros.get('editar')) : null));
  const [aviso, setAviso] = useState('');
  const [erro, setErro] = useState('');

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) {
      sair('expirou');
      return;
    }
    setErro(mensagemDaFalha(falha));
  }, [sair]);

  const carregar = useCallback(() => api.empresas(token).then(setEmpresas).catch(tratar), [token, tratar]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  async function criar(evento: FormEvent) {
    evento.preventDefault();
    setErro('');
    try {
      await api.criarEmpresa(token, { nome: nome.trim(), plano: plano.trim(), assentos });
      setNome('');
      await carregar();
    } catch (falha) {
      tratar(falha);
    }
  }

  function abrirConvite(empresa: EmpresaResumo) {
    setErro('');
    setCriado(null);
    setLoginDoGestor('');
    setConvidando(empresa.id);
  }

  async function convidar(evento: FormEvent, empresa: EmpresaResumo) {
    evento.preventDefault();
    setErro('');
    try {
      const gestor = await api.convidarGestor(token, empresa.id, loginDoGestor.trim());
      setCriado({ ...gestor, empresa: empresa.nome });
      setConvidando(null);
      await carregar();
    } catch (falha) {
      tratar(falha);
    }
  }

  function abrir(painel: 'nova' | number) {
    setErro('');
    setAviso('');
    setContextoDe(null);
    setLateral(painel);
  }

  const emEdicao = typeof lateral === 'number' ? empresas?.find((e) => e.id === lateral) : undefined;
  const comPainel = lateral !== null || contextoDe !== null;
  const novaEmpresa = <button className="botao botao-fantasma" type="button" onClick={() => abrir('nova')}>+ Nova empresa</button>;

  return (
    <div className="pagina">
      <Topo secao="empresas"><MenuDoAdmin /></Topo>

      <Pagina rotulo="Administração" titulo="Empresas" subtitulo={empresas ? resumoDasEmpresas(empresas) : undefined} acao={novaEmpresa} emLinha>
        {erro && <p className="erro" role="alert">{erro}</p>}
        {aviso && <p className="aviso" role="status">{aviso}</p>}
        {criado && (
          <p className="aviso senha-temporaria" role="status">
            Gestor <strong>{criado.login}</strong> criado para {criado.empresa}. Senha inicial: <code>{criado.senhaInicial}</code>
            <BotaoCopiar texto={criado.senhaInicial} rotulo="Copiar a senha inicial" /> — ela não aparece de novo; o primeiro acesso exige a troca.
          </p>
        )}

        <div className={comPainel ? 'com-lateral com-lateral-larga' : 'sem-lateral'}>
          <section className="cartao cartao-de-tabela" aria-label="Empresas cadastradas">
            {!empresas ? (
              <p className="carregando" role="status">Carregando…</p>
            ) : empresas.length === 0 ? (
              <p className="vazio">Nenhuma empresa cadastrada.</p>
            ) : (
              <div className="tabela-rolavel-simples">
                <table className="tabela tabela-do-cartao tabela-de-empresas">
                  <thead>
                    <tr><th>Empresa</th><th>Plano</th><th>Assentos</th><th>Código de adesão</th>{!comPainel && <th>Criada em</th>}<th>Gestor</th></tr>
                  </thead>
                  <tbody>
                    {empresas.map((e) => (
                      <tr key={e.id} className={lateral === e.id ? 'linha-selecionada' : undefined}>
                        <td>
                          <span className="com-avatar">
                            <Avatar nome={e.nome} letras={1} />
                            <span className="nome-e-login"><strong>{e.nome}</strong><small>{apoioDaEmpresa(e)}</small></span>
                          </span>
                        </td>
                        <td><Selo>{e.plano}</Selo></td>
                        <td><BarraDeUso usados={e.assentosEmUso} total={e.assentos} rotulo={`Assentos em uso de ${e.nome}`} /></td>
                        <td>
                          <span className="valor-copiavel">
                            <code className="codigo">{e.codigoDeAdesao}</code>
                            <BotaoCopiar texto={e.codigoDeAdesao} rotulo={`Copiar o código de adesão de ${e.nome}`} />
                          </span>
                        </td>
                        {!comPainel && <td>{formatar.data(e.criadaEm)}</td>}
                        <td>
                          {convidando === e.id ? (
                            <form className="acoes" onSubmit={(ev) => void convidar(ev, e)} aria-label={`Convidar o gestor de ${e.nome}`}>
                              <input
                                aria-label={`Login do gestor de ${e.nome}`}
                                value={loginDoGestor}
                                onChange={(ev) => setLoginDoGestor(ev.target.value)}
                                placeholder="login"
                                autoFocus
                                required
                              />
                              <button className="botao botao-primario botao-pequeno" type="submit" disabled={!loginDoGestor.trim()}>Enviar convite</button>
                              <button className="botao botao-fantasma botao-pequeno" type="button" onClick={() => setConvidando(null)}>Cancelar</button>
                            </form>
                          ) : (
                            <span className="acoes acoes-da-empresa">
                              {e.gestor ? <span className="gestor-da-empresa">{e.gestor}</span> : <Selo variante="aviso">Sem gestor</Selo>}
                              <button className={`botao botao-fantasma botao-pequeno${lateral === e.id ? ' botao-ativo' : ''}`} type="button" aria-label={`Editar ${e.nome}`}
                                onClick={() => abrir(e.id)}>
                                Editar
                              </button>
                              <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Convidar o gestor de ${e.nome}`} onClick={() => abrirConvite(e)}>
                                Convidar
                              </button>
                              <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Contexto de ${e.nome}`}
                                onClick={() => { setErro(''); setLateral(null); setContextoDe(e); }}>
                                Contexto
                              </button>
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

          {contextoDe ? (
            <ContextoDaEmpresa empresa={contextoDe} token={token} tratar={tratar} aoFechar={() => setContextoDe(null)} />
          ) : emEdicao ? (
            <PainelDaEmpresa
              key={emEdicao.id}
              token={token}
              empresa={emEdicao}
              planos={PLANOS}
              aoFechar={() => setLateral(null)}
              aoSalvar={(editada) => {
                setLateral(null);
                setAviso(`Empresa ${editada.nome} atualizada: ${editada.assentos} assentos.`);
                void carregar();
              }}
            />
          ) : lateral === 'nova' && (
            <PainelLateral id="titulo-nova-empresa" titulo="Nova empresa">
              <form className="formulario-lateral" onSubmit={criar} aria-label="Nova empresa">
                <label>
                  Nome
                  <input value={nome} onChange={(e) => setNome(e.target.value)} maxLength={100} placeholder="Nome da empresa" required />
                </label>
                <div className="campos-lado-a-lado">
                  <label>
                    Plano
                    <select value={plano} onChange={(e) => setPlano(e.target.value)}>
                      {PLANOS.map((p) => <option key={p} value={p}>{p}</option>)}
                    </select>
                  </label>
                  <label>
                    Assentos
                    <input type="number" min={1} value={assentos} onChange={(e) => setAssentos(Number(e.target.value))} required />
                  </label>
                </div>
                <p className="nota-do-formulario">Ao criar, a empresa recebe um código de adesão para os devs entrarem.</p>
                <button className="botao botao-primario botao-largo" type="submit">Criar empresa</button>
              </form>
            </PainelLateral>
          )}
        </div>
      </Pagina>
    </div>
  );
}

/**
 * O painel Contexto de uma empresa (US #550) — o admin geral vê e gerencia os TRÊS níveis do contexto do dev.kit dela:
 * a empresa, cada equipe e cada usuário (pelo login), pelas rotas /api/empresas/{id}/... — ele não tem empresa no
 * token e fica fora de /api/empresa/*.
 */
function ContextoDaEmpresa({ empresa, token, tratar, aoFechar }: {
  empresa: EmpresaResumo; token: string; tratar: (falha: unknown) => void; aoFechar: () => void;
}) {
  const [dados, setDados] = useState<{ convencoes: ConvencaoDaEmpresa[]; equipes: string[]; usuarios: UsuarioDoContexto[] } | null>(null);
  const [aviso, setAviso] = useState('');

  const carregar = useCallback(async () => {
    try {
      const [convencoes, equipes, usuarios] = await Promise.all([
        api.convencoesDaEmpresaDoAdmin(token, empresa.id), api.equipesDaEmpresaDoAdmin(token, empresa.id), api.usuarios(token),
      ]);
      setDados({ convencoes, equipes, usuarios: usuarios.filter((u) => u.empresaId === empresa.id).map((u) => ({ login: u.login, nome: u.nome })) });
    } catch (falha) {
      tratar(falha);
    }
  }, [token, empresa.id, tratar]);

  useEffect(() => {
    setDados(null);
    setAviso('');
    void carregar();
  }, [carregar]);

  /** Roda uma escrita do admin: grava, avisa e recarrega (também na falha: o 409 mostra a versão do servidor). */
  async function escrever(acao: () => Promise<unknown>, mensagem: string) {
    setAviso('');
    try {
      await acao();
      setAviso(mensagem);
      await carregar();
      return true;
    } catch (falha) {
      tratar(falha);
      await carregar();
      return false;
    }
  }

  const gravar: GravarContexto = (nivel, chave, valor, time, usuario) =>
    escrever(() => api.gravarConvencaoDoAdmin(token, empresa.id, nivel, chave, valor, time, usuario),
      valor ? `Tema ${temaDa(chave)} gravado.` : `Tema ${temaDa(chave)} removido.`);

  const promover: AcaoNaSugestao = (c) =>
    escrever(() => api.promoverContextoDoAdmin(token, empresa.id, c.id!, c.versao), `Sugestão ${temaDa(c.chave)} promovida a regra.`);

  const descartar: AcaoNaSugestao = (c) =>
    escrever(() => api.descartarContextoDoAdmin(token, empresa.id, c.id!), `Sugestão ${temaDa(c.chave)} descartada.`);

  return (
    <PainelLateral id="titulo-contexto-da-empresa" titulo={`Contexto de ${empresa.nome}`}>
      <button className="botao botao-fantasma botao-pequeno" type="button" onClick={aoFechar}>Fechar</button>
      {aviso && <p className="aviso" role="status">{aviso}</p>}
      {!dados ? (
        <p className="carregando" role="status">Carregando…</p>
      ) : (
        <ContextoPorNivel key={empresa.id} convencoes={dados.convencoes} equipes={dados.equipes} podeGerir usuarios={dados.usuarios} aoGravar={gravar}
          aoPromover={promover} aoDescartar={descartar} />
      )}
    </PainelLateral>
  );
}
