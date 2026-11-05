/** Contrato de leitura da fase 1, espelhado de Contracts/V1/RemotoV1.cs. */
export interface MaquinaRemota {
  maquinaId: string;
  nome: string;
  versaoDevKit: string;
  origem: string;
  appAberto: boolean;
  estadoDoAgente: string;
  turnoEmAndamento: boolean;
  conectada: boolean;
  ultimoHeartbeatEm: string | null;
  retratoGeradoEm: string | null;
}

export interface RetratoDaMaquina {
  maquina: MaquinaRemota;
  recebidoEm: string;
  retrato: {
    contrato: number;
    maquinaId: string;
    geradoEm: string;
    projetos: Array<{
      slug: string; nome: string; situacao: string; ondaAtual: number;
      itens: Array<{ numero: number; titulo: string; situacao: string; onda: number }> | null;
    }> | null;
    fluxos: Array<{
      id: string; nome: string; etapa: string; situacao: string;
      projeto?: string | null; item?: number | null; agente?: string | null; desdeEm?: string | null;
    }> | null;
    pendencias: Array<{
      id: string; tipo: string; titulo: string; pergunta?: string | null; opcoes?: string[] | null;
      jaTentei?: string | null; projeto?: string | null; item?: number | null; fluxo?: string | null;
    }> | null;
  };
}

/** Conversa v1: o estado é confirmado pela API, nunca inferido do heartbeat. */
export interface ComandoRemoto {
  id: string; maquinaId: string; tipo: string; texto: string; dispositivo: string;
  estado: 'pendente' | 'na-fila' | 'entregue' | 'respondido' | 'concluido' | 'recusado' | 'expirado' | 'falhou';
  criadoEm: string; expiraEm: string; entregueEm: string | null; concluidoEm: string | null; erro: string;
}
export interface ComandoNovo {
  id: string; maquinaId: string; tipo: 'mensagem'; texto: string; dispositivo: 'celular' | 'navegador';
}
export interface RespostaRemota {
  id: string; maquinaId: string; comandoId: string | null; tipo: string; texto: string; geradaEm: string; recebidaEm: string;
}
export interface ConversaRemota {
  comandos: ComandoRemoto[]; respostas: RespostaRemota[]; ultimoEvento: number;
}
export interface EventoDaConversa {
  id: number; tipo: string; maquinaId: string; comando: ComandoRemoto | null; resposta: RespostaRemota | null; em: string;
}
