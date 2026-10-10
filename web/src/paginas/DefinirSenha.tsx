import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';

/**
 * A página do link do e-mail (US #425, /definir-senha?token=…): o link é de uso único e vale 24 horas — a API diz
 * quando ele já foi usado ou venceu. Sem o token (ou depois de um link vencido), a página pede um link novo pelo
 * e-mail; a resposta é a mesma exista ou não a conta.
 */
export function DefinirSenha() {
  const [parametros] = useSearchParams();
  const token = parametros.get('token') ?? '';
  const [nova, setNova] = useState('');
  const [confirmacao, setConfirmacao] = useState('');
  const [email, setEmail] = useState('');
  const [erro, setErro] = useState('');
  const [aviso, setAviso] = useState('');
  const [definida, setDefinida] = useState(false);

  async function definir(evento: FormEvent) {
    evento.preventDefault();
    setErro('');
    if (nova !== confirmacao) {
      setErro('A confirmação não confere com a nova senha.');
      return;
    }
    try {
      await api.definirSenha(token, nova);
      setDefinida(true);
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível falar com a API. Tente de novo.');
    }
  }

  async function pedirLink(evento: FormEvent) {
    evento.preventDefault();
    setErro('');
    try {
      setAviso((await api.pedirLink(email.trim())).mensagem);
    } catch (falha) {
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível falar com a API. Tente de novo.');
    }
  }

  if (definida) {
    return (
      <div className="pagina pagina-centro">
        <div className="cartao formulario">
          <h1>Senha definida</h1>
          <p className="aviso" role="status">Pronto: entre no dev.kit (e aqui no site) com o seu e-mail e a senha nova.</p>
          <Link className="botao botao-primario" to="/login">Entrar</Link>
        </div>
      </div>
    );
  }

  return (
    <div className="pagina pagina-centro">
      {token ? (
        <form className="cartao formulario" onSubmit={definir} aria-labelledby="titulo-definir">
          <Link to="/" className="marca">dev<span className="marca-ponto">.</span>kit</Link>
          <h1 id="titulo-definir">Defina a sua senha</h1>
          <label>
            Nova senha (10+ caracteres, letras e números)
            <input type="password" value={nova} onChange={(e) => setNova(e.target.value)} autoComplete="new-password" required />
          </label>
          <label>
            Confirme a nova senha
            <input type="password" value={confirmacao} onChange={(e) => setConfirmacao(e.target.value)} autoComplete="new-password" required />
          </label>
          {erro && <p className="erro" role="alert">{erro}</p>}
          <button className="botao botao-primario" type="submit">Definir senha</button>
        </form>
      ) : (
        <form className="cartao formulario" onSubmit={pedirLink} aria-labelledby="titulo-link">
          <Link to="/" className="marca">dev<span className="marca-ponto">.</span>kit</Link>
          <h1 id="titulo-link">Receber um link</h1>
          <p className="nota-da-secao">Mandamos ao seu e-mail um link para definir a senha (vale 24 horas, uma vez só).</p>
          <label>
            E-mail
            <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="email" required />
          </label>
          {aviso && <p className="aviso" role="status">{aviso}</p>}
          {erro && <p className="erro" role="alert">{erro}</p>}
          <button className="botao botao-primario" type="submit">Enviar o link</button>
        </form>
      )}
    </div>
  );
}
