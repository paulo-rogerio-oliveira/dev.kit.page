import { useState, type FormEvent } from 'react';
import { api } from '../../api/cliente';
import type { CatalogoNomeado } from '../../api/tipos';
import { PainelLateral } from '../../componentes/PainelLateral';
import { Selo } from '../../componentes/Selo';
import { formatar } from '../../formatar';
import { DetalheDoCatalogo } from './DetalheDoCatalogo';
import { Publicacao } from './Publicacao';

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
  const [origem, setOrigem] = useState('branco');
  const [repositorio, setRepositorio] = useState('');
  const [referencia, setReferencia] = useState('main');
  const [pasta, setPasta] = useState('');
  const [arquivos, setArquivos] = useState<File[]>([]);
  const [ocupado, setOcupado] = useState(false);
  const [resultado, setResultado] = useState('');
  const [publicando, setPublicando] = useState<string | null>(null);

  async function arquivosLocais() {
    const json = arquivos.filter((a) => a.name.toLowerCase().endsWith('.json'));
    if (!json.length || json.length > 2000 || json.some((a) => a.size > 1024 * 1024) || json.reduce((n, a) => n + a.size, 0) > 16 * 1024 * 1024) {
      throw new Error('Escolha uma pasta com até 2000 arquivos JSON, 16 MB no total e 1 MB por arquivo.');
    }
    return Promise.all(json.map(async (arquivo) => {
      const caminho = arquivo.webkitRelativePath || arquivo.name;
      return { caminho: caminho.includes('/') ? caminho.slice(caminho.indexOf('/') + 1) : caminho, conteudo: await arquivo.text() };
    }));
  }

  async function criar(evento: FormEvent) {
    evento.preventDefault();
    if (ocupado) return;
    setErro('');
    setResultado('');
    const novo = slug.trim();
    if (!SLUG.test(novo)) {
      setErro('O slug usa só minúsculas, números e hífen, de 2 a 40 caracteres (ex.: equipe-uau).');
      return;
    }
    setOcupado(true);
    try {
      const catalogo = { slug: novo, nome: nome.trim(), descricao: descricao.trim() };
      if (origem !== 'branco') {
        const importado = await api.importarCatalogo(token, origem === 'local'
          ? { catalogo, arquivos: await arquivosLocais() }
          : { catalogo, repositorio: repositorio.trim(), referencia: referencia.trim(), pasta: pasta.trim() });
        setResultado(`Importado ${origem === 'local' ? 'da pasta local' : 'do GitHub'}: ${importado.catalogo.slug} com ${importado.catalogo.itens} itens (${importado.skills} skills, ${importado.rules} Rules, ${importado.fluxos} fluxos). Revise e publique para as máquinas receberem.`);
        await aoMudarLista();
        return;
      }
      const criado = await api.criarCatalogo(token, catalogo);
      setSlug('');
      setNome('');
      setDescricao('');
      await aoMudarLista();
      aoAbrir(criado.slug);
    } catch (falha) {
      setErro(tratar(falha));
    } finally {
      setOcupado(false);
    }
  }

  const doAberto = aberto ? catalogos?.find((c) => c.slug === aberto) : undefined;
  const paraPublicar = catalogos?.find((c) => c.slug === publicando);
  if (paraPublicar) {
    return <Publicacao token={token} catalogo={paraPublicar} aoPublicar={aoPublicar} aoCancelar={() => setPublicando(null)} tratar={tratar} />;
  }
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
      <div>
      {resultado && <p className="aviso sucesso" role="status">{resultado}</p>}
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
                        {c.origem && <small><Selo variante={c.origem.startsWith('GitHub') ? 'info' : 'neutro'}>{c.origem}</Selo></small>}
                      </span>
                    </td>
                    <td className="numero">{c.versao || '—'}</td>
                    <td>{formatar.data(c.publicadaEm)}</td>
                    <td className="numero">{c.itens}</td>
                    <td>{c.pendentes > 0 ? <Selo variante="aviso">{c.pendentes} a publicar</Selo> : <span className="texto-mudo">—</span>}</td>
                    <td>{c.basico ? 'Todas' : c.empresas.length === 0 ? <span className="texto-mudo">Nenhuma</span> : c.empresas.map((e) => e.nome).join(', ')}</td>
                    <td className="acoes-do-catalogo">
                      <button className="botao botao-fantasma botao-pequeno" type="button" aria-label={`Abrir ${c.nome}`} onClick={() => aoAbrir(c.slug)}>Abrir</button>
                      <button className={`botao ${c.pendentes > 0 ? 'botao-primario' : 'botao-fantasma'} botao-pequeno`} type="button" disabled={c.pendentes === 0 || ocupado} aria-label={`Publicar ${c.nome}`} onClick={() => setPublicando(c.slug)}>Publicar</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
      </div>

      <PainelLateral id="titulo-novo-catalogo" titulo="Novo catálogo">
        <form className="formulario-lateral" onSubmit={criar} aria-label="Novo catálogo">
          <fieldset disabled={ocupado} className="origem-do-catalogo">
            <legend className="visualmente-oculto">Origem do catálogo</legend>
            {([['branco', 'Em branco'], ['local', 'Pasta local'], ['github', 'GitHub']] as const).map(([valor, rotulo]) => (
              <button key={valor} type="button" className={`botao botao-pequeno ${origem === valor ? 'botao-secundario' : 'botao-fantasma'}`} aria-pressed={origem === valor} onClick={() => { setOrigem(valor); setErro(''); }}>{rotulo}</button>
            ))}
          </fieldset>
          {origem === 'github' && <>
            <label>Repositório<input value={repositorio} onChange={(e) => setRepositorio(e.target.value)} placeholder="github.com/org/catalogo-exemplo" required disabled={ocupado} maxLength={220} /></label>
            <div className="campos-da-origem">
              <label>Branch ou tag<input value={referencia} onChange={(e) => setReferencia(e.target.value)} placeholder="main" disabled={ocupado} maxLength={200} /></label>
              <label>Pasta<input value={pasta} onChange={(e) => setPasta(e.target.value)} placeholder="/ (raiz)" disabled={ocupado} maxLength={300} /></label>
            </div>
          </>}
          {origem === 'local' && <label>Escolher pasta…
            <input type="file" multiple {...{ webkitdirectory: '' }} disabled={ocupado} onChange={(e) => setArquivos(Array.from(e.target.files ?? []))} />
            <small>{arquivos.length ? `${arquivos.length} arquivos selecionados; somente JSON será enviado.` : 'Selecione a pasta com manifesto.json e os itens do catálogo.'}</small>
          </label>}
          <div className={origem === 'branco' ? 'formulario-lateral' : 'campos-da-origem'}>
          <label>
            Slug
            <input value={slug} onChange={(e) => setSlug(e.target.value.toLowerCase())} maxLength={40} placeholder="equipe-uau" required disabled={ocupado} className="fonte-mono" />
          </label>
          <label>
            Nome
            <input value={nome} onChange={(e) => setNome(e.target.value)} maxLength={80} placeholder="Nome que aparece no dev.kit" required disabled={ocupado} />
          </label>
          </div>
          {origem === 'branco' && <label>
            Descrição
            <textarea value={descricao} onChange={(e) => setDescricao(e.target.value)} maxLength={500} rows={3} />
          </label>}
          <p className="nota-do-formulario">{origem === 'branco' ? 'O slug é a identidade do catálogo nas máquinas e não muda depois; o nome aparece na coluna Origem do dev.kit.' : 'Selecione um catálogo com manifesto.json. Os itens serão importados para revisão antes de publicar.'}</p>
          {erro && <p className="erro" role="alert">{erro}</p>}
          <button className="botao botao-primario botao-largo" type="submit" disabled={ocupado}>{ocupado ? 'Importando…' : origem === 'branco' ? 'Criar catálogo' : origem === 'local' ? 'Importar pasta local' : 'Importar do GitHub'}</button>
        </form>
      </PainelLateral>
    </div>
  );
}
