import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { useSessao } from '../sessao';

/**
 * O cabeçalho das páginas da US #425 (a conta, a empresa do gestor, o catálogo): a marca com o nome da seção, os
 * links que a página passa e, com sessão, quem está logado e o Sair; sem sessão, o Entrar.
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
            <span className="usuario">{sessao.login}{sessao.empresa ? ` · ${sessao.empresa}` : ''}</span>
            <button className="botao botao-fantasma" type="button" onClick={() => sair()}>Sair</button>
          </>
        ) : (
          <Link className="botao botao-fantasma" to="/login">Entrar</Link>
        )}
      </nav>
    </header>
  );
}
