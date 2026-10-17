import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import type { EmpresaDoGestor, UsuarioDaEmpresa } from '../api/tipos';
import { Topo } from '../componentes/Topo';
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
      <Topo secao="empresa">
        <Link className="botao botao-fantasma" to="/dashboard">Dashboard</Link>
        <Link className="botao botao-fantasma" to="/empresa/catalogo">Catálogo da empresa</Link>
        <Link className="botao botao-fantasma" to="/contexto">Contexto</Link>
        <Link className="botao botao-fantasma" to="/empresa/conhecimento">Repositórios</Link>
      </Topo>
      <main className="painel">
        {erro && <p className="erro" role="alert">{erro}</p>}
        {aviso && <p className="aviso" role="status">{aviso}</p>}
        <section aria-labelledby="titulo-empresa">
          <h1 id="titulo-empresa">{empresa?.nome ?? 'Empresa'}</h1>
          {empresa && (
            <div className="filtros">
              <p className="kpi" data-testid="assentos">{empresa.assentosOcupados} de {empresa.assentos} assentos ocupados</p>
              <p>Código de adesão: <code>{empresa.codigoDeAdesao}</code></p>
              <button className="botao botao-fantasma" type="button" onClick={() => void novoCodigo()}>Gerar código novo</button>
              <label>
                Login no dev.kit
                <select value={politica} onChange={(e) => void mudarPolitica(e.target.value as Politica)}>
                  <option value="padrao">Padrão do dev.kit{empresa.loginObrigatorio === null ? ` (${empresa.loginObrigatorioEfetivo ? 'obrigatório' : 'opcional'})` : ''}</option>
                  <option value="sim">Obrigatório</option>
                  <option value="nao">Opcional</option>
                </select>
              </label>
            </div>
          )}
          <p className="nota-da-secao">Cada dev ativo ocupa um assento; bloquear libera o assento. O time adere em /aderir com o código e o e-mail.</p>
        </section>

        <section aria-labelledby="titulo-usuarios-da-empresa">
          <h2 id="titulo-usuarios-da-empresa">Usuários</h2>
          {!usuarios ? (
            <p className="carregando" role="status">Carregando…</p>
          ) : (
            <table className="tabela">
              <thead>
                <tr><th>Login</th><th>Nome</th><th>Papel</th><th>Situação</th><th>Desde</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {usuarios.map((u) => (
                  <tr key={u.id}>
                    <td>{u.login}</td>
                    <td>{u.nome || '—'}</td>
                    <td>{u.papel === 'gestor' ? 'Gestor' : 'Dev'}</td>
                    <td><span className={`estado ${u.bloqueado ? 'estado-regrediu' : 'estado-resolvido'}`}>{u.bloqueado ? 'Bloqueado' : 'Ativo'}</span></td>
                    <td>{formatar.dataHora(u.criadoEm)}</td>
                    <td>
                      {u.login !== sessao!.login && (
                        <button className="botao botao-fantasma" type="button" aria-label={`${u.bloqueado ? 'Desbloquear' : 'Bloquear'} ${u.login}`} onClick={() => void alternar(u)}>
                          {u.bloqueado ? 'Desbloquear' : 'Bloquear'}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      </main>
    </div>
  );
}
