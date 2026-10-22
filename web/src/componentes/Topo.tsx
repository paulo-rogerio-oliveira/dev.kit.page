import type { ReactNode } from 'react';
import { Link, NavLink } from 'react-router-dom';
import { useSessao } from '../sessao';
import { Avatar } from './Avatar';

/**
 * A marca do dev.kit (a identidade da versão 5): o selo "dK" vermelho, o "dev.kit" com o ponto vermelho e, quando
 * há, o nome da seção numa pílula mono. O selo é decorativo: o nome acessível do link é "dev.kit".
 */
export function Marca({ secao, para = '/' }: { secao?: string; para?: string }) {
  return (
    <Link to={para} className="marca">
      <span className="marca-icone" aria-hidden="true">dK</span>
      <span>dev<span className="marca-ponto">.</span>kit</span>
      {secao && <small className="marca-secao">{secao}</small>}
    </Link>
  );
}

/**
 * O cabeçalho das páginas da US #425 (a conta, a empresa do gestor, o catálogo) — e, desde a US #461, também de
 * Empresas e Usuários: a marca com o nome da seção, os links que a página passa (agrupados no menu, com o atual
 * marcado em vermelho) e, com sessão, quem está logado (com o avatar) e o Sair; sem sessão, o Entrar.
 */
export function Topo({ secao, children }: { secao: string; children?: ReactNode }) {
  const { sessao, sair } = useSessao();
  return (
    <header className="topo">
      <Marca secao={secao} />
      <nav>
        {children && <div className="topo-menu">{children}</div>}
        {sessao ? (
          <>
            <span className="usuario"><Avatar nome={sessao.login} /><span>{sessao.login}{sessao.empresa ? ` · ${sessao.empresa}` : ''}</span></span>
            <button className="botao botao-fantasma" type="button" onClick={() => sair()}>Sair</button>
          </>
        ) : (
          <Link className="botao botao-fantasma" to="/login">Entrar</Link>
        )}
      </nav>
    </header>
  );
}

const classeDoLink = ({ isActive }: { isActive: boolean }) => `botao botao-fantasma${isActive ? ' link-ativo' : ''}`;

/**
 * Os links do admin entre as páginas dele (US #461): Uso (o dashboard), Empresas, Usuários, Catálogo e, desde a US #602,
 * Relatos — o atual destacado.
 */
export function MenuDoAdmin() {
  return (
    <>
      <NavLink className={classeDoLink} to="/dashboard">Uso</NavLink>
      <NavLink className={classeDoLink} to="/empresas">Empresas</NavLink>
      <NavLink className={classeDoLink} to="/usuarios">Usuários</NavLink>
      <NavLink className={classeDoLink} to="/catalogo">Catálogo</NavLink>
      <NavLink className={classeDoLink} to="/relatos">Relatos</NavLink>
    </>
  );
}

/**
 * Os links do GESTOR entre as páginas da empresa (as propostas de marca): Dashboard, Usuários da empresa, Catálogo da
 * empresa, Repositórios e convenções e Relatos — o atual destacado, como no menu do admin.
 */
export function MenuDaEmpresa() {
  return (
    <>
      <NavLink className={classeDoLink} to="/dashboard">Dashboard</NavLink>
      <NavLink className={classeDoLink} to="/empresa/usuarios">Usuários da empresa</NavLink>
      <NavLink className={classeDoLink} to="/empresa/catalogo">Catálogo da empresa</NavLink>
      <NavLink className={classeDoLink} to="/empresa/conhecimento">Repositórios e convenções</NavLink>
      <NavLink className={classeDoLink} to="/relatos">Relatos</NavLink>
    </>
  );
}
