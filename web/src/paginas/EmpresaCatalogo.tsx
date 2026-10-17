import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ErroDaApi } from '../api/cliente';
import type { ItemDaEmpresa } from '../api/tipos';
import { Topo } from '../componentes/Topo';
import { formatar } from '../formatar';
import { useSessao } from '../sessao';
import { NOME_DO_TIPO } from './catalogo/tipos';

/**
 * O catálogo da empresa visto pelo GESTOR (US #425, /empresa/catalogo): os itens que o time publicou (pelo
 * `devcli skills --publicar`), a versão, o autor e quantos assinam — e o Retirar, que tira o item da sincronia de
 * todo mundo, de qualquer autor.
 */
export function EmpresaCatalogo() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const [itens, setItens] = useState<ItemDaEmpresa[] | null>(null);
  const [aviso, setAviso] = useState('');
  const [erro, setErro] = useState('');

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) {
      sair('expirou');
      return;
    }
    setErro(falha instanceof Error ? falha.message : 'Falha ao consultar a API.');
  }, [sair]);

  const carregar = useCallback(() => api.catalogoDaEmpresa(token).then(setItens).catch(tratar), [token, tratar]);

  useEffect(() => {
    void carregar();
  }, [carregar]);

  async function retirar(item: ItemDaEmpresa) {
    setErro('');
    setAviso('');
    if (!window.confirm(`Retirar ${item.nome}? Ele sai da sincronia de quem o assina.`)) return;
    try {
      await api.retirarItem(token, item.id);
      setAviso(`${item.nome} retirado do catálogo da empresa.`);
      await carregar();
    } catch (falha) {
      tratar(falha);
    }
  }

  return (
    <div className="pagina">
      <Topo secao="catálogo da empresa">
        <Link className="botao botao-fantasma" to="/dashboard">Dashboard</Link>
        <Link className="botao botao-fantasma" to="/empresa/usuarios">Usuários da empresa</Link>
        <Link className="botao botao-fantasma" to="/empresa/conhecimento">Repositórios e convenções</Link>
      </Topo>
      <main className="painel">
        <section aria-labelledby="titulo-catalogo-da-empresa">
          <h1 id="titulo-catalogo-da-empresa">Catálogo da empresa</h1>
          <p className="nota-da-secao">O time publica com o devcli (skills --publicar) e assina o que usa; a sincronia do dev.kit traz os itens assinados.</p>
          {erro && <p className="erro" role="alert">{erro}</p>}
          {aviso && <p className="aviso" role="status">{aviso}</p>}
          {!itens ? (
            <p className="carregando" role="status">Carregando…</p>
          ) : itens.length === 0 ? (
            <p className="vazio">Nenhum item publicado ainda.</p>
          ) : (
            <table className="tabela">
              <thead>
                <tr><th>Item</th><th>Tipo</th><th className="numero">Versão</th><th>Autor</th><th className="numero">Assinantes</th><th>Atualizado em</th><th>Situação</th><th>Ações</th></tr>
              </thead>
              <tbody>
                {itens.map((i) => (
                  <tr key={i.id}>
                    <td>{i.nome}</td>
                    <td>{NOME_DO_TIPO[i.tipo] ?? i.tipo}</td>
                    <td className="numero">{i.versao}</td>
                    <td>{i.autor || '—'}</td>
                    <td className="numero">{i.assinantes}</td>
                    <td>{formatar.dataHora(i.publicadoEm)}</td>
                    <td><span className={`estado ${i.retirado ? 'estado-ignorado' : 'estado-resolvido'}`}>{i.retirado ? 'Retirado' : 'Publicado'}</span></td>
                    <td>
                      {!i.retirado && (
                        <button className="botao botao-fantasma" type="button" aria-label={`Retirar ${i.nome}`} onClick={() => void retirar(i)}>Retirar</button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      </main>
    </div>
  );
}
