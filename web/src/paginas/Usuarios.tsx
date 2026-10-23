import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { EmpresaResumo, Papel, UsuarioResumo } from '../api/tipos';
import { Avatar } from '../componentes/Avatar';
import { BotaoCopiar } from '../componentes/BotaoCopiar';
import { Pagina } from '../componentes/Pagina';
import { PainelLateral } from '../componentes/PainelLateral';
import { Selo, type VarianteDoSelo } from '../componentes/Selo';
import { MenuDoAdmin, Topo } from '../componentes/Topo';
import { formatar } from '../formatar';
import { useSessao } from '../sessao';

/** Os papéis na ordem do seletor, com o que cada um abre (a dica embaixo do seletor). */
export const PAPEIS: { valor: Papel; rotulo: string; dica: string }[] = [
  { valor: 'dev', rotulo: 'Dev', dica: 'Usa o dev.kit' },
  { valor: 'gestor', rotulo: 'Gestor', dica: 'Vê o painel e administra a empresa dele' },
  { valor: 'admin', rotulo: 'Admin', dica: 'Administra tudo' },
];

const NOME_DO_PAPEL: Record<Papel, string> = { dev: 'Dev', gestor: 'Gestor', admin: 'Admin' };
const COR_DO_PAPEL: Record<Papel, VarianteDoSelo> = { dev: 'neutro', gestor: 'marca', admin: 'papel' };

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

/** A situação do usuário em uma palavra (e o porquê, quando é o bloqueio temporário), com a cor do selo. */
function situacao(u: UsuarioResumo): { texto: string; variante: VarianteDoSelo } {
  if (u.bloqueado) return { texto: 'Bloqueado', variante: 'aviso' };
  if (u.bloqueadoAte) return { texto: `Bloqueado até ${formatar.dataHora(u.bloqueadoAte)}`, variante: 'aviso' };
  if (u.deveTrocarSenha) return { texto: 'Troca de senha pendente', variante: 'info' };
  return { texto: 'Ativo', variante: 'sucesso' };
}

/** "2 usuários · 1 admin, 1 dev": o resumo do cabeçalho (o gestor só entra quando há). */
export function resumoDosUsuarios(usuarios: UsuarioResumo[]): string {
  const conta = (p: Papel) => usuarios.filter((u) => u.papel === p).length;
  const gestores = conta('gestor');
  return `${usuarios.length} ${usuarios.length === 1 ? 'usuário' : 'usuários'} · ${conta('admin')} admin, ${conta('dev')} dev`
    + (gestores > 0 ? `, ${gestores} ${gestores === 1 ? 'gestor' : 'gestores'}` : '');
}

/** A busca ignora maiúsculas e acentos ("jose" acha "José"). */
const normalizar = (texto: string) => texto.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();

/** O menu ⋯ de uma linha: as ações do usuário; fecha ao escolher, no Esc e no clique fora. */
function MenuDoUsuario({ usuario, acoes }: { usuario: UsuarioResumo; acoes: { rotulo: string; aoEscolher: () => void }[] }) {
  const [aberto, setAberto] = useState(false);
  const raiz = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!aberto) return;
    const fora = (evento: MouseEvent) => {
      if (!raiz.current?.contains(evento.target as Node)) setAberto(false);
    };
    document.addEventListener('mousedown', fora);
    return () => document.removeEventListener('mousedown', fora);
  }, [aberto]);

  return (
    <div className="menu-da-linha" ref={raiz} onKeyDown={(e) => { if (e.key === 'Escape') setAberto(false); }}>
      <button className="botao-icone" type="button" aria-label={`Ações de ${usuario.login}`} aria-haspopup="menu" aria-expanded={aberto} onClick={() => setAberto(!aberto)}>
        ⋯
      </button>
      {aberto && (
        <div className="menu-flutuante" role="menu" aria-label={`Ações de ${usuario.login}`}>
          {acoes.map((a) => (
            <button key={a.rotulo} type="button" role="menuitem" onClick={() => { setAberto(false); a.aoEscolher(); }}>{a.rotulo}</button>
          ))}
        </div>
      )}
    </div>
  );
}

/**
 * Os usuários (US #405; visual da US #461) — só do admin. A lista tem a busca por nome ou login e os filtros de
 * papel e empresa (no navegador: a lista é pequena e já vem inteira), e cada linha o menu ⋯ com as ações: editar o
 * nome, o papel e a empresa, bloquear e desbloquear, e redefinir a senha. O cadastro fica no painel lateral; a
 * senha é TEMPORÁRIA, gerada pela API e devolvida UMA vez (a tela a mostra e não a guarda). O papel `dev` entra no
 * app e baixa o dev.kit, mas não no painel; o gestor precisa de uma empresa.
 */
export function Usuarios() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const [usuarios, setUsuarios] = useState<UsuarioResumo[] | null>(null);
  const [empresas, setEmpresas] = useState<EmpresaResumo[]>([]);
  const [busca, setBusca] = useState('');
  const [filtroDePapel, setFiltroDePapel] = useState<Papel | ''>('');
  const [filtroDeEmpresa, setFiltroDeEmpresa] = useState('');
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
    setErro(mensagemDaFalha(falha));
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

  const termo = normalizar(busca.trim());
  const visiveis = (usuarios ?? []).filter((u) =>
    (!termo || normalizar(`${u.nome} ${u.login}`).includes(termo))
    && (!filtroDePapel || u.papel === filtroDePapel)
    && (!filtroDeEmpresa || (filtroDeEmpresa === 'nenhuma' ? u.empresaId === null : String(u.empresaId) === filtroDeEmpresa)));

  return (
    <div className="pagina">
      <Topo secao="usuários"><MenuDoAdmin /></Topo>

      <Pagina rotulo="Administração" titulo="Usuários"subtitulo={usuarios ? resumoDosUsuarios(usuarios) : undefined} emLinha>
        {erro && <p className="erro" role="alert">{erro}</p>}
        {aviso && <p className="aviso" role="status">{aviso}</p>}
        {senha && (
          <p className="aviso senha-temporaria" role="status">
            {senha.motivo === 'criado' ? <>Usuário <strong>{senha.login}</strong> criado.</> : <>Senha de <strong>{senha.login}</strong> redefinida.</>}{' '}
            Senha temporária: <code>{senha.senha}</code><BotaoCopiar texto={senha.senha} rotulo="Copiar a senha temporária" /> — ela não aparece de novo; o
            primeiro acesso exige a troca.
          </p>
        )}

        <div className="com-lateral">
          <section className="cartao cartao-de-tabela" aria-label="Usuários cadastrados">
            <div className="barra-de-filtros" role="search">
              <input
                className="campo-de-busca"
                type="search"
                aria-label="Buscar por nome ou login"
                placeholder="Buscar por nome ou login"
                value={busca}
                onChange={(e) => setBusca(e.target.value)}
              />
              <select aria-label="Filtrar por papel" value={filtroDePapel} onChange={(e) => setFiltroDePapel(e.target.value as Papel | '')}>
                <option value="">Todos os papéis</option>
                {PAPEIS.map((p) => <option key={p.valor} value={p.valor}>{p.rotulo}</option>)}
              </select>
              <select aria-label="Filtrar por empresa" value={filtroDeEmpresa} onChange={(e) => setFiltroDeEmpresa(e.target.value)}>
                <option value="">Todas as empresas</option>
                <option value="nenhuma">Sem empresa</option>
                {empresas.map((e) => <option key={e.id} value={String(e.id)}>{e.nome}</option>)}
              </select>
            </div>

            {!usuarios ? (
              <p className="carregando" role="status">Carregando…</p>
            ) : visiveis.length === 0 ? (
              <p className="vazio">{usuarios.length === 0 ? 'Nenhum usuário cadastrado.' : 'Nenhum usuário com esses filtros.'}</p>
            ) : (
              <div className="tabela-rolavel-simples">
                <table className="tabela tabela-do-cartao">
                  <thead>
                    <tr><th>Usuário</th><th>Papel</th><th>Empresa</th><th>Situação</th><th>Criado em</th><th><span className="visualmente-oculto">Ações</span></th></tr>
                  </thead>
                  <tbody>
                    {visiveis.map((u) => {
                      const s = situacao(u);
                      const identidade = (
                        <span className="com-avatar">
                          <Avatar nome={u.nome || u.login} />
                          <span className="nome-e-login">
                            <strong>{u.nome || u.login}</strong>
                            <small>{u.nome ? u.login : 'sem nome cadastrado'}</small>
                          </span>
                        </span>
                      );
                      return edicao?.id === u.id ? (
                        <tr key={u.id} className="linha-aberta">
                          <td>{identidade}</td>
                          <td>
                            <select aria-label={`Papel de ${u.login}`} value={edicao.papel} onChange={(e) => setEdicao({ ...edicao, papel: e.target.value as Papel })}>
                              {PAPEIS.map((p) => <option key={p.valor} value={p.valor}>{p.rotulo}</option>)}
                            </select>
                          </td>
                          <td>{seletorDeEmpresa(edicao.empresaId, (id) => setEdicao({ ...edicao, empresaId: id }), `Empresa de ${u.login}`, edicao.papel)}</td>
                          <td colSpan={2}>
                            <input aria-label={`Nome de ${u.login}`} value={edicao.nome} maxLength={100} placeholder="Nome" onChange={(e) => setEdicao({ ...edicao, nome: e.target.value })} />
                          </td>
                          <td>
                            <div className="acoes">
                              <button className="botao botao-primario botao-pequeno" type="button" onClick={() => void salvar()}>Salvar</button>
                              <button className="botao botao-fantasma botao-pequeno" type="button" onClick={() => setEdicao(null)}>Cancelar</button>
                            </div>
                          </td>
                        </tr>
                      ) : (
                        <tr key={u.id}>
                          <td>{identidade}</td>
                          <td><Selo variante={COR_DO_PAPEL[u.papel] ?? 'neutro'}>{NOME_DO_PAPEL[u.papel] ?? u.papel}</Selo></td>
                          <td>{u.empresa ?? '—'}</td>
                          <td><Selo variante={s.variante} ponto={s.variante === 'sucesso'}>{s.texto}</Selo></td>
                          <td>{formatar.data(u.criadoEm)}</td>
                          <td className="coluna-do-menu">
                            <MenuDoUsuario
                              usuario={u}
                              acoes={[
                                { rotulo: 'Editar', aoEscolher: () => { limpar(); setEdicao({ id: u.id, nome: u.nome, papel: u.papel, empresaId: u.empresaId }); } },
                                // O admin não se bloqueia: a linha dele não tem a ação.
                                ...(u.login !== sessao!.login
                                  ? [{ rotulo: u.bloqueado ? 'Desbloquear' : 'Bloquear', aoEscolher: () => void alternarBloqueio(u) }]
                                  : []),
                                { rotulo: 'Redefinir senha', aoEscolher: () => void redefinir(u) },
                              ]}
                            />
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <PainelLateral id="titulo-novo-usuario" titulo="Novo usuário">
            <form className="formulario-lateral" onSubmit={criar} aria-label="Novo usuário">
              <label>
                Login
                <input value={login} onChange={(e) => setLogin(e.target.value)} maxLength={100} required autoComplete="off" placeholder="nome@empresa.com.br" />
              </label>
              <label>
                Nome
                <input value={nome} onChange={(e) => setNome(e.target.value)} maxLength={100} />
              </label>
              <div className="campos-lado-a-lado">
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
              </div>
              <p className="nota-do-formulario">{PAPEIS.find((p) => p.valor === papel)?.dica}</p>
              <p className="nota-informativa">
                A senha temporária aparece uma única vez depois de criar. Repasse ao usuário; no primeiro acesso ele troca.
              </p>
              <button className="botao botao-primario botao-largo" type="submit">Criar usuário</button>
            </form>
          </PainelLateral>
        </div>
      </Pagina>
    </div>
  );
}
