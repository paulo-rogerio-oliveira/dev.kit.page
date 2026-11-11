import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { api, ErroDaApi } from '../../api/cliente';
import { renderizar } from '../../testes/renderizar';
import { tokenDoDev } from '../../testes/servidor';
import type { Projeto } from '../../api/projetos';
import { revisaoDeTeste } from './fixtures';

const projeto: Projeto = { id: 9, slug: 'matrix', nome: 'Matrix', nivel: 'usuario', time: '', repositorios: ['demo/aplicacao'], branch: 'feature/demo',
  semanaConvertida: false, definicao: '', anexos: [], autonomia: true, paralelismo: 1, destinoDaPr: 'main', situacao: 'aprovado', versao: 1,
  autor: 'demo', atualizadoEm: '2026-11-10T10:00:00Z', itens: [], podeApagar: true, featureId: 0, epicId: 0, iteracao: '', board: 'nao' };

beforeEach(() => {
  vi.spyOn(api, 'projetos').mockResolvedValue([projeto]);
  vi.spyOn(api, 'acessosDoRecurso').mockResolvedValue({ projeto: 9, recurso: 'projeto', nome: 'Matrix', papel: 'dono', dono: { id: 1, login: 'demo', nome: 'Demo' }, acessos: [], candidatos: [] });
});
afterEach(() => vi.restoreAllMocks());

it('cliente anterior sem Matrix mantém o projeto e explica que o recurso está indisponível', async () => {
  vi.spyOn(api, 'matrizes').mockRejectedValue(new ErroDaApi(404, 'Rota inexistente'));
  const gravar = vi.spyOn(api, 'editarMatrix');
  renderizar('/projetos?slug=matrix&visao=impactos', tokenDoDev());
  expect(await screen.findByText(/Impactos indisponíveis neste servidor ou cliente anterior/)).toBeInTheDocument();
  expect(screen.getByRole('article', { name: 'Detalhe do projeto' })).toHaveTextContent('feature/demo');
  await userEvent.click(screen.getByRole('button', { name: 'Ver recursos disponíveis' }));
  expect(screen.getByRole('navigation', { name: 'Recursos disponíveis' })).toHaveTextContent('Board');
  expect(gravar).not.toHaveBeenCalled();
});

it('documento de contrato desconhecido é recusado em consulta, sem tentativa de regravar', async () => {
  vi.spyOn(api, 'matrizes').mockResolvedValue([{ aplicacao: 'demo', tipo: 'mapa', id: 'demo', projeto: 'matrix', versao: 1 }]);
  const r = revisaoDeTeste(); r.documento.contrato = 2;
  vi.spyOn(api, 'historicoMatrix').mockResolvedValue([r]);
  const gravar = vi.spyOn(api, 'editarMatrix');
  renderizar('/projetos?slug=matrix&visao=impactos', tokenDoDev());
  expect(await screen.findByText('Contrato Matrix desconhecido. Atualize o cliente.')).toBeInTheDocument();
  expect(gravar).not.toHaveBeenCalled();
});
