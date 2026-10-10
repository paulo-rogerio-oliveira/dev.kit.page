import { useState, type FormEvent } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import { destinoDa, useSessao } from '../sessao';

/**
 * O login: credencial inválida mostra o erro; a troca de senha pendente leva à troca. Depois dele a
 * sessão vai para a página que pediu o login (a landing, ao baixar o dev.kit) ou para o seu destino —
 * o painel, ou a página inicial para o dev (US #405).
 */
export function Login() {
  const { sessao, motivo, entrar } = useSessao();
  const navegar = useNavigate();
  const de = (useLocation().state as { de?: string } | null)?.de;
  const [login, setLogin] = useState('');
  const [senha, setSenha] = useState('');
  const [erro, setErro] = useState('');
  const [enviando, setEnviando] = useState(false);

  if (sessao) return <Navigate to={destinoDa(sessao, de)} replace />;

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setErro('');
    setEnviando(true);
    try {
      const resposta = await api.login(login.trim(), senha);
      entrar(resposta);
      navegar(destinoDa(resposta, de), { replace: true });
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível falar com a API. Tente de novo.');
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="pagina pagina-centro">
      <form className="cartao formulario" onSubmit={enviar} aria-labelledby="titulo-login">
        <Link to="/" className="marca">dev<span className="marca-ponto">.</span>kit</Link>
        <h1 id="titulo-login">Entrar</h1>
        {motivo === 'expirou' && <p className="aviso" role="status">Sua sessão expirou. Entre de novo.</p>}
        {motivo !== 'expirou' && de === '/' && <p className="aviso" role="status">Entre para baixar o dev.kit.</p>}
        <label>
          Login
          <input value={login} onChange={(e) => setLogin(e.target.value)} autoComplete="username" required />
        </label>
        <label>
          Senha
          <input type="password" value={senha} onChange={(e) => setSenha(e.target.value)} autoComplete="current-password" required />
        </label>
        {erro && <p className="erro" role="alert">{erro}</p>}
        <button className="botao botao-primario" type="submit" disabled={enviando}>
          {enviando ? 'Entrando…' : 'Entrar'}
        </button>
      </form>
    </div>
  );
}
