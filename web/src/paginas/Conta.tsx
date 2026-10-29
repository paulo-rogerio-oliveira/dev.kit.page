import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { ConvencaoDaEmpresa, Conta as DadosDaConta } from '../api/tipos';
import { EditorDeContexto, ehContexto, ehSugestao, temaDa } from '../componentes/EditorDeContexto';
import { Topo } from '../componentes/Topo';
import { papelDa, useSessao } from '../sessao';

/**
 * A conta de quem está logado (US #425, /conta) — o destino do dev depois do login: quem é, a empresa, a política
 * de login que vale para ele e, na assinatura individual, o "Gerenciar assinatura" (o portal do gateway). O gestor
 * chega daqui à administração da empresa. Desde a US #550, é aqui que cada um cadastra o PRÓPRIO contexto do dev.kit.
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
        {daEmpresa && <Link className="botao botao-fantasma" to="/empresa/conhecimento">Repositórios e convenções</Link>}
        <Link className="botao botao-fantasma" to="/catalogo">Catálogo</Link>
        <Link className="botao botao-fantasma" to="/design">Design</Link>
        <Link className="botao botao-fantasma" to="/projetos">Projetos</Link>
      </Topo>
      <main className="painel conta">
        <header className="cabecalho-da-conta">
          <p className="rotulo-da-pagina">Conta</p>
          <h1 id="titulo-conta">Sua conta</h1>
        </header>
        <section aria-labelledby="titulo-conta" className="cartao dados-da-conta">
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
          <section aria-labelledby="titulo-assinatura" className="cartao">
            <h2 id="titulo-assinatura">Assinatura</h2>
            <p className="nota-da-secao">Troque o cartão, veja as faturas ou cancele no portal de pagamento.</p>
            <a className="botao botao-primario" href={conta.urlGerenciarAssinatura} target="_blank" rel="noreferrer">Gerenciar assinatura</a>
          </section>
        )}
        {conta && daEmpresa && <MeuContexto token={sessao!.token} temEmpresa={!!conta.empresa} sair={sair} />}
        <section aria-labelledby="titulo-baixar" className="cartao">
          <h2 id="titulo-baixar">O dev.kit</h2>
          <p className="nota-da-secao">Entre no dev.kit com o mesmo login: a licença se renova sozinha enquanto a assinatura estiver ativa.</p>
          <Link className="botao botao-fantasma" to="/">Baixar o dev.kit</Link>
        </section>
      </main>
    </div>
  );
}

/**
 * O contexto do dev.kit NO NÍVEL DO USUÁRIO (US #550): o que os agentes do dev.kit dele recebem em toda task, por
 * cima do da equipe e do da empresa — em conflito, vale o dele. Sem empresa, não há a quem o dev.kit pedir o contexto.
 */
function MeuContexto({ token, temEmpresa, sair }: { token: string; temEmpresa: boolean; sair: (motivo?: 'expirou') => void }) {
  const [convencoes, setConvencoes] = useState<ConvencaoDaEmpresa[] | null>(null);
  const [erro, setErro] = useState('');
  const [aviso, setAviso] = useState('');

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) sair('expirou');
    else setErro(mensagemDaFalha(falha));
  }, [sair]);

  const carregar = useCallback(() => api.minhasConvencoes(token).then(setConvencoes).catch(tratar), [token, tratar]);

  useEffect(() => {
    if (temEmpresa) void carregar();
  }, [temEmpresa, carregar]);

  async function gravar(chave: string, valor: string) {
    setErro('');
    setAviso('');
    try {
      await api.gravarConvencao(token, 'usuario', chave, valor, '');
      setAviso(valor ? `Tema ${temaDa(chave)} gravado no seu contexto.` : `Tema ${temaDa(chave)} removido do seu contexto.`);
      await carregar();
      return true;
    } catch (falha) {
      tratar(falha);
      return false;
    }
  }

  /** Promove ou descarta a sugestão de um agente (US #663): a mesma rota do site e do dev.kit. */
  async function naSugestao(acao: () => Promise<unknown>, mensagem: string) {
    setErro('');
    setAviso('');
    try {
      await acao();
      setAviso(mensagem);
      await carregar();
      return true;
    } catch (falha) {
      tratar(falha);
      await carregar();
      return false;
    }
  }

  const meus = (convencoes ?? []).filter((c) => c.nivel === 'usuario' && ehContexto(c.chave));
  const daEmpresa = (convencoes ?? []).filter((c) => c.nivel === 'empresa' && ehContexto(c.chave) && !ehSugestao(c));
  return (
    <section aria-labelledby="titulo-meu-contexto" className="cartao secao-do-contexto">
      <p className="rotulo-da-pagina">Contexto</p>
      <h2 id="titulo-meu-contexto">Meu contexto</h2>
      <p className="nota-da-secao">
        O que os agentes do seu dev.kit devem saber de você em toda task: como prefere as respostas, o que revisar, o que
        evitar. Ele chega por cima do contexto da equipe e do da empresa — num tema em conflito, vale o seu.
      </p>
      {!temEmpresa ? (
        <p className="vazio">O seu usuário não é de uma empresa: o contexto do dev.kit é compartilhado pela empresa.</p>
      ) : (
        <>
          {erro && <p className="erro" role="alert">{erro}</p>}
          {aviso && <p className="aviso" role="status">{aviso}</p>}
          {!convencoes ? (
            !erro && <p className="carregando" role="status">Carregando…</p>
          ) : (
            <EditorDeContexto rotulo="Seu contexto" itens={meus} editavel aoGravar={gravar}
              vazio="Nenhum tema ainda: o dev.kit usa só o contexto da equipe e o da empresa."
              aoPromover={(c) => naSugestao(() => api.promoverContexto(token, c.id!, c.versao), `Sugestão ${temaDa(c.chave)} promovida a regra do seu contexto.`)}
              aoDescartar={(c) => naSugestao(() => api.descartarContexto(token, c.id!), `Sugestão ${temaDa(c.chave)} descartada.`)} />
          )}
          {daEmpresa.length > 0 && (
            <p className="nota-da-secao">A empresa já define: {daEmpresa.map((c) => temaDa(c.chave)).join(', ')}.</p>
          )}
        </>
      )}
    </section>
  );
}
