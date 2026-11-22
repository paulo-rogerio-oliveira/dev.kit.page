import type { ItemDoProjeto, Projeto } from './projetos';

/** Contrato portátil de Contracts/V1/CicloDosItensV1.cs; parada física continua exclusiva do host. */
export const recursoDoCicloDosItens = 'ciclo-humano-item-v1';
export type AcaoHumanaDoItem = 'concluir-manualmente' | 'cancelar-item';

export interface DecisaoHumanaDoItem {
  pedido: string; acao: AcaoHumanaDoItem; autor: string; confirmacao: string;
  motivo: string; em: string; versaoEsperada: string; estadoAnterior: string;
  snapshot: string; paradaPendente: boolean; geracao: number;
}

export interface ReservaDeEscritaDoItem { maquina: string; instancia: string; sessao: string; tentativa: string }
export interface ParadaDeEscritaDoItem {
  pedido: string; reserva: ReservaDeEscritaDoItem; recibo: number; em: string;
}
export interface ReservasDoCicloDoItem { reservas: ReservaDeEscritaDoItem[]; paradas: ParadaDeEscritaDoItem[] }
export interface ImportacaoDaDecisao { autorAutenticado: string; em: string; hashDoEnvio: string }

/** O consumidor só envia aplicar/confirmar após a decisão humana; simulação não registra a escolha. */
export type PedidoDeDecisaoDoItem = {
  pedido: string; acao: AcaoHumanaDoItem; versaoEsperada: number;
} & ({ aplicar?: false; confirmar?: false; motivo?: string; confirmacao?: string }
  | { aplicar: true; confirmar: true; motivo: string; confirmacao: string });

export interface ImpactoDaDecisaoDoItem {
  simulacao: boolean; projeto: Projeto; item: number; dependentes: number[];
  grafoEfetivo: ItemDoProjeto[]; consequencia: string;
}

export interface RecursosDosProjetos { recursos: string[] }
