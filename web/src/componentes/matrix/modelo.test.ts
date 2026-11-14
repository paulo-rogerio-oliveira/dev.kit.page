import { celulasAlteradas, escreverDefinicao, lerDefinicao, reaplicar, registroVigente, validarCelula } from './modelo';
import { documentoDeTeste, revisaoDeTeste } from './fixtures';
import type { RegistroMatrix } from '../../api/matrix';

describe('revisões compartilhadas Matrix', () => {
  it('mantém obrigação e motivo independentes no texto legível do contrato anterior', () => {
    expect(lerDefinicao(escreverDefinicao('Editar no celular', 'Mesmo significado no Page'))).toEqual({ obrigacao: 'Editar no celular', motivo: 'Mesmo significado no Page' });
    expect(lerDefinicao('Texto anterior')).toEqual({ obrigacao: 'Texto anterior', motivo: 'Texto anterior' });
    expect(validarCelula({ ...documentoDeTeste().matriz!.celulas[0], justificativa: escreverDefinicao('Obrigação', '') })).toMatch(/justificativa/);
  });
  it('reaplica só a célula local, preservando alteração concorrente em outra task', () => {
    const base = documentoDeTeste(), local = structuredClone(base), remoto = structuredClone(base);
    local.matriz!.celulas[0].criterio = 'Critério do celular';
    remoto.matriz!.celulas[1].criterio = 'Critério salvo pelo desktop';
    const aplicado = reaplicar(base, local, remoto);
    expect(aplicado.matriz!.celulas.find(c => c.id === 'imp-page')!.criterio).toBe('Critério do celular');
    expect(aplicado.matriz!.celulas.find(c => c.id === 'imp-api')!.criterio).toBe('Critério salvo pelo desktop');
    expect(celulasAlteradas(remoto.matriz, aplicado.matriz)).toEqual(['imp-page']);
  });
  it('uma mudança no nome do eixo invalida só avaliações das células daquele eixo', () => {
    const base = documentoDeTeste(), atual = structuredClone(base);
    atual.matriz!.layers[0].nome = 'Celular e Page';
    const r: RegistroMatrix = { celula: 'imp-page', revisaoDaDefinicao: 1, responsavel: 'item-4', tipo: 'avaliacao', resultado: 'atendido', justificativa: 'Conferido', evidencias: ['captura.png'], shas: { demo: 'a'.repeat(40) }, evento: 'gatilho:123' };
    const h = [revisaoDeTeste(base, 1), revisaoDeTeste(atual, 2)];
    expect(registroVigente(r, h)).toBe(false);
    expect(registroVigente({ ...r, celula: 'imp-api', responsavel: 'item-2' }, h)).toBe(true);
  });
  it('preserva outro campo remoto da mesma célula, inclusive responsáveis', () => {
    const base = documentoDeTeste(), local = structuredClone(base), remoto = structuredClone(base);
    local.matriz!.celulas[0].criterio = 'Critério local';
    remoto.matriz!.celulas[0].evidenciaEsperada = 'Evidência remota';
    remoto.matriz!.celulas[0].responsaveis.push({ id: 'apoio', item: null });
    const r = reaplicar(base, local, remoto).matriz!.celulas[0];
    expect(r.criterio).toBe('Critério local');
    expect(r.evidenciaEsperada).toBe('Evidência remota');
    expect(r.responsaveis).toEqual(remoto.matriz!.celulas[0].responsaveis);
  });
  it('mescla por campo nos eixos e conserva inclusões remotas', () => {
    const base = documentoDeTeste(), local = structuredClone(base), remoto = structuredClone(base);
    local.matriz!.componentes[0].nome = 'Nome local';
    remoto.matriz!.componentes[0].orientacao = 'Orientação remota';
    remoto.matriz!.layers.push({ id: 'nova', nome: 'Nova layer', orientacao: '' });
    const r = reaplicar(base, local, remoto).matriz!;
    expect(r.componentes[0]).toMatchObject({ nome: 'Nome local', orientacao: 'Orientação remota' });
    expect(r.layers.at(-1)?.id).toBe('nova');
  });
  it('mescla campos e listas de layers da mesma relação do mapa', () => {
    const base = { ...documentoDeTeste(), tipo: 'mapa' as const, matriz: undefined, mapa: {
      nome: 'Aplicação', repositorios: ['repo'], componentes: [], layers: [], relacoes: [{ id: 'rel', origem: 'a', destino: 'b',
        layers: ['page'], gatilho: 'Gatilho', motivo: 'Motivo', propagacao: 'Propagação', verificacao: 'Critério' }],
    } };
    const local = structuredClone(base), remoto = structuredClone(base);
    local.mapa.relacoes[0].motivo = 'Motivo local'; local.mapa.relacoes[0].layers.push('desktop');
    remoto.mapa.relacoes[0].verificacao = 'Critério remoto'; remoto.mapa.relacoes[0].layers.push('api');
    expect(reaplicar(base, local, remoto).mapa!.relacoes[0]).toMatchObject({ motivo: 'Motivo local', verificacao: 'Critério remoto', layers: ['page', 'api', 'desktop'] });
  });
  it('uma colisão real usa o campo local somente após reaplicação explícita', () => {
    const base = documentoDeTeste(), local = structuredClone(base), remoto = structuredClone(base);
    local.matriz!.celulas[0].criterio = 'Local'; remoto.matriz!.celulas[0].criterio = 'Servidor';
    expect(reaplicar(base, local, remoto).matriz!.celulas[0].criterio).toBe('Local');
    expect(remoto.matriz!.celulas[0].criterio).toBe('Servidor');
  });
  it('não confunde não analisada com não aplicável; exige motivo e responsável quando aplicável', () => {
    const c = documentoDeTeste().matriz!.celulas[0];
    expect(validarCelula({ ...c, aplicavel: false, justificativa: '' })).toMatch(/justificativa/);
    expect(validarCelula({ ...c, responsaveis: [] })).toMatch(/responsável/);
    expect(validarCelula({ ...c, aplicavel: false, responsaveis: [], justificativa: 'Fora da superfície' })).toBe('');
  });
});
