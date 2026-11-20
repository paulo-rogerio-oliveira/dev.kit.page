import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Link, NavLink, useLocation } from 'react-router-dom';
import { useSessao } from '../sessao';
import { comRetorno, destinoDoCaminho, destinos, inicioDaSessao, listaDeProjetos, podeAbrir, retornoSeguro } from '../navegacao';
import { Avatar } from './Avatar';
import './Topo.css';

export function Marca({ secao, para = '/' }: { secao?: string; para?: string }) {
  return <Link to={para} className="marca">
    <span className="marca-icone" aria-hidden="true">dK</span>
    <span>dev<span className="marca-ponto">.</span>kit</span>
    {secao && <small className="marca-secao">{secao}</small>}
  </Link>;
}
/** Um único menu para todos os papéis e páginas, no desktop e no celular. */
export function Topo({ secao, children }: { secao: string; children?: ReactNode }) {
  const { sessao, sair } = useSessao();
  const local = useLocation();
  const [aberto, setAberto] = useState(false);
  const botao = useRef<HTMLButtonElement>(null);
  const painel = useRef<HTMLElement>(null);
  const anterior = useRef(local.pathname);
  const destino = destinoDoCaminho(local.pathname);
  const logado = !!sessao && !!destino && !sessao.deveTrocarSenha;
  const consulta = new URLSearchParams(local.search);
  const versao = local.pathname.startsWith('/design/');
  const detalhe = local.pathname === '/projetos' && (consulta.has('projeto') || consulta.has('slug'));
  const retorno = sessao ? retornoSeguro(sessao, consulta.get('retorno'), local.pathname) : null;
  const voltar = retorno ?? (detalhe ? listaDeProjetos(consulta) : versao ? '/design' : local.pathname === '/conta' ? '/board' : '/conta');
  const nomeDeVolta = destinoDoCaminho(voltar.split(/[?#]/)[0])?.nome ?? 'Minha conta';

  useEffect(() => {
    setAberto(false);
    if (anterior.current !== local.pathname) {
      anterior.current = local.pathname;
      const titulo = document.querySelector<HTMLElement>('main h1');
      if (titulo) { titulo.tabIndex = -1; titulo.focus({ preventScroll: true }); }
    }
  }, [local.key, local.pathname]);
  useEffect(() => {
    if (!aberto) return;
    painel.current?.querySelector<HTMLAnchorElement>('a')?.focus();
    const fechar = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { setAberto(false); botao.current?.focus(); }
    };
    document.addEventListener('keydown', fechar);
    return () => document.removeEventListener('keydown', fechar);
  }, [aberto]);

  return <>
    <header className={`topo${logado ? ' topo-unificado' : ''}`}>
      <Marca secao={logado ? undefined : secao} para={sessao ? inicioDaSessao(sessao) : '/'} />
      {logado && <a className="pular-conteudo" href="#conteudo-principal">Ir para o conteúdo</a>}
      <nav aria-label="Sessão">
        {!logado && children && <div className="topo-menu">{children}</div>}
        {sessao ? <>
          <span className="usuario"><Avatar nome={sessao.login} /><span>{sessao.login}{sessao.empresa ? ` · ${sessao.empresa}` : ''}</span></span>
          <button className="botao botao-fantasma sair-da-sessao" type="button" onClick={() => sair()}>Sair</button>
        </> : <Link className="botao botao-fantasma" to="/login">Entrar</Link>}
        {logado && <button ref={botao} className="botao botao-fantasma navegacao-menu-botao" type="button" aria-expanded={aberto}
          aria-controls="navegacao-principal" onClick={() => setAberto(a => !a)}>☰ Menu</button>}
      </nav>
    </header>
    {logado && <>
      <aside className={`navegacao-lateral${aberto ? ' aberta' : ''}`}>
        <nav id="navegacao-principal" aria-label="Navegação principal" ref={painel}>
          {destinos.filter(d => podeAbrir(sessao, d.caminho)).map(d => <NavLink key={d.caminho}
            to={d.caminho === '/design' && local.pathname !== '/design' && !versao ? comRetorno(d.caminho, local.pathname + local.search + local.hash) : d.caminho}
            onClick={() => { if (aberto) { setAberto(false); requestAnimationFrame(() => document.getElementById('conteudo-principal')?.focus({ preventScroll: true })); } }}
            className={({ isActive }) => isActive ? 'navegacao-link ativo' : 'navegacao-link'}>{d.nome}</NavLink>)}
        </nav>
        <p>Links de administração aparecem conforme o seu acesso.</p>
        <button className="botao botao-fantasma navegacao-sair" onClick={() => sair()}>Sair</button>
      </aside>
      <div className="navegacao-localizacao">
        <nav aria-label="Localização">
          <Link to={local.pathname === '/conta' ? '/board' : '/conta'}>{local.pathname === '/conta' ? 'Meu espaço' : 'Minha conta'}</Link><span aria-hidden="true"> › </span>
          {versao ? <><Link to={retorno?.startsWith('/design?') ? retorno : '/design'}>Design</Link><span aria-hidden="true"> › </span><span aria-current="page">Versão</span></>
            : detalhe ? <><Link to={listaDeProjetos(consulta)}>Projetos</Link><span aria-hidden="true"> › </span><span aria-current="page">Projeto</span></>
            : <span aria-current="page">{destino.nome}</span>}
        </nav>
        <Link className="botao botao-fantasma navegacao-voltar" to={voltar}>← {local.pathname === '/conta' ? 'Meu espaço' : nomeDeVolta}</Link>
        {local.state?.acessoNegado && <p className="aviso" role="status">Acesso negado à seção solicitada. Você pode continuar pelas opções do seu menu.</p>}
      </div>
    </>}
  </>;
}
