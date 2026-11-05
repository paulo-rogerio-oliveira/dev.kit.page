import { useEffect, useState, type FormEvent } from 'react';
import { api } from '../api/cliente';
import type { AcessosDoProjeto, AcessosDoDesign, PapelDeAcessoAoDesign, PapelNoDesign, UsuarioDoDesign } from '../api/tipos';
import { iniciais } from './Avatar';
import { Selo } from './Selo';

const ROTULO_DO_PAPEL: Record<PapelNoDesign, string> = { dono: 'Dono', editor: 'Editor', leitor: 'Leitor' };

/** O que cada papel faz, na mesma frase do devcli e da recusa da API. */
const O_QUE_FAZ: Record<PapelNoDesign, string> = {
  dono: 'criou o projeto: dá e tira acesso, e envia versões',
  editor: 'vê, baixa e envia versões',
  leitor: 'vê e baixa as versões',
};

const nomeDe = (u: UsuarioDoDesign) => (u.nome ? `${u.nome} (${u.login})` : u.login);

/**
 * O painel ACESSO compartilhado pelos projetos em ondas e de design: o dono (quem criou o projeto), quem mais tem acesso e com que papel — leitor (vê e
 * baixa) ou editor (também envia) —, e, para o dono, o Dar acesso (um usuário da empresa + o papel), a troca do papel e o
 * Remover, com confirmação. Quem não é o dono vê a lista e o próprio papel, sem os controles.
 */
export function AcessoAoProjeto({ token, projeto, tratar, recurso = 'design' }: {
  token: string;
  projeto: { id: number; chave: string };
  recurso?: 'design' | 'projeto';
  tratar: (falha: unknown) => void;
}) {
  const [acessos, setAcessos] = useState<AcessosDoDesign | AcessosDoProjeto | null>(null);
  const [usuario, setUsuario] = useState('');
  const [papel, setPapel] = useState<PapelDeAcessoAoDesign>('leitor');
  const [aviso, setAviso] = useState('');
  const [ocupado, setOcupado] = useState(false);

  useEffect(() => {
    let vivo = true;
    setAcessos(null);
    setAviso('');
    setUsuario('');
    api.acessosDoRecurso(token, recurso, projeto.id).then((lidos) => { if (vivo) setAcessos(lidos); }).catch((falha: unknown) => { if (vivo) tratar(falha); });
    return () => { vivo = false; };
  }, [token, recurso, projeto.id, tratar]);

  const ehDesign = recurso === 'design';
  const oQueFaz: Record<PapelNoDesign, string> = ehDesign ? O_QUE_FAZ : {
    dono: 'criou o projeto: dá e tira acesso, altera, aprova e dispara',
    editor: 'vê o projeto, altera, aprova e dispara',
    leitor: 'vê o projeto, os itens e o andamento',
  };
  const ehDono = acessos?.papel === 'dono';

  async function gravar(acao: () => Promise<AcessosDoDesign | AcessosDoProjeto>, feito: string) {
    setOcupado(true);
    setAviso('');
    try {
      setAcessos(await acao());
      setAviso(feito);
    } catch (falha) {
      tratar(falha);
    } finally {
      setOcupado(false);
    }
  }

  async function darAcesso(evento: FormEvent) {
    evento.preventDefault();
    const alvo = acessos?.candidatos.find((c) => c.login === usuario);
    if (!alvo) return;
    await gravar(() => api.concederAcessoAoRecurso(token, recurso, projeto.id, alvo.login, papel), `${nomeDe(alvo)} agora é ${papel} do projeto.`);
    setUsuario('');
  }

  async function mudarPapel(alvo: UsuarioDoDesign, novo: PapelDeAcessoAoDesign) {
    await gravar(() => api.alterarPapelNoRecurso(token, recurso, projeto.id, alvo.id, novo), `${nomeDe(alvo)} agora é ${novo} do projeto.`);
  }

  async function remover(alvo: UsuarioDoDesign) {
    if (!window.confirm(`Remover o acesso de ${nomeDe(alvo)} ao ${ehDesign ? 'design de' : 'projeto'} ${projeto.chave}? ${ehDesign ? 'Ele deixa de ver e de enviar as versões.' : 'Ele deixa de ver o projeto, os itens e o andamento.'}`)) return;
    await gravar(() => api.removerAcessoAoRecurso(token, recurso, projeto.id, alvo.id), `${nomeDe(alvo)} não tem mais acesso ao projeto.`);
  }

  return (
    <section className="cartao design-acesso" aria-label="Acesso">
      <div className="design-acesso-cabecalho">
        <h3 className="design-rotulo">Acesso</h3>
        {acessos && (
          <span className="texto-mudo">
            Seu papel: <Selo variante={acessos.papel === 'dono' ? 'marca' : acessos.papel === 'editor' ? 'papel' : 'neutro'}>{acessos.papel ? ROTULO_DO_PAPEL[acessos.papel] : 'sem acesso'}</Selo>
            {acessos.papel && <> — {oQueFaz[acessos.papel]}</>}
          </span>
        )}
      </div>
      {aviso && <p className="aviso" role="status">{aviso}</p>}
      {!acessos ? (
        <p className="carregando" role="status">Carregando o acesso…</p>
      ) : (
        <>
          <ul className="design-acesso-lista">
            {acessos.dono && (
              <li>
                <span className="login-com-iniciais fonte-mono" data-iniciais={iniciais(acessos.dono.login)}>{nomeDe(acessos.dono)}</span>
                <Selo variante="marca">Dono</Selo>
              </li>
            )}
            {acessos.acessos.map((a) => (
              <li key={a.usuario.id}>
                <span className="login-com-iniciais fonte-mono" data-iniciais={iniciais(a.usuario.login)}>{nomeDe(a.usuario)}</span>
                {ehDono ? (
                  <>
                    <select aria-label={`Papel de ${a.usuario.login}`} value={a.papel} disabled={ocupado}
                      onChange={(e) => void mudarPapel(a.usuario, e.target.value as PapelDeAcessoAoDesign)}>
                      <option value="leitor">Leitor</option>
                      <option value="editor">Editor</option>
                    </select>
                    <button className="botao botao-fantasma botao-pequeno botao-perigo" type="button" disabled={ocupado}
                      aria-label={`Remover ${a.usuario.login}`} onClick={() => void remover(a.usuario)}>Remover</button>
                  </>
                ) : (
                  <Selo variante={a.papel === 'editor' ? 'papel' : 'neutro'}>{ROTULO_DO_PAPEL[a.papel]}</Selo>
                )}
              </li>
            ))}
            {acessos.acessos.length === 0 && <li className="texto-mudo">Ninguém além do dono tem acesso a este projeto.</li>}
          </ul>

          {ehDono ? (
            <form className="acoes design-acesso-dar" onSubmit={(e) => void darAcesso(e)} aria-label="Dar acesso">
              <select aria-label="Usuário da empresa" value={usuario} onChange={(e) => setUsuario(e.target.value)} disabled={ocupado || acessos.candidatos.length === 0}>
                <option value="">{acessos.candidatos.length === 0 ? 'Todos da empresa já têm acesso' : 'Escolha o usuário da empresa'}</option>
                {acessos.candidatos.map((c) => <option key={c.id} value={c.login}>{nomeDe(c)}</option>)}
              </select>
              <select aria-label="Papel do acesso" value={papel} onChange={(e) => setPapel(e.target.value as PapelDeAcessoAoDesign)} disabled={ocupado}>
                <option value="leitor">Leitor — {ehDesign ? 'vê e baixa' : 'vê o projeto'}</option>
                <option value="editor">Editor — {ehDesign ? 'também envia' : 'altera, aprova e dispara'}</option>
              </select>
              <button className="botao" type="submit" disabled={ocupado || !usuario}>Dar acesso</button>
            </form>
          ) : (
            <p className="texto-mudo design-acesso-nota">
              Só o dono{acessos.dono ? ` (${nomeDe(acessos.dono)})` : ''} dá e tira acesso.
              {acessos.papel === 'leitor' && (ehDesign ? ' Para enviar versões, peça a ele o papel editor.' : ' Para alterar, aprovar ou disparar, peça a ele o papel editor.')}
            </p>
          )}
        </>
      )}
    </section>
  );
}
