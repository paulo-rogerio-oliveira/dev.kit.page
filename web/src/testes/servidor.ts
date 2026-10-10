import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import type {
  ColaboradorResumo, DemonstracaoResumo, EmpresaResumo, GrupoDeErroDetalhe, GrupoDeErroResumo, LoginResponse, MaquinaResumo, Pagina,
  EventoDoLog, PedidoDeDemonstracao, QualidadeResposta, QuantidadeResposta, RoiResposta, UsuarioEditado, UsuarioNovo, UsuarioResumo, VersaoDoDevKit,
} from '../api/tipos';

/** A raiz da API nos testes (o VITE_API_URL do vite.config.ts). */
export const API = 'http://api.test';

export const tokenValido = (deveTrocarSenha = false): LoginResponse => ({
  token: deveTrocarSenha ? 'token-troca' : 'token-ok',
  expiraEm: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
  deveTrocarSenha,
  login: 'admin',
  ehAdmin: true,
  papel: 'admin',
  empresa: null,
});

/** O gestor da Empresa A (US #381): vê só a empresa dele. */
export const tokenDoGestor = (): LoginResponse => ({
  ...tokenValido(), token: 'token-gestor', login: 'gestor.a', ehAdmin: false, papel: 'gestor', empresa: 'Empresa A',
});

export const colaboradores: ColaboradorResumo[] = [
  { maquinaId: 1, colaborador: 'Ana Souza', apelido: 'máquina a1b2c3d4', empresa: 'Empresa A', versaoDevKit: '1.4.0', consentiuEm: '2026-10-01T10:00:00Z', ultimoEnvioEm: '2026-10-03T10:00:00Z' },
  { maquinaId: 3, colaborador: '', apelido: 'máquina k1l2m3n4', empresa: 'Empresa A', versaoDevKit: '1.4.0', consentiuEm: '2026-10-02T10:00:00Z', ultimoEnvioEm: null },
];

export const empresa: EmpresaResumo = {
  id: 4, nome: 'Empresa A', plano: 'Empresarial', assentos: 10, codigoDeAdesao: 'DK-7QH4-M2XA', colaboradores: 2, criadaEm: '2026-10-01T10:00:00Z',
};

/** As empresas criadas e os gestores convidados pelo admin no servidor de mentira. */
export const cadastros: { empresas: unknown[]; gestores: { empresa: string; login: string }[] } = { empresas: [], gestores: [] };

export const maquinas: MaquinaResumo[] = [
  { id: 1, maquinaId: 'a1b2c3d4e5', apelido: 'máquina a1b2c3d4', versaoDevKit: '1.4.0', registradaEm: '2026-10-01T10:00:00Z', ultimoEnvioEm: '2026-10-03T10:00:00Z', eventos: 12 },
  { id: 2, maquinaId: 'f6g7h8i9j0', apelido: 'máquina f6g7h8i9', versaoDevKit: '1.4.0', registradaEm: '2026-10-01T10:00:00Z', ultimoEnvioEm: null, eventos: 0 },
];

export const quantidade: QuantidadeResposta = {
  de: '2026-09-04', ate: '2026-10-03', sessoes: 3, turnos: 8, fluxos: 2, ferramentas: 41, comandosDelegados: 5, arquivosAlterados: 17,
  maquinasAtivas: 1, maquinasRegistradas: 2,
  serieDiaria: [
    { dia: '2026-10-02', sessoes: 1, turnos: 3, fluxos: 1, ferramentas: 20, comandosDelegados: 2, arquivosAlterados: 7 },
    { dia: '2026-10-03', sessoes: 2, turnos: 5, fluxos: 1, ferramentas: 21, comandosDelegados: 3, arquivosAlterados: 10 },
  ],
};

export const qualidade: QualidadeResposta = {
  de: '2026-09-04', ate: '2026-10-03', turnos: 8, turnosComFalha: 2, taxaDeFalha: 0.25, duracaoMediaDoTurnoMs: 42500,
  falhasPorCausa: [{ causa: 'limite-de-uso', quantidade: 2 }], avaliacoes: 2, notaMedia: 91.5,
  objetivosCumpridos: 1, objetivosRecusados: 1, razaoCumpridosRecusados: 1, turnosPorObjetivoCumprido: 8,
  // US #405: os cartões Impasses e Árbitro.
  impasses: {
    detectados: 3, porOrigem: [{ recorte: 'mensagem-parada', quantidade: 2 }, { recorte: 'objetivo-parado', quantidade: 1 }],
    minutosParadoNaDeteccao: 32.5, destravados: 2, minutosAteDestravar: 12,
    comoDestravaram: [{ recorte: 'arbitro-reagiu', quantidade: 1 }, { recorte: 'dev-falou', quantidade: 1 }],
  },
  arbitro: {
    acoes: 7, cobrancas: 4, cobrancasPorRegra: [{ recorte: 'Arquivos alterados no turno', quantidade: 3 }, { recorte: '', quantidade: 1 }],
    corrigidas: 3, taxaDeCorrecao: 0.75, escaladasAoDev: 1,
    porAcao: [{ recorte: 'cobrou', quantidade: 4 }, { recorte: 'corrigido', quantidade: 3 }],
  },
};

/** A qualidade de um período sem turno, nota nem objetivo: todo divisor é zero. */
export const qualidadeVazia: QualidadeResposta = {
  ...qualidade, turnos: 0, turnosComFalha: 0, taxaDeFalha: null, duracaoMediaDoTurnoMs: null, falhasPorCausa: [],
  avaliacoes: 0, notaMedia: null, objetivosCumpridos: 0, objetivosRecusados: 0, razaoCumpridosRecusados: null, turnosPorObjetivoCumprido: null,
  impasses: { detectados: 0, porOrigem: [], minutosParadoNaDeteccao: null, destravados: 0, minutosAteDestravar: null, comoDestravaram: [] },
  arbitro: { acoes: 0, cobrancas: 0, cobrancasPorRegra: [], corrigidas: 0, taxaDeCorrecao: null, escaladasAoDev: 0, porAcao: [] },
};

/** A última versão do dev.kit (US #405), como a API a devolve da GitHub Release. */
export const versao: VersaoDoDevKit = {
  versao: 136, tag: 'v136', nome: 'dev.kit 136', publicadaEm: '2026-10-09T18:00:00Z', destaques: ['"Precisa de você" no Board'],
  tamanhoBytes: 52_428_800, sha256: 'ab'.repeat(32), urlDownload: '/api/versoes/136/download', loginObrigatorio: false,
};

/** O dev da gestão de usuários (US #405): entra no app, mas não no painel. */
export const tokenDoDev = (): LoginResponse => ({
  ...tokenValido(), token: 'token-dev', login: 'ana.dev', ehAdmin: false, papel: 'dev', empresa: null,
});

/** Os usuários da lista do admin (US #405). */
export const usuarios: UsuarioResumo[] = [
  { id: 1, login: 'admin', nome: '', papel: 'admin', empresaId: null, empresa: null, bloqueado: false, bloqueadoAte: null, deveTrocarSenha: false, criadoEm: '2026-10-01T10:00:00Z' },
  { id: 2, login: 'ana.dev', nome: 'Ana Souza', papel: 'dev', empresaId: 4, empresa: 'Empresa A', bloqueado: false, bloqueadoAte: null, deveTrocarSenha: true, criadoEm: '2026-10-02T10:00:00Z' },
];

/** O que chegou às rotas de escrita da gestão de usuários (o corpo, ou a ação). */
export const gestaoDeUsuarios: { metodo: string; caminho: string; corpo: unknown }[] = [];

/** Os downloads do zip que chegaram ao servidor de mentira, com o Authorization de cada um. */
export const downloads: (string | null)[] = [];

export const eventos: Pagina<EventoDoLog> = {
  itens: [{ eventId: 'e1', maquinaId: 1, apelido: 'máquina a1b2c3d4', tipo: 'TurnoExecutado', sessaoId: 's1', quantidade: 1, valor: 42500, detalhe: 'claude', em: '2026-10-03T10:00:00Z' }],
  total: 1, numeroDaPagina: 1, tamanho: 20,
};

export const demonstracao: DemonstracaoResumo = {
  id: 7, nome: 'Ana Souza', email: 'ana@empresa.com.br', empresa: 'Empresa X', mensagem: 'Quero ver o fluxo com avaliadores.',
  recebidoEm: '2026-10-03T10:00:00Z', consentimentoEm: '2026-10-03T10:00:00Z',
};

export const demonstracoes: Pagina<DemonstracaoResumo> = { itens: [demonstracao], total: 1, numeroDaPagina: 1, tamanho: 10 };

/** Um grupo de exceção não classificada (US #381). */
export const grupoDeErro: GrupoDeErroResumo = {
  id: 5, assinatura: 'abc123', tipo: 'System.InvalidOperationException', estado: 'Novo', resolvidoNaVersao: null,
  ocorrencias: 3, maquinas: 2, primeiraVersao: '1.4.0', ultimaVersao: '1.4.2',
  primeiroVistoEm: '2026-10-01T10:00:00Z', ultimoVistoEm: '2026-10-03T10:00:00Z',
};

export const erros: Pagina<GrupoDeErroResumo> = { itens: [grupoDeErro], total: 1, numeroDaPagina: 1, tamanho: 10 };

/** O detalhe do grupo — o trace como o dev.kit o manda: sem caminhos. */
export const erroDetalhe: GrupoDeErroDetalhe = {
  grupo: grupoDeErro,
  trace: 'System.InvalidOperationException: Sequence contains no elements\n   at GitKit.Core.Services.Planejador.Escolher() linha 42',
  ocorrencias: [{ eventId: 'x1', apelido: 'máquina a1b2c3d4', versaoDevKit: '1.4.2', trace: '…', em: '2026-10-03T10:00:00Z' }],
  porDia: [{ dia: '2026-10-01', quantidade: 1 }, { dia: '2026-10-03', quantidade: 2 }],
  versoes: ['1.4.0', '1.4.2'],
  maquinas: ['máquina a1b2c3d4', 'máquina f6g7h8i9'],
};

/** O ROI por work item (US #387): uma US encerrada e um Bug aberto (sem turno: horas/turno nulo). */
export const roi: RoiResposta = {
  de: '2026-09-04', ate: '2026-10-03',
  itens: [
    {
      maquinaId: 1, apelido: 'máquina a1b2c3d4', colaborador: 'Ana Souza', workItem: 387, tipo: 'User Story', estado: 'Closed',
      de: '2026-10-01', ate: '2026-10-04', turnosDoAgente: 42, sessoes: 3, horas: 31.5, horasNoBoard: 30, horasNoTimesheet: 31.5,
      horasPorTurno: 0.75, leadTimeDias: 3.5, aberto: false, pullRequests: 2, pullRequestsMergeadas: 1, atualizadoEm: '2026-10-03T13:01:07Z',
    },
    {
      maquinaId: 2, apelido: 'máquina f6g7h8i9', colaborador: '', workItem: 401, tipo: 'Bug', estado: 'Active',
      de: '2026-10-02', ate: '2026-10-03', turnosDoAgente: 0, sessoes: 1, horas: 2, horasNoBoard: 2, horasNoTimesheet: null,
      horasPorTurno: null, leadTimeDias: null, aberto: true, pullRequests: 0, pullRequestsMergeadas: 0, atualizadoEm: '2026-10-02T10:00:00Z',
    },
  ],
  totais: { itens: 2, turnos: 42, horas: 33.5, leadTimeMedioDias: 3.5, pullRequests: 2, pullRequestsMergeadas: 1 },
};

/** As reações (PUT de estado) que chegaram ao servidor de mentira. */
export const reacoes: { id: string; estado: string; versao: string | null }[] = [];

/** Os pedidos de demonstração que chegaram ao POST público. */
export const pedidosRecebidos: PedidoDeDemonstracao[] = [];

/** As requisições que chegaram ao servidor de mentira (para conferir os filtros). */
export const requisicoes: URL[] = [];

export const handlersPadrao = [
  http.post(`${API}/api/auth/login`, async ({ request }) => {
    const { login, senha } = (await request.json()) as { login: string; senha: string };
    if (login === 'admin' && senha === 'certa') return HttpResponse.json(tokenValido());
    if (login === 'admin' && senha === 'inicial') return HttpResponse.json(tokenValido(true));
    if (login === 'gestor.a' && senha === 'certa') return HttpResponse.json(tokenDoGestor());
    if (login === 'ana.dev' && senha === 'certa') return HttpResponse.json(tokenDoDev());
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
  http.get(`${API}/api/dashboard/erros`, () => HttpResponse.json(erros)),
  http.get(`${API}/api/dashboard/erros/:id`, () => HttpResponse.json(erroDetalhe)),
  http.put(`${API}/api/dashboard/erros/:id/estado`, async ({ params, request }) => {
    const corpo = (await request.json()) as { estado: string; versao: string | null };
    reacoes.push({ id: String(params.id), ...corpo });
    return new HttpResponse(null, { status: 204 });
  }),
  http.get(`${API}/api/dashboard/erros/:id/exportar`, () => new HttpResponse(JSON.stringify(erroDetalhe), {
    headers: { 'Content-Type': 'application/json', 'Content-Disposition': 'attachment; filename=excecao-abc123.json; filename*=UTF-8\'\'excecao-abc123.json' },
  })),
  http.get(`${API}/api/dashboard/roi`, ({ request }) => {
    requisicoes.push(new URL(request.url));
    return HttpResponse.json(roi);
  }),
  http.get(`${API}/api/dashboard/colaboradores`, () => HttpResponse.json(colaboradores)),
  http.get(`${API}/api/dashboard/exportar`, ({ request }) => {
    requisicoes.push(new URL(request.url));
    return new HttpResponse('colaborador;maquina\r\nAna Souza;máquina a1b2c3d4\r\n', {
      headers: { 'Content-Type': 'text/csv', 'Content-Disposition': 'attachment; filename=uso-dos-colaboradores-20260904-20261003.csv' },
    });
  }),
  http.get(`${API}/api/empresas`, () => HttpResponse.json([empresa])),
  http.post(`${API}/api/empresas`, async ({ request }) => {
    const corpo = await request.json();
    cadastros.empresas.push(corpo);
    return HttpResponse.json({ ...empresa, id: 9, ...(corpo as object), codigoDeAdesao: 'DK-NOVA-0001', colaboradores: 0 }, { status: 201 });
  }),
  http.post(`${API}/api/empresas/:id/gestores`, async ({ params, request }) => {
    const { login } = (await request.json()) as { login: string };
    cadastros.gestores.push({ empresa: String(params.id), login });
    return HttpResponse.json({ id: 12, login, senhaInicial: 'DkSENHA1234a1' }, { status: 201 });
  }),
  // US #405: a versão do dev.kit e o download autenticado.
  http.get(`${API}/api/versoes/ultima`, () => HttpResponse.json(versao)),
  http.get(`${API}/api/versoes/:versao/download`, ({ request }) => {
    const autorizacao = request.headers.get('Authorization');
    downloads.push(autorizacao);
    if (!autorizacao) return new HttpResponse(null, { status: 401 });
    return new HttpResponse(new Uint8Array([80, 75, 3, 4]), {
      headers: { 'Content-Type': 'application/zip', 'Content-Disposition': 'attachment; filename=devkit-136.zip' },
    });
  }),
  // US #405: a gestão de usuários.
  http.get(`${API}/api/usuarios`, () => HttpResponse.json(usuarios)),
  http.post(`${API}/api/usuarios`, async ({ request }) => {
    const corpo = (await request.json()) as UsuarioNovo;
    gestaoDeUsuarios.push({ metodo: 'POST', caminho: '/api/usuarios', corpo });
    const criado: UsuarioResumo = { ...usuarios[1], id: 3, ...corpo, empresa: corpo.empresaId ? 'Empresa A' : null, deveTrocarSenha: true };
    return HttpResponse.json({ usuario: criado, senhaTemporaria: 'DkTEMPORARIA99a1' }, { status: 201 });
  }),
  http.put(`${API}/api/usuarios/:id`, async ({ params, request }) => {
    const corpo = (await request.json()) as UsuarioEditado;
    gestaoDeUsuarios.push({ metodo: 'PUT', caminho: `/api/usuarios/${params.id}`, corpo });
    return HttpResponse.json({ ...usuarios.find((u) => String(u.id) === params.id)!, ...corpo });
  }),
  http.put(`${API}/api/usuarios/:id/bloqueio`, async ({ params, request }) => {
    const corpo = (await request.json()) as { bloqueado: boolean };
    gestaoDeUsuarios.push({ metodo: 'PUT', caminho: `/api/usuarios/${params.id}/bloqueio`, corpo });
    return HttpResponse.json({ ...usuarios.find((u) => String(u.id) === params.id)!, bloqueado: corpo.bloqueado });
  }),
  http.post(`${API}/api/usuarios/:id/senha`, ({ params }) => {
    gestaoDeUsuarios.push({ metodo: 'POST', caminho: `/api/usuarios/${params.id}/senha`, corpo: null });
    return HttpResponse.json({ usuario: { ...usuarios.find((u) => String(u.id) === params.id)!, deveTrocarSenha: true }, senhaTemporaria: 'DkREDEFINIDA77a1' });
  }),
];

export const servidor = setupServer(...handlersPadrao);
