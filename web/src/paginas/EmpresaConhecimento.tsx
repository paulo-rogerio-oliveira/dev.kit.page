import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { Link, Navigate, useSearchParams } from 'react-router-dom';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { RepositorioDaEmpresa } from '../api/tipos';
import { Pagina } from '../componentes/Pagina';
import { PainelLateral } from '../componentes/PainelLateral';
import { Topo } from '../componentes/Topo';
import { formatar } from '../formatar';
import { papelDa, useSessao } from '../sessao';

/**
 * O CATÁLOGO DE REPOSITÓRIOS da empresa (US #491, /empresa/conhecimento) — para AUDITAR (quem gravou e quando) e
 * EDITAR o caminho e a descrição que o agente grava ao terminar cada task. É do gestor e do dev. As convenções que
 * moravam aqui viraram o CONTEXTO do dev.kit (US #556, /contexto): o link antigo (?aba=convencoes) leva para lá.
 */
export function EmpresaConhecimento() {
  const [busca] = useSearchParams();
  return busca.get('aba') === 'convencoes' ? <Navigate to="/contexto" replace /> : <Repositorios />;
}

function Repositorios() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const ehGestor = papelDa(sessao!) === 'gestor';

  const [repositorios, setRepositorios] = useState<RepositorioDaEmpresa[] | null>(null);
  const [aviso, setAviso] = useState('');
  const [erro, setErro] = useState('');

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) {
      sair('expirou');
      return;
    }
    setErro(mensagemDaFalha(falha));
  }, [sair]);

  const carregar = useCallback(async () => {
    try {
      setRepositorios(await api.repositoriosDaEmpresa(token));
    } catch (falha) {
      tratar(falha);
    }
  }, [token, tratar]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  /** Roda uma escrita: limpa as mensagens, grava, avisa e recarrega. */
  async function escrever(acao: () => Promise<unknown>, mensagem: string) {
    setErro('');
    setAviso('');
    try {
      await acao();
      setAviso(mensagem);
      await carregar();
      return true;
    } catch (falha) {
      tratar(falha);
      return false;
    }
  }

  return (
    <div className="pagina">
      <Topo secao="conhecimento da empresa">
        {ehGestor ? <Link className="botao botao-fantasma" to="/dashboard">Dashboard</Link> : <Link className="botao botao-fantasma" to="/conta">Conta</Link>}
        {ehGestor && <Link className="botao botao-fantasma" to="/empresa/catalogo">Catálogo da empresa</Link>}
        <Link className="botao botao-fantasma" to="/contexto">Contexto</Link>
      </Topo>
      <Pagina titulo="Repositórios da empresa" subtitulo="O que o dev.kit consulta para resolver o repositório de um pedido: auditado aqui, com quem gravou e quando, e editável.">
        {erro && <p className="erro" role="alert">{erro}</p>}
        {aviso && <p className="aviso" role="status">{aviso}</p>}
        <AbaRepositorios repositorios={repositorios} ehGestor={ehGestor} token={token} escrever={escrever} />
      </Pagina>
    </div>
  );
}

type Escrever = (acao: () => Promise<unknown>, mensagem: string) => Promise<boolean>;

/** O catálogo de repositórios: a lista com o autor e a data, e o formulário que grava (ou corrige) uma descrição. */
function AbaRepositorios({ repositorios, ehGestor, token, escrever }: {
  repositorios: RepositorioDaEmpresa[] | null; ehGestor: boolean; token: string; escrever: Escrever;
}) {
  const [origem, setOrigem] = useState('');
  const [descricao, setDescricao] = useState('');

  async function gravar(evento: FormEvent) {
    evento.preventDefault();
    if (await escrever(() => api.gravarRepositorio(token, origem.trim(), descricao.trim()), `${origem.trim()} gravado no catálogo da empresa.`)) {
      setOrigem('');
      setDescricao('');
    }
  }

  async function remover(r: RepositorioDaEmpresa) {
    if (!window.confirm(`Remover ${r.origem} do catálogo da empresa?`)) return;
    await escrever(() => api.removerRepositorio(token, r.origem), `${r.origem} removido do catálogo da empresa.`);
  }

  return (
    <div className="com-lateral">
      <section className="cartao cartao-de-tabela" aria-label="Repositórios da empresa">
        {!repositorios ? (
          <p className="carregando" role="status">Carregando…</p>
        ) : repositorios.length === 0 ? (
          <p className="vazio">Nenhum repositório descrito ainda — o agente do dev.kit grava cada um ao terminar a task.</p>
        ) : (
          <div className="tabela-rolavel-simples">
            <table className="tabela tabela-do-cartao">
              <thead>
                <tr><th>Repositório</th><th>Descrição</th><th>Autor</th><th>Atualizado em</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {repositorios.map((r) => (
                  <tr key={r.origem}>
                    <td><code className="codigo">{r.origem}</code></td>
                    <td>{r.descricao}</td>
                    <td>{r.autor || '—'}</td>
                    <td>{formatar.dataHora(r.atualizadoEm)}</td>
                    <td>
                      <span className="acoes">
                        <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Editar ${r.origem}`}
                          onClick={() => { setOrigem(r.origem); setDescricao(r.descricao); }}>Editar</button>
                        {ehGestor && (
                          <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Remover ${r.origem}`} onClick={() => void remover(r)}>Remover</button>
                        )}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <PainelLateral id="titulo-gravar-repositorio" titulo="Descrever um repositório">
        <form className="formulario-lateral" onSubmit={(e) => void gravar(e)} aria-label="Descrever um repositório">
          <label>
            Caminho
            <input value={origem} onChange={(e) => setOrigem(e.target.value)} maxLength={400} placeholder="https://github.com/empresa/repositorio" required />
          </label>
          <label>
            Descrição
            <textarea value={descricao} onChange={(e) => setDescricao(e.target.value)} maxLength={1000} rows={4} placeholder="Para que serve, a tecnologia, o que mora nele" required />
          </label>
          <p className="nota-do-formulario">O mesmo caminho troca a descrição. O dev.kit consulta este catálogo para resolver o repositório de um pedido.</p>
          <button className="botao botao-primario botao-largo" type="submit" disabled={!origem.trim() || !descricao.trim()}>Gravar</button>
        </form>
      </PainelLateral>
    </div>
  );
}
