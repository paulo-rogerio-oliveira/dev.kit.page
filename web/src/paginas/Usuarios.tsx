import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import type { EmpresaResumo, Papel, UsuarioResumo } from '../api/tipos';
import { formatar } from '../formatar';
import { useSessao } from '../sessao';

/** Os papéis na ordem do seletor, com o que cada um abre. */
export const PAPEIS: { valor: Papel; rotulo: string }[] = [
  { valor: 'dev', rotulo: 'Dev (usa o dev.kit)' },
  { valor: 'gestor', rotulo: 'Gestor (painel da empresa)' },
  { valor: 'admin', rotulo: 'Admin (tudo)' },
];

const NOME_DO_PAPEL: Record<Papel, string> = { dev: 'Dev', gestor: 'Gestor', admin: 'Admin' };

/** A senha temporária acabada de gerar: aparece UMA vez, com o login a quem ela pertence. */
interface SenhaMostrada {
  login: string;
  senha: string;
  motivo: 'criado' | 'redefinida';
}

/** A linha em edição: o que o admin muda (o login não muda — é a identidade da trilha de auditoria). */
interface Edicao {
  id: number;
  nome: string;
  papel: Papel;
  empresaId: number | null;
}

/** A situação do usuário em uma palavra (e o porquê, quando é o bloqueio temporário). */
function situacao(u: UsuarioResumo): string {
  if (u.bloqueado) return 'Bloqueado';
  if (u.bloqueadoAte) return `Bloqueado até ${formatar.dataHora(u.bloqueadoAte)}`;
  if (u.deveTrocarSenha) return 'Troca de senha pendente';
  return 'Ativo';
}

/**
 * Os usuários (US #405) — só do admin, no mesmo desenho da página Empresas. Cria o usuário com uma
 * senha TEMPORÁRIA que a API gera e devolve UMA vez (a tela a mostra e não a guarda), edita o nome, o
 * papel e a empresa, bloqueia e desbloqueia, e redefine a senha. O papel `dev` entra no app e baixa o
 * dev.kit, mas não no painel; o gestor precisa de uma empresa.
 */
export function Usuarios() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const [usuarios, setUsuarios] = useState<UsuarioResumo[] | null>(null);
  const [empresas, setEmpresas] = useState<EmpresaResumo[]>([]);
  const [login, setLogin] = useState('');
  const [nome, setNome] = useState('');
  const [papel, setPapel] = useState<Papel>('dev');
  const [empresaId, setEmpresaId] = useState<number | null>(null);
  const [edicao, setEdicao] = useState<Edicao | null>(null);
  const [senha, setSenha] = useState<SenhaMostrada | null>(null);
  const [aviso, setAviso] = useState('');
  const [erro, setErro] = useState('');

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) {
      sair('expirou');
      return;
    }
    const campos = falha instanceof ErroDaApi ? Object.values(falha.erros).flat() : [];
    setErro(campos.length > 0 ? campos.join(' ') : falha instanceof Error ? falha.message : 'Falha ao consultar a API.');
  }, [sair]);

  const carregar = useCallback(() => api.usuarios(token).then(setUsuarios).catch(tratar), [token, tratar]);

  useEffect(() => {
    void carregar();
    api.empresas(token).then(setEmpresas).catch(tratar);
  }, [carregar, token, tratar]);

  function limpar() {
    setErro('');
    setAviso('');
    setSenha(null);
  }

  async function criar(evento: FormEvent) {
    evento.preventDefault();
    limpar();
    try {
      const criado = await api.criarUsuario(token, { login: login.trim(), nome: nome.trim(), papel, empresaId: papel === 'admin' ? null : empresaId });
      setSenha({ login: criado.usuario.login, senha: criado.senhaTemporaria, motivo: 'criado' });
      setLogin('');
      setNome('');
      await carregar();
    } catch (falha) {
      tratar(falha);
    }
  }

  async function salvar() {
    if (!edicao) return;
    limpar();
    try {
      const editado = await api.editarUsuario(token, edicao.id, {
        nome: edicao.nome.trim(), papel: edicao.papel, empresaId: edicao.papel === 'admin' ? null : edicao.empresaId,
      });
      setEdicao(null);
      setAviso(`Usuário ${editado.login} atualizado.`);
      await carregar();
    } catch (falha) {
      tratar(falha);
    }
  }

  async function alternarBloqueio(u: UsuarioResumo) {
    limpar();
    try {
      const alterado = await api.bloquearUsuario(token, u.id, !u.bloqueado);
      setAviso(alterado.bloqueado ? `Usuário ${u.login} bloqueado: ele não entra até ser desbloqueado.` : `Usuário ${u.login} desbloqueado.`);
      await carregar();
    } catch (falha) {
      tratar(falha);
    }
  }

  async function redefinir(u: UsuarioResumo) {
    limpar();
    if (!window.confirm(`Gerar uma senha temporária nova para ${u.login}? A atual deixa de valer.`)) return;
    try {
      const redefinido = await api.redefinirSenha(token, u.id);
      setSenha({ login: u.login, senha: redefinido.senhaTemporaria, motivo: 'redefinida' });
      await carregar();
    } catch (falha) {
      tratar(falha);
    }
  }

  const seletorDeEmpresa = (valor: number | null, mudar: (id: number | null) => void, rotulo: string, papelAtual: Papel) => (
    <select aria-label={rotulo} value={valor ?? ''} disabled={papelAtual === 'admin'} onChange={(e) => mudar(e.target.value ? Number(e.target.value) : null)}>
      <option value="">{papelAtual === 'gestor' ? 'Escolha a empresa' : 'Nenhuma'}</option>
      {empresas.map((e) => <option key={e.id} value={e.id}>{e.nome}</option>)}
    </select>
  );

  return (
    <div className="pagina">
      <header className="topo">
        <span className="marca">dev<span className="marca-ponto">.</span>kit <small>usuários</small></span>
        <nav>
          <Link className="botao botao-fantasma" to="/dashboard">Dashboard</Link>
          <Link className="botao botao-fantasma" to="/empresas">Empresas</Link>
          <span className="usuario">{sessao!.login}</span>
          <button className="botao botao-fantasma" type="button" onClick={() => sair()}>Sair</button>
        </nav>
      </header>

      <main className="painel">
        <section aria-labelledby="titulo-novo-usuario">
          <h2 id="titulo-novo-usuario">Novo usuário</h2>
          <form className="filtros" onSubmit={criar} aria-label="Novo usuário">
            <label>
              Login
              <input value={login} onChange={(e) => setLogin(e.target.value)} maxLength={100} required autoComplete="off" />
            </label>
            <label>
              Nome
              <input value={nome} onChange={(e) => setNome(e.target.value)} maxLength={100} />
            </label>
            <label>
              Papel
              <select value={papel} onChange={(e) => setPapel(e.target.value as Papel)}>
                {PAPEIS.map((p) => <option key={p.valor} value={p.valor}>{p.rotulo}</option>)}
              </select>
            </label>
            <label>
              Empresa
              {seletorDeEmpresa(empresaId, setEmpresaId, 'Empresa do novo usuário', papel)}
            </label>
            <button className="botao botao-primario" type="submit">Criar usuário</button>
          </form>
          <p className="nota-da-secao">
            A senha é temporária, gerada pela API e mostrada uma única vez: repasse-a ao usuário — o primeiro acesso exige a troca.
          </p>
        </section>

        {erro && <p className="erro" role="alert">{erro}</p>}
        {aviso && <p className="aviso" role="status">{aviso}</p>}
        {senha && (
          <p className="aviso senha-temporaria" role="status">
            {senha.motivo === 'criado' ? <>Usuário <strong>{senha.login}</strong> criado.</> : <>Senha de <strong>{senha.login}</strong> redefinida.</>}{' '}
            Senha temporária: <code>{senha.senha}</code> — ela não aparece de novo; o primeiro acesso exige a troca.
          </p>
        )}

        <section aria-labelledby="titulo-usuarios">
          <h2 id="titulo-usuarios">Usuários</h2>
          {!usuarios ? (
            <p className="carregando" role="status">Carregando…</p>
          ) : (
            <table className="tabela">
              <thead>
                <tr><th>Login</th><th>Nome</th><th>Papel</th><th>Empresa</th><th>Situação</th><th>Criado em</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {usuarios.map((u) => (edicao?.id === u.id ? (
                  <tr key={u.id} className="linha-aberta">
                    <td>{u.login}</td>
                    <td><input aria-label={`Nome de ${u.login}`} value={edicao.nome} maxLength={100} onChange={(e) => setEdicao({ ...edicao, nome: e.target.value })} /></td>
                    <td>
                      <select aria-label={`Papel de ${u.login}`} value={edicao.papel} onChange={(e) => setEdicao({ ...edicao, papel: e.target.value as Papel })}>
                        {PAPEIS.map((p) => <option key={p.valor} value={p.valor}>{NOME_DO_PAPEL[p.valor]}</option>)}
                      </select>
                    </td>
                    <td>{seletorDeEmpresa(edicao.empresaId, (id) => setEdicao({ ...edicao, empresaId: id }), `Empresa de ${u.login}`, edicao.papel)}</td>
                    <td>{situacao(u)}</td>
                    <td>{formatar.dataHora(u.criadoEm)}</td>
                    <td>
                      <div className="acoes">
                        <button className="botao botao-primario" type="button" onClick={() => void salvar()}>Salvar</button>
                        <button className="botao botao-fantasma" type="button" onClick={() => setEdicao(null)}>Cancelar</button>
                      </div>
                    </td>
                  </tr>
                ) : (
                  <tr key={u.id}>
                    <td>{u.login}</td>
                    <td>{u.nome || '—'}</td>
                    <td>{NOME_DO_PAPEL[u.papel] ?? u.papel}</td>
                    <td>{u.empresa ?? '—'}</td>
                    <td><span className={`estado ${u.bloqueado || u.bloqueadoAte ? 'estado-regrediu' : u.deveTrocarSenha ? 'estado-novo' : 'estado-resolvido'}`}>{situacao(u)}</span></td>
                    <td>{formatar.dataHora(u.criadoEm)}</td>
                    <td>
                      <div className="acoes">
                        <button className="botao botao-fantasma" type="button" aria-label={`Editar ${u.login}`}
                          onClick={() => { limpar(); setEdicao({ id: u.id, nome: u.nome, papel: u.papel, empresaId: u.empresaId }); }}>
                          Editar
                        </button>
                        {u.login !== sessao!.login && (
                          <button className="botao botao-fantasma" type="button" aria-label={`${u.bloqueado ? 'Desbloquear' : 'Bloquear'} ${u.login}`} onClick={() => void alternarBloqueio(u)}>
                            {u.bloqueado ? 'Desbloquear' : 'Bloquear'}
                          </button>
                        )}
                        <button className="botao botao-fantasma" type="button" aria-label={`Redefinir a senha de ${u.login}`} onClick={() => void redefinir(u)}>
                          Redefinir senha
                        </button>
                      </div>
                    </td>
                  </tr>
                )))}
              </tbody>
            </table>
          )}
        </section>
      </main>
    </div>
  );
}
