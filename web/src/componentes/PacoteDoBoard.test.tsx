import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api, ErroDaApi } from '../api/cliente';
import type { Projeto, ResumoDoProjeto, PedidoDoPacote } from '../api/projetos';
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
  const pedido = vi.spyOn(api, 'pedidosDoPacote').mockResolvedValue([]);
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

it('resposta de links em andamento não libera a skill após 409; só reler o projeto libera com a nova versão', async () => {
  const tela = montar({ ...projeto, skillDoPacote: 'pacote-demo' });
  let concluir!: (p: PedidoDoPacote[]) => void;
  tela.pedido.mockReturnValueOnce(new Promise(pronto => { concluir = pronto; }));
  const salvar = vi.spyOn(api, 'editarSkillDoPacote').mockRejectedValue(new ErroDaApi(409, 'O projeto mudou.'));
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  await screen.findByRole('option', { name: 'pacote-demo · somente comando' });
  await userEvent.selectOptions(screen.getByLabelText('Skill do pacote (opcional)'), '');
  expect(screen.getByLabelText('Skill do pacote (opcional)')).toBeDisabled();
  concluir([]);
  await screen.findByRole('button', { name: 'Atualizar lista e links' });
  expect(screen.getByLabelText('Skill do pacote (opcional)')).toBeDisabled();
  expect(salvar).toHaveBeenCalledTimes(1);
  tela.detalhe.mockResolvedValue({ ...projeto, versao: 8, skillDoPacote: 'pacote-demo' });
  await userEvent.click(screen.getByRole('button', { name: 'Atualizar lista e links' }));
  await waitFor(() => expect(screen.getByLabelText('Skill do pacote (opcional)')).toBeEnabled());
  salvar.mockResolvedValue({ ...projeto, versao: 9, skillDoPacote: '' });
  await userEvent.selectOptions(screen.getByLabelText('Skill do pacote (opcional)'), '');
  expect(salvar).toHaveBeenLastCalledWith('token-demo', 1, 8, '');
});

it('reabrir após refresh descobre pedidos do desktop sem IDs na conversa e conserva todas as origens', async () => {
  const grupos: PedidoDoPacote[] = Array.from({ length: 7 }, (_, i) => ({ id: `desktop-${i}`, projetoId: 1,
    criadoEm: new Date(Date.now() - i * 60_000).toISOString(), estado: 'falhou', resultados: [1, 2, 3].map(n => ({
      id: i * 3 + n, projetoId: 1, tipo: 'pacote', repositorio: `repo-${n}`, sha: 'a'.repeat(40), situacao: 'falhou',
      link: '', validoAte: null, hash: '', tamanho: 0, ambiente: '', detalhe: `Motivo completo ${n}`, criadoEm: new Date().toISOString(),
      idRemoto: 0, estado: 'falhou', ciclo: { estado: 'falhou', etapa: 'compilacao', branch: 'feature/desktop', pedidoId: `desktop-${i}` },
      produtor: { nome: 'pacote-demo', tipo: 'skill' },
    })) }));
  const abrirEConferir = async () => {
    const tela = montar();
    tela.pedido.mockResolvedValue(grupos);
    await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
    await waitFor(() => expect(screen.queryByText('Nenhum pedido identificado nesta consulta.')).not.toBeInTheDocument());
    const ultimo = within(screen.getByText('Último pacote').parentElement!);
    for (const n of [1, 2, 3]) expect(ultimo.getByText(`repo-${n}`)).toBeInTheDocument();
    expect(ultimo.getAllByText(/feature\/desktop/)).toHaveLength(3);
    expect(tela.container.querySelectorAll('details')).toHaveLength(6);
    expect(tela.pedido).toHaveBeenCalledWith('token-demo', 1, expect.any(AbortSignal));
    return tela;
  };
  const primeira = await abrirEConferir(); primeira.unmount();
  const segunda = await abrirEConferir(); segunda.unmount();
});

it('usa previsão nome/tipo projetada pelo host, inclusive para leitor', async () => {
  const tela = montar({ ...projeto, papel: 'leitor' }); tela.unmount();
  render(<PacoteDoBoard nome="Demo" slug="demo" branch="feature/demo" repositorios={['repo']} estados={[]}
    capacidade desabilitado={false} agora={Date.now()} criacao={{ gerarArtefato: tela.gerar, desabilitado: false, motivo: '',
      origensDoPacote: [], pedidosDoPacote: [] } as unknown as CriacaoDoBoard}
    previsaoRemota={[{ repositorio: 'repo', produtor: { nome: 'pacote-demo', tipo: 'skill' }, motivo: 'Comando direto', aplicavel: true, formato: 'pacote' }]} />);
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  expect(await screen.findByText('skill pacote-demo · Comando direto')).toBeInTheDocument();
  await screen.findByRole('option', { name: 'pacote-demo · somente comando' });
  expect(tela.gerar).not.toHaveBeenCalled();
});

it('editor usa a previsão atual sem conferir de novo e descarta a previsão da skill anterior ao editar', async () => {
  const tela = montar(); tela.unmount();
  const salvar = vi.spyOn(api, 'editarSkillDoPacote').mockResolvedValue({ ...projeto, versao: 8, skillDoPacote: 'pacote-demo' });
  render(<PacoteDoBoard nome="Demo" slug="demo" branch="feature/demo" repositorios={['repo']} estados={[]}
    capacidade desabilitado={false} agora={Date.now()} criacao={{ gerarArtefato: tela.gerar, desabilitado: false, motivo: '',
      origensDoPacote: [], pedidosDoPacote: [] } as unknown as CriacaoDoBoard}
    skillNoRetrato="" previsaoRemota={[{ repositorio: 'repo', produtor: { nome: '', tipo: 'principal' },
      motivo: 'Previsão antes da edição', aplicavel: true, formato: 'pacote' }]} />);
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  expect(await screen.findByText(/Previsão antes da edição/)).toBeInTheDocument();
  expect(tela.gerar).not.toHaveBeenCalled();
  await userEvent.selectOptions(screen.getByLabelText('Skill do pacote (opcional)'), 'pacote-demo');
  expect(salvar).toHaveBeenCalledExactlyOnceWith('token-demo', 1, 7, 'pacote-demo');
  await waitFor(() => expect(tela.gerar).toHaveBeenCalledExactlyOnceWith('demo', { conferir: true }));
  expect(screen.queryByText(/Previsão antes da edição/)).not.toBeInTheDocument();
});
