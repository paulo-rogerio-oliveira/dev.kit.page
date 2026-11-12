import { render, screen } from '@testing-library/react';
import type { RegistroMatrix, RevisaoMatrix } from '../../api/matrix';
import { RegistrosDaCelula } from './CelulaDaMatrix';
import { documentoDeTeste, revisaoDeTeste } from './fixtures';
import { estadoDaCelula, motivoDaObsolescencia, registroVigente } from './modelo';

const resultado = (parcial: Partial<RegistroMatrix> = {}): RegistroMatrix => ({
  celula: 'imp-page', revisaoDaDefinicao: 1, responsavel: 'item-4', tipo: 'avaliacao', resultado: 'atendido',
  justificativa: 'Pedido e cenário conferidos', evidencias: ['log do cenário'], shas: { repo: 'a'.repeat(40) },
  evento: 'gatilho:123', sessaoAvaliada: 'task-demo', avaliador: 7, revisaoEfetiva: 1, revisaoDoContexto: 1,
  revisaoDaEntrega: 'manifesto imutável', ...parcial,
});
const registro = (r = resultado(), versao = 1): RevisaoMatrix => revisaoDeTeste({
  contrato: 1, aplicacao: 'demo', tipo: 'registro', id: 'matrix', projeto: 'matrix', registro: r,
}, versao);

it('mostra evento, avaliador, recorte e SHAs nas áreas existentes e mantém registros antigos como histórico', () => {
  render(<RegistrosDaCelula tipo="avaliacao" celula="imp-page" registros={[registro(), registro(resultado({ evento: undefined }), 2)]}
    historico={[revisaoDeTeste()]} />);
  expect(screen.getByText('Atendido · Vigente')).toBeInTheDocument();
  expect(screen.getByText(/Evento gatilho:123 · avaliador 7 · task task-demo · revisão efetiva 1/)).toBeInTheDocument();
  expect(screen.getByText(/Histórico · Avaliação anterior sem vínculo verificável/)).toBeInTheDocument();
  expect(screen.getAllByText('a'.repeat(40))).toHaveLength(2);
});

it('revisão global alheia e evidência de implementação não substituem nem invalidam avaliação da célula', () => {
  const antes = documentoDeTeste(), depois = structuredClone(antes);
  depois.matriz!.celulas[1].criterio = 'Outra task revisada';
  const historico = [revisaoDeTeste(antes), revisaoDeTeste(depois, 2)];
  const implementacao = registro(resultado({ tipo: 'implementacao', evento: undefined, resultado: 'parcial' }), 2);
  expect(registroVigente(resultado(), historico)).toBe(true);
  expect(estadoDaCelula(antes.matriz!.celulas[0], [registro(), implementacao], historico)).toBe('Comprovado');
  expect(estadoDaCelula(antes.matriz!.celulas[0], [implementacao], historico)).toBe('A avaliar');
});

it('reintroduzir atribuição anterior conserva o motivo da invalidação e não ressuscita o resultado', () => {
  const antes = documentoDeTeste(), depois = structuredClone(antes);
  depois.matriz!.celulas[0].responsaveis = [{ id: 'item-3', item: 3 }];
  const historico = [revisaoDeTeste(antes), revisaoDeTeste(depois, 2), revisaoDeTeste(antes, 3)];
  expect(registroVigente(resultado(), historico)).toBe(false);
  expect(motivoDaObsolescencia(resultado(), historico)).toContain('revisão 2');
  expect(estadoDaCelula(antes.matriz!.celulas[0], [registro()], historico)).toBe('A avaliar');
});

it('obrigação compartilhada exige resultado de todos os donos e considera a última avaliação', () => {
  const documento = documentoDeTeste(), c = documento.matriz!.celulas[0];
  c.responsaveis.push({ id: 'item-3', item: 3 });
  const historico = [revisaoDeTeste(documento)];
  expect(estadoDaCelula(c, [registro()], historico)).toBe('A avaliar');
  const segundo = registro(resultado({ responsavel: 'item-3' }), 2);
  expect(estadoDaCelula(c, [registro(), segundo], historico)).toBe('Comprovado');
  expect(estadoDaCelula(c, [registro(), segundo, registro(resultado({ resultado: 'parcial' }), 3)], historico)).toBe('A avaliar');
});
