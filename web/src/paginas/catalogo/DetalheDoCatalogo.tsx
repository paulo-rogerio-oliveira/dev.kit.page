import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api } from '../../api/cliente';
import type { CatalogoNomeado, EmpresaResumo, ItemEditavel, TipoDeItem } from '../../api/tipos';
import { Chips } from '../../componentes/Chips';
import { Selo } from '../../componentes/Selo';
import { formatar } from '../../formatar';
import { EditorDeItem } from './EditorDeItem';
import { Publicacao } from './Publicacao';
import { NOME_DO_TIPO, SITUACAO_DO_ITEM } from './tipos';

type FiltroDeTipo = 'todos' | TipoDeItem;

/** O que ocupa o lugar da tabela de itens: nada, o editor (novo ou de um item) ou a publicação. */
type Modo = { tipo: 'lista' } | { tipo: 'editor'; tipoDoItem: TipoDeItem; item: ItemEditavel | null } | { tipo: 'publicar' };

/**
 * O detalhe de um catálogo (US #461): o nome e a descrição (editáveis; o slug não muda — é o que as máquinas
 * guardam), as empresas relacionadas (o básico vale para todas), os itens com a situação de cada um em relação à
 * última release, o editor e o Publicar. Edição grava RASCUNHO: nada chega às máquinas antes de publicar.
 */
export function DetalheDoCatalogo({ token, catalogo, aoVoltar, aoMudar, aoPublicar, tratar }: {
  token: string;
  catalogo: CatalogoNomeado;
  aoVoltar: () => void;
  aoMudar: () => Promise<unknown>;
  aoPublicar: (slug: string) => void;
  tratar: (falha: unknown) => string;
}) {
  const slug = catalogo.slug;
  const [itens, setItens] = useState<ItemEditavel[] | null>(null);
  const [empresas, setEmpresas] = useState<EmpresaResumo[]>([]);
  const [empresaEscolhida, setEmpresaEscolhida] = useState('');
  const [filtro, setFiltro] = useState<FiltroDeTipo>('todos');
  const [modo, setModo] = useState<Modo>({ tipo: 'lista' });
  const [editandoDados, setEditandoDados] = useState(false);
  const [nome, setNome] = useState(catalogo.nome);
  const [descricao, setDescricao] = useState(catalogo.descricao);
  const [aviso, setAviso] = useState('');
  const [erro, setErro] = useState('');

  const falhar = useCallback((falha: unknown) => setErro(tratar(falha)), [tratar]);

  const carregarItens = useCallback(() => api.itensDoCatalogo(token, slug).then(setItens).catch(falhar), [token, slug, falhar]);

  useEffect(() => {
    void carregarItens();
    // O básico não se relaciona: a lista de empresas só serve aos outros.
    if (!catalogo.basico) api.empresas(token).then(setEmpresas).catch(falhar);
  }, [carregarItens, catalogo.basico, token, falhar]);

  function limpar() {
    setAviso('');
    setErro('');
  }

  async function depoisDeMudar(mensagem: string) {
    setAviso(mensagem);
    await Promise.all([carregarItens(), aoMudar()]);
  }

  async function salvarDados(evento: FormEvent) {
    evento.preventDefault();
    limpar();
    try {
      await api.editarCatalogo(token, slug, nome.trim(), descricao.trim());
      setEditandoDados(false);
      await depoisDeMudar('Catálogo atualizado.');
    } catch (falha) {
      falhar(falha);
    }
  }

  async function excluir() {
    limpar();
    if (!window.confirm(`Excluir o catálogo ${catalogo.nome}? As empresas relacionadas deixam de recebê-lo.`)) return;
    try {
      await api.excluirCatalogo(token, slug);
      await aoMudar();
      aoVoltar();
    } catch (falha) {
      falhar(falha);
    }
  }

  async function relacionar(evento: FormEvent) {
    evento.preventDefault();
    limpar();
    if (!empresaEscolhida) return;
    try {
      await api.relacionarEmpresa(token, slug, Number(empresaEscolhida));
      setEmpresaEscolhida('');
      await depoisDeMudar('Empresa relacionada: os devs dela recebem o catálogo na próxima sincronia.');
    } catch (falha) {
      falhar(falha);
    }
  }

  async function desrelacionar(id: number, nomeDaEmpresa: string) {
    limpar();
    if (!window.confirm(`Desrelacionar ${nomeDaEmpresa}? Os itens deste catálogo saem das máquinas dela (os editados localmente ficam).`)) return;
    try {
      await api.desrelacionarEmpresa(token, slug, id);
      await depoisDeMudar(`${nomeDaEmpresa} desrelacionada.`);
    } catch (falha) {
      falhar(falha);
    }
  }

  async function retirar(item: ItemEditavel) {
    limpar();
    const pergunta = item.versaoPublicada > 0
      ? `Retirar ${item.nome}? Ele sai das máquinas na próxima publicação.`
      : `Retirar ${item.nome}? Ele nunca foi publicado e some do catálogo.`;
    if (!window.confirm(pergunta)) return;
    try {
      await api.retirarDoCatalogo(token, slug, item.id);
      await depoisDeMudar(`${item.nome} retirado.`);
    } catch (falha) {
      falhar(falha);
    }
  }

  async function importar() {
    limpar();
    try {
      const importacao = await api.importarBasico(token);
      await depoisDeMudar(importacao.mensagem);
    } catch (falha) {
      falhar(falha);
    }
  }

  async function salvo(item: ItemEditavel, novo: boolean) {
    setModo({ tipo: 'lista' });
    await depoisDeMudar(`${NOME_DO_TIPO[item.tipo]} ${item.nome} ${novo ? 'criado' : 'salvo'} como rascunho: publique para chegar às máquinas.`);
  }

  const ativos = itens ?? [];
  const conta = (t: TipoDeItem) => ativos.filter((i) => i.tipo === t).length;
  const visiveis = ativos.filter((i) => filtro === 'todos' || i.tipo === filtro);
  const livres = empresas.filter((e) => !catalogo.empresas.some((v) => v.id === e.id));

  return (
    <div className="detalhe-do-catalogo">
      <div className="detalhe-topo">
        <button className="link" type="button" onClick={aoVoltar}>← Catálogos</button>
      </div>

      <section className="cartao" aria-labelledby="titulo-do-catalogo">
        {editandoDados ? (
          <form className="formulario-lateral" onSubmit={salvarDados} aria-label="Dados do catálogo">
            <label>
              Nome
              <input value={nome} onChange={(e) => setNome(e.target.value)} maxLength={100} required />
            </label>
            <label>
              Descrição
              <textarea value={descricao} onChange={(e) => setDescricao(e.target.value)} maxLength={500} rows={3} />
            </label>
            <div className="acoes">
              <button className="botao botao-primario" type="submit">Salvar</button>
              <button className="botao botao-fantasma" type="button" onClick={() => { setEditandoDados(false); setNome(catalogo.nome); setDescricao(catalogo.descricao); }}>Cancelar</button>
            </div>
          </form>
        ) : (
          <div className="detalhe-cabecalho">
            <div>
              <h2 id="titulo-do-catalogo">{catalogo.nome} <code className="codigo">{catalogo.slug}</code></h2>
              {catalogo.descricao && <p className="nota-da-secao">{catalogo.descricao}</p>}
              <p className="texto-mudo">
                {catalogo.versao > 0 ? `Versão ${catalogo.versao} · publicada em ${formatar.data(catalogo.publicadaEm)}` : 'Ainda sem release publicada'}
                {' · '}{catalogo.itens} {catalogo.itens === 1 ? 'item' : 'itens'}
              </p>
            </div>
            <div className="acoes">
              <button className="botao botao-fantasma" type="button" onClick={() => { limpar(); setEditandoDados(true); }}>Editar dados</button>
              {!catalogo.basico && <button className="botao botao-fantasma" type="button" onClick={() => void excluir()}>Excluir</button>}
            </div>
          </div>
        )}
      </section>

      {erro && <p className="erro" role="alert">{erro}</p>}
      {aviso && <p className="aviso" role="status">{aviso}</p>}

      <section className="cartao" aria-labelledby="titulo-empresas-do-catalogo">
        <h2 id="titulo-empresas-do-catalogo" className="titulo-do-cartao">Empresas</h2>
        {catalogo.basico ? (
          <p className="nota-da-secao">Vale para todas as empresas: o básico chega a toda máquina.</p>
        ) : (
          <>
            {catalogo.empresas.length === 0 ? (
              <p className="vazio">Nenhuma empresa relacionada: o catálogo ainda não chega a máquina nenhuma.</p>
            ) : (
              <ul className="lista-de-empresas">
                {catalogo.empresas.map((e) => (
                  <li key={e.id}>
                    <span>{e.nome} <small className="texto-mudo">desde {formatar.data(e.relacionadaEm)}</small></span>
                    <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Desrelacionar ${e.nome}`} onClick={() => void desrelacionar(e.id, e.nome)}>
                      Desrelacionar
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <form className="acoes" onSubmit={relacionar} aria-label="Relacionar empresa">
              <select aria-label="Empresa a relacionar" value={empresaEscolhida} onChange={(e) => setEmpresaEscolhida(e.target.value)}>
                <option value="">Escolha a empresa</option>
                {livres.map((e) => <option key={e.id} value={e.id}>{e.nome}</option>)}
              </select>
              <button className="botao botao-fantasma" type="submit" disabled={!empresaEscolhida}>Relacionar</button>
            </form>
          </>
        )}
      </section>

      {modo.tipo === 'editor' ? (
        <EditorDeItem
          token={token}
          slug={slug}
          tipo={modo.tipoDoItem}
          item={modo.item}
          aoSalvar={(item) => void salvo(item, modo.item === null)}
          aoCancelar={() => setModo({ tipo: 'lista' })}
          tratar={tratar}
        />
      ) : modo.tipo === 'publicar' ? (
        <Publicacao token={token} catalogo={catalogo} aoPublicar={aoPublicar} aoCancelar={() => setModo({ tipo: 'lista' })} tratar={tratar} />
      ) : (
        <section className="cartao cartao-de-tabela" aria-labelledby="titulo-itens-do-catalogo">
          <div className="itens-topo">
            <h2 id="titulo-itens-do-catalogo" className="titulo-do-cartao">Itens</h2>
            <div className="acoes">
              <button className="botao botao-fantasma botao-pequeno" type="button" onClick={() => { limpar(); setModo({ tipo: 'editor', tipoDoItem: 'skill', item: null }); }}>Nova skill</button>
              <button className="botao botao-fantasma botao-pequeno" type="button" onClick={() => { limpar(); setModo({ tipo: 'editor', tipoDoItem: 'plan', item: null }); }}>Novo tech plan</button>
              <button className="botao botao-fantasma botao-pequeno" type="button" onClick={() => { limpar(); setModo({ tipo: 'editor', tipoDoItem: 'fluxo', item: null }); }}>Novo fluxo</button>
            </div>
          </div>

          <div className="pendencias">
            <span>
              {catalogo.pendentes > 0
                ? <><Selo variante="aviso">{catalogo.pendentes} pendente(s)</Selo> rascunho(s) que ainda não chegaram às máquinas.</>
                : 'Nenhuma pendência: as máquinas têm o que está aqui.'}
            </span>
            <button className="botao botao-primario" type="button" disabled={catalogo.pendentes === 0} onClick={() => { limpar(); setModo({ tipo: 'publicar' }); }}>
              Publicar
            </button>
          </div>

          <div className="itens-filtros">
            <Chips<FiltroDeTipo>
              rotulo="Tipo do item"
              valor={filtro}
              aoMudar={setFiltro}
              opcoes={[
                { valor: 'todos', rotulo: 'Todos', contagem: ativos.length },
                { valor: 'skill', rotulo: 'Skills', contagem: conta('skill') },
                { valor: 'plan', rotulo: 'Tech plans', contagem: conta('plan') },
                { valor: 'fluxo', rotulo: 'Fluxos', contagem: conta('fluxo') },
              ]}
            />
          </div>

          {!itens ? (
            <p className="carregando" role="status">Carregando…</p>
          ) : itens.length === 0 ? (
            <div className="vazio-com-acao">
              <p className="vazio">Nenhum item neste catálogo.</p>
              {catalogo.basico && (
                <button className="botao botao-fantasma" type="button" onClick={() => void importar()}>Importar do GitHub</button>
              )}
            </div>
          ) : (
            <div className="tabela-rolavel-simples">
              <table className="tabela tabela-do-cartao">
                <thead>
                  <tr><th>Tipo</th><th>Nome</th><th>Categoria</th><th className="numero">Versão</th><th>Situação</th><th>Alterado em</th><th><span className="visualmente-oculto">Ações</span></th></tr>
                </thead>
                <tbody>
                  {visiveis.map((i) => (
                    <tr key={i.id}>
                      <td><Selo>{NOME_DO_TIPO[i.tipo] ?? i.tipo}</Selo></td>
                      <td>
                        <span className="nome-e-login">
                          <code className="nome-do-item">{i.nome}</code>
                          {i.descricao && <small>{i.descricao}</small>}
                        </span>
                      </td>
                      <td>{i.categoria || '—'}</td>
                      <td className="numero">{i.versaoPublicada > 0 ? i.versaoPublicada : '—'}</td>
                      <td><Selo variante={SITUACAO_DO_ITEM[i.situacao]?.variante ?? 'neutro'}>{SITUACAO_DO_ITEM[i.situacao]?.rotulo ?? i.situacao}</Selo></td>
                      <td>{formatar.dataHora(i.alteradoEm)}{i.alteradoPor ? ` · ${i.alteradoPor}` : ''}</td>
                      <td>
                        {i.situacao !== 'retirado' && i.situacao !== 'retirada-pendente' && (
                          <div className="acoes">
                            <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Editar ${i.nome}`}
                              onClick={() => { limpar(); setModo({ tipo: 'editor', tipoDoItem: i.tipo, item: i }); }}>
                              Editar
                            </button>
                            <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Retirar ${i.nome}`} onClick={() => void retirar(i)}>
                              Retirar
                            </button>
                          </div>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}
    </div>
  );
}
