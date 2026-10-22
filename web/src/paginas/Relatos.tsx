import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { api, ErroDaApi, mensagemDaFalha } from '../api/cliente';
import type { CategoriaDeRelato, EmpresaResumo, EstadoDoRelato, FiltroDeRelatos, Pagina as PaginaDaApi, RelatoDetalhe, RelatoResumo } from '../api/tipos';
import { CATEGORIAS_DE_RELATO, ESTADOS_DE_RELATO, ListaDeRelatos, SeloDaCategoria, SeloDoEstado } from '../componentes/ListaDeRelatos';
import { Pagina } from '../componentes/Pagina';
import { MenuDaEmpresa, MenuDoAdmin, Topo } from '../componentes/Topo';
import { formatar } from '../formatar';
import { papelDa, useSessao } from '../sessao';

/** Quantos relatos por página. */
export const RELATOS_POR_PAGINA = 20;

/** O teto da resposta (RelatosV1.TamanhoMaximoDaResposta). */
export const TAMANHO_MAXIMO_DA_RESPOSTA = 4000;

/** A mensagem do 403: o dev (ou o gestor sem empresa) não vê os relatos da gestão. */
export const SEM_PERMISSAO = 'Sem permissão para ver os relatos.';

const SEM_FILTRO: FiltroDeRelatos = { estado: '', categoria: '', empresa: null, de: '', ate: '' };

/** O tipo da imagem pelo começo do base64 (o JPEG começa com FF D8 FF, "/9j/"); o resto é PNG. */
export function enderecoDaCaptura(base64: string): string {
  return `data:image/${base64.startsWith('/9j/') ? 'jpeg' : 'png'};base64,${base64}`;
}

/** O rótulo do estado para os avisos ("em análise"). */
const rotuloDoEstado = (estado: EstadoDoRelato) => ESTADOS_DE_RELATO.find((e) => e.valor === estado)?.rotulo ?? estado;

/**
 * Os relatos dos usuários do dev.kit (US #602, /relatos): o defeito, a sugestão ou a dúvida que chegou pelo app ou pelo
 * `devcli relatar`. O admin geral vê todos (e filtra por empresa); o gestor, só os da empresa dele — a API aplica o
 * escopo (o relato de outra empresa é 404). Filtros por estado, categoria, empresa e período; o detalhe traz o texto, o
 * contexto mascarado, a captura, a troca de estado e a resposta, que o autor lê em `GET /api/relatos/meus`.
 */
export function Relatos() {
  const { sessao, sair } = useSessao();
  const token = sessao!.token;
  const ehAdmin = papelDa(sessao!) === 'admin';
  const [filtro, setFiltro] = useState<FiltroDeRelatos>(SEM_FILTRO);
  const [pagina, setPagina] = useState(1);
  const [lista, setLista] = useState<PaginaDaApi<RelatoResumo> | null>(null);
  const [empresas, setEmpresas] = useState<EmpresaResumo[]>([]);
  const [aberto, setAberto] = useState<number | null>(null);
  const [detalhe, setDetalhe] = useState<RelatoDetalhe | null>(null);
  const [resposta, setResposta] = useState('');
  const [aviso, setAviso] = useState('');
  const [erro, setErro] = useState('');
  const [semPermissao, setSemPermissao] = useState(false);

  const tratar = useCallback((falha: unknown) => {
    if (falha instanceof ErroDaApi && falha.status === 401) {
      sair('expirou');
      return;
    }
    if (falha instanceof ErroDaApi && falha.status === 403) {
      setSemPermissao(true);
      return;
    }
    setErro(mensagemDaFalha(falha));
  }, [sair]);

  const carregarLista = useCallback(
    () => api.relatos(token, filtro, pagina, RELATOS_POR_PAGINA).then(setLista).catch(tratar),
    [token, filtro, pagina, tratar],
  );

  useEffect(() => {
    void carregarLista();
  }, [carregarLista]);

  // O filtro de empresa é só do admin: a mesma lista que Empresas e Usuários carregam.
  useEffect(() => {
    if (ehAdmin) api.empresas(token).then(setEmpresas).catch(tratar);
  }, [ehAdmin, token, tratar]);

  useEffect(() => {
    setDetalhe(null);
    setResposta('');
    setAviso('');
    if (aberto !== null) api.relato(token, aberto).then((d) => { setDetalhe(d); setResposta(d.resposta); }).catch(tratar);
  }, [aberto, token, tratar]);

  function filtrar(mudanca: Partial<FiltroDeRelatos>) {
    setPagina(1);
    setAberto(null);
    setFiltro((atual) => ({ ...atual, ...mudanca }));
  }

  async function mudarEstado(estado: EstadoDoRelato) {
    if (!detalhe) return;
    setErro('');
    try {
      setDetalhe(await api.alterarEstadoDoRelato(token, detalhe.id, estado));
      setAviso(`Relato ${detalhe.id} marcado como ${rotuloDoEstado(estado).toLowerCase()}.`);
      await carregarLista();
    } catch (falha) {
      tratar(falha);
    }
  }

  async function responder(evento: FormEvent) {
    evento.preventDefault();
    if (!detalhe || !resposta.trim()) return;
    setErro('');
    try {
      const respondido = await api.responderRelato(token, detalhe.id, resposta.trim());
      setDetalhe(respondido);
      setResposta(respondido.resposta);
      setAviso(`Resposta gravada no relato ${detalhe.id}: o autor a vê nos relatos dele.`);
      await carregarLista();
    } catch (falha) {
      tratar(falha);
    }
  }

  const filtrando = Boolean(filtro.estado || filtro.categoria || filtro.empresa !== null || filtro.de || filtro.ate);

  return (
    <div className="pagina">
      <Topo secao="relatos">
        {ehAdmin ? <MenuDoAdmin /> : <MenuDaEmpresa />}
      </Topo>

      <Pagina
        rotulo="Suporte"
        titulo="Relatos"
        subtitulo={ehAdmin
          ? 'Defeitos, sugestões e dúvidas que os usuários mandaram pelo dev.kit.'
          : `Defeitos, sugestões e dúvidas dos usuários da ${sessao!.empresa ?? 'sua empresa'}.`}
      >
        {semPermissao ? (
          <p className="erro" role="alert">{SEM_PERMISSAO}</p>
        ) : (
          <>
            {erro && <p className="erro" role="alert">{erro}</p>}
            {aviso && <p className="aviso" role="status">{aviso}</p>}

            <section className="cartao cartao-de-tabela" aria-label="Relatos">
              <div className="barra-de-filtros" role="search" aria-label="Filtrar os relatos">
                <select aria-label="Filtrar por estado" value={filtro.estado} onChange={(e) => filtrar({ estado: e.target.value as EstadoDoRelato | '' })}>
                  <option value="">Todos os estados</option>
                  {ESTADOS_DE_RELATO.map((e) => <option key={e.valor} value={e.valor}>{e.rotulo}</option>)}
                </select>
                <select aria-label="Filtrar por categoria" value={filtro.categoria} onChange={(e) => filtrar({ categoria: e.target.value as CategoriaDeRelato | '' })}>
                  <option value="">Todas as categorias</option>
                  {CATEGORIAS_DE_RELATO.map((c) => <option key={c.valor} value={c.valor}>{c.rotulo}</option>)}
                </select>
                {ehAdmin && (
                  <select aria-label="Filtrar por empresa" value={filtro.empresa ?? ''} onChange={(e) => filtrar({ empresa: e.target.value ? Number(e.target.value) : null })}>
                    <option value="">Todas as empresas</option>
                    {empresas.map((e) => <option key={e.id} value={e.id}>{e.nome}</option>)}
                  </select>
                )}
                <label>
                  De
                  <input type="date" value={filtro.de} max={filtro.ate || undefined} onChange={(e) => filtrar({ de: e.target.value })} />
                </label>
                <label>
                  Até
                  <input type="date" value={filtro.ate} min={filtro.de || undefined} onChange={(e) => filtrar({ ate: e.target.value })} />
                </label>
                {filtrando && <button className="botao botao-fantasma botao-pequeno" type="button" onClick={() => filtrar(SEM_FILTRO)}>Limpar filtros</button>}
              </div>

              <ListaDeRelatos dados={lista} aberto={aberto} aoAbrir={setAberto} pagina={pagina} aoMudarDePagina={setPagina} filtrando={filtrando} />
            </section>

            {aberto !== null && (
              <article className="detalhe-do-erro" aria-label="Detalhe do relato">
                {!detalhe ? (
                  <p className="carregando" role="status">Carregando o relato…</p>
                ) : (
                  <>
                    <header>
                      <h3>Relato {detalhe.id}</h3>
                      <p>
                        <SeloDaCategoria categoria={detalhe.categoria} /> <SeloDoEstado estado={detalhe.estado} /> · {detalhe.autor}
                        {detalhe.empresa ? ` · ${detalhe.empresa}` : ''} · recebido em {formatar.dataHora(detalhe.criadoEm)}
                        {detalhe.versaoDevKit ? ` · ${formatar.versaoDoDevKit(detalhe.versaoDevKit)}` : ''}
                      </p>
                    </header>

                    <h4>Texto</h4>
                    <p className="texto-do-relato">{detalhe.texto}</p>

                    <h4>Contexto</h4>
                    {detalhe.contexto ? (
                      <pre className="trace" tabIndex={0} aria-label="Contexto do relato">{detalhe.contexto}</pre>
                    ) : (
                      <p className="vazio">O usuário não mandou o contexto.</p>
                    )}

                    {detalhe.captura && (
                      <>
                        <h4>Captura</h4>
                        <img className="captura-do-relato" src={enderecoDaCaptura(detalhe.captura)} alt={`Captura de tela enviada com o relato ${detalhe.id}`} />
                      </>
                    )}

                    <div className="acoes" role="group" aria-label="Estado do relato">
                      {ESTADOS_DE_RELATO.map((e) => (
                        <button
                          key={e.valor}
                          className="botao botao-fantasma"
                          type="button"
                          aria-pressed={detalhe.estado === e.valor}
                          disabled={detalhe.estado === e.valor}
                          onClick={() => void mudarEstado(e.valor)}
                        >
                          {e.rotulo}
                        </button>
                      ))}
                    </div>

                    <form className="formulario-lateral" aria-label="Responder ao relato" onSubmit={(e) => void responder(e)}>
                      {detalhe.resposta ? (
                        <p className="nota-informativa" data-testid="resposta-atual">
                          Respondido por <strong>{detalhe.respondidoPor || '—'}</strong> em {formatar.dataHora(detalhe.respondidoEm)}: {detalhe.resposta}
                        </p>
                      ) : (
                        <p className="nota-do-formulario">Ainda sem resposta. Responder um relato novo o leva a em análise.</p>
                      )}
                      <label>
                        Resposta
                        <textarea rows={4} value={resposta} maxLength={TAMANHO_MAXIMO_DA_RESPOSTA} onChange={(e) => setResposta(e.target.value)} />
                      </label>
                      <button className="botao botao-primario" type="submit" disabled={!resposta.trim() || resposta.trim() === detalhe.resposta}>
                        {detalhe.resposta ? 'Atualizar a resposta' : 'Responder'}
                      </button>
                    </form>
                  </>
                )}
              </article>
            )}
          </>
        )}
      </Pagina>
    </div>
  );
}
