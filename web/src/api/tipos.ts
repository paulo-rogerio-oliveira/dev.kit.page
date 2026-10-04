// Os tipos do contrato da API — espelho de api/src/DevKitPage.Contracts/V1 (JSON camelCase).
// O documento OpenAPI da API (/openapi/v1.json) é a fonte: `npm run gerar:tipos` gera o
// openapi.d.ts a partir dele para conferir este arquivo quando o contrato mudar.

export interface LoginResponse {
  token: string;
  expiraEm: string;
  deveTrocarSenha: boolean;
  login: string;
  ehAdmin: boolean;
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

/** O filtro do dashboard: período (AAAA-MM-DD, inclusive) e máquina (vazio = todas). */
export interface Filtro {
  de: string;
  ate: string;
  maquina: number | null;
}
