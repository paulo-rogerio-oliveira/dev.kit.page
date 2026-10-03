import type {
  EventoDoLog, Filtro, LoginResponse, MaquinaResumo, Pagina, QualidadeResposta, QuantidadeResposta,
} from './tipos';

/** A raiz da API, por variável de ambiente (VITE_API_URL); vazia é a mesma origem. */
export const baseDaApi = (import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '');

/** Uma resposta de erro da API, com o status e a mensagem do ProblemDetails. */
export class ErroDaApi extends Error {
  readonly status: number;

  constructor(status: number, mensagem: string) {
    super(mensagem);
    this.status = status;
  }
}

async function chamar<T>(caminho: string, opcoes: { metodo?: string; corpo?: unknown; token?: string } = {}): Promise<T> {
  const cabecalhos: Record<string, string> = { Accept: 'application/json' };
  if (opcoes.corpo !== undefined) cabecalhos['Content-Type'] = 'application/json';
  if (opcoes.token) cabecalhos.Authorization = `Bearer ${opcoes.token}`;

  const resposta = await fetch(`${baseDaApi}${caminho}`, {
    method: opcoes.metodo ?? 'GET',
    headers: cabecalhos,
    body: opcoes.corpo === undefined ? undefined : JSON.stringify(opcoes.corpo),
  });

  if (!resposta.ok) {
    let mensagem = `A API respondeu ${resposta.status}.`;
    try {
      const problema = (await resposta.json()) as { detail?: string; title?: string };
      mensagem = problema.detail ?? problema.title ?? mensagem;
    } catch {
      // Sem corpo (o 401 do JWT): fica a mensagem padrão.
    }
    throw new ErroDaApi(resposta.status, mensagem);
  }

  return (await resposta.json()) as T;
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
};
