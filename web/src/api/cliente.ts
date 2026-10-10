import type {
  ArquivoBaixado, ColaboradorResumo, DemonstracaoResumo, EmpresaNova, EmpresaResumo, EstadoDoGrupo, EventoDoLog, Filtro,
  GestorCriado, GrupoDeErroDetalhe, GrupoDeErroResumo, LoginResponse, MaquinaResumo, Pagina, PedidoDeDemonstracao,
  PedidoDeDemonstracaoCriado, QualidadeResposta, QuantidadeResposta, RoiResposta, UsuarioComSenha, UsuarioEditado, UsuarioNovo,
  UsuarioResumo, VersaoDoDevKit,
} from './tipos';

/** A raiz da API, por variável de ambiente (VITE_API_URL); vazia é a mesma origem. */
export const baseDaApi = (import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '');

/** Uma resposta de erro da API, com o status, a mensagem do ProblemDetails e os erros por campo (400 de validação). */
export class ErroDaApi extends Error {
  readonly status: number;
  readonly erros: Record<string, string[]>;

  constructor(status: number, mensagem: string, erros: Record<string, string[]> = {}) {
    super(mensagem);
    this.status = status;
    this.erros = erros;
  }
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
    try {
      const problema = (await resposta.json()) as { detail?: string; title?: string; errors?: Record<string, string[]> };
      mensagem = problema.detail ?? problema.title ?? mensagem;
      erros = problema.errors ?? {};
    } catch {
      // Sem corpo (o 401 do JWT): fica a mensagem padrão.
    }
    throw new ErroDaApi(resposta.status, mensagem, erros);
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

/** Baixa um arquivo da API (a exportação): o conteúdo e o nome que ela sugeriu. */
async function baixar(caminho: string, token: string, padrao: string): Promise<ArquivoBaixado> {
  const resposta = await requisitar(caminho, { token, aceita: '*/*' });
  return { conteudo: await resposta.blob(), nome: nomeDoArquivo(resposta.headers.get('Content-Disposition'), padrao) };
}

function consulta(filtro: Filtro, extra: Record<string, string> = {}): string {
  const parametros = new URLSearchParams({ de: filtro.de, ate: filtro.ate, ...extra });
  if (filtro.maquina !== null) parametros.set('maquina', String(filtro.maquina));
  return parametros.toString();
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

  eventos: (token: string, filtro: Filtro, pagina: number, tamanho = 20) =>
    chamar<Pagina<EventoDoLog>>(`/api/dashboard/eventos?${consulta(filtro, { pagina: String(pagina), tamanho: String(tamanho) })}`, { token }),

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
  erros: (token: string, filtro: Filtro, pagina: number, tamanho = 10) =>
    chamar<Pagina<GrupoDeErroResumo>>(`/api/dashboard/erros?${consulta(filtro, { pagina: String(pagina), tamanho: String(tamanho) })}`, { token }),

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

  // A versão do dev.kit (US #405): a última é anônima; o zip exige o token — por isso é baixado com
  // fetch + Bearer e entregue como blob, e não por um link direto (que não levaria o token).
  ultimaVersao: () => chamar<VersaoDoDevKit>('/api/versoes/ultima'),

  baixarVersao: (token: string, versao: VersaoDoDevKit) =>
    baixar(versao.urlDownload, token, `devkit-${versao.versao}.zip`),

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
