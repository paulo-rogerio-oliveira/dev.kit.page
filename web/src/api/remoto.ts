/** Contrato de leitura v1; a API define os códigos e pode acrescentar valores. */
export interface MaquinaRemota {
  maquinaId: string;
  apelido: string;
  versaoDevKit: string;
  conectada: boolean;
  devKitAberto: boolean;
  agente: string;
  turnosEmAndamento: number;
  ultimoHeartbeat: string;
  retratoRecebidoEm: string | null;
}

export interface RetratoRemoto {
  versao: string;
  maquinaId: string;
  geradoEm: string;
  projetos: {
    slug: string; nome: string; situacao: string; itens: number; integrados: number;
    rodando: number; onda: number; ondas: number;
  }[];
  fluxos: {
    id: string; titulo: string; fluxo: string; etapa: string; situacao: string;
    projeto: string | null; item: number | null; desde: string | null;
  }[];
  pendencias: {
    id: string; tipo: string; titulo: string; resumo: string; opcoes: string[];
    projeto: string | null; item: number | null; desde: string | null;
  }[];
}

export interface PainelRemoto {
  maquina: MaquinaRemota;
  retrato: RetratoRemoto | null;
}
