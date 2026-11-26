import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api, ErroDaApi } from '../api/cliente';
import type { Projeto, ResumoDoProjeto } from '../api/projetos';
import { PacoteDoBoard } from './PacoteDoBoard';
import type { CriacaoDoBoard } from './CriacaoRemota';

vi.mock('../sessao', () => ({ useSessao: () => ({ sessao: { token: 'token-demo' } }) }));
const projeto: Projeto = { id: 1, slug: 'demo', nome: 'Demo', nivel: 'usuario', time: '', repositorios: ['repo'], branch: 'feature/demo',
  semanaConvertida: false, definicao: '', anexos: [], autonomia: true, paralelismo: 1, destinoDaPr: '', situacao: 'aprovado',
  versao: 7, autor: '', atualizadoEm: new Date().toISOString(), itens: [], podeApagar: false, featureId: 0, epicId: 0, iteracao: '', board: '',
  papel: 'editor', skillDoPacote: '', skillsDoPacote: [{ nome: 'pacote-demo', tipo: 'somente-comando' }] };
function montar(p = projeto) {
  const resumos = vi.spyOn(api, 'resumosDosProjetos').mockResolvedValue([{ id: 1, slug: 'demo' } as ResumoDoProjeto]);
  const detalhe = vi.spyOn(api, 'projeto').mockResolvedValue(p);
  const links = vi.spyOn(api, 'artefatosDoProjeto').mockResolvedValue([]);
  const pedido = vi.spyOn(api, 'pedidoDoPacote');
  const gerar = vi.fn().mockResolvedValue(true);
  const criacao = { gerarArtefato: gerar, desabilitado: false, motivo: '', falha: '', origensDoPacote: [], pedidosDoPacote: [] } as unknown as CriacaoDoBoard;
  const tela = render(<PacoteDoBoard nome="Demo" slug="demo" branch="feature/demo" repositorios={['repo']} estados={[]}
    capacidade desabilitado={false} criacao={criacao} agora={Date.now()} />);
  return { ...tela, resumos, detalhe, links, pedido, gerar };
}
afterEach(() => vi.restoreAllMocks());

it('painel fechado não carrega detalhe, pedidos ou links; abrir consulta só o projeto e recolher mantém o Board', async () => {
  const tela = montar();
  expect(tela.resumos).not.toHaveBeenCalled(); expect(tela.detalhe).not.toHaveBeenCalled();
  expect(tela.links).not.toHaveBeenCalled(); expect(tela.pedido).not.toHaveBeenCalled();
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  await waitFor(() => expect(tela.links).toHaveBeenCalledWith('token-demo', 1, expect.any(AbortSignal)));
  expect(tela.detalhe).toHaveBeenCalledExactlyOnceWith('token-demo', 1, expect.any(AbortSignal));
  expect(tela.gerar).toHaveBeenCalledExactlyOnceWith('demo', { conferir: true });
  await userEvent.click(screen.getByRole('button', { name: 'Recolher pacote' }));
  expect(screen.queryByLabelText('Skill do pacote (opcional)')).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Ver pacote do projeto' })).toBeInTheDocument();
});

it('grava e limpa só o nome, usando a versão retornada da edição anterior', async () => {
  montar();
  const salvar = vi.spyOn(api, 'editarSkillDoPacote').mockResolvedValue({ ...projeto, versao: 8, skillDoPacote: 'pacote-demo' });
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  const campo = await screen.findByRole('option', { name: 'pacote-demo · somente comando' });
  expect(campo).toBeInTheDocument();
  await userEvent.selectOptions(screen.getByLabelText('Skill do pacote (opcional)'), 'pacote-demo');
  expect(salvar).toHaveBeenCalledExactlyOnceWith('token-demo', 1, 7, 'pacote-demo');
  await waitFor(() => expect(screen.getByLabelText('Skill do pacote (opcional)')).toHaveValue('pacote-demo'));
  salvar.mockResolvedValue({ ...projeto, versao: 9 });
  await userEvent.selectOptions(screen.getByLabelText('Skill do pacote (opcional)'), '');
  expect(salvar).toHaveBeenLastCalledWith('token-demo', 1, 8, '');
});

it.each([401, 403, 409])('edição recusada com %i conserva valor e bloqueia novas gravações até releitura', async status => {
  montar({ ...projeto, skillDoPacote: 'pacote-demo' });
  const salvar = vi.spyOn(api, 'editarSkillDoPacote').mockRejectedValue(new ErroDaApi(status, 'Releia o projeto antes de salvar.'));
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  await screen.findByRole('option', { name: 'pacote-demo · somente comando' });
  await userEvent.selectOptions(screen.getByLabelText('Skill do pacote (opcional)'), '');
  expect(salvar).toHaveBeenCalledOnce();
  expect(screen.getByRole('status')).toHaveTextContent('Releia o projeto');
  expect(screen.getByLabelText('Skill do pacote (opcional)')).toHaveValue('pacote-demo');
  expect(screen.getByLabelText('Skill do pacote (opcional)')).toBeDisabled();
});

it('leitor consulta o pacote mas não gera nem altera skill', async () => {
  const tela = montar({ ...projeto, papel: 'leitor' });
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  await screen.findByRole('option', { name: 'pacote-demo · somente comando' });
  expect(screen.getByRole('button', { name: 'Gerar pacote' })).toBeDisabled();
  expect(screen.getByLabelText('Skill do pacote (opcional)')).toBeDisabled();
  expect(tela.gerar).not.toHaveBeenCalled();
});
