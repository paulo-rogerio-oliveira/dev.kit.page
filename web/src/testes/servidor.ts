import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import type {
  CatalogoNomeado, ColaboradorResumo, Conta, ConvencaoDaEmpresa, DemonstracaoResumo, EmpresaDoGestor, EmpresaResumo, GrupoDeErroDetalhe, GrupoDeErroResumo,
  ItemDaEmpresa, ItemEditavel, LoginResponse, MaquinaResumo, Pagina, EventoDoLog, PedidoDeDemonstracao, QualidadeResposta, QuantidadeResposta,
  EstadoDoRelato, RelatoDetalhe, RelatoResumo, RelatorioDePublicacao, ReleaseDoCatalogo, RepositorioDaEmpresa, RoiResposta, UsuarioDaEmpresa, UsuarioEditado, UsuarioNovo, UsuarioResumo, VersaoDoDevKit,
  ProjetoDeDesign, UsoDoDesign, VersaoDeDesign, Vitrine,
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
  gestor: null, assentosEmUso: 3,
};

/** A segunda empresa (US #461): já tem gestor e o único assento ocupado. */
export const outraEmpresa: EmpresaResumo = {
  id: 5, nome: 'Empresa B', plano: 'Individual', assentos: 1, codigoDeAdesao: 'DK-B0B0-0001', colaboradores: 1, criadaEm: '2026-10-05T10:00:00Z',
  gestor: 'gestor.b', assentosEmUso: 1,
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

// ----- US #425: o catálogo, a conta e a compra -----

/** A vitrine do catálogo básico. */
export const vitrine: Vitrine = {
  versao: 2, publicadaEm: '2026-10-08T12:00:00Z',
  grupos: [
    { tecnologia: 'Angular', itens: [{ tipo: 'skill', nome: 'compilar-angular', descricao: 'Compila o Angular', tecnologia: 'Angular', versao: 1 }] },
    { tecnologia: 'Git', itens: [
      { tipo: 'skill', nome: 'revisar-alteracoes', descricao: 'Revisa antes do commit', tecnologia: 'Git', versao: 2 },
      { tipo: 'fluxo', nome: 'tarefa-avulsa', descricao: 'Uma tarefa sem work item', tecnologia: 'Git', versao: 1 },
    ] },
  ],
};

/** As releases do catálogo básico (a aba Releases do admin). */
export const releasesDoCatalogo: ReleaseDoCatalogo[] = [
  { versao: 2, tag: 'catalogo-v2', nome: 'Catálogo 2', publicadaEm: '2026-10-08T12:00:00Z', itens: 4, retirados: 1, tamanhoBytes: 3_145_728, sha256: 'b'.repeat(64) },
  { versao: 1, tag: 'catalogo-v1', nome: 'Catálogo 1', publicadaEm: '2026-10-02T12:00:00Z', itens: 1, retirados: 0, tamanhoBytes: 1_048_576, sha256: null },
];

// ----- US #461: os catálogos nomeados -----

/** O básico (vale para todas as empresas) e um catálogo do admin, relacionado à Empresa A e ainda sem release. */
export const catalogoBasico: CatalogoNomeado = {
  id: 1, slug: 'basico', nome: 'Catálogo básico', descricao: 'O que vem com o dev.kit.', basico: true, versao: 2,
  publicadaEm: '2026-10-08T12:00:00Z', itens: 4, pendentes: 0, empresas: [], criadoEm: '2026-10-01T10:00:00Z',
};

export const catalogoUau: CatalogoNomeado = {
  id: 2, slug: 'equipe-uau', nome: 'Equipe UAU', descricao: 'As skills do time do UAU.', basico: false, versao: 0, publicadaEm: null,
  itens: 2, pendentes: 2, empresas: [{ id: 4, nome: 'Empresa A', relacionadaEm: '2026-10-09T10:00:00Z' }], criadoEm: '2026-10-09T09:00:00Z',
};

/** Os itens do catálogo Equipe UAU: a skill e o fluxo que a usa, os dois nunca publicados. */
export const itensDoCatalogo: ItemEditavel[] = [
  {
    id: 21, tipo: 'skill', nome: 'compilar-uau', descricao: 'Compila o UAU', categoria: 'UAU', hash: 'h1', versaoPublicada: 0, situacao: 'novo',
    alteradoEm: '2026-10-09T11:00:00Z', alteradoPor: 'admin', dependencias: [], conteudo: null,
  },
  {
    id: 22, tipo: 'fluxo', nome: 'entrega-uau', descricao: 'A entrega do UAU', categoria: 'UAU', hash: 'h2', versaoPublicada: 0, situacao: 'novo',
    alteradoEm: '2026-10-09T11:30:00Z', alteradoPor: 'admin', dependencias: ['skill:compilar-uau'], conteudo: null,
  },
];

/** O conteúdo guardado da skill compilar-uau — o SkillJson do dev.kit. */
export const conteudoDaSkill = JSON.stringify({
  name: 'compilar-uau', description: 'Compila o UAU', content: '# Compilar\nRode o build do UAU.', arguments: ['branch'], repositories: [],
  isCommand: true, isBackground: false, isPinned: false,
}, null, 2);

/** O envelope devkit-fluxo do entrega-uau: a task e a etapa que roda a skill. */
export const fluxoDeExemplo = {
  formato: 'devkit-fluxo', versao: 1, nome: 'entrega uau', descricao: 'Compila e entrega o UAU.',
  desenho: {
    nodes: [
      { id: 'task', kind: 'Task', title: 'Task', skillName: '' },
      { id: 'e1', kind: 'Step', title: 'compilar-uau', skillName: 'compilar-uau' },
    ],
    links: [{ from: 'task', fromPort: 'next', to: 'e1', kind: 'Sequence' }],
  },
  agentes: [],
  planos: [],
};

/** A conferência da publicação do Equipe UAU: pode publicar a v1. */
export const relatorioDePublicacao: RelatorioDePublicacao = {
  slug: 'equipe-uau', versaoAtual: 0, proxima: 1, podePublicar: true, minDevKit: 136,
  mudancas: ['+ skill:compilar-uau v1', '+ fluxo:entrega-uau v1'], problemas: [],
};

/** O manifesto de uma release (o formato do dev.kit, resumido). */
export const manifestoDaRelease = { catalogo: 2, minDevKit: 130, itens: [{ tipo: 'skill', nome: 'revisar-alteracoes', versao: 2, catalogo: 2 }] };

/** A release que a publicação do catálogo grava (a primeira dele). */
const releaseNova = (slug: string): ReleaseDoCatalogo => ({
  versao: 1, tag: `${slug}-v1`, nome: `${slug} 1`, publicadaEm: '2026-10-13T10:00:00Z', itens: 2, retirados: 0, tamanhoBytes: 104_857, sha256: 'c'.repeat(64),
});

/** O catálogo da Empresa A: uma skill publicada e um fluxo retirado. */
export const itensDaEmpresa: ItemDaEmpresa[] = [
  {
    id: 11, tipo: 'skill', nome: 'compilar-uau', descricao: 'Compila o UAU', versao: 3, hash: 'h3', autor: 'Ana Souza', assinantes: 4, assinado: false,
    publicadoEm: '2026-10-09T10:00:00Z', retirado: null, dependencias: [],
  },
  {
    id: 12, tipo: 'fluxo', nome: 'entrega-uau', descricao: 'A entrega do UAU', versao: 1, hash: 'f1', autor: 'Bia', assinantes: 0, assinado: false,
    publicadoEm: '2026-10-05T10:00:00Z', retirado: '2026-10-06T10:00:00Z', dependencias: ['skill:compilar-uau'],
  },
];

/** A conta da Ana (dev da Empresa A). */
export const conta: Conta = {
  login: 'ana.dev', nome: 'Ana Souza', email: 'ana@empresa.com', papel: 'dev', empresa: 'Empresa A', individual: false,
  assinaturaAtiva: true, urlGerenciarAssinatura: null, loginObrigatorio: true,
};

/** A conta do Rui (a compra individual). */
export const contaIndividual: Conta = {
  ...conta, login: 'rui@solo.com', nome: 'Rui', email: 'rui@solo.com', empresa: 'Rui', individual: true, urlGerenciarAssinatura: 'https://gateway.test/portal',
};

/** A empresa do gestor e os usuários dela. */
export const empresaDoGestor: EmpresaDoGestor = {
  id: 4, nome: 'Empresa A', plano: 'Empresarial', assentos: 10, assentosOcupados: 3, codigoDeAdesao: 'DK-7QH4-M2XA', loginObrigatorio: null, loginObrigatorioEfetivo: false,
};

export const usuariosDaEmpresa: UsuarioDaEmpresa[] = [
  { id: 2, login: 'ana@empresa.com', nome: 'Ana Souza', email: 'ana@empresa.com', papel: 'dev', bloqueado: false, criadoEm: '2026-10-02T10:00:00Z' },
  { id: 3, login: 'bia@empresa.com', nome: '', email: 'bia@empresa.com', papel: 'dev', bloqueado: true, criadoEm: '2026-10-03T10:00:00Z' },
  { id: 5, login: 'gestor.a', nome: '', email: '', papel: 'gestor', bloqueado: false, criadoEm: '2026-10-01T10:00:00Z' },
];

/** O que chegou às rotas de escrita da US #425 (o método, o caminho e o corpo). */
export const escritas: { metodo: string; caminho: string; corpo: unknown }[] = [];

/** O catálogo de repositórios da empresa (US #491). */
export const repositoriosDaEmpresa: RepositorioDaEmpresa[] = [
  { origem: 'https://github.com/acme/vendas-api', descricao: 'API de vendas (.NET 8)', autor: 'Ana Souza', atualizadoEm: '2026-10-14T10:00:00Z' },
];

/** As convenções em seus níveis, como a auditoria as devolve (US #491). */
export const convencoesDaEmpresa: ConvencaoDaEmpresa[] = [
  { nivel: 'empresa', chave: 'branch.feature', valor: 'feature/{equipe}/{id}', time: '', usuario: '', autor: 'Gestor A', atualizadoEm: '2026-10-14T09:00:00Z' },
  { nivel: 'time', chave: 'branch.feature', valor: 'vendas/{id}', time: 'vendas', usuario: '', autor: 'Ana Souza', atualizadoEm: '2026-10-14T09:30:00Z' },
  { nivel: 'usuario', chave: 'branch.bug', valor: 'ana/bug-{id}', time: '', usuario: 'Ana Souza', autor: 'Ana Souza', atualizadoEm: '2026-10-14T09:45:00Z' },
];

/** O contexto do dev.kit nos três níveis (US #550): as chaves contexto.<tema> das mesmas convenções. */
export const contextosDaEmpresa: ConvencaoDaEmpresa[] = [
  { nivel: 'empresa', chave: 'contexto.stack', valor: '.NET 10 na API e React 19 na web.', time: '', usuario: '', autor: 'Gestor A', atualizadoEm: '2026-10-16T09:00:00Z' },
  { nivel: 'time', chave: 'contexto.stack', valor: 'Angular 19 no front.\nTestes com Jasmine.', time: 'vendas', usuario: '', autor: 'Gestor A', atualizadoEm: '2026-10-16T09:10:00Z' },
  { nivel: 'usuario', chave: 'contexto.estilo', valor: 'Respostas curtas, com o porquê.', time: '', usuario: 'Ana Souza', autor: 'Ana Souza', atualizadoEm: '2026-10-16T09:20:00Z' },
];

/** As SUGESTÕES dos agentes (US #663), ao lado das regras: origem agente, com o id e a versão. */
export const sugestoesDaEmpresa: ConvencaoDaEmpresa[] = [
  { nivel: 'empresa', chave: 'contexto.testes', valor: 'Rode o lint antes do PR.', time: '', usuario: '', autor: 'Ana Souza', atualizadoEm: '2026-10-22T18:00:00Z', id: 101, origem: 'agente', versao: 1 },
  { nivel: 'time', chave: 'contexto.stack', valor: 'Angular 19 standalone.', time: 'vendas', usuario: '', autor: 'Ana Souza', atualizadoEm: '2026-10-22T18:10:00Z', id: 102, origem: 'agente', versao: 2 },
  { nivel: 'usuario', chave: 'contexto.revisao', valor: 'Prefere diffs pequenos.', time: '', usuario: 'Ana Souza', autor: 'Ana Souza', atualizadoEm: '2026-10-22T18:20:00Z', id: 103, origem: 'agente', versao: 1 },
];

// ----- US #669: o design compartilhado -----

const versaoDeDesign = (numero: number, tipo: 'captura' | 'proposta', extra: Partial<VersaoDeDesign> = {}): VersaoDeDesign => ({
  numero, tipo, base: null, tema: 'light', largura: 1440, altura: 900, origem: tipo === 'proposta' ? 'proposta' : 'web', alvo: 'http://localhost:5173/checkout',
  branch: 'feature/dev/semana5', commit: '3f9c2a1', descricao: '', autor: 'Ana Souza', autorTipo: 'agente', runner: 'claude',
  criadaEm: '2026-10-23T14:05:00Z', sha256: String(numero).repeat(64).slice(0, 64), bytes: 48_000,
  link: `/api/design/7/checkout/v${numero}`, podeApagar: false, ...extra,
});

/**
 * Os projetos de design que a API devolve (US #669): o portal da empresa (a tela Checkout com captura, proposta e
 * recaptura — a da Ana o gestor apaga — e o Login só com captura, de outro branch) e um projeto do nível usuário.
 */
export const projetosDeDesign: ProjetoDeDesign[] = [
  {
    id: 7, repositorio: 'https://github.com/acme/portal', chave: 'github.com/acme/portal', nivel: 'empresa', time: '', dono: '', atualizadoEm: '2026-10-23T16:40:00Z',
    telas: [
      {
        id: 'checkout', nome: 'Checkout', estado: '', versoes: [
          versaoDeDesign(1, 'captura', { branch: 'feature/dev/semana4', commit: '81c0e2d', criadaEm: '2026-10-21T10:12:00Z' }),
          versaoDeDesign(2, 'proposta', { base: 1, descricao: 'Botão de pagar em destaque.', podeApagar: true }),
          versaoDeDesign(3, 'captura', { base: 2, commit: '9d41b07', criadaEm: '2026-10-23T16:40:00Z', podeApagar: true }),
        ],
      },
      {
        id: 'login', nome: 'Login', estado: '', versoes: [
          versaoDeDesign(1, 'captura', { branch: 'feature/dev/semana3', link: '/api/design/7/login/v1' }),
        ],
      },
    ],
  },
  {
    id: 9, repositorio: 'https://github.com/acme/portal', chave: 'github.com/acme/portal', nivel: 'usuario', time: '', dono: 'Ana Souza', atualizadoEm: '2026-10-22T10:00:00Z',
    telas: [{ id: 'rascunho', nome: 'Rascunho', estado: '', versoes: [versaoDeDesign(1, 'proposta', { link: '/api/design/9/rascunho/v1', podeApagar: true })] }],
  },
];

export const usoDoDesign: UsoDoDesign = { versoes: 37, tetoDeVersoes: 500, bytes: 48 * 1_048_576, tetoDeBytes: 2048 * 1_048_576, plano: 'Empresarial' };

// ----- US #602: os relatos -----

/** O PNG de 1x1 em base64 (a captura do relato). */
export const CAPTURA_PNG = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';

/** O defeito do caso real (da Empresa A, com contexto e captura) e uma sugestão já respondida (da Empresa B). */
const relatosIniciais = (): RelatoDetalhe[] => [
  {
    id: 41, categoria: 'defeito', estado: 'novo', texto: 'A PR da task multirepo foi criada no repositório errado (dev.kit.page em vez de git.kit)',
    contexto: '{"versao":"137","task":"t_602","repositorios":["git.kit","dev.kit.page"]}', captura: CAPTURA_PNG, versaoDevKit: '137',
    autor: 'Ana Souza', empresaId: 4, empresa: 'Empresa A', criadoEm: '2026-10-18T10:00:00Z', atualizadoEm: '2026-10-18T10:00:00Z',
    resposta: '', respondidoPor: '', respondidoEm: null,
  },
  {
    id: 42, categoria: 'sugestao', estado: 'em-analise', texto: 'Mostrar o custo do turno no Board.', contexto: '', captura: null, versaoDevKit: '136',
    autor: 'bia@empresa-b.com', empresaId: 5, empresa: 'Empresa B', criadoEm: '2026-10-17T10:00:00Z', atualizadoEm: '2026-10-17T12:00:00Z',
    resposta: 'Entrou no planejamento.', respondidoPor: 'admin', respondidoEm: '2026-10-17T12:00:00Z',
  },
];

/** Os relatos do servidor de mentira: a troca de estado e a resposta os alteram (o setup os reinicia a cada teste). */
export const relatosNoServidor: RelatoDetalhe[] = relatosIniciais();

export function reiniciarRelatos() {
  relatosNoServidor.splice(0, relatosNoServidor.length, ...relatosIniciais());
}

const resumoDoRelato = (r: RelatoDetalhe): RelatoResumo => ({
  id: r.id, categoria: r.categoria, estado: r.estado, resumo: r.texto.slice(0, 120), autor: r.autor, empresaId: r.empresaId, empresa: r.empresa,
  criadoEm: r.criadoEm, atualizadoEm: r.atualizadoEm, temCaptura: r.captura !== null, respondido: r.resposta !== '',
});

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
  http.get(`${API}/api/dashboard/qualidade`, ({ request }) => {
    requisicoes.push(new URL(request.url));
    return HttpResponse.json(qualidade);
  }),
  http.get(`${API}/api/dashboard/eventos`, ({ request }) => {
    requisicoes.push(new URL(request.url));
    return HttpResponse.json(eventos);
  }),
  http.post(`${API}/api/demonstracoes`, async ({ request }) => {
    pedidosRecebidos.push((await request.json()) as PedidoDeDemonstracao);
    return HttpResponse.json({ id: 8, recebidoEm: '2026-10-03T10:00:00Z' }, { status: 201 });
  }),
  http.get(`${API}/api/dashboard/demonstracoes`, () => HttpResponse.json(demonstracoes)),
  http.delete(`${API}/api/dashboard/demonstracoes/:id`, () => new HttpResponse(null, { status: 204 })),
  http.get(`${API}/api/dashboard/erros`, ({ request }) => {
    requisicoes.push(new URL(request.url));
    return HttpResponse.json(erros);
  }),
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
  http.get(`${API}/api/empresas`, () => HttpResponse.json([empresa, outraEmpresa])),
  http.post(`${API}/api/empresas`, async ({ request }) => {
    const corpo = await request.json();
    cadastros.empresas.push(corpo);
    return HttpResponse.json({ ...empresa, id: 9, ...(corpo as object), codigoDeAdesao: 'DK-NOVA-0001', colaboradores: 0, assentosEmUso: 0 }, { status: 201 });
  }),
  http.post(`${API}/api/empresas/:id/gestores`, async ({ params, request }) => {
    const { login } = (await request.json()) as { login: string };
    cadastros.gestores.push({ empresa: String(params.id), login });
    return HttpResponse.json({ id: 12, login, senhaInicial: 'DkSENHA1234a1' }, { status: 201 });
  }),
  // US #405: a versão do dev.kit; o download é anônimo desde a US #425 (guarda o Authorization para o teste
  // conferir que nenhum foi mandado).
  http.get(`${API}/api/versoes/ultima`, () => HttpResponse.json(versao)),
  http.get(`${API}/api/versoes/:versao/download`, ({ request }) => {
    downloads.push(request.headers.get('Authorization'));
    return new HttpResponse(new Uint8Array([80, 75, 3, 4]), {
      headers: { 'Content-Type': 'application/zip', 'Content-Disposition': 'attachment; filename=devkit-136.zip' },
    });
  }),
  // US #425: o catálogo, a conta, a empresa do gestor e a compra.
  http.get(`${API}/api/catalogo/base/vitrine`, () => HttpResponse.json(vitrine)),
  // US #461: os catálogos nomeados (o admin), as releases de cada um e o /meus.
  http.get(`${API}/api/catalogos`, () => HttpResponse.json([catalogoBasico, catalogoUau])),
  http.get(`${API}/api/catalogos/meus`, () => HttpResponse.json([
    { slug: 'basico', nome: 'Catálogo básico', basico: true, versao: 2, publicadaEm: '2026-10-08T12:00:00Z' },
  ])),
  http.post(`${API}/api/catalogos`, async ({ request }) => {
    const corpo = (await request.json()) as { slug: string; nome: string; descricao: string };
    escritas.push({ metodo: 'POST', caminho: '/api/catalogos', corpo });
    return HttpResponse.json({ ...catalogoUau, id: 3, ...corpo, itens: 0, pendentes: 0, empresas: [] }, { status: 201 });
  }),
  http.post(`${API}/api/catalogos/basico/importar`, () => {
    escritas.push({ metodo: 'POST', caminho: '/api/catalogos/basico/importar', corpo: null });
    return HttpResponse.json({ versao: 2, itens: 58, retirados: 0, importada: true, mensagem: 'Catálogo básico importado da catalogo-v2: 58 itens.' });
  }),
  http.put(`${API}/api/catalogos/:slug`, async ({ params, request }) => {
    const corpo = await request.json();
    escritas.push({ metodo: 'PUT', caminho: `/api/catalogos/${params.slug}`, corpo });
    return HttpResponse.json({ ...catalogoUau, ...(corpo as object) });
  }),
  http.delete(`${API}/api/catalogos/:slug`, ({ params }) => {
    escritas.push({ metodo: 'DELETE', caminho: `/api/catalogos/${params.slug}`, corpo: null });
    return new HttpResponse(null, { status: 204 });
  }),
  http.post(`${API}/api/catalogos/:slug/empresas`, async ({ params, request }) => {
    const corpo = (await request.json()) as { empresaId: number };
    escritas.push({ metodo: 'POST', caminho: `/api/catalogos/${params.slug}/empresas`, corpo });
    return HttpResponse.json({ ...catalogoUau, empresas: [...catalogoUau.empresas, { id: corpo.empresaId, nome: 'Empresa B', relacionadaEm: '2026-10-13T10:00:00Z' }] });
  }),
  http.delete(`${API}/api/catalogos/:slug/empresas/:empresa`, ({ params }) => {
    escritas.push({ metodo: 'DELETE', caminho: `/api/catalogos/${params.slug}/empresas/${params.empresa}`, corpo: null });
    return HttpResponse.json({ ...catalogoUau, empresas: [] });
  }),
  http.get(`${API}/api/catalogos/:slug/itens`, ({ params }) => HttpResponse.json(params.slug === 'basico' ? [] : itensDoCatalogo)),
  http.get(`${API}/api/catalogos/:slug/itens/:id`, ({ params }) => {
    const item = itensDoCatalogo.find((i) => String(i.id) === params.id)!;
    return HttpResponse.json({ ...item, conteudo: item.tipo === 'skill' ? conteudoDaSkill : JSON.stringify(fluxoDeExemplo) });
  }),
  http.post(`${API}/api/catalogos/:slug/itens`, async ({ params, request }) => {
    const corpo = (await request.json()) as { tipo: string; conteudo: string };
    escritas.push({ metodo: 'POST', caminho: `/api/catalogos/${params.slug}/itens`, corpo });
    const lido = JSON.parse(corpo.conteudo) as { name?: string; nome?: string; key?: string };
    return HttpResponse.json({ ...itensDoCatalogo[0], id: 23, tipo: corpo.tipo, nome: lido.name ?? lido.key ?? lido.nome ?? '', conteudo: corpo.conteudo }, { status: 201 });
  }),
  http.put(`${API}/api/catalogos/:slug/itens/:id`, async ({ params, request }) => {
    const corpo = await request.json();
    escritas.push({ metodo: 'PUT', caminho: `/api/catalogos/${params.slug}/itens/${params.id}`, corpo });
    return HttpResponse.json({ ...itensDoCatalogo.find((i) => String(i.id) === params.id)!, situacao: 'alterado' });
  }),
  http.delete(`${API}/api/catalogos/:slug/itens/:id`, ({ params }) => {
    escritas.push({ metodo: 'DELETE', caminho: `/api/catalogos/${params.slug}/itens/${params.id}`, corpo: null });
    return new HttpResponse(null, { status: 204 });
  }),
  http.get(`${API}/api/catalogos/:slug/publicacao`, ({ request }) => {
    requisicoes.push(new URL(request.url));
    return HttpResponse.json(relatorioDePublicacao);
  }),
  http.post(`${API}/api/catalogos/:slug/publicar`, async ({ params, request }) => {
    const corpo = await request.json();
    escritas.push({ metodo: 'POST', caminho: `/api/catalogos/${params.slug}/publicar`, corpo });
    return HttpResponse.json({ release: releaseNova(String(params.slug)), relatorio: relatorioDePublicacao }, { status: 201 });
  }),
  // As releases: as do básico são as da US #425; um catálogo do admin só tem release depois de publicado.
  http.get(`${API}/api/catalogos/:slug/releases`, ({ params }) => HttpResponse.json(
    params.slug === 'basico' ? releasesDoCatalogo
      : escritas.some((e) => e.caminho === `/api/catalogos/${params.slug}/publicar`) ? [releaseNova(String(params.slug))] : [],
  )),
  http.get(`${API}/api/catalogos/:slug/releases/:versao/manifesto`, () => HttpResponse.json(manifestoDaRelease)),
  http.get(`${API}/api/catalogos/:slug/releases/:versao/download`, ({ params, request }) => {
    downloads.push(request.headers.get('Authorization'));
    return new HttpResponse(new Uint8Array([80, 75, 3, 4]), {
      headers: { 'Content-Type': 'application/zip', 'Content-Disposition': `attachment; filename=catalogo-v${params.versao}.zip` },
    });
  }),
  http.get(`${API}/api/catalogo/empresa`, () => HttpResponse.json(itensDaEmpresa)),
  http.delete(`${API}/api/catalogo/empresa/:id`, ({ params }) => {
    escritas.push({ metodo: 'DELETE', caminho: `/api/catalogo/empresa/${params.id}`, corpo: null });
    return HttpResponse.json({ ...itensDaEmpresa.find((i) => String(i.id) === params.id)!, retirado: '2026-10-10T10:00:00Z' });
  }),
  http.post(`${API}/api/conta/adesao`, async ({ request }) => {
    const corpo = (await request.json()) as { codigo: string; email: string };
    escritas.push({ metodo: 'POST', caminho: '/api/conta/adesao', corpo });
    if (corpo.email === 'ana@outra.com') return HttpResponse.json({ detail: 'Este e-mail já está cadastrado em outra empresa. Fale com o gestor dela.' }, { status: 409 });
    return HttpResponse.json({ mensagem: `Pronto: enviamos para ${corpo.email} o link para definir a senha (vale 24 horas).` }, { status: 202 });
  }),
  http.post(`${API}/api/conta/link`, async ({ request }) => {
    escritas.push({ metodo: 'POST', caminho: '/api/conta/link', corpo: await request.json() });
    return HttpResponse.json({ mensagem: 'Se o e-mail tiver uma conta, o link para definir a senha chega em instantes.' }, { status: 202 });
  }),
  http.post(`${API}/api/conta/definir-senha`, async ({ request }) => {
    const corpo = (await request.json()) as { token: string; novaSenha: string };
    escritas.push({ metodo: 'POST', caminho: '/api/conta/definir-senha', corpo });
    if (corpo.token === 'usado') return HttpResponse.json({ detail: 'Este link já foi usado. Peça um link novo.' }, { status: 400 });
    return new HttpResponse(null, { status: 204 });
  }),
  http.get(`${API}/api/conta`, () => HttpResponse.json(conta)),
  http.get(`${API}/api/empresa`, () => HttpResponse.json(empresaDoGestor)),
  http.get(`${API}/api/empresa/usuarios`, () => HttpResponse.json(usuariosDaEmpresa)),
  http.put(`${API}/api/empresa/usuarios/:id/bloqueio`, async ({ params, request }) => {
    const corpo = (await request.json()) as { bloqueado: boolean };
    escritas.push({ metodo: 'PUT', caminho: `/api/empresa/usuarios/${params.id}/bloqueio`, corpo });
    return HttpResponse.json({ ...usuariosDaEmpresa.find((u) => String(u.id) === params.id)!, bloqueado: corpo.bloqueado });
  }),
  http.post(`${API}/api/empresa/codigo`, () => {
    escritas.push({ metodo: 'POST', caminho: '/api/empresa/codigo', corpo: null });
    return HttpResponse.json({ codigoDeAdesao: 'DK-NOVO-0002' });
  }),
  http.put(`${API}/api/empresa/politica`, async ({ request }) => {
    const corpo = (await request.json()) as { loginObrigatorio: boolean | null };
    escritas.push({ metodo: 'PUT', caminho: '/api/empresa/politica', corpo });
    return HttpResponse.json({ ...empresaDoGestor, loginObrigatorio: corpo.loginObrigatorio, loginObrigatorioEfetivo: corpo.loginObrigatorio ?? false });
  }),
  // US #491: o catálogo de repositórios e as convenções da empresa.
  http.get(`${API}/api/empresa/repositorios`, () => HttpResponse.json(repositoriosDaEmpresa)),
  http.post(`${API}/api/empresa/repositorios`, async ({ request }) => {
    const corpo = (await request.json()) as { origem: string; descricao: string };
    escritas.push({ metodo: 'POST', caminho: '/api/empresa/repositorios', corpo });
    return HttpResponse.json({ ...corpo, autor: 'Gestor A', atualizadoEm: '2026-10-15T10:00:00Z' });
  }),
  http.delete(`${API}/api/empresa/repositorios`, ({ request }) => {
    const origem = new URL(request.url).searchParams.get('origem');
    escritas.push({ metodo: 'DELETE', caminho: `/api/empresa/repositorios?origem=${origem}`, corpo: null });
    return HttpResponse.json(repositoriosDaEmpresa.find((r) => r.origem === origem) ?? repositoriosDaEmpresa[0]);
  }),
  http.get(`${API}/api/empresa/convencoes/todas`, () => HttpResponse.json([...convencoesDaEmpresa, ...contextosDaEmpresa, ...sugestoesDaEmpresa])),
  http.put(`${API}/api/empresa/convencoes`, async ({ request }) => {
    const corpo = (await request.json()) as { nivel: string; chave: string; valor: string; time: string };
    escritas.push({ metodo: 'PUT', caminho: '/api/empresa/convencoes', corpo });
    return HttpResponse.json({ ...corpo, usuario: '', autor: 'Gestor A', atualizadoEm: '2026-10-15T10:00:00Z' });
  }),
  // US #550: o que o dev.kit lê sem time (a da empresa e a de quem consulta), as equipes conhecidas e as rotas do admin.
  // US #663: sem ?origem só as regras (o dev.kit antigo); com ?origem=todas, também as sugestões.
  http.get(`${API}/api/empresa/convencoes`, ({ request }) => {
    const todas = new URL(request.url).searchParams.get('origem') === 'todas';
    return HttpResponse.json([...convencoesDaEmpresa, ...contextosDaEmpresa, ...(todas ? sugestoesDaEmpresa : [])].filter((c) => c.nivel !== 'time'));
  }),
  // US #663: promover (a versão lida tem de ser a atual, senão 409) e descartar a sugestão do agente.
  http.post(`${API}/api/empresa/contexto/:id/promover`, async ({ params, request }) => {
    const corpo = (await request.json()) as { versaoEsperada: number | null };
    escritas.push({ metodo: 'POST', caminho: `/api/empresa/contexto/${params.id}/promover`, corpo });
    const sugestao = sugestoesDaEmpresa.find((s) => String(s.id) === params.id);
    if (!sugestao) return HttpResponse.json({ detail: 'A sugestão não existe mais (alguém a promoveu ou descartou).' }, { status: 404 });
    if (corpo.versaoEsperada !== null && corpo.versaoEsperada !== sugestao.versao)
      return HttpResponse.json({ detail: 'A sugestão mudou depois que você a leu (outro agente a reescreveu). Confira o texto novo e promova de novo.' }, { status: 409 });
    return HttpResponse.json({ ...sugestao, origem: 'usuario', autor: 'Gestor A' });
  }),
  http.delete(`${API}/api/empresa/contexto/:id`, ({ params }) => {
    escritas.push({ metodo: 'DELETE', caminho: `/api/empresa/contexto/${params.id}`, corpo: null });
    const sugestao = sugestoesDaEmpresa.find((s) => String(s.id) === params.id);
    return sugestao ? HttpResponse.json(sugestao) : HttpResponse.json({ detail: 'Só quem grava a regra desta camada, ou o autor da sugestão, a descarta.' }, { status: 403 });
  }),
  http.post(`${API}/api/empresas/:empresa/contexto/:id/promover`, async ({ params, request }) => {
    escritas.push({ metodo: 'POST', caminho: `/api/empresas/${params.empresa}/contexto/${params.id}/promover`, corpo: await request.json() });
    return HttpResponse.json({ ...sugestoesDaEmpresa[0], origem: 'usuario', autor: 'admin' });
  }),
  http.delete(`${API}/api/empresas/:empresa/contexto/:id`, ({ params }) => {
    escritas.push({ metodo: 'DELETE', caminho: `/api/empresas/${params.empresa}/contexto/${params.id}`, corpo: null });
    return HttpResponse.json(sugestoesDaEmpresa[0]);
  }),
  http.get(`${API}/api/empresa/equipes`, () => HttpResponse.json(['vendas'])),
  http.get(`${API}/api/empresas/:id/convencoes/todas`, ({ params }) => HttpResponse.json(params.id === '4' ? [...convencoesDaEmpresa, ...contextosDaEmpresa, ...sugestoesDaEmpresa] : [])),
  http.get(`${API}/api/empresas/:id/equipes`, ({ params }) => HttpResponse.json(params.id === '4' ? ['vendas'] : [])),
  http.put(`${API}/api/empresas/:id/convencoes`, async ({ params, request }) => {
    const corpo = (await request.json()) as { nivel: string; chave: string; valor: string; time: string; usuario: string };
    escritas.push({ metodo: 'PUT', caminho: `/api/empresas/${params.id}/convencoes`, corpo });
    return HttpResponse.json({ ...corpo, autor: 'admin', atualizadoEm: '2026-10-16T10:00:00Z' });
  }),
  http.post(`${API}/api/pagamentos/checkout`, async ({ request }) => {
    const corpo = (await request.json()) as { plano: string };
    escritas.push({ metodo: 'POST', caminho: '/api/pagamentos/checkout', corpo });
    return HttpResponse.json({ url: `/compra-concluida?simulado=1&plano=${corpo.plano}`, simulado: true });
  }),
  // US #602: os relatos — a lista filtra por estado e categoria como a API (o escopo do gestor é da API, não daqui).
  http.get(`${API}/api/relatos`, ({ request }) => {
    const url = new URL(request.url);
    requisicoes.push(url);
    const estado = url.searchParams.get('estado');
    const categoria = url.searchParams.get('categoria');
    const empresaId = url.searchParams.get('empresa');
    const itens = relatosNoServidor
      .filter((r) => (!estado || r.estado === estado) && (!categoria || r.categoria === categoria) && (!empresaId || String(r.empresaId) === empresaId))
      .map(resumoDoRelato);
    return HttpResponse.json({ itens, total: itens.length, numeroDaPagina: Number(url.searchParams.get('pagina') ?? 1), tamanho: Number(url.searchParams.get('tamanho') ?? 20) });
  }),
  http.get(`${API}/api/relatos/meus`, () => HttpResponse.json([])),
  http.get(`${API}/api/relatos/:id`, ({ params }) => {
    const relato = relatosNoServidor.find((r) => String(r.id) === params.id);
    return relato ? HttpResponse.json(relato) : HttpResponse.json({ detail: 'Relato não encontrado.' }, { status: 404 });
  }),
  http.patch(`${API}/api/relatos/:id/estado`, async ({ params, request }) => {
    const corpo = (await request.json()) as { estado: EstadoDoRelato };
    escritas.push({ metodo: 'PATCH', caminho: `/api/relatos/${params.id}/estado`, corpo });
    const relato = relatosNoServidor.find((r) => String(r.id) === params.id)!;
    relato.estado = corpo.estado;
    relato.atualizadoEm = '2026-10-18T11:00:00Z';
    return HttpResponse.json(relato);
  }),
  http.post(`${API}/api/relatos/:id/resposta`, async ({ params, request }) => {
    const corpo = (await request.json()) as { texto: string };
    escritas.push({ metodo: 'POST', caminho: `/api/relatos/${params.id}/resposta`, corpo });
    const relato = relatosNoServidor.find((r) => String(r.id) === params.id)!;
    Object.assign(relato, {
      resposta: corpo.texto, respondidoPor: 'admin', respondidoEm: '2026-10-18T11:30:00Z', atualizadoEm: '2026-10-18T11:30:00Z',
      estado: relato.estado === 'novo' ? 'em-analise' : relato.estado,
    });
    return HttpResponse.json(relato);
  }),
  // US #669: o design compartilhado — os projetos, o uso do teto, a imagem pelo link estável e o apagar.
  http.get(`${API}/api/design/projetos`, () => HttpResponse.json(projetosDeDesign)),
  http.get(`${API}/api/design/projetos/:id`, ({ params }) => {
    const projeto = projetosDeDesign.find((p) => String(p.id) === params.id);
    return projeto ? HttpResponse.json(projeto) : HttpResponse.json({ detail: 'Projeto de design não encontrado.' }, { status: 404 });
  }),
  http.get(`${API}/api/design/uso`, () => HttpResponse.json(usoDoDesign)),
  http.get(`${API}/api/design/:projeto/:tela/:versao`, () =>
    new HttpResponse(Uint8Array.from(atob(CAPTURA_PNG), (c) => c.charCodeAt(0)), { headers: { 'Content-Type': 'image/png' } })),
  http.delete(`${API}/api/design/:projeto/:tela/:versao`, ({ params }) => {
    escritas.push({ metodo: 'DELETE', caminho: `/api/design/${params.projeto}/${params.tela}/${params.versao}`, corpo: null });
    return HttpResponse.json(projetosDeDesign[0].telas[0].versoes[1]);
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
