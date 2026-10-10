import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import type { EmpresaResumo, GestorCriado } from '../api/tipos';
import { formatar } from '../formatar';
import { useSessao } from '../sessao';

/**
 * As empresas do plano empresarial (US #381) — só do admin. Cadastra a empresa (nome, plano e
 * assentos), mostra o CÓDIGO DE ADESÃO que o gestor entrega ao time (o colaborador o informa no
 * dev.kit, com o aceite do aviso de coleta) e convida o gestor: a senha inicial aparece UMA vez, e o
 * primeiro acesso dele exige a troca, como o do admin.
 */
export function Empresas() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const [empresas, setEmpresas] = useState<EmpresaResumo[] | null>(null);
  const [nome, setNome] = useState('');
  const [plano, setPlano] = useState('Empresarial');
  const [assentos, setAssentos] = useState(10);
  const [convites, setConvites] = useState<Record<number, string>>({});
  const [criado, setCriado] = useState<(GestorCriado & { empresa: string }) | null>(null);
  const [erro, setErro] = useState('');

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) {
      sair('expirou');
      return;
    }
    const campos = falha instanceof ErroDaApi ? Object.values(falha.erros).flat() : [];
    setErro(campos.length > 0 ? campos.join(' ') : falha instanceof Error ? falha.message : 'Falha ao consultar a API.');
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

  async function convidar(empresa: EmpresaResumo) {
    setErro('');
    setCriado(null);
    try {
      const gestor = await api.convidarGestor(token, empresa.id, (convites[empresa.id] ?? '').trim());
      setCriado({ ...gestor, empresa: empresa.nome });
      setConvites({ ...convites, [empresa.id]: '' });
    } catch (falha) {
      tratar(falha);
    }
  }

  return (
    <div className="pagina">
      <header className="topo">
        <span className="marca">dev<span className="marca-ponto">.</span>kit <small>empresas</small></span>
        <nav>
          <Link className="botao botao-fantasma" to="/dashboard">Dashboard</Link>
          <Link className="botao botao-fantasma" to="/usuarios">Usuários</Link>
          <span className="usuario">{sessao!.login}</span>
          <button className="botao botao-fantasma" type="button" onClick={() => sair()}>Sair</button>
        </nav>
      </header>

      <main className="painel">
        <section aria-labelledby="titulo-nova-empresa">
          <h2 id="titulo-nova-empresa">Nova empresa</h2>
          <form className="filtros" onSubmit={criar} aria-label="Nova empresa">
            <label>
              Nome
              <input value={nome} onChange={(e) => setNome(e.target.value)} maxLength={100} required />
            </label>
            <label>
              Plano
              <input value={plano} onChange={(e) => setPlano(e.target.value)} maxLength={50} required />
            </label>
            <label>
              Assentos
              <input type="number" min={1} value={assentos} onChange={(e) => setAssentos(Number(e.target.value))} required />
            </label>
            <button className="botao botao-primario" type="submit">Criar empresa</button>
          </form>
        </section>

        {erro && <p className="erro" role="alert">{erro}</p>}
        {criado && (
          <p className="aviso" role="status">
            Gestor <strong>{criado.login}</strong> criado para {criado.empresa}. Senha inicial: <code>{criado.senhaInicial}</code> — ela
            não aparece de novo; o primeiro acesso exige a troca.
          </p>
        )}

        <section aria-labelledby="titulo-empresas">
          <h2 id="titulo-empresas">Empresas</h2>
          {!empresas ? (
            <p className="carregando" role="status">Carregando…</p>
          ) : empresas.length === 0 ? (
            <p className="vazio">Nenhuma empresa cadastrada.</p>
          ) : (
            <table className="tabela">
              <thead>
                <tr><th>Empresa</th><th>Plano</th><th className="numero">Assentos</th><th>Código de adesão</th><th>Criada em</th><th>Convidar gestor</th></tr>
              </thead>
              <tbody>
                {empresas.map((e) => (
                  <tr key={e.id}>
                    <td>{e.nome}</td>
                    <td>{e.plano}</td>
                    <td className="numero">{e.colaboradores} / {e.assentos}</td>
                    <td><code>{e.codigoDeAdesao}</code></td>
                    <td>{formatar.dataHora(e.criadaEm)}</td>
                    <td>
                      <div className="acoes">
                        <input
                          aria-label={`Login do gestor de ${e.nome}`}
                          value={convites[e.id] ?? ''}
                          onChange={(ev) => setConvites({ ...convites, [e.id]: ev.target.value })}
                          placeholder="login"
                        />
                        <button className="botao botao-fantasma" type="button" disabled={!(convites[e.id] ?? '').trim()} onClick={() => void convidar(e)}>
                          Convidar
                        </button>
                      </div>
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
