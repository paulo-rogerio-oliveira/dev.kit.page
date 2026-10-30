import { useEffect, useRef, useState } from 'react';
import { api, ErroDaApi } from '../api/cliente';
import type { AssinanteDoItem, ItemDaEmpresa, Pagina } from '../api/tipos';
import { useSessao } from '../sessao';
import './AssinantesDaSkill.css';

const dataUtc = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short', timeZone: 'UTC' });

export function AssinantesDaSkill({ item, fechar }: { item: ItemDaEmpresa; fechar: () => void }) {
  const { sessao, sair } = useSessao();
  const [pagina, setPagina] = useState(1);
  const [tentativa, setTentativa] = useState(0);
  const [dados, setDados] = useState<Pagina<AssinanteDoItem> | null>(null);
  const [erro, setErro] = useState('');
  const titulo = useRef<HTMLHeadingElement>(null);
  const token = sessao!.token;

  useEffect(() => { titulo.current?.focus(); }, []);
  useEffect(() => {
    let atual = true;
    setDados(null);
    setErro('');
    void api.assinantesDoItem(token, item.id, pagina).then((resposta) => {
      if (atual) setDados(resposta);
    }).catch((falha: unknown) => {
      if (!atual) return;
      if (falha instanceof ErroDaApi && falha.status === 401) { sair('expirou'); return; }
      setErro(falha instanceof Error ? falha.message : 'Falha ao consultar os assinantes.');
    });
    return () => { atual = false; };
  }, [token, item.id, pagina, tentativa, sair]);

  const totalDePaginas = Math.max(1, Math.ceil((dados?.total ?? 0) / (dados?.tamanho ?? 20)));
  function mudarPagina(numero: number) { setDados(null); setPagina(numero); }

  return (
    <section className="cartao assinantes-da-skill" aria-labelledby="titulo-assinantes">
      <header className="assinantes-cabecalho">
        <div>
          <p className="rotulo-da-pagina">Assinantes da skill</p>
          <h2 id="titulo-assinantes" ref={titulo} tabIndex={-1}>{item.nome}</h2>
          {dados && <p>{dados.total} {dados.total === 1 ? 'usuário' : 'usuários'} com assinatura registrada nesta empresa</p>}
        </div>
        <button className="botao botao-fantasma" type="button" onClick={fechar}>Fechar</button>
      </header>
      <p className="assinantes-observacao">Esta lista mostra quem assinou a skill. A assinatura não comprova a instalação na máquina.</p>
      {erro ? <div role="alert"><p className="erro">{erro}</p><button className="botao botao-fantasma" type="button" onClick={() => { setErro(''); setTentativa(tentativa + 1); }}>Tentar novamente</button></div>
        : !dados ? <p role="status">Carregando assinantes…</p>
        : <>
          {dados.itens.length === 0 ? <p role="status">{dados.total === 0 ? 'Nenhum assinante registrado para esta skill.' : 'Nenhum assinante nesta página.'}</p>
            : <div className="tabela-rolavel-simples"><table className="tabela">
              <thead><tr><th scope="col">Usuário</th><th scope="col">Assinou em</th></tr></thead>
              <tbody>{dados.itens.map((assinante) => <tr key={assinante.usuarioId}>
                <td>{assinante.nome}</td><td><time dateTime={assinante.assinadaEm}>{dataUtc.format(new Date(assinante.assinadaEm))} UTC</time></td>
              </tr>)}</tbody>
            </table></div>}
          <div className="assinantes-rodape">
            <span>{dados.itens.length ? `${(pagina - 1) * dados.tamanho + 1}–${(pagina - 1) * dados.tamanho + dados.itens.length}` : '0'} de {dados.total} assinantes</span>
            <nav className="paginacao" aria-label="Páginas de assinantes">
              <button className="botao botao-fantasma" type="button" disabled={pagina <= 1} onClick={() => mudarPagina(pagina - 1)}>Anterior</button>
              <span>Página {pagina} de {totalDePaginas}</span>
              <button className="botao botao-fantasma" type="button" disabled={pagina >= totalDePaginas} onClick={() => mudarPagina(pagina + 1)}>Próxima</button>
            </nav>
          </div>
        </>}
    </section>
  );
}
