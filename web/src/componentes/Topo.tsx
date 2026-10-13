import type { ReactNode } from 'react';
import { Link, NavLink } from 'react-router-dom';
import { useSessao } from '../sessao';
import { Avatar } from './Avatar';

/**
 * O cabeçalho das páginas da US #425 (a conta, a empresa do gestor, o catálogo) — e, desde a US #461, também de
 * Empresas e Usuários: a marca com o nome da seção, os links que a página passa e, com sessão, quem está logado
 * (com o avatar) e o Sair; sem sessão, o Entrar.
 */
export function Topo({ secao, children }: { secao: string; children?: ReactNode }) {
  const { sessao, sair } = useSessao();
  return (
    <header className="topo">
      <Link to="/" className="marca">dev<span className="marca-ponto">.</span>kit <small>{secao}</small></Link>
      <nav>
        {children}
        {sessao ? (
          <>
            <span className="usuario"><Avatar nome={sessao.login} />{sessao.login}{sessao.empresa ? ` · ${sessao.empresa}` : ''}</span>
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

/** Os links do admin entre as páginas dele (US #461): Uso (o dashboard), Empresas, Usuários e Catálogo — o atual destacado. */
export function MenuDoAdmin() {
  return (
    <>
      <NavLink className={classeDoLink} to="/dashboard">Uso</NavLink>
      <NavLink className={classeDoLink} to="/empresas">Empresas</NavLink>
      <NavLink className={classeDoLink} to="/usuarios">Usuários</NavLink>
      <NavLink className={classeDoLink} to="/catalogo">Catálogo</NavLink>
    </>
  );
}
