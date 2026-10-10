// Os tipos do contrato da API — espelho de api/src/DevKitPage.Contracts/V1 (JSON camelCase).
// O documento OpenAPI da API (/openapi/v1.json) é a fonte: `npm run gerar:tipos` gera o
// openapi.d.ts a partir dele para conferir este arquivo quando o contrato mudar.

/**
 * O papel do usuário (US #381, US #405): o gestor vê só a empresa dele; o dev é o usuário do dev.kit —
 * entra no app e baixa a versão, mas não no painel.
 */
export type Papel = 'admin' | 'gestor' | 'dev';

export interface LoginResponse {
  token: string;
  expiraEm: string;
  deveTrocarSenha: boolean;
  login: string;
  ehAdmin: boolean;
  /** Ausente numa sessão gravada antes da US #381: vale como admin. */
  papel?: Papel;
  empresa?: string | null;
}

export interface MaquinaResumo {
  id: number;
  maquinaId: string;
  apelido: string;
  versaoDevKit: string;
  registradaEm: string;
  ultimoEnvioEm: string | null;
  eventos: number;
}

export interface DiaDeUso {
  dia: string;
  sessoes: number;
  turnos: number;
  fluxos: number;
  ferramentas: number;
  comandosDelegados: number;
  arquivosAlterados: number;
}

export interface QuantidadeResposta {
  de: string;
  ate: string;
  sessoes: number;
  turnos: number;
  fluxos: number;
  ferramentas: number;
  comandosDelegados: number;
  arquivosAlterados: number;
  serieDiaria: DiaDeUso[];
  /** Máquinas distintas com evento no período (US #381). */
  maquinasAtivas: number;
  /** Máquinas registradas até o fim do período, no escopo de quem consulta (US #381). */
  maquinasRegistradas: number;
}

export interface CausaDeFalha {
  causa: string;
  quantidade: number;
}

/** As taxas vêm nulas quando não há denominador — a tela mostra um traço. */
export interface QualidadeResposta {
  de: string;
  ate: string;
  turnos: number;
  turnosComFalha: number;
  taxaDeFalha: number | null;
  duracaoMediaDoTurnoMs: number | null;
  falhasPorCausa: CausaDeFalha[];
  avaliacoes: number;
  notaMedia: number | null;
  objetivosCumpridos: number;
  objetivosRecusados: number;
  razaoCumpridosRecusados: number | null;
  turnosPorObjetivoCumprido: number | null;
  /** O cartão Impasses (US #405); ausente numa API anterior a ele. */
  impasses?: ImpassesResumo | null;
  /** O cartão Árbitro (US #405); ausente numa API anterior a ele. */
  arbitro?: ArbitroResumo | null;
}

/** Quantas vezes um recorte (origem, desfecho, ação, regra) aparece no período. */
export interface ContagemPorRecorte {
  recorte: string;
  quantidade: number;
}

/** Os impasses do fluxo no período (US #405): as médias são nulas sem evento. */
export interface ImpassesResumo {
  detectados: number;
  porOrigem: ContagemPorRecorte[];
  minutosParadoNaDeteccao: number | null;
  destravados: number;
  minutosAteDestravar: number | null;
  comoDestravaram: ContagemPorRecorte[];
}

/** O árbitro no período (US #405): a taxa de correção é nula sem cobrança. */
export interface ArbitroResumo {
  acoes: number;
  cobrancas: number;
  /** A regra é o título da seção do CLAUDE.md; vazia é a cobrança sem seção. */
  cobrancasPorRegra: ContagemPorRecorte[];
  corrigidas: number;
  taxaDeCorrecao: number | null;
  escaladasAoDev: number;
  porAcao: ContagemPorRecorte[];
}

/** A última versão estável do dev.kit (US #405, GET /api/versoes/ultima — anônima). */
export interface VersaoDoDevKit {
  versao: number;
  tag: string;
  nome: string;
  publicadaEm: string;
  destaques: string[];
  tamanhoBytes: number;
  sha256: string | null;
  /** A rota da API que entrega o zip — autenticada (Bearer). */
  urlDownload: string;
  loginObrigatorio: boolean;
}

/** Um usuário na lista do admin (US #405). A senha nunca vem aqui. */
export interface UsuarioResumo {
  id: number;
  login: string;
  nome: string;
  papel: Papel;
  empresaId: number | null;
  empresa: string | null;
  bloqueado: boolean;
  /** O bloqueio temporário por falhas de login, quando vale. */
  bloqueadoAte: string | null;
  deveTrocarSenha: boolean;
  criadoEm: string;
}

export interface UsuarioNovo {
  login: string;
  nome: string;
  papel: Papel;
  empresaId: number | null;
}

export interface UsuarioEditado {
  nome: string;
  papel: Papel;
  empresaId: number | null;
}

/** A resposta da criação e da redefinição: a senha temporária vem UMA vez. */
export interface UsuarioComSenha {
  usuario: UsuarioResumo;
  senhaTemporaria: string;
}

export interface EventoDoLog {
  eventId: string;
  maquinaId: number;
  apelido: string;
  tipo: string;
  sessaoId: string;
  quantidade: number;
  valor: number | null;
  detalhe: string;
  em: string;
}

export interface Pagina<T> {
  itens: T[];
  total: number;
  numeroDaPagina: number;
  tamanho: number;
}

/** O pedido de demonstração do formulário da landing (POST público). */
export interface PedidoDeDemonstracao {
  nome: string;
  email: string;
  empresa: string;
  mensagem: string;
  consentimento: boolean;
}

export interface PedidoDeDemonstracaoCriado {
  id: number;
  recebidoEm: string;
}

/** Um pedido na lista do dashboard (rota autenticada). */
export interface DemonstracaoResumo {
  id: number;
  nome: string;
  email: string;
  empresa: string;
  mensagem: string;
  recebidoEm: string;
  consentimentoEm: string;
}

/** Os estados de um grupo de exceção (US #381); "Regrediu" só o servidor põe. */
export type EstadoDoGrupo = 'Novo' | 'Visto' | 'Resolvido' | 'Ignorado' | 'Regrediu';

/** Um grupo de exceção não classificada: as ocorrências da mesma assinatura no período. */
export interface GrupoDeErroResumo {
  id: number;
  assinatura: string;
  tipo: string;
  estado: EstadoDoGrupo;
  resolvidoNaVersao: string | null;
  ocorrencias: number;
  maquinas: number;
  primeiraVersao: string;
  ultimaVersao: string;
  primeiroVistoEm: string;
  ultimoVistoEm: string;
}

export interface OcorrenciaDeErroResumo {
  eventId: string;
  apelido: string;
  versaoDevKit: string;
  trace: string;
  em: string;
}

export interface OcorrenciasNoDia {
  dia: string;
  quantidade: number;
}

/** O detalhe de um grupo — também o corpo da exportação em JSON. */
export interface GrupoDeErroDetalhe {
  grupo: GrupoDeErroResumo;
  trace: string;
  ocorrencias: OcorrenciaDeErroResumo[];
  porDia: OcorrenciasNoDia[];
  versoes: string[];
  maquinas: string[];
}

/** Uma empresa do plano empresarial (US #381) — a lista do admin. */
export interface EmpresaResumo {
  id: number;
  nome: string;
  plano: string;
  assentos: number;
  codigoDeAdesao: string;
  colaboradores: number;
  criadaEm: string;
}

export interface EmpresaNova {
  nome: string;
  plano: string;
  assentos: number;
}

/** O gestor convidado: a senha inicial vem UMA vez. */
export interface GestorCriado {
  id: number;
  login: string;
  senhaInicial: string;
}

/** Um colaborador que consentiu (uma máquina vinculada à empresa). */
export interface ColaboradorResumo {
  maquinaId: number;
  colaborador: string;
  apelido: string;
  empresa: string;
  versaoDevKit: string;
  consentiuEm: string;
  ultimoEnvioEm: string | null;
}

/**
 * O ROI de um work item numa máquina (US #387): a foto MAIS RECENTE que o dev.kit dela calculou
 * (`devcli roi`). O mesmo item em duas máquinas são duas linhas.
 */
export interface RoiDoWorkItem {
  maquinaId: number;
  apelido: string;
  /** O nome que o colaborador informou no dev.kit; vazio na máquina anônima. */
  colaborador: string;
  workItem: number;
  tipo: string;
  estado: string;
  de: string;
  ate: string;
  turnosDoAgente: number;
  sessoes: number;
  horas: number;
  horasNoBoard: number;
  horasNoTimesheet: number | null;
  /** Horas sobre turnos; nulo sem turno (a tela mostra um traço). */
  horasPorTurno: number | null;
  /** Da criação ao encerramento; nulo enquanto o item está aberto. */
  leadTimeDias: number | null;
  aberto: boolean;
  pullRequests: number;
  pullRequestsMergeadas: number;
  /** Quando o dev.kit calculou a foto. */
  atualizadoEm: string;
}

/** Os totais do ROI no período (de todas as fotos, não só das da lista). */
export interface RoiTotais {
  itens: number;
  turnos: number;
  horas: number;
  /** A média dos ENCERRADOS; nula sem nenhum. */
  leadTimeMedioDias: number | null;
  pullRequests: number;
  pullRequestsMergeadas: number;
}

export interface RoiResposta {
  de: string;
  ate: string;
  itens: RoiDoWorkItem[];
  totais: RoiTotais;
}

/** Um arquivo baixado da API: o conteúdo e o nome sugerido. */
export interface ArquivoBaixado {
  conteudo: Blob;
  nome: string;
}

/** O filtro do dashboard: período (AAAA-MM-DD, inclusive) e máquina (vazio = todas). */
export interface Filtro {
  de: string;
  ate: string;
  maquina: number | null;
}
