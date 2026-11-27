import { act, render, renderHook, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { api, ErroDaApi } from '../api/cliente';
import type { Projeto, ResumoDoProjeto, PedidoDoPacote, OrigensDoPacote } from '../api/projetos';
import { PacoteDoBoard } from './PacoteDoBoard';
import { useCriacaoRemota, type CriacaoDoBoard } from './CriacaoRemota';
import type { ConversaDaMaquina } from './useConversaRemota';

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
  const conferir = vi.fn().mockResolvedValue('conferencia-atual');
  const criacao = { gerarArtefato: gerar, conferirPacote: conferir, desabilitado: false, motivo: '', falha: '', origensDoPacote: [], pedidosDoPacote: [] } as unknown as CriacaoDoBoard;
  const tela = render(<PacoteDoBoard nome="Demo" slug="demo" branch="feature/demo" repositorios={['repo']} estados={[]}
    capacidade desabilitado={false} criacao={criacao} agora={Date.now()} />);
  return { ...tela, resumos, detalhe, links, pedido, gerar, conferir, criacao };
}
afterEach(() => vi.restoreAllMocks());

it('configuração padrão é Debug e a edição envia só o valor com a versão atual', async () => {
  const tela = montar();
  const salvar = vi.spyOn(api, 'editarConfiguracaoDoPacote').mockResolvedValue({ ...projeto, versao: 8, configuracaoDoPacote: 'Release' });
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  await screen.findByRole('option', { name: 'pacote-demo · somente comando' });
  expect(screen.getByLabelText('Configuração do pacote')).toHaveValue('Debug');
  await userEvent.selectOptions(screen.getByLabelText('Configuração do pacote'), 'Release');
  expect(salvar).toHaveBeenCalledExactlyOnceWith('token-demo', 1, 7, 'Release');
  await waitFor(() => expect(screen.getByLabelText('Configuração do pacote')).toHaveValue('Release'));
  expect(tela.gerar).not.toHaveBeenCalled();
  salvar.mockResolvedValue({ ...projeto, versao: 9, configuracaoDoPacote: 'Debug' });
  await userEvent.selectOptions(screen.getByLabelText('Configuração do pacote'), 'Debug');
  expect(salvar).toHaveBeenLastCalledWith('token-demo', 1, 8, 'Debug');
});

it.each([401, 403, 409])('configuração recusada com %i conserva a escolha até releitura', async status => {
  montar({ ...projeto, configuracaoDoPacote: 'Release' });
  const salvar = vi.spyOn(api, 'editarConfiguracaoDoPacote').mockRejectedValue(new ErroDaApi(status, 'Releia a configuração.'));
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  await screen.findByRole('option', { name: 'pacote-demo · somente comando' });
  await userEvent.selectOptions(screen.getByLabelText('Configuração do pacote'), 'Debug');
  expect(salvar).toHaveBeenCalledOnce();
  expect(screen.getByRole('status')).toHaveTextContent('Releia a configuração');
  expect(screen.getByLabelText('Configuração do pacote')).toHaveValue('Release');
  expect(screen.getByLabelText('Configuração do pacote')).toBeDisabled();
});

it('a conferência usa o mesmo ID do transporte e descarta respostas sem comando associado', async () => {
  const texto = JSON.stringify({ tipo: 'origens-artefato', projeto: 'demo', resultados: [] });
  const criar = vi.fn().mockResolvedValue({ ok: true });
  const conversa = { conversa: { comandos: [], respostas: [
    { comandoId: null, texto }, { comandoId: '', texto }, { comandoId: 'conferencia-conhecida', texto },
  ] }, criar } as unknown as ConversaDaMaquina;
  const hook = renderHook(() => useCriacaoRemota({ maquina: undefined, conversa, agora: Date.now() }));
  expect(hook.result.current.origensDoPacote).toEqual([
    { tipo: 'origens-artefato', projeto: 'demo', resultados: [], comandoId: 'conferencia-conhecida' },
  ]);
  let id: string | null = null;
  await act(async () => { id = await hook.result.current.conferirPacote('demo'); });
  expect(id).toBeTruthy();
  expect(criar).toHaveBeenCalledExactlyOnceWith('projeto', { acao: 'artefato', projeto: 'demo', conferir: true }, id);
  criar.mockResolvedValue({ ok: false });
  await act(async () => { id = await hook.result.current.conferirPacote('demo'); });
  expect(id).toBeNull();
});

it('painel fechado não carrega detalhe, pedidos ou links; abrir consulta só o projeto e recolher mantém o Board', async () => {
  const tela = montar();
  expect(tela.resumos).not.toHaveBeenCalled(); expect(tela.detalhe).not.toHaveBeenCalled();
  expect(tela.links).not.toHaveBeenCalled(); expect(tela.pedido).not.toHaveBeenCalled();
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  await waitFor(() => expect(tela.links).toHaveBeenCalledWith('token-demo', 1, expect.any(AbortSignal)));
  expect(tela.detalhe).toHaveBeenCalledExactlyOnceWith('token-demo', 1, expect.any(AbortSignal));
  expect(tela.conferir).toHaveBeenCalledExactlyOnceWith('demo');
  expect(tela.gerar).not.toHaveBeenCalled();
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
  expect(screen.getByLabelText('Configuração do pacote')).toBeDisabled();
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
    capacidade desabilitado={false} agora={Date.now()} criacao={{ gerarArtefato: tela.gerar, conferirPacote: tela.conferir, desabilitado: false, motivo: '',
      origensDoPacote: [], pedidosDoPacote: [] } as unknown as CriacaoDoBoard}
    previsaoRemota={[{ repositorio: 'repo', produtor: { nome: 'pacote-demo', tipo: 'skill' }, motivo: 'Comando direto', aplicavel: true, formato: 'pacote' }]} />);
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  expect(await screen.findByText('skill pacote-demo · Comando direto · Debug')).toBeInTheDocument();
  await screen.findByRole('option', { name: 'pacote-demo · somente comando' });
  expect(tela.gerar).not.toHaveBeenCalled();
});

it.each(['leitor', 'editor'])('previsão de %s compara a URL da API com o nome sanitizado do retrato sem pedir conferência', async papel => {
  const tela = montar({ ...projeto, papel, repositorios: ['https://github.com/acme/app', 'https://github.com/acme/web.git/'] });
  tela.unmount();
  render(<PacoteDoBoard nome="Demo" slug="demo" branch="feature/demo" repositorios={['github.com/acme/web', 'github.com/acme/app']}
    estados={[]} capacidade={papel === 'editor'} desabilitado={false} agora={Date.now()} criacao={tela.criacao}
    previsaoRemota={[{ repositorio: 'github.com/acme/app', produtor: { nome: '', tipo: 'principal' }, motivo: 'Motivo do host', aplicavel: true, formato: 'pacote' },
      { repositorio: 'github.com/acme/web', produtor: { nome: '', tipo: 'previa' }, motivo: 'Front do host', aplicavel: true, formato: 'preview' }]} />);
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  expect(await screen.findByText('executável principal · Motivo do host · Debug')).toBeInTheDocument();
  expect(screen.getByText('prévia · Front do host')).toBeInTheDocument();
  expect(tela.conferir).not.toHaveBeenCalled(); expect(tela.gerar).not.toHaveBeenCalled();
  if (papel === 'leitor') {
    expect(screen.getByRole('button', { name: 'Gerar pacote' })).toBeDisabled();
    expect(screen.getByLabelText('Skill do pacote (opcional)')).toBeDisabled();
  }
});

it.each(['git@github.com:acme/app.git', 'ssh://git@github.com/acme/app.git', 'HTTPS://GitHub.com/acme/App.git/'])
('leitor reconhece a identidade sanitizada da origem %s', async url => {
  const tela = montar({ ...projeto, papel: 'leitor', repositorios: [url] }); tela.unmount();
  render(<PacoteDoBoard nome="Demo" slug="demo" branch="feature/demo" repositorios={['github.com/acme/app']} estados={[]}
    capacidade={false} desabilitado={false} agora={Date.now()} criacao={tela.criacao}
    previsaoRemota={[{ repositorio: 'github.com/acme/app', produtor: { nome: '', tipo: 'principal' }, motivo: 'Motivo do host', aplicavel: true, formato: 'pacote' }]} />);
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  expect(await screen.findByText('executável principal · Motivo do host · Debug')).toBeInTheDocument();
  expect(tela.conferir).not.toHaveBeenCalled(); expect(tela.gerar).not.toHaveBeenCalled();
});

it.each([
  { repo: 'github.com/outro/app', branch: 'feature/demo', skill: '', configuracao: 'Debug' as const },
  { repo: 'outro.example/acme/app', branch: 'feature/demo', skill: '', configuracao: 'Debug' as const },
  { repo: 'github.com/acme/app', branch: 'feature/outra', skill: '', configuracao: 'Debug' as const },
  { repo: 'github.com/acme/app', branch: 'feature/demo', skill: 'outra-skill', configuracao: 'Debug' as const },
  { repo: 'github.com/acme/app', branch: 'feature/demo', skill: '', configuracao: 'Release' as const },
])('normalização não aceita outra origem, branch, skill ou configuração: %j', async retrato => {
  const tela = montar({ ...projeto, papel: 'leitor', repositorios: ['https://github.com/acme/app'] }); tela.unmount();
  render(<PacoteDoBoard nome="Demo" slug="demo" branch={retrato.branch} skillNoRetrato={retrato.skill} configuracaoNoRetrato={retrato.configuracao} repositorios={[retrato.repo]}
    estados={[]} capacidade={false} desabilitado={false} agora={Date.now()} criacao={tela.criacao}
    previsaoRemota={[{ repositorio: retrato.repo, produtor: { nome: '', tipo: 'principal' }, motivo: 'PREVISAO_DIVERGENTE', aplicavel: true, formato: 'pacote' }]} />);
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  await screen.findByRole('option', { name: 'pacote-demo · somente comando' });
  expect(screen.queryByText(/PREVISAO_DIVERGENTE/)).not.toBeInTheDocument();
  expect(screen.getByText('Aguardando a previsão do produtor no host.')).toBeInTheDocument();
  expect(tela.conferir).not.toHaveBeenCalled(); expect(tela.gerar).not.toHaveBeenCalled();
});

it('editor usa a previsão atual sem conferir de novo e descarta a previsão da skill anterior ao editar', async () => {
  const tela = montar(); tela.unmount();
  const salvar = vi.spyOn(api, 'editarSkillDoPacote').mockResolvedValue({ ...projeto, versao: 8, skillDoPacote: 'pacote-demo' });
  render(<PacoteDoBoard nome="Demo" slug="demo" branch="feature/demo" repositorios={['repo']} estados={[]}
    capacidade desabilitado={false} agora={Date.now()} criacao={{ gerarArtefato: tela.gerar, conferirPacote: tela.conferir, desabilitado: false, motivo: '',
      origensDoPacote: [], pedidosDoPacote: [] } as unknown as CriacaoDoBoard}
    skillNoRetrato="" previsaoRemota={[{ repositorio: 'repo', produtor: { nome: '', tipo: 'principal' },
      motivo: 'Previsão antes da edição', aplicavel: true, formato: 'pacote' }]} />);
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  expect(await screen.findByText(/Previsão antes da edição/)).toBeInTheDocument();
  expect(tela.gerar).not.toHaveBeenCalled();
  await userEvent.selectOptions(screen.getByLabelText('Skill do pacote (opcional)'), 'pacote-demo');
  expect(salvar).toHaveBeenCalledExactlyOnceWith('token-demo', 1, 7, 'pacote-demo');
  await waitFor(() => expect(tela.conferir).toHaveBeenCalledExactlyOnceWith('demo'));
  expect(screen.queryByText(/Previsão antes da edição/)).not.toBeInTheDocument();
});

it('trocar e limpar skill ignora previsão não vazia da conversa e respostas tardias de outras conferências', async () => {
  const atual = { ...projeto, skillDoPacote: 'pacote-demo', skillsDoPacote: [...projeto.skillsDoPacote!,
    { nome: 'pacote-parametros', tipo: 'com-parametros' as const }] };
  const tela = montar(atual); tela.unmount();
  tela.conferir.mockResolvedValueOnce('nova-1').mockResolvedValueOnce('nova-2');
  const salvar = vi.spyOn(api, 'editarSkillDoPacote').mockResolvedValue({ ...atual, versao: 8, skillDoPacote: 'pacote-parametros' });
  const origem = (comandoId: string, motivo: string): OrigensDoPacote => ({ tipo: 'origens-artefato', projeto: 'demo', comandoId,
    resultados: [{ repositorio: 'repo', formato: 'pacote', aplicavel: true, produtor: motivo, motivo }] });
  let respostas = [origem('antiga', 'Produtor antigo')];
  const painel = () => <PacoteDoBoard nome="Demo" slug="demo" branch="feature/demo" repositorios={['repo']} estados={[]}
    capacidade desabilitado={false} agora={Date.now()} skillNoRetrato="pacote-demo"
    previsaoRemota={[{ repositorio: 'repo', produtor: { nome: 'pacote-demo', tipo: 'skill' }, motivo: 'Previsão original', aplicavel: true, formato: 'pacote' }]}
    criacao={{ ...tela.criacao, origensDoPacote: respostas }} />;
  const vista = render(painel());
  await userEvent.click(screen.getByRole('button', { name: 'Ver pacote do projeto' }));
  await screen.findByText(/Previsão original/);
  await userEvent.selectOptions(screen.getByLabelText('Skill do pacote (opcional)'), 'pacote-parametros');
  await waitFor(() => expect(tela.conferir).toHaveBeenCalledTimes(1));
  expect(screen.queryByText(/Produtor antigo/)).not.toBeInTheDocument();
  respostas = [...respostas, origem('outra-antiga', 'Resposta tardia antiga')]; vista.rerender(painel());
  expect(screen.queryByText(/Resposta tardia antiga/)).not.toBeInTheDocument();
  respostas = [...respostas, origem('nova-1', 'Produtor com parâmetros')]; vista.rerender(painel());
  expect(await screen.findByText(/Produtor com parâmetros/)).toBeInTheDocument();
  salvar.mockResolvedValue({ ...atual, versao: 9, skillDoPacote: '' });
  await userEvent.selectOptions(screen.getByLabelText('Skill do pacote (opcional)'), '');
  await waitFor(() => expect(tela.conferir).toHaveBeenCalledTimes(2));
  respostas = [...respostas, origem('nova-1', 'Parâmetros atrasados')]; vista.rerender(painel());
  expect(screen.queryByText(/Parâmetros atrasados|Produtor com parâmetros/)).not.toBeInTheDocument();
  respostas = [...respostas, origem('nova-2', 'Executável principal')]; vista.rerender(painel());
  expect(await screen.findByText(/Executável principal · Executável principal/)).toBeInTheDocument();
  expect(tela.gerar).not.toHaveBeenCalled();
});
