import type { Chip } from './Chips';

/** O filtro de dia do Board do dev.kit (US #858): o período das execuções do painel. */
export type PeriodoDoDia = 'hoje' | 'ontem' | 'semana' | 'tudo';

export const periodosDoDia: Chip<PeriodoDoDia>[] = [
  { valor: 'hoje', rotulo: 'Hoje' }, { valor: 'ontem', rotulo: 'Ontem' },
  { valor: 'semana', rotulo: 'Semana' }, { valor: 'tudo', rotulo: 'Tudo' },
];

/** O período do parâmetro `dia` da URL; ausente ou desconhecido, Hoje. */
export function periodoDaUrl(valor: string | null): PeriodoDoDia {
  return periodosDoDia.some(p => p.valor === valor) ? valor as PeriodoDoDia : 'hoje';
}

function inicioDoDia(momento: number, diasAtras = 0) {
  const dia = new Date(momento);
  dia.setHours(0, 0, 0, 0);
  dia.setDate(dia.getDate() - diasAtras);
  return dia.getTime();
}

/** "Hoje" para a data do mesmo dia de `agora`; senão, a data por extenso curto (a separação de dias do chat). */
export function rotuloDoDia(data: string, agora: number) {
  const momento = Date.parse(data);
  return inicioDoDia(momento) === inicioDoDia(agora) ? 'Hoje' : new Date(momento).toLocaleDateString('pt-BR');
}

/** Se a data cai no período. Sem data (ou inválida), vale para todos: esconder o que não tem data perderia a execução. */
export function noPeriodo(data: string | null | undefined, periodo: PeriodoDoDia, agora: number) {
  const momento = data ? Date.parse(data) : NaN;
  if (periodo === 'tudo' || !Number.isFinite(momento)) return true;
  const hoje = inicioDoDia(agora);
  if (periodo === 'hoje') return momento >= hoje;
  if (periodo === 'ontem') return momento >= inicioDoDia(agora, 1) && momento < hoje;
  return momento >= inicioDoDia(agora, 6);
}

/** As execuções do período, pela data de início (`desdeEm`). Semana são os sete dias até hoje, inclusive. */
export function filtrarPorDia<T extends { desdeEm?: string | null }>(itens: T[], periodo: PeriodoDoDia, agora: number): T[] {
  return itens.filter(i => noPeriodo(i.desdeEm, periodo, agora));
}
