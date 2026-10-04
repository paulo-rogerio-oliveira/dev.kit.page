import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import type {
  DemonstracaoResumo, LoginResponse, MaquinaResumo, Pagina, EventoDoLog, PedidoDeDemonstracao, QualidadeResposta, QuantidadeResposta,
} from '../api/tipos';

/** A raiz da API nos testes (o VITE_API_URL do vite.config.ts). */
export const API = 'http://api.test';

export const tokenValido = (deveTrocarSenha = false): LoginResponse => ({
  token: deveTrocarSenha ? 'token-troca' : 'token-ok',
  expiraEm: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
  deveTrocarSenha,
  login: 'admin',
  ehAdmin: true,
});

export const maquinas: MaquinaResumo[] = [
  { id: 1, maquinaId: 'a1b2c3d4e5', apelido: 'máquina a1b2c3d4', versaoDevKit: '1.4.0', registradaEm: '2026-10-01T10:00:00Z', ultimoEnvioEm: '2026-10-03T10:00:00Z', eventos: 12 },
  { id: 2, maquinaId: 'f6g7h8i9j0', apelido: 'máquina f6g7h8i9', versaoDevKit: '1.4.0', registradaEm: '2026-10-01T10:00:00Z', ultimoEnvioEm: null, eventos: 0 },
];

export const quantidade: QuantidadeResposta = {
  de: '2026-09-04', ate: '2026-10-03', sessoes: 3, turnos: 8, fluxos: 2, ferramentas: 41, comandosDelegados: 5, arquivosAlterados: 17,
  serieDiaria: [
    { dia: '2026-10-02', sessoes: 1, turnos: 3, fluxos: 1, ferramentas: 20, comandosDelegados: 2, arquivosAlterados: 7 },
    { dia: '2026-10-03', sessoes: 2, turnos: 5, fluxos: 1, ferramentas: 21, comandosDelegados: 3, arquivosAlterados: 10 },
  ],
};

export const qualidade: QualidadeResposta = {
  de: '2026-09-04', ate: '2026-10-03', turnos: 8, turnosComFalha: 2, taxaDeFalha: 0.25, duracaoMediaDoTurnoMs: 42500,
  falhasPorCausa: [{ causa: 'limite-de-uso', quantidade: 2 }], avaliacoes: 2, notaMedia: 91.5,
  objetivosCumpridos: 1, objetivosRecusados: 1, razaoCumpridosRecusados: 1, turnosPorObjetivoCumprido: 8,
};

/** A qualidade de um período sem turno, nota nem objetivo: todo divisor é zero. */
export const qualidadeVazia: QualidadeResposta = {
  ...qualidade, turnos: 0, turnosComFalha: 0, taxaDeFalha: null, duracaoMediaDoTurnoMs: null, falhasPorCausa: [],
  avaliacoes: 0, notaMedia: null, objetivosCumpridos: 0, objetivosRecusados: 0, razaoCumpridosRecusados: null, turnosPorObjetivoCumprido: null,
};

export const eventos: Pagina<EventoDoLog> = {
  itens: [{ eventId: 'e1', maquinaId: 1, apelido: 'máquina a1b2c3d4', tipo: 'TurnoExecutado', sessaoId: 's1', quantidade: 1, valor: 42500, detalhe: 'claude', em: '2026-10-03T10:00:00Z' }],
  total: 1, numeroDaPagina: 1, tamanho: 20,
};

export const demonstracao: DemonstracaoResumo = {
  id: 7, nome: 'Ana Souza', email: 'ana@empresa.com.br', empresa: 'Empresa X', mensagem: 'Quero ver o fluxo com avaliadores.',
  recebidoEm: '2026-10-03T10:00:00Z', consentimentoEm: '2026-10-03T10:00:00Z',
};

export const demonstracoes: Pagina<DemonstracaoResumo> = { itens: [demonstracao], total: 1, numeroDaPagina: 1, tamanho: 10 };

/** Os pedidos de demonstração que chegaram ao POST público. */
export const pedidosRecebidos: PedidoDeDemonstracao[] = [];

/** As requisições que chegaram ao servidor de mentira (para conferir os filtros). */
export const requisicoes: URL[] = [];

export const handlersPadrao = [
  http.post(`${API}/api/auth/login`, async ({ request }) => {
    const { login, senha } = (await request.json()) as { login: string; senha: string };
    if (login === 'admin' && senha === 'certa') return HttpResponse.json(tokenValido());
    if (login === 'admin' && senha === 'inicial') return HttpResponse.json(tokenValido(true));
    return HttpResponse.json({ detail: 'Login ou senha inválidos.' }, { status: 401 });
  }),
  http.post(`${API}/api/auth/trocar-senha`, () => HttpResponse.json(tokenValido())),
  http.get(`${API}/api/dashboard/maquinas`, () => HttpResponse.json(maquinas)),
  http.get(`${API}/api/dashboard/quantidade`, ({ request }) => {
    requisicoes.push(new URL(request.url));
    return HttpResponse.json(quantidade);
  }),
  http.get(`${API}/api/dashboard/qualidade`, () => HttpResponse.json(qualidade)),
  http.get(`${API}/api/dashboard/eventos`, () => HttpResponse.json(eventos)),
  http.post(`${API}/api/demonstracoes`, async ({ request }) => {
    pedidosRecebidos.push((await request.json()) as PedidoDeDemonstracao);
    return HttpResponse.json({ id: 8, recebidoEm: '2026-10-03T10:00:00Z' }, { status: 201 });
  }),
  http.get(`${API}/api/dashboard/demonstracoes`, () => HttpResponse.json(demonstracoes)),
  http.delete(`${API}/api/dashboard/demonstracoes/:id`, () => new HttpResponse(null, { status: 204 })),
];

export const servidor = setupServer(...handlersPadrao);
