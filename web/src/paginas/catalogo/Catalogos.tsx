import { useState, type FormEvent } from 'react';
import { api } from '../../api/cliente';
import type { CatalogoNomeado } from '../../api/tipos';
import { PainelLateral } from '../../componentes/PainelLateral';
import { Selo } from '../../componentes/Selo';
import { formatar } from '../../formatar';
import { DetalheDoCatalogo } from './DetalheDoCatalogo';

/** O slug: minúsculas, números e hífen, de 2 a 40 — a mesma regra da API (que também recusa os reservados). */
export const SLUG = /^[a-z0-9][a-z0-9-]{0,38}[a-z0-9]$/;

/**
 * A aba Catálogos (US #461) — só do admin: a lista dos catálogos (o básico primeiro), o cadastro de um novo no
 * painel lateral e, ao abrir um, o detalhe dele (empresas, itens, edição e Publicar).
 */
export function Catalogos({ token, catalogos, aberto, aoAbrir, aoMudarLista, aoPublicar, tratar }: {
  token: string;
  catalogos: CatalogoNomeado[] | null;
  aberto: string | null;
  aoAbrir: (slug: string | null) => void;
  aoMudarLista: () => Promise<unknown>;
  aoPublicar: (slug: string) => void;
  tratar: (falha: unknown) => string;
}) {
  const [slug, setSlug] = useState('');
  const [nome, setNome] = useState('');
  const [descricao, setDescricao] = useState('');
  const [erro, setErro] = useState('');

  async function criar(evento: FormEvent) {
    evento.preventDefault();
    setErro('');
    const novo = slug.trim();
    if (!SLUG.test(novo)) {
      setErro('O slug usa só minúsculas, números e hífen, de 2 a 40 caracteres (ex.: equipe-uau).');
      return;
    }
    try {
      const criado = await api.criarCatalogo(token, { slug: novo, nome: nome.trim(), descricao: descricao.trim() });
      setSlug('');
      setNome('');
      setDescricao('');
      await aoMudarLista();
      aoAbrir(criado.slug);
    } catch (falha) {
      setErro(tratar(falha));
    }
  }

  const doAberto = aberto ? catalogos?.find((c) => c.slug === aberto) : undefined;
  if (aberto && doAberto) {
    return (
      <DetalheDoCatalogo
        token={token}
        catalogo={doAberto}
        aoVoltar={() => aoAbrir(null)}
        aoMudar={aoMudarLista}
        aoPublicar={aoPublicar}
        tratar={tratar}
      />
    );
  }

  return (
    <div className="com-lateral">
      <section className="cartao cartao-de-tabela" aria-label="Catálogos cadastrados">
        {!catalogos ? (
          <p className="carregando" role="status">Carregando…</p>
        ) : (
          <div className="tabela-rolavel-simples">
            <table className="tabela tabela-do-cartao">
              <thead>
                <tr>
                  <th>Catálogo</th><th className="numero">Versão</th><th>Publicada em</th><th className="numero">Itens</th><th>Pendências</th><th>Empresas</th>
                  <th><span className="visualmente-oculto">Ações</span></th>
                </tr>
              </thead>
              <tbody>
                {catalogos.map((c) => (
                  <tr key={c.slug}>
                    <td>
                      <span className="nome-e-login">
                        <strong>{c.nome}</strong>
                        <small><code>{c.slug}</code>{c.basico ? ' · básico' : ''}</small>
                      </span>
                    </td>
                    <td className="numero">{c.versao || '—'}</td>
                    <td>{formatar.data(c.publicadaEm)}</td>
                    <td className="numero">{c.itens}</td>
                    <td>{c.pendentes > 0 ? <Selo variante="aviso">{c.pendentes} pendente(s)</Selo> : <span className="texto-mudo">—</span>}</td>
                    <td>{c.basico ? 'Todas' : c.empresas.length === 0 ? <span className="texto-mudo">Nenhuma</span> : c.empresas.map((e) => e.nome).join(', ')}</td>
                    <td>
                      <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Abrir ${c.nome}`} onClick={() => aoAbrir(c.slug)}>Abrir</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <PainelLateral id="titulo-novo-catalogo" titulo="Novo catálogo">
        <form className="formulario-lateral" onSubmit={criar} aria-label="Novo catálogo">
          <label>
            Slug
            <input value={slug} onChange={(e) => setSlug(e.target.value.toLowerCase())} maxLength={40} placeholder="equipe-uau" required className="fonte-mono" />
          </label>
          <label>
            Nome
            <input value={nome} onChange={(e) => setNome(e.target.value)} maxLength={100} placeholder="Nome que aparece no dev.kit" required />
          </label>
          <label>
            Descrição
            <textarea value={descricao} onChange={(e) => setDescricao(e.target.value)} maxLength={500} rows={3} />
          </label>
          <p className="nota-do-formulario">O slug é a identidade do catálogo nas máquinas e não muda depois; o nome aparece na coluna Origem do dev.kit.</p>
          {erro && <p className="erro" role="alert">{erro}</p>}
          <button className="botao botao-primario botao-largo" type="submit">Criar catálogo</button>
        </form>
      </PainelLateral>
    </div>
  );
}
