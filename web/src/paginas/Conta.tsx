import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import type { Conta as DadosDaConta } from '../api/tipos';
import { Topo } from '../componentes/Topo';
import { papelDa, useSessao } from '../sessao';

/**
 * A conta de quem está logado (US #425, /conta) — o destino do dev depois do login: quem é, a empresa, a política
 * de login que vale para ele e, na assinatura individual, o "Gerenciar assinatura" (o portal do gateway). O gestor
 * chega daqui à administração da empresa.
 */
export function Conta() {
  const { sessao, sair } = useSessao();
  const [conta, setConta] = useState<DadosDaConta | null>(null);
  const [erro, setErro] = useState('');

  useEffect(() => {
    api.conta(sessao!.token).then(setConta).catch((falha) => {
      if (falha instanceof ErroDaApi && falha.status === 401) sair('expirou');
      else setErro(falha instanceof Error ? falha.message : 'Falha ao consultar a API.');
    });
  }, [sessao, sair]);

  const ehGestor = papelDa(sessao!) === 'gestor';
  const daEmpresa = papelDa(sessao!) !== 'admin';
  return (
    <div className="pagina">
      <Topo secao="conta">
        {ehGestor && <Link className="botao botao-fantasma" to="/empresa/usuarios">Usuários da empresa</Link>}
        {ehGestor && <Link className="botao botao-fantasma" to="/empresa/catalogo">Catálogo da empresa</Link>}
        {daEmpresa && <Link className="botao botao-fantasma" to="/contexto">Contexto</Link>}
        {daEmpresa && <Link className="botao botao-fantasma" to="/empresa/conhecimento">Repositórios</Link>}
        <Link className="botao botao-fantasma" to="/catalogo">Catálogo</Link>
      </Topo>
      <main className="painel conta">
        <section aria-labelledby="titulo-conta">
          <h1 id="titulo-conta">Sua conta</h1>
          {erro && <p className="erro" role="alert">{erro}</p>}
          {!conta ? (
            !erro && <p className="carregando" role="status">Carregando…</p>
          ) : (
            <dl>
              <dt>Login</dt><dd>{conta.login}</dd>
              <dt>Nome</dt><dd>{conta.nome || '—'}</dd>
              <dt>E-mail</dt><dd>{conta.email || '—'}</dd>
              <dt>Empresa</dt><dd>{conta.empresa ?? '—'}{conta.individual ? ' (individual)' : ''}</dd>
              <dt>Assinatura</dt><dd>{conta.empresa ? (conta.assinaturaAtiva ? 'Ativa' : 'Cancelada: a licença não renova mais') : '—'}</dd>
              <dt>Login no dev.kit</dt><dd>{conta.loginObrigatorio ? 'Obrigatório' : 'Opcional'}</dd>
            </dl>
          )}
        </section>
        {conta?.urlGerenciarAssinatura && (
          <section aria-labelledby="titulo-assinatura">
            <h2 id="titulo-assinatura">Assinatura</h2>
            <p className="nota-da-secao">Troque o cartão, veja as faturas ou cancele no portal de pagamento.</p>
            <a className="botao botao-primario" href={conta.urlGerenciarAssinatura} target="_blank" rel="noreferrer">Gerenciar assinatura</a>
          </section>
        )}
        <section aria-labelledby="titulo-baixar">
          <h2 id="titulo-baixar">O dev.kit</h2>
          <p className="nota-da-secao">Entre no dev.kit com o mesmo login: a licença se renova sozinha enquanto a assinatura estiver ativa.</p>
          <Link className="botao botao-fantasma" to="/">Baixar o dev.kit</Link>
        </section>
      </main>
    </div>
  );
}
