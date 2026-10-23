import { useCallback, useEffect, useState } from 'react';
import { api, ErroDaApi } from '../api/cliente';
import type { EmpresaDoGestor, UsuarioDaEmpresa } from '../api/tipos';
import { iniciais } from '../componentes/Avatar';
import { Selo } from '../componentes/Selo';
import { MenuDaEmpresa, Topo } from '../componentes/Topo';
import { formatar } from '../formatar';
import { useSessao } from '../sessao';

/** O valor do seletor da política de login: a escolha da empresa, ou o padrão global. */
type Politica = 'padrao' | 'sim' | 'nao';

const DA_POLITICA: Record<Politica, boolean | null> = { padrao: null, sim: true, nao: false };

/**
 * Os usuários da empresa do GESTOR (US #425, /empresa/usuarios): os assentos ocupados (por usuário), o código de
 * adesão — e gerar um novo, que invalida o antigo —, a política de login da empresa e o bloqueio, que libera o
 * assento. A API só deixa o gestor mexer na empresa dele (403 na de outra).
 */
export function EmpresaUsuarios() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const [empresa, setEmpresa] = useState<EmpresaDoGestor | null>(null);
  const [usuarios, setUsuarios] = useState<UsuarioDaEmpresa[] | null>(null);
  const [aviso, setAviso] = useState('');
  const [erro, setErro] = useState('');

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) {
      sair('expirou');
      return;
    }
    setErro(falha instanceof Error ? falha.message : 'Falha ao consultar a API.');
  }, [sair]);

  const carregar = useCallback(async () => {
    try {
      const [dados, lista] = await Promise.all([api.empresaDoGestor(token), api.usuariosDaEmpresa(token)]);
      setEmpresa(dados);
      setUsuarios(lista);
    } catch (falha) {
      tratar(falha);
    }
  }, [token, tratar]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  async function alternar(u: UsuarioDaEmpresa) {
    setErro('');
    setAviso('');
    try {
      await api.bloquearNaEmpresa(token, u.id, !u.bloqueado);
      setAviso(u.bloqueado ? `${u.login} desbloqueado: voltou a ocupar um assento.` : `${u.login} bloqueado: o assento ficou livre.`);
      await carregar();
    } catch (falha) {
      tratar(falha);
    }
  }

  async function novoCodigo() {
    setErro('');
    setAviso('');
    if (!window.confirm('Gerar um código de adesão novo? O atual deixa de valer na hora.')) return;
    try {
      const { codigoDeAdesao } = await api.novoCodigo(token);
      setAviso(`Código novo: ${codigoDeAdesao}. Entregue-o ao time; o anterior não vale mais.`);
      await carregar();
    } catch (falha) {
      tratar(falha);
    }
  }

  async function mudarPolitica(valor: Politica) {
    setErro('');
    try {
      setEmpresa(await api.politicaDaEmpresa(token, DA_POLITICA[valor]));
    } catch (falha) {
      tratar(falha);
    }
  }

  const politica: Politica = empresa === null || empresa.loginObrigatorio === null ? 'padrao' : empresa.loginObrigatorio ? 'sim' : 'nao';
  return (
    <div className="pagina">
      <Topo secao="empresa"><MenuDaEmpresa /></Topo>
      <main className="painel">
        {erro && <p className="erro" role="alert">{erro}</p>}
        {aviso && <p className="aviso" role="status">{aviso}</p>}
        <section aria-labelledby="titulo-empresa" className="secao-da-empresa">
          <header className="cabecalho-da-pagina">
            <div className="cabecalho-textos">
              <p className="rotulo-da-pagina">Empresa</p>
              <h1 id="titulo-empresa">{empresa?.nome ?? 'Empresa'}</h1>
            </div>
          </header>
          {empresa && (
            <div className="cartoes-da-empresa">
              <div className="kpi kpi-destaque">
                <span className="kpi-rotulo">Assentos ocupados</span>
                <p className="kpi-valor" data-testid="assentos">
                  {empresa.assentosOcupados} <small>de {empresa.assentos}</small><span className="visualmente-oculto"> assentos ocupados</span>
                </p>
                <span className="barra-de-uso" aria-hidden="true">
                  <span className="trilho"><span className={`barra${empresa.assentosOcupados >= empresa.assentos ? ' barra-cheia' : ''}`} style={{ width: `${empresa.assentos > 0 ? Math.min(1, empresa.assentosOcupados / empresa.assentos) * 100 : 0}%` }} /></span>
                </span>
              </div>
              <div className="kpi">
                <span className="kpi-rotulo">Código de adesão</span>
                <div className="kpi-linha">
                  <code className="codigo-de-adesao">{empresa.codigoDeAdesao}</code>
                  <button className="botao botao-fantasma" type="button" onClick={() => void novoCodigo()}>Gerar código novo</button>
                </div>
                <span className="kpi-nota">O time adere em /aderir com o código e o e-mail.</span>
              </div>
              <div className="kpi">
                <label>
                  Login no dev.kit
                  <select value={politica} onChange={(e) => void mudarPolitica(e.target.value as Politica)}>
                    <option value="padrao">Padrão do dev.kit{empresa.loginObrigatorio === null ? ` (${empresa.loginObrigatorioEfetivo ? 'obrigatório' : 'opcional'})` : ''}</option>
                    <option value="sim">Obrigatório</option>
                    <option value="nao">Opcional</option>
                  </select>
                </label>
              </div>
            </div>
          )}
          <p className="nota-da-secao">Cada dev ativo ocupa um assento; bloquear libera o assento.</p>
        </section>

        <section aria-labelledby="titulo-usuarios-da-empresa">
          <h2 id="titulo-usuarios-da-empresa" className="titulo-com-fio">Usuários</h2>
          {!usuarios ? (
            <p className="carregando" role="status">Carregando…</p>
          ) : (
            <div className="cartao cartao-de-tabela">
              <div className="tabela-rolavel-simples">
                <table className="tabela tabela-do-cartao">
                  <thead>
                    <tr><th>Login</th><th>Nome</th><th>Papel</th><th>Situação</th><th>Desde</th><th>Ações</th></tr>
                  </thead>
                  <tbody>
                    {usuarios.map((u) => (
                      <tr key={u.id}>
                        <td><span className="login-com-iniciais fonte-mono" data-iniciais={iniciais(u.login)}>{u.login}</span></td>
                        <td>{u.nome || '—'}</td>
                        <td><Selo variante={u.papel === 'gestor' ? 'marca' : 'neutro'}>{u.papel === 'gestor' ? 'Gestor' : 'Dev'}</Selo></td>
                        <td><Selo variante={u.bloqueado ? 'aviso' : 'sucesso'} ponto>{u.bloqueado ? 'Bloqueado' : 'Ativo'}</Selo></td>
                        <td>{formatar.dataHora(u.criadoEm)}</td>
                        <td>
                          {u.login === sessao!.login ? <span className="texto-mudo">você</span> : (
                            <button className="botao botao-fantasma" type="button" aria-label={`${u.bloqueado ? 'Desbloquear' : 'Bloquear'} ${u.login}`} onClick={() => void alternar(u)}>
                              {u.bloqueado ? 'Desbloquear' : 'Bloquear'}
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </section>
      </main>
    </div>
  );
}
