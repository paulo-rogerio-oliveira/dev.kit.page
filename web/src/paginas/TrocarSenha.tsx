import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import { useSessao } from '../sessao';

/** A troca de senha — obrigatória no primeiro acesso do admin, antes de qualquer outra tela. */
export function TrocarSenha() {
  const { sessao, entrar, sair } = useSessao();
  const navegar = useNavigate();
  const [atual, setAtual] = useState('');
  const [nova, setNova] = useState('');
  const [confirmacao, setConfirmacao] = useState('');
  const [erro, setErro] = useState('');

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setErro('');
    if (nova !== confirmacao) {
      setErro('A confirmação não confere com a nova senha.');
      return;
    }
    try {
      entrar(await api.trocarSenha(sessao!.token, atual, nova));
      navegar('/dashboard', { replace: true });
    } catch (falha) {
      if (falha instanceof ErroDaApi && falha.status === 401) {
        sair('expirou');
        return;
      }
      setErro(falha instanceof ErroDaApi ? falha.message : 'Não foi possível falar com a API.');
    }
  }

  return (
    <div className="pagina pagina-centro">
      <form className="cartao formulario" onSubmit={enviar} aria-labelledby="titulo-troca">
        <h1 id="titulo-troca">Defina a sua senha</h1>
        {sessao?.deveTrocarSenha && (
          <p className="aviso" role="status">Primeiro acesso: troque a senha inicial para acessar o dashboard.</p>
        )}
        <label>
          Senha atual
          <input type="password" value={atual} onChange={(e) => setAtual(e.target.value)} autoComplete="current-password" required />
        </label>
        <label>
          Nova senha (10+ caracteres, letras e números)
          <input type="password" value={nova} onChange={(e) => setNova(e.target.value)} autoComplete="new-password" required />
        </label>
        <label>
          Confirme a nova senha
          <input type="password" value={confirmacao} onChange={(e) => setConfirmacao(e.target.value)} autoComplete="new-password" required />
        </label>
        {erro && <p className="erro" role="alert">{erro}</p>}
        <button className="botao botao-primario" type="submit">Trocar senha</button>
        <button className="botao botao-fantasma" type="button" onClick={() => sair()}>Sair</button>
      </form>
    </div>
  );
}
