import { useState, type FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import { TelaDeAcesso } from '../componentes/TelaDeAcesso';

/**
 * A adesão do dev por código e e-mail (US #425, /aderir?codigo=…): cria o usuário na empresa do código e manda o
 * link de definir a senha ao e-mail. O e-mail que já está em outra empresa e a empresa sem assento são recusados
 * pela API, com a mensagem dela. Na tela dividida da marca (versão 5), como o login.
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
    <TelaDeAcesso
      rotulo="Entrar no time"
      titulo="Entre no time e comece a entregar com agentes."
      destaque="entregar com agentes"
      texto="Informe o código de adesão que o gestor entregou e o seu e-mail: o link para definir a senha chega por e-mail."
      itens={[
        <><b>1.</b> Código de adesão entregue pelo gestor</>,
        <><b>2.</b> Link para definir a senha no seu e-mail</>,
        <><b>3.</b> Entre no dev.kit com o mesmo login</>,
      ]}
    >
      <form className="cartao formulario formulario-de-acesso" onSubmit={enviar} aria-labelledby="titulo-aderir">
        <div className="formulario-cabecalho">
          <h1 id="titulo-aderir">Entrar no time</h1>
          <p>O link para definir a senha chega por e-mail.</p>
        </div>
        {feito ? (
          <p className="aviso" role="status">{feito}</p>
        ) : (
          <>
            <label>
              Código de adesão
              <input className="campo-codigo" value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="DK-XXXX-XXXX" required autoComplete="off" aria-describedby="dica-codigo" />
            </label>
            <p className="dica-do-campo" id="dica-codigo">Peça ao gestor da sua empresa.</p>
            <label>
              E-mail
              <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="nome@empresa.com.br" required autoComplete="email" />
            </label>
            <label>
              Nome
              <input value={nome} onChange={(e) => setNome(e.target.value)} maxLength={100} placeholder="Como o time te chama" autoComplete="name" />
            </label>
            {erro && <p className="erro" role="alert">{erro}</p>}
            <button className="botao botao-primario botao-grande" type="submit" disabled={enviando}>{enviando ? 'Enviando…' : 'Aderir'}</button>
          </>
        )}
        <p className="divisor"><span>já tem senha?</span></p>
        <Link className="botao botao-fantasma botao-grande" to="/login">Entrar</Link>
      </form>
    </TelaDeAcesso>
  );
}
