import type { ItemDoProjeto, Projeto, ResumoDoProjeto } from '../../api/projetos';
import { acaoDoProjeto, espelhoNoResumo, estadoDoItem, estadosDoConjunto, listar, nivelDoPai, ondaQueEspera, situacaoDoDetalhe,
  situacaoDoResumo, situacaoExibida, workItemsCriados } from './situacaoDoProjeto';

const item = (numero: number, onda: number, situacao = 'planejado', extra: Partial<ItemDoProjeto> = {}): ItemDoProjeto => ({
  numero, titulo: `Item ${numero}`, descricao: '', fluxo: '14', repositorios: [], componentes: [], dependencias: [], onda,
  workItemId: 0, situacao, tipo: '', prioridade: 1, horas: 0, branch: '', agente: 'codex', modelo: 'gpt-6.1-sol', ...extra });

const projeto = (extra: Partial<Projeto> = {}): Projeto => ({
  id: 1, slug: 'boards-estados', nome: 'Boards', nivel: 'usuario', time: '', repositorios: ['repo'], branch: 'feature/x',
  semanaConvertida: false, definicao: '', anexos: [], autonomia: true, paralelismo: 2, destinoDaPr: 'develop', situacao: 'aprovado',
  versao: 3, autor: 'Dev', atualizadoEm: '2026-11-27T12:00:00Z', itens: [item(1, 1), item(2, 2)], podeApagar: true,
  featureId: 0, epicId: 0, iteracao: '', board: 'nao', papel: 'dono', ...extra });

const resumo = (extra: Partial<ResumoDoProjeto> = {}): ResumoDoProjeto => ({
  id: 1, slug: 'boards-estados', nome: 'Boards', nivel: 'usuario', time: '', situacao: 'aprovado', versao: 3,
  atualizadoEm: '2026-11-27T12:00:00Z', papel: 'dono', totalDeItens: 2, itensConcluidos: 0, itensEmExecucao: 0,
  itensComPergunta: 0, itensFalhos: 0, progresso: 0, temPendencias: false, ...extra });

const situacoes: [string, boolean, string][] = [
  ['rascunho', false, 'Rascunho'], ['aguardando', false, 'Aguardando aprovação'], ['aprovado', false, 'Aprovado · aguardando disparo'],
  ['aprovado', true, 'Em execução'], ['pausado', true, 'Pausado'], ['fechando', true, 'Fechando'], ['concluido', true, 'Concluído'],
  ['inventada', false, 'inventada'],
];

describe('situação exibida do projeto (boards-estados, item 5)', () => {
  it.each(situacoes)('%s (disparado: %s) é "%s"', (situacao, disparado, rotulo) => expect(situacaoExibida(situacao, disparado).rotulo).toBe(rotulo));

  it('o resumo separa o aprovado pela marca e, sem ela (dev.kit antigo), por item que saiu do planejado', () => {
    expect(situacaoDoResumo(resumo()).chave).toBe('aguardando-disparo');
    expect(situacaoDoResumo(resumo({ disparadoEm: '2026-11-27T13:00:00Z' })).chave).toBe('em-execucao');
    for (const contagem of [{ itensConcluidos: 1 }, { itensEmExecucao: 1 }, { itensComPergunta: 1 }, { itensFalhos: 1 }])
      expect(situacaoDoResumo(resumo(contagem)).chave).toBe('em-execucao');
    expect(situacaoDoResumo(resumo({ situacao: 'aguardando', disparadoEm: '2026-11-27T13:00:00Z' })).rotulo).toBe('Aguardando aprovação');
  });

  it('o detalhe usa a marca ou os itens', () => {
    expect(situacaoDoDetalhe(projeto()).rotulo).toBe('Aprovado · aguardando disparo');
    expect(situacaoDoDetalhe(projeto({ disparadoEm: '2026-11-27T13:00:00Z' })).rotulo).toBe('Em execução');
    expect(situacaoDoDetalhe(projeto({ itens: [item(1, 1, 'rodando'), item(2, 2)] })).rotulo).toBe('Em execução');
  });
});

describe('ação de aprovar ou disparar', () => {
  it('o leitor (ou quem não tem papel) não tem ação', () => {
    for (const papel of ['leitor', '', undefined])
      for (const situacao of ['aguardando', 'aprovado'])
        expect(acaoDoProjeto(projeto({ papel, situacao }), projeto().itens)).toBeNull();
  });

  it('aguardando aprovação é Aprovar plano; aprovado sem disparo é Disparar projeto, para dono e editor', () => {
    for (const papel of ['dono', 'editor']) {
      expect(acaoDoProjeto(projeto({ papel, situacao: 'aguardando' }), projeto().itens)).toEqual({ acao: 'aprovar', rotulo: 'Aprovar plano' });
      expect(acaoDoProjeto(projeto({ papel }), projeto().itens)).toEqual({ acao: 'disparar', rotulo: 'Disparar projeto' });
    }
  });

  it.each(['rascunho', 'pausado', 'fechando', 'concluido'])('%s não tem ação de aprovar nem de disparar', situacao =>
    expect(acaoDoProjeto(projeto({ situacao }), projeto().itens)).toBeNull());

  it('com autonomia, o projeto disparado (pela marca ou pelos itens) não oferece outro disparo', () => {
    expect(acaoDoProjeto(projeto({ disparadoEm: '2026-11-27T13:00:00Z' }), projeto().itens)).toBeNull();
    const iniciado = projeto({ itens: [item(1, 1, 'integrado'), item(2, 2)] });
    expect(acaoDoProjeto(iniciado, iniciado.itens)).toBeNull();
  });

  it('sem autonomia, a onda seguinte que espera vira Disparar onda N; a onda em andamento, não', () => {
    const manual = (itens: ItemDoProjeto[]) => projeto({ autonomia: false, disparadoEm: '2026-11-27T13:00:00Z', itens });
    const espera = manual([item(1, 1, 'integrado'), item(2, 1, 'integrado'), item(3, 2), item(4, 2), item(5, 3)]);
    expect(ondaQueEspera(espera, espera.itens)).toBe(2);
    expect(acaoDoProjeto(espera, espera.itens)).toEqual({ acao: 'disparar', rotulo: 'Disparar onda 2' });
    const emAndamento = manual([item(1, 1, 'integrado'), item(3, 2, 'rodando'), item(4, 2)]);
    expect(acaoDoProjeto(emAndamento, emAndamento.itens)).toBeNull();
    // Recém-disparado: a primeira onda ainda planejada já foi pedida, não se pede de novo.
    const recem = manual([item(1, 1), item(2, 2)]);
    expect(acaoDoProjeto(recem, recem.itens)).toBeNull();
    const terminado = manual([item(1, 1, 'integrado'), item(2, 2, 'integrado')]);
    expect(acaoDoProjeto(terminado, terminado.itens)).toBeNull();
    // Antes do disparo, o manual também começa pelo Disparar projeto (a primeira onda).
    expect(acaoDoProjeto(projeto({ autonomia: false }), projeto().itens)?.rotulo).toBe('Disparar projeto');
  });
});

describe('espelho no resumo e estados configurados', () => {
  it('sem Azure Boards ou sem a escolha configurada não há espelho', () => {
    expect(espelhoNoResumo(projeto({ board: 'nao', featureId: 812 }))).toBeNull();
    expect(espelhoNoResumo(projeto({ board: '' }))).toBeNull();
  });

  it('o pai pelo nível, com o id quando existe; sem id, quando será criado ou vinculado', () => {
    expect(espelhoNoResumo(projeto({ board: 'criar' }))).toEqual({ tipoDoPai: 'Feature', id: 0, detalhe: 'criada na aprovação' });
    expect(espelhoNoResumo(projeto({ board: 'criar', nivelNoBoard: 'epic' }))?.detalhe).toBe('criado na aprovação');
    expect(espelhoNoResumo(projeto({ board: 'vincular' }))?.detalhe).toBe('vínculo pendente');
    const tipos = [item(1, 1, 'planejado', { tipo: 'User Story' }), item(2, 1, 'planejado', { tipo: 'Bug' }), item(3, 2, 'planejado', { tipo: 'User Story' })];
    expect(espelhoNoResumo(projeto({ board: 'criar', featureId: 812, itens: tipos }))).toEqual({ tipoDoPai: 'Feature', id: 812, detalhe: 'itens User Story e Bug' });
    expect(espelhoNoResumo(projeto({ board: 'vincular', featureId: 9, nivelNoBoard: 'requisito' }))).toEqual({ tipoDoPai: 'Requisito', id: 9, detalhe: 'itens Task' });
    expect(espelhoNoResumo(projeto({ board: 'criar', featureId: 812 }))?.detalhe).toBe('itens requisito ou Bug');
  });

  it('o nível vazio, ausente ou desconhecido é a Feature; a caixa e os espaços não importam', () => {
    for (const nivel of [undefined, null, '', 'feature', 'inventado']) expect(nivelDoPai(nivel)).toBe('feature');
    expect(nivelDoPai(' Epic ')).toBe('epic');
    expect(nivelDoPai('REQUISITO')).toBe('requisito');
  });

  it('o estado vazio é o padrão pela categoria; o configurado vem do contrato', () => {
    expect(estadosDoConjunto('itens', null)).toEqual([{ nome: 'New', configurado: false }, { nome: 'Active', configurado: false }, { nome: 'Closed', configurado: false }]);
    expect(estadosDoConjunto('pai', undefined).map(e => e.nome)).toEqual(['New', 'Active', 'Resolved']);
    const estados = { itens: { inicial: ' ', execucao: null, finalizacao: ' Review ' }, pai: { finalizacao: '' } };
    expect(estadosDoConjunto('itens', estados)).toEqual([{ nome: 'New', configurado: false }, { nome: 'Active', configurado: false }, { nome: 'Review', configurado: true }]);
    expect(estadosDoConjunto('pai', estados).every(e => !e.configurado)).toBe(true);
  });

  it('o estado do item acompanha a fase: planejado no inicial, integrado na finalização, o resto na execução', () => {
    const estados = { itens: { finalizacao: 'Resolved' } };
    expect(estadoDoItem(item(1, 1), estados)).toBe('New');
    for (const situacao of ['rodando', 'pergunta', 'falhou']) expect(estadoDoItem(item(1, 1, situacao), estados)).toBe('Active');
    expect(estadoDoItem(item(1, 1, 'integrado'), estados)).toBe('Resolved');
  });

  it('os work items criados contam o pai só quando o projeto cria o pai', () => {
    const itens = [item(1, 1, 'planejado', { workItemId: 813 }), item(2, 1, 'planejado', { workItemId: 814 }), item(3, 2)];
    expect(workItemsCriados(projeto({ board: 'criar', featureId: 812 }), itens)).toBe(3);
    expect(workItemsCriados(projeto({ board: 'vincular', featureId: 812 }), itens)).toBe(2);
    expect(workItemsCriados(projeto({ board: 'criar', featureId: 0 }), [])).toBe(0);
  });

  it('lista os nomes em português', () => {
    expect(listar([])).toBe('');
    expect(listar(['User Story'])).toBe('User Story');
    expect(listar(['User Story', 'Bug'])).toBe('User Story e Bug');
    expect(listar(['A', 'B', 'C'])).toBe('A, B e C');
  });
});
