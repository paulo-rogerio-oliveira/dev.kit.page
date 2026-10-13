import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { EmpresaResumo, GestorCriado } from '../api/tipos';
import { Avatar } from '../componentes/Avatar';
import { BarraDeUso } from '../componentes/BarraDeUso';
import { BotaoCopiar } from '../componentes/BotaoCopiar';
import { Pagina } from '../componentes/Pagina';
import { PainelLateral } from '../componentes/PainelLateral';
import { Selo } from '../componentes/Selo';
import { MenuDoAdmin, Topo } from '../componentes/Topo';
import { formatar } from '../formatar';
import { useSessao } from '../sessao';

/** Os planos que o admin escolhe ao criar a empresa (o da compra individual nasce pelo gateway). */
export const PLANOS = ['Empresarial', 'Individual'];

/** "1 empresa · 10 assentos contratados": o resumo do cabeçalho. */
export function resumoDasEmpresas(empresas: EmpresaResumo[]): string {
  const assentos = empresas.reduce((soma, e) => soma + e.assentos, 0);
  return `${empresas.length} ${empresas.length === 1 ? 'empresa' : 'empresas'} · ${assentos} ${assentos === 1 ? 'assento contratado' : 'assentos contratados'}`;
}

/**
 * As empresas do plano empresarial (US #381; visual da US #461) — só do admin. A tabela mostra o plano, os
 * assentos EM USO (os devs ativos, a mesma conta do login), o CÓDIGO DE ADESÃO que o gestor entrega ao time (com o
 * copiar) e o gestor — ou "Sem gestor" e o Convidar: a senha inicial do convidado aparece UMA vez, e o primeiro
 * acesso dele exige a troca. O cadastro fica no painel lateral.
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

  return (
    <div className="pagina">
      <Topo secao="empresas"><MenuDoAdmin /></Topo>

      <Pagina titulo="Empresas" subtitulo={empresas ? resumoDasEmpresas(empresas) : undefined} emLinha>
        {erro && <p className="erro" role="alert">{erro}</p>}
        {criado && (
          <p className="aviso senha-temporaria" role="status">
            Gestor <strong>{criado.login}</strong> criado para {criado.empresa}. Senha inicial: <code>{criado.senhaInicial}</code>
            <BotaoCopiar texto={criado.senhaInicial} rotulo="Copiar a senha inicial" /> — ela não aparece de novo; o primeiro acesso exige a troca.
          </p>
        )}

        <div className="com-lateral">
          <section className="cartao cartao-de-tabela" aria-label="Empresas cadastradas">
            {!empresas ? (
              <p className="carregando" role="status">Carregando…</p>
            ) : empresas.length === 0 ? (
              <p className="vazio">Nenhuma empresa cadastrada.</p>
            ) : (
              <div className="tabela-rolavel-simples">
                <table className="tabela tabela-do-cartao">
                  <thead>
                    <tr><th>Empresa</th><th>Plano</th><th>Assentos</th><th>Código de adesão</th><th>Criada em</th><th>Gestor</th></tr>
                  </thead>
                  <tbody>
                    {empresas.map((e) => (
                      <tr key={e.id}>
                        <td><span className="com-avatar"><Avatar nome={e.nome} letras={1} /><strong>{e.nome}</strong></span></td>
                        <td><Selo>{e.plano}</Selo></td>
                        <td><BarraDeUso usados={e.assentosEmUso} total={e.assentos} rotulo={`Assentos em uso de ${e.nome}`} /></td>
                        <td>
                          <span className="valor-copiavel">
                            <code className="codigo">{e.codigoDeAdesao}</code>
                            <BotaoCopiar texto={e.codigoDeAdesao} rotulo={`Copiar o código de adesão de ${e.nome}`} />
                          </span>
                        </td>
                        <td>{formatar.data(e.criadaEm)}</td>
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
                            <span className="acoes">
                              {e.gestor ? <span>{e.gestor}</span> : <Selo variante="aviso">Sem gestor</Selo>}
                              <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Convidar o gestor de ${e.nome}`} onClick={() => abrirConvite(e)}>
                                Convidar
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
        </div>
      </Pagina>
    </div>
  );
}
