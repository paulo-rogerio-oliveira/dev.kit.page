import { filtrarPorDia, noPeriodo, periodoDaUrl, rotuloDoDia } from './filtroDeDia';

// Quinta, 05/11/2026 às 10h (hora local): as datas abaixo são relativas a ela.
const agora = new Date(2026, 10, 5, 10, 0).getTime();
const em = (dia: number, hora = 9) => new Date(2026, 10, dia, hora, 0).toISOString();
const execucoes = [
  { id: 'hoje-cedo', desdeEm: em(5, 0) },
  { id: 'ontem-tarde', desdeEm: em(4, 23) },
  { id: 'ontem-cedo', desdeEm: em(4, 0) },
  { id: 'seis-dias', desdeEm: new Date(2026, 9, 30, 0, 0).toISOString() },
  { id: 'sete-dias', desdeEm: new Date(2026, 9, 29, 23, 59).toISOString() },
  { id: 'sem-data', desdeEm: null },
  { id: 'data-invalida', desdeEm: 'ontem' },
];
const ids = (periodo: Parameters<typeof filtrarPorDia>[1]) => filtrarPorDia(execucoes, periodo, agora).map(e => e.id);

it('Hoje fica com o que começou desde a meia-noite, e o que não tem data vale para todos', () => {
  expect(ids('hoje')).toEqual(['hoje-cedo', 'sem-data', 'data-invalida']);
});

it('Ontem é o dia de calendário anterior, inteiro', () => {
  expect(ids('ontem')).toEqual(['ontem-tarde', 'ontem-cedo', 'sem-data', 'data-invalida']);
});

it('Semana são os sete dias até hoje, inclusive; o oitavo fica de fora', () => {
  expect(ids('semana')).toEqual(['hoje-cedo', 'ontem-tarde', 'ontem-cedo', 'seis-dias', 'sem-data', 'data-invalida']);
});

it('Tudo não filtra', () => {
  expect(ids('tudo')).toHaveLength(execucoes.length);
  expect(noPeriodo(em(1), 'tudo', agora)).toBe(true);
});

it('o parâmetro dia da URL vira o período; ausente ou desconhecido, Hoje', () => {
  expect(periodoDaUrl('ontem')).toBe('ontem');
  expect(periodoDaUrl('semana')).toBe('semana');
  expect(periodoDaUrl(null)).toBe('hoje');
  expect(periodoDaUrl('mes')).toBe('hoje');
});

it('o rótulo do dia do chat diz Hoje só para o mesmo dia de calendário', () => {
  expect(rotuloDoDia(em(5, 0), agora)).toBe('Hoje');
  expect(rotuloDoDia(em(4, 23), agora)).toBe(new Date(2026, 10, 4, 23).toLocaleDateString('pt-BR'));
});
