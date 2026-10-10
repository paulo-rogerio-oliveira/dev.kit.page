import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';

/**
 * A adesão do dev por código e e-mail (US #425, /aderir?codigo=…): cria o usuário na empresa do código e manda o
 * link de definir a senha ao e-mail. O e-mail que já está em outra empresa e a empresa sem assento são recusados
 * pela API, com a mensagem dela.
 */
export function Aderir() {
  const [parametros] = useSearchParams();
  const [codigo, setCodigo] = useState(parametros.get('codigo') ?? '');
  const [email, setEmail] = useState('');
  const [nome, setNome] = useState('');
  const [erro, setErro] = useState('');
  const [feito, setFeito] = useState('');
  const [enviando, setEnviando] = useState(false);

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setErro('');
    setEnviando(true);
    try {
      setFeito((await api.aderir(codigo.trim(), email.trim(), nome.trim())).mensagem);
    } catch (falha) {
      const campos = falha instanceof ErroDaApi ? Object.values(falha.erros).flat() : [];
      setErro(campos.length > 0 ? campos.join(' ') : falha instanceof ErroDaApi ? falha.message : 'Não foi possível falar com a API. Tente de novo.');
    } finally {
      setEnviando(false);
    }
  }

  return (
    <div className="pagina pagina-centro">
      <form className="cartao formulario" onSubmit={enviar} aria-labelledby="titulo-aderir">
        <Link to="/" className="marca">dev<span className="marca-ponto">.</span>kit</Link>
        <h1 id="titulo-aderir">Entrar no time</h1>
        <p className="nota-da-secao">Informe o código de adesão que o gestor entregou e o seu e-mail: o link para definir a senha chega por e-mail.</p>
        {feito ? (
          <p className="aviso" role="status">{feito}</p>
        ) : (
          <>
            <label>
              Código de adesão
              <input value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="DK-XXXX-XXXX" required autoComplete="off" />
            </label>
            <label>
              E-mail
              <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoComplete="email" />
            </label>
            <label>
              Nome
              <input value={nome} onChange={(e) => setNome(e.target.value)} maxLength={100} autoComplete="name" />
            </label>
            {erro && <p className="erro" role="alert">{erro}</p>}
            <button className="botao botao-primario" type="submit" disabled={enviando}>{enviando ? 'Enviando…' : 'Aderir'}</button>
          </>
        )}
        <Link to="/login">Já tenho senha: entrar</Link>
      </form>
    </div>
  );
}
