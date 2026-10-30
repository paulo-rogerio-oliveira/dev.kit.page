import { useCallback, useEffect, useRef, useState } from 'react';
import { api, ErroDaApi } from '../api/cliente';
import type { ItemDaEmpresa } from '../api/tipos';
import { Selo } from '../componentes/Selo';
import { MenuDaEmpresa, Topo } from '../componentes/Topo';
import { formatar } from '../formatar';
import { useSessao } from '../sessao';
import { NOME_DO_TIPO } from './catalogo/tipos';
import { AssinantesDaSkill } from './AssinantesDaSkill';

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
  const [selecionado, setSelecionado] = useState<ItemDaEmpresa | null>(null);
  const origemDoFoco = useRef<HTMLButtonElement | null>(null);

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
      <Topo secao="catálogo da empresa"><MenuDaEmpresa /></Topo>
      <main className="painel">
        <section aria-labelledby="titulo-catalogo-da-empresa" className="painel-da-aba">
          <header className="cabecalho-da-pagina">
            <div className="cabecalho-textos">
              {sessao!.empresa && <p className="rotulo-da-pagina">{sessao!.empresa}</p>}
              <h1 id="titulo-catalogo-da-empresa">Catálogo da empresa</h1>
              <p className="subtitulo">O time publica com o devcli (skills --publicar) e assina o que usa; a sincronia do dev.kit traz os itens assinados.</p>
            </div>
          </header>
          {erro && <p className="erro" role="alert">{erro}</p>}
          {aviso && <p className="aviso" role="status">{aviso}</p>}
          {!itens ? (
            <p className="carregando" role="status">Carregando…</p>
          ) : itens.length === 0 ? (
            <div className="cartao estado-vazio">
              <span className="icone" aria-hidden="true">
                <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round"><rect x="4" y="4" width="16" height="16" rx="4" /><path d="M9 10h6M9 14h6" /></svg>
              </span>
              <h2>Nenhum item publicado ainda.</h2>
              <p>Publique uma skill, um fluxo ou uma Rule do time pelo devcli. Depois de publicada, ela aparece aqui para os devs da empresa assinarem.</p>
              <code className="comando"><span aria-hidden="true">$ </span>devcli skills --nome &lt;skill&gt; --publicar --aplicar</code>
            </div>
          ) : (
            <div className="cartao cartao-de-tabela">
              <div className="tabela-rolavel-simples">
                <table className="tabela tabela-do-cartao">
                  <thead>
                    <tr><th>Item</th><th>Tipo</th><th className="numero">Versão</th><th>Autor</th><th className="numero">Assinantes</th><th>Atualizado em</th><th>Situação</th><th>Ações</th></tr>
                  </thead>
                  <tbody>
                    {itens.map((i) => (
                      <tr key={i.id} className={selecionado?.id === i.id ? 'skill-selecionada' : undefined}>
                        <td className="nome-do-item">{i.nome}</td>
                        <td><Selo variante="sucesso">{NOME_DO_TIPO[i.tipo] ?? i.tipo}</Selo></td>
                        <td className="numero">{i.versao}</td>
                        <td>{i.autor || '—'}</td>
                        <td className="numero">{sessao!.papel === 'gestor' && i.tipo === 'skill' ? (
                          <button className="assinantes-link" type="button" aria-label={`Ver assinantes de ${i.nome}`} aria-expanded={selecionado?.id === i.id} aria-controls={selecionado?.id === i.id ? 'titulo-assinantes' : undefined} onClick={(evento) => { origemDoFoco.current = evento.currentTarget; setSelecionado(i); }}>
                            {i.assinantes}<span>Ver assinantes</span>
                          </button>
                        ) : i.assinantes}</td>
                        <td>{formatar.dataHora(i.publicadoEm)}</td>
                        <td><Selo variante={i.retirado ? 'neutro' : 'sucesso'} ponto={!i.retirado}>{i.retirado ? 'Retirado' : 'Publicado'}</Selo></td>
                        <td>
                          {!i.retirado && (
                            <button className="botao botao-fantasma" type="button" aria-label={`Retirar ${i.nome}`} onClick={() => void retirar(i)}>Retirar</button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
          {sessao!.papel === 'gestor' && selecionado && <AssinantesDaSkill key={selecionado.id} item={selecionado} fechar={() => { setSelecionado(null); origemDoFoco.current?.focus(); }} />}
        </section>
      </main>
    </div>
  );
}
