import { render, screen, within } from '@testing-library/react';
import { HistoricoDaMatrix } from './MapaDaMatrix';
import { documentoDeTeste, revisaoDeTeste } from './fixtures';

it('preserva a task afetada pela remoção de uma célula e pela desativação de sua layer', () => {
  const antes = documentoDeTeste(), depois = structuredClone(antes);
  depois.matriz!.layers = depois.matriz!.layers.filter(l => l.id !== 'page');
  depois.matriz!.celulas = depois.matriz!.celulas.filter(c => c.id !== 'imp-page');
  render(<HistoricoDaMatrix historico={[revisaoDeTeste(antes), revisaoDeTeste(depois, 2)]} />);
  const atual = within(screen.getAllByRole('article')[0]);
  expect(atual.getByText('1 obrigação(ões) alterada(s) · Item 4')).toBeInTheDocument();
  expect(atual.queryByText(/nenhuma task afetada/)).not.toBeInTheDocument();
  expect(atual.getByText('mapa r1')).toBeInTheDocument();
});

it('mostra responsáveis anteriores e novos, sem duplicar a task em reatribuições', () => {
  const antes = documentoDeTeste(), depois = structuredClone(antes);
  depois.matriz!.celulas[0].responsaveis = [{ id: 'item-3', item: 3 }, { id: 'revisor', item: null }];
  depois.matriz!.celulas[1].responsaveis = [{ id: 'item-3', item: 3 }];
  render(<HistoricoDaMatrix historico={[revisaoDeTeste(antes), revisaoDeTeste(depois, 2)]} />);
  const atual = within(screen.getAllByRole('article')[0]);
  expect(atual.getByText('2 obrigação(ões) alterada(s) · Item 4, Item 2, Item 3, revisor')).toBeInTheDocument();
  expect(atual.queryByText(/nenhuma task afetada/)).not.toBeInTheDocument();
});

it('não atribui impacto a revisões sem alteração e conserva a criação inicial', () => {
  const d = documentoDeTeste();
  render(<HistoricoDaMatrix historico={[revisaoDeTeste(d), revisaoDeTeste(structuredClone(d), 2)]} />);
  const artigos = screen.getAllByRole('article');
  expect(within(artigos[0]).getByText('0 obrigação(ões) alterada(s) · nenhuma task afetada')).toBeInTheDocument();
  expect(within(artigos[1]).getByText('2 obrigação(ões) alterada(s) · Item 4, Item 2')).toBeInTheDocument();
});
