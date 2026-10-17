import type {
  ArquivoBaixado, CatalogoDisponivel, CatalogoNomeado, CatalogoNovo, CheckoutCriado, ColaboradorResumo, Conta, DemonstracaoResumo, EmpresaDoGestor, GravacaoDeContexto, ItemDeContexto,
  EmpresaNova, EmpresaResumo, EstadoDoGrupo, EventoDoLog, Filtro, GestorCriado, GrupoDeErroDetalhe, GrupoDeErroResumo, Harness, ImportacaoDoBasico,
  ItemDaEmpresa, ItemEditado, ItemEditavel, LoginResponse, MaquinaResumo, Mensagem, Pagina, PedidoDeDemonstracao, PedidoDeDemonstracaoCriado,
  ParticipanteDeEquipe, PublicacaoDoCatalogo, QualidadeResposta, QuantidadeResposta, RecorteDoLog, RelatorioDePublicacao, ReleaseDoCatalogo, RepositorioDaEmpresa,
  RoiResposta, UsuarioComSenha,
  UsuarioDaEmpresa, UsuarioEditado, UsuarioNovo, UsuarioResumo, VersaoDoDevKit, VisaoDoContexto, Vitrine,
} from './tipos';

/** A raiz da API, por variável de ambiente (VITE_API_URL); vazia é a mesma origem. */
export const baseDaApi = (import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '');

/** Uma resposta de erro da API, com o status, a mensagem do ProblemDetails e os erros por campo (400 de validação). */
export class ErroDaApi extends Error {
  readonly status: number;
  readonly erros: Record<string, string[]>;
  /** O corpo da resposta como veio — o 409 da publicação (US #461) traz o relatório, e não um ProblemDetails. */
  readonly corpo: unknown;

  constructor(status: number, mensagem: string, erros: Record<string, string[]> = {}, corpo: unknown = null) {
    super(mensagem);
    this.status = status;
    this.erros = erros;
    this.corpo = corpo;
  }
}

/** O texto de uma falha para a tela: os erros por campo do 400, senão o `detail` do ProblemDetails. */
export function mensagemDaFalha(falha: unknown): string {
  const campos = falha instanceof ErroDaApi ? Object.values(falha.erros).flat() : [];
  if (campos.length > 0) return campos.join(' ');
  return falha instanceof Error ? falha.message : 'Falha ao consultar a API.';
}

interface Opcoes {
  metodo?: string;
  corpo?: unknown;
  token?: string;
  aceita?: string;
}

/** A requisição: o 2xx volta como está; o resto vira {@link ErroDaApi} com a mensagem do ProblemDetails. */
async function requisitar(caminho: string, opcoes: Opcoes): Promise<Response> {
  const cabecalhos: Record<string, string> = { Accept: opcoes.aceita ?? 'application/json' };
  if (opcoes.corpo !== undefined) cabecalhos['Content-Type'] = 'application/json';
  if (opcoes.token) cabecalhos.Authorization = `Bearer ${opcoes.token}`;

  const resposta = await fetch(`${baseDaApi}${caminho}`, {
    method: opcoes.metodo ?? 'GET',
    headers: cabecalhos,
    body: opcoes.corpo === undefined ? undefined : JSON.stringify(opcoes.corpo),
  });

  if (!resposta.ok) {
    let mensagem = `A API respondeu ${resposta.status}.`;
    let erros: Record<string, string[]> = {};
    let corpo: unknown = null;
    try {
      corpo = await resposta.json();
      const problema = (corpo ?? {}) as { detail?: string; title?: string; errors?: Record<string, string[]> };
      mensagem = problema.detail ?? problema.title ?? mensagem;
      erros = problema.errors ?? {};
    } catch {
      // Sem corpo (o 401 do JWT): fica a mensagem padrão.
    }
    throw new ErroDaApi(resposta.status, mensagem, erros, corpo);
  }

  return resposta;
}

async function chamar<T>(caminho: string, opcoes: Opcoes = {}): Promise<T> {
  const resposta = await requisitar(caminho, opcoes);
  // 204 (a exclusão, a reação ao erro): sem corpo.
  if (resposta.status === 204) return undefined as T;
  return (await resposta.json()) as T;
}

/**
 * O nome do arquivo do Content-Disposition (o `filename*` UTF-8 primeiro). A API o expõe ao CORS;
 * sem ele (um proxy que o tira), vale o nome padrão de quem pediu.
 */
export function nomeDoArquivo(disposicao: string | null, padrao: string): string {
  if (!disposicao) return padrao;
  const estendido = /filename\*=UTF-8''([^;]+)/i.exec(disposicao);
  if (estendido) return decodeURIComponent(estendido[1].trim());
  const simples = /filename="?([^";]+)"?/i.exec(disposicao);
  return simples ? simples[1].trim() : padrao;
}

/** Baixa um arquivo da API (a exportação, o dev.kit): o conteúdo e o nome que ela sugeriu. Sem token, o pedido é anônimo. */
async function baixar(caminho: string, token: string | undefined, padrao: string): Promise<ArquivoBaixado> {
  const resposta = await requisitar(caminho, { token, aceita: '*/*' });
  return { conteudo: await resposta.blob(), nome: nomeDoArquivo(resposta.headers.get('Content-Disposition'), padrao) };
}

function consulta(filtro: Filtro, extra: Record<string, string> = {}): string {
  const parametros = new URLSearchParams({ de: filtro.de, ate: filtro.ate, ...extra });
  if (filtro.maquina !== null) parametros.set('maquina', String(filtro.maquina));
  return parametros.toString();
}

/** Os parâmetros de um recorte, sem os vazios: o que não foi escolhido não vai na URL. */
function preenchidos(valores: Record<string, string | null | undefined>): Record<string, string> {
  return Object.fromEntries(Object.entries(valores).filter((par): par is [string, string] => !!par[1]?.trim()));
}

/** A raiz das rotas do contexto (US #556): a do token, ou a do admin sobre uma empresa. */
function raizDoContexto(empresaDoAdmin?: number): string {
  return empresaDoAdmin === undefined ? '/api/contexto' : `/api/admin/empresas/${empresaDoAdmin}/contexto`;
}

export const api = {
  login: (login: string, senha: string) =>
    chamar<LoginResponse>('/api/auth/login', { metodo: 'POST', corpo: { login, senha } }),

  trocarSenha: (token: string, senhaAtual: string, novaSenha: string) =>
    chamar<LoginResponse>('/api/auth/trocar-senha', { metodo: 'POST', corpo: { senhaAtual, novaSenha }, token }),

  maquinas: (token: string) => chamar<MaquinaResumo[]>('/api/dashboard/maquinas', { token }),

  quantidade: (token: string, filtro: Filtro) =>
    chamar<QuantidadeResposta>(`/api/dashboard/quantidade?${consulta(filtro)}`, { token }),

  qualidade: (token: string, filtro: Filtro) =>
    chamar<QualidadeResposta>(`/api/dashboard/qualidade?${consulta(filtro)}`, { token }),

  // O recorte (tipo, detalhe e harness da sessão) lista uma a uma as falhas que a qualidade só conta.
  eventos: (token: string, filtro: Filtro, pagina: number, tamanho = 20, recorte: RecorteDoLog = {}) =>
    chamar<Pagina<EventoDoLog>>(
      `/api/dashboard/eventos?${consulta(filtro, { pagina: String(pagina), tamanho: String(tamanho), ...preenchidos({ ...recorte }) })}`,
      { token },
    ),

  // O ROI por work item (US #387): a foto mais recente de cada (máquina, item) no período.
  roi: (token: string, filtro: Filtro) =>
    chamar<RoiResposta>(`/api/dashboard/roi?${consulta(filtro)}`, { token }),

  pedirDemonstracao: (pedido: PedidoDeDemonstracao) =>
    chamar<PedidoDeDemonstracaoCriado>('/api/demonstracoes', { metodo: 'POST', corpo: pedido }),

  demonstracoes: (token: string, pagina: number, tamanho = 10) =>
    chamar<Pagina<DemonstracaoResumo>>(`/api/dashboard/demonstracoes?${new URLSearchParams({ pagina: String(pagina), tamanho: String(tamanho) })}`, { token }),

  excluirDemonstracao: (token: string, id: number) =>
    chamar<void>(`/api/dashboard/demonstracoes/${id}`, { metodo: 'DELETE', token }),

  // As exceções não classificadas (US #381).
  // O harness deixa só as falhas de turno daquele CLI (as exceções do app não têm harness).
  erros: (token: string, filtro: Filtro, pagina: number, tamanho = 10, harness: Harness | null = null) =>
    chamar<Pagina<GrupoDeErroResumo>>(
      `/api/dashboard/erros?${consulta(filtro, { pagina: String(pagina), tamanho: String(tamanho), ...preenchidos({ harness }) })}`,
      { token },
    ),

  erro: (token: string, id: number, filtro: Filtro) =>
    chamar<GrupoDeErroDetalhe>(`/api/dashboard/erros/${id}?${new URLSearchParams({ de: filtro.de, ate: filtro.ate })}`, { token }),

  alterarEstado: (token: string, id: number, estado: Exclude<EstadoDoGrupo, 'Regrediu'>, versao: string | null = null) =>
    chamar<void>(`/api/dashboard/erros/${id}/estado`, { metodo: 'PUT', corpo: { estado, versao }, token }),

  exportarErro: (token: string, id: number, filtro: Filtro) =>
    baixar(`/api/dashboard/erros/${id}/exportar?${new URLSearchParams({ de: filtro.de, ate: filtro.ate })}`, token, `excecao-${id}.json`),

  // O plano empresarial (US #381).
  colaboradores: (token: string) => chamar<ColaboradorResumo[]>('/api/dashboard/colaboradores', { token }),

  exportar: (token: string, filtro: Filtro, formato: 'csv' | 'json' = 'csv') =>
    baixar(`/api/dashboard/exportar?${consulta(filtro, { formato })}`, token, `uso-dos-colaboradores.${formato}`),

  empresas: (token: string) => chamar<EmpresaResumo[]>('/api/empresas', { token }),

  criarEmpresa: (token: string, empresa: EmpresaNova) =>
    chamar<EmpresaResumo>('/api/empresas', { metodo: 'POST', corpo: empresa, token }),

  convidarGestor: (token: string, empresa: number, login: string) =>
    chamar<GestorCriado>(`/api/empresas/${empresa}/gestores`, { metodo: 'POST', corpo: { login }, token }),

  // A versão do dev.kit (US #405): a última é anônima, e o zip também desde a US #425 (a reversão do login
  // da US #405, com o trial de 14 dias).
  ultimaVersao: () => chamar<VersaoDoDevKit>('/api/versoes/ultima'),

  // O endereço ABSOLUTO do zip da última versão (US #467 · task #471), para o href do link: o alias permanente da
  // API, e não o /{N}/download, para que a URL copiada continue valendo na próxima versão. Sem VITE_API_URL (a
  // mesma origem), o endereço é completado pela página — o que se copia tem de colar em outra aba.
  enderecoDoDownload: () => new URL(`${baseDaApi}/api/versoes/ultima/download`, window.location.href).href,

  // O catálogo (US #425): a vitrine pública do básico e o catálogo da empresa.
  vitrine: () => chamar<Vitrine>('/api/catalogo/base/vitrine'),

  // Os catálogos nomeados (US #461) — só o admin, menos o /meus. O slug vai codificado: é texto do admin.
  catalogos: (token: string) => chamar<CatalogoNomeado[]>('/api/catalogos', { token }),

  criarCatalogo: (token: string, catalogo: CatalogoNovo) =>
    chamar<CatalogoNomeado>('/api/catalogos', { metodo: 'POST', corpo: catalogo, token }),

  editarCatalogo: (token: string, slug: string, nome: string, descricao: string) =>
    chamar<CatalogoNomeado>(`/api/catalogos/${encodeURIComponent(slug)}`, { metodo: 'PUT', corpo: { nome, descricao }, token }),

  excluirCatalogo: (token: string, slug: string) =>
    chamar<void>(`/api/catalogos/${encodeURIComponent(slug)}`, { metodo: 'DELETE', token }),

  relacionarEmpresa: (token: string, slug: string, empresaId: number) =>
    chamar<CatalogoNomeado>(`/api/catalogos/${encodeURIComponent(slug)}/empresas`, { metodo: 'POST', corpo: { empresaId }, token }),

  desrelacionarEmpresa: (token: string, slug: string, empresaId: number) =>
    chamar<CatalogoNomeado>(`/api/catalogos/${encodeURIComponent(slug)}/empresas/${empresaId}`, { metodo: 'DELETE', token }),

  itensDoCatalogo: (token: string, slug: string) =>
    chamar<ItemEditavel[]>(`/api/catalogos/${encodeURIComponent(slug)}/itens`, { token }),

  itemDoCatalogo: (token: string, slug: string, id: number) =>
    chamar<ItemEditavel>(`/api/catalogos/${encodeURIComponent(slug)}/itens/${id}`, { token }),

  criarItem: (token: string, slug: string, item: ItemEditado) =>
    chamar<ItemEditavel>(`/api/catalogos/${encodeURIComponent(slug)}/itens`, { metodo: 'POST', corpo: item, token }),

  editarItem: (token: string, slug: string, id: number, item: ItemEditado) =>
    chamar<ItemEditavel>(`/api/catalogos/${encodeURIComponent(slug)}/itens/${id}`, { metodo: 'PUT', corpo: item, token }),

  // O item nunca publicado some (204, sem corpo); o publicado vira 'retirada-pendente'.
  retirarDoCatalogo: (token: string, slug: string, id: number) =>
    chamar<ItemEditavel | undefined>(`/api/catalogos/${encodeURIComponent(slug)}/itens/${id}`, { metodo: 'DELETE', token }),

  conferirPublicacao: (token: string, slug: string, minDevKit: number | null = null) =>
    chamar<RelatorioDePublicacao>(
      `/api/catalogos/${encodeURIComponent(slug)}/publicacao${minDevKit === null ? '' : `?${new URLSearchParams({ minDevKit: String(minDevKit) })}`}`,
      { token },
    ),

  // O 409 traz o RelatorioDePublicacao no corpo (ErroDaApi.corpo), e não um ProblemDetails.
  publicarCatalogo: (token: string, slug: string, minDevKit: number | null = null) =>
    chamar<PublicacaoDoCatalogo>(`/api/catalogos/${encodeURIComponent(slug)}/publicar`, {
      metodo: 'POST', corpo: minDevKit === null ? {} : { minDevKit }, token,
    }),

  importarBasico: (token: string) => chamar<ImportacaoDoBasico>('/api/catalogos/basico/importar', { metodo: 'POST', token }),

  catalogosMeus: (token?: string) => chamar<CatalogoDisponivel[]>('/api/catalogos/meus', { token }),

  // As releases de um catálogo (a aba Releases): a lista, o manifesto de uma e o zip — este por fetch e blob,
  // porque a rota é autenticada e um link direto não levaria o token.
  releasesDoCatalogo: (token: string, slug: string) =>
    chamar<ReleaseDoCatalogo[]>(`/api/catalogos/${encodeURIComponent(slug)}/releases`, { token }),

  manifestoDaRelease: (token: string, slug: string, versao: number) =>
    chamar<unknown>(`/api/catalogos/${encodeURIComponent(slug)}/releases/${versao}/manifesto`, { token }),

  baixarRelease: (token: string, slug: string, release: ReleaseDoCatalogo) =>
    baixar(`/api/catalogos/${encodeURIComponent(slug)}/releases/${release.versao}/download`, token, `${release.tag}.zip`),

  catalogoDaEmpresa: (token: string) => chamar<ItemDaEmpresa[]>('/api/catalogo/empresa', { token }),

  retirarItem: (token: string, id: number) => chamar<ItemDaEmpresa>(`/api/catalogo/empresa/${id}`, { metodo: 'DELETE', token }),

  // A conta do dev (US #425): adesão, link e senha são anônimos; a conta e a empresa do gestor, não.
  aderir: (codigo: string, email: string, nome: string) =>
    chamar<Mensagem>('/api/conta/adesao', { metodo: 'POST', corpo: { codigo, email, nome } }),

  pedirLink: (email: string) => chamar<Mensagem>('/api/conta/link', { metodo: 'POST', corpo: { email } }),

  definirSenha: (token: string, novaSenha: string) =>
    chamar<void>('/api/conta/definir-senha', { metodo: 'POST', corpo: { token, novaSenha } }),

  conta: (token: string) => chamar<Conta>('/api/conta', { token }),

  empresaDoGestor: (token: string) => chamar<EmpresaDoGestor>('/api/empresa', { token }),

  usuariosDaEmpresa: (token: string) => chamar<UsuarioDaEmpresa[]>('/api/empresa/usuarios', { token }),

  bloquearNaEmpresa: (token: string, id: number, bloqueado: boolean) =>
    chamar<UsuarioDaEmpresa>(`/api/empresa/usuarios/${id}/bloqueio`, { metodo: 'PUT', corpo: { bloqueado }, token }),

  novoCodigo: (token: string) => chamar<{ codigoDeAdesao: string }>('/api/empresa/codigo', { metodo: 'POST', token }),

  politicaDaEmpresa: (token: string, loginObrigatorio: boolean | null) =>
    chamar<EmpresaDoGestor>('/api/empresa/politica', { metodo: 'PUT', corpo: { loginObrigatorio }, token }),

  // O que a empresa compartilha com o dev.kit (US #491): o catálogo de repositórios — do gestor e do dev.
  repositoriosDaEmpresa: (token: string) => chamar<RepositorioDaEmpresa[]>('/api/empresa/repositorios', { token }),

  gravarRepositorio: (token: string, origem: string, descricao: string) =>
    chamar<RepositorioDaEmpresa>('/api/empresa/repositorios', { metodo: 'POST', corpo: { origem, descricao }, token }),

  removerRepositorio: (token: string, origem: string) =>
    chamar<RepositorioDaEmpresa>(`/api/empresa/repositorios?origem=${encodeURIComponent(origem)}`, { metodo: 'DELETE', token }),

  // O contexto do dev.kit (US #556). Sem `empresaDoAdmin`, as rotas do token (o dev e o gestor, na empresa deles); com
  // ela, as do admin do board sobre a empresa escolhida — o mesmo serviço, e a permissão é sempre da API.
  visaoDoContexto: (token: string, empresaDoAdmin?: number) =>
    chamar<VisaoDoContexto>(empresaDoAdmin === undefined ? '/api/contexto/visao' : raizDoContexto(empresaDoAdmin), { token }),

  /** Grava o item; o 409 (editado por outra pessoa) traz o ConflitoDeContexto no ErroDaApi.corpo. */
  gravarContexto: (token: string, gravacao: GravacaoDeContexto, empresaDoAdmin?: number) =>
    chamar<ItemDeContexto>(raizDoContexto(empresaDoAdmin), { metodo: 'PUT', corpo: gravacao, token }),

  /** Exclui o item — descartar a sugestão de um agente é isto. */
  excluirContexto: (token: string, id: number, empresaDoAdmin?: number) =>
    chamar<ItemDeContexto>(`${raizDoContexto(empresaDoAdmin)}/${id}`, { metodo: 'DELETE', token }),

  /** A sugestão do agente vira a instrução da mesma chave. */
  promoverContexto: (token: string, id: number, empresaDoAdmin?: number) =>
    chamar<ItemDeContexto>(`${raizDoContexto(empresaDoAdmin)}/${id}/promover`, { metodo: 'POST', token }),

  /** O gestor (ou o admin) aprova ou recusa a participação de um usuário numa equipe. */
  decidirParticipacao: (token: string, equipe: string, usuarioId: number, situacao: 'aprovado' | 'recusado', empresaDoAdmin?: number) =>
    chamar<ParticipanteDeEquipe>(`${raizDoContexto(empresaDoAdmin)}/equipes/${encodeURIComponent(equipe)}/participantes`, {
      metodo: 'PUT', corpo: { usuarioId, situacao }, token,
    }),

  // A compra individual (US #425): o checkout leva ao gateway (ou ao simulado, sem gateway configurado).
  checkout: (email: string, nome: string, plano: string) =>
    chamar<CheckoutCriado>('/api/pagamentos/checkout', { metodo: 'POST', corpo: { email, nome, plano } }),

  // A gestão de usuários (US #405) — só o admin.
  usuarios: (token: string) => chamar<UsuarioResumo[]>('/api/usuarios', { token }),

  criarUsuario: (token: string, usuario: UsuarioNovo) =>
    chamar<UsuarioComSenha>('/api/usuarios', { metodo: 'POST', corpo: usuario, token }),

  editarUsuario: (token: string, id: number, edicao: UsuarioEditado) =>
    chamar<UsuarioResumo>(`/api/usuarios/${id}`, { metodo: 'PUT', corpo: edicao, token }),

  bloquearUsuario: (token: string, id: number, bloqueado: boolean) =>
    chamar<UsuarioResumo>(`/api/usuarios/${id}/bloqueio`, { metodo: 'PUT', corpo: { bloqueado }, token }),

  redefinirSenha: (token: string, id: number) =>
    chamar<UsuarioComSenha>(`/api/usuarios/${id}/senha`, { metodo: 'POST', token }),
};
