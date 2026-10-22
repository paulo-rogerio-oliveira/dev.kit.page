import { useState, type FormEvent } from 'react';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import { TelaDeAcesso } from '../componentes/TelaDeAcesso';
import { destinoDa, useSessao } from '../sessao';

/**
 * O login: credencial inválida mostra o erro; a troca de senha pendente leva à troca. Depois dele a
 * sessão vai para a página que pediu o login ou para o seu destino — o painel, ou a conta para o dev
 * (US #425). O download do dev.kit não pede mais login (US #425, a reversão da US #405); daqui saem o
 * link novo de senha e a adesão por código. Na tela dividida da marca (versão 5): o painel à esquerda e o
 * formulário maior, num cartão, à direita.
 */
export function Login() {
  const { sessao, motivo, entrar } = useSessao();
  const navegar = useNavigate();
  const de = (useLocation().state as { de?: string } | null)?.de;
  const [login, setLogin] = useState('');
  const [senha, setSenha] = useState('');
  const [mostrar, setMostrar] = useState(false);
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
    <TelaDeAcesso
      rotulo="Entrar no dev.kit"
      titulo="Seu time de agentes, do work item à Pull Request."
      destaque="Pull Request"
      texto="Entre com o login do seu time para acompanhar as tasks, aprovar entregas e falar com o Orquestrador."
      itens={[
        'Agentes que implementam, compilam e testam com as regras do time',
        'Avaliadores com nota mínima antes do commit e do PR',
        'Você acompanha e aprova pelo Board',
      ]}
    >
      <form className="cartao formulario formulario-de-acesso" onSubmit={enviar} aria-labelledby="titulo-login">
        <div className="formulario-cabecalho">
          <h1 id="titulo-login">Entrar</h1>
          <p>Use o login e a senha do seu time.</p>
        </div>
        {motivo === 'expirou' && <p className="aviso" role="status">Sua sessão expirou. Entre de novo.</p>}
        <label>
          Login
          <input value={login} onChange={(e) => setLogin(e.target.value)} autoComplete="username" placeholder="nome@empresa.com.br" required />
        </label>
        <div className="campo">
          <label htmlFor="login-senha">Senha</label>
          <div className="campo-com-acao">
            <input id="login-senha" type={mostrar ? 'text' : 'password'} value={senha} onChange={(e) => setSenha(e.target.value)} autoComplete="current-password" required />
            {/* Sem aria-label com "senha": o rótulo do campo é só dele (o getByLabel('Senha') acha um elemento). */}
            <button className="campo-acao" type="button" aria-pressed={mostrar} aria-controls="login-senha" onClick={() => setMostrar((m) => !m)}>
              {mostrar ? 'ocultar' : 'mostrar'}
            </button>
          </div>
        </div>
        <Link className="link-do-formulario" to="/definir-senha">Esqueci a senha</Link>
        {erro && <p className="erro" role="alert">{erro}</p>}
        <button className="botao botao-primario botao-grande" type="submit" disabled={enviando}>
          {enviando ? 'Entrando…' : 'Entrar'}
        </button>
        <p className="divisor"><span>primeira vez no time?</span></p>
        <Link className="botao botao-fantasma botao-grande" to="/aderir">Tenho um código de adesão</Link>
      </form>
    </TelaDeAcesso>
  );
}
