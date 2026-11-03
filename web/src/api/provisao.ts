/**
 * O contrato v1 da provisão (projeto controle-remoto, item 4; `api/src/DevKitPage.Contracts/V1/ProvisaoV1.cs`). A API grava o
 * pedido e um trabalhador separado, com identidade própria no Azure, o executa: o site só acompanha o estado.
 */
export type EstadoDaProvisao = 'pedido' | 'em-provisao' | 'pronto' | 'falhou' | 'removido';

export type OperacaoDaProvisao = 'criar' | 'remover';

/** Uma linha da auditoria do recurso. */
export interface EventoDaProvisao {
  em: string;
  operacao: OperacaoDaProvisao;
  estado: EstadoDaProvisao;
  /** `usuario` ou `trabalhador:<nome>`. */
  autor: string;
  mensagem: string;
}

/** Um recurso do usuário no Azure (hoje, a fila: uma conta de armazenamento com uma Storage Queue). */
export interface RecursoNaNuvem {
  id: number;
  tipo: 'fila';
  operacao: OperacaoDaProvisao;
  estado: EstadoDaProvisao;
  regiao: string;
  grupoDeRecursos: string;
  conta: string;
  fila: string;
  tentativas: number;
  ultimoErro: string;
  pedidoEm: string;
  atualizadoEm: string;
  prontoEm: string | null;
  /** Só na leitura de um recurso (GET /recursos/{id}). */
  historico?: EventoDaProvisao[] | null;
}

/** A área de provisão de quem está logado (GET /api/provisao/v1). */
export interface SituacaoDaProvisao {
  liberada: boolean;
  origem: 'usuario' | 'plano' | null;
  regioes: string[];
  regiaoPadrao: string;
  recursos: RecursoNaNuvem[];
}

/** A liberação de um usuário, como o admin a vê (GET /api/provisao/v1/liberacoes). */
export interface LiberacaoDaProvisao {
  usuarioId: number;
  login: string;
  liberada: boolean;
  alteradaPor: string;
  alteradaEm: string;
}

/** O trabalhador ainda vai mexer no recurso: a tela acompanha. */
export function emAndamento(recurso: RecursoNaNuvem): boolean {
  return recurso.estado === 'pedido' || recurso.estado === 'em-provisao';
}
