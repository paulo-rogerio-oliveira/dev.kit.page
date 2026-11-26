import { act, render, renderHook, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ArtefatosDoCard, tamanhoDoArtefato, useArtefatosDoBoard, type PropsDoPacote } from './ArtefatosDoProjeto';
import { origensDoPacote } from './CriacaoRemota';
import { api, ErroDaApi } from '../api/cliente';
import type { Artefato, PedidoDoPacote } from '../api/projetos';

const repo = 'https://github.com/acme/desktop';
const web = 'https://github.com/acme/web';
const sha = 'abcdef0123456789abcdef0123456789abcdef01';
const agora = Date.now();
const pacote: Artefato = { id: 41, projetoId: 1, tipo: 'pacote', repositorio: repo, sha, link: 'https://exemplo.test/pacote',
  expiraEm: new Date(agora + 3600_000).toISOString(), hash: 'a'.repeat(64), tamanho: 445, ambiente: '', autor: 'Demo', criadoEm: new Date(agora).toISOString() };
const base: PropsDoPacote = { nome: 'Projeto', slug: 'projeto', artefatos: [], gerar: vi.fn().mockResolvedValue(true),
  desabilitado: false, motivo: '', agora, repositorios: [repo, web], branch: 'feature/projeto', capacidade: true };
const resultado = { id: 2, projetoId: 1, tipo: 'pacote', repositorio: repo, sha, estado: 'publicado', situacao: 'pronto',
  link: pacote.link, validoAte: pacote.expiraEm, hash: pacote.hash, tamanho: 445, ambiente: '', detalhe: 'Pacote verificado pelo executor.',
  criadoEm: pacote.criadoEm, idRemoto: pacote.id, ciclo: { estado: 'publicado', etapa: 'concluido', branch: 'feature/projeto', pedidoId: 'pedido-a' } };
const pedido: PedidoDoPacote = { id: 'pedido-a', projetoId: 1, criadoEm: pacote.criadoEm, estado: 'parcial', resultados: [resultado,
  { ...resultado, id: 3, idRemoto: 0, repositorio: web, tipo: 'preview', link: '', estado: 'nao-aplicavel', detalhe: 'Destino da prévia não configurado. Informe o Static Web App da prévia do projeto.' }] };

describe('pacote do projeto sem confirmação', () => {
  it.each([[445, '445 bytes'], [1023, '1023 bytes'], [1024, '1 KB'], [12_800, '12,5 KB'], [1048575, '1,0 MB'], [43_200_000, '41,2 MB']])('%i bytes → %s', (bytes, esperado) => {
    expect(tamanhoDoArtefato(bytes as number)).toBe(esperado);
  });

  it('um clique pede todos os repositórios sem diálogo, repositório, SHA, formato ou confirmação', async () => {
    const gerar = vi.fn().mockResolvedValue(true);
    render(<ArtefatosDoCard {...base} gerar={gerar} />);
    await userEvent.click(screen.getByRole('button', { name: 'Gerar pacote' }));
    expect(gerar).toHaveBeenCalledExactlyOnceWith('projeto', {});
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Repositório')).not.toBeInTheDocument();
    expect(screen.queryByText('Confirmar geração')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Gerar pacote' })).toBeDisabled();
  });

  it.each(['enfileirando', 'enfileirado', 'gerando', 'agente-pendente'])('desabilita durante %s, inclusive enquanto o agente resolve parâmetros', async estado => {
    const gerar = vi.fn();
    render(<ArtefatosDoCard {...base} gerar={gerar} estados={[{ ...resultado, estado, etapa: 'fila' }]} />);
    expect(screen.getByRole('button', { name: 'Gerar pacote' })).toBeDisabled();
    expect(screen.getByText(/Um pedido deste projeto está em andamento/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Gerar pacote' }));
    expect(gerar).not.toHaveBeenCalled();
    if (estado === 'agente-pendente') expect(screen.getByText(/agente resolvendo os parâmetros da skill/)).toBeInTheDocument();
  });

  it('mostra todas as origens, motivos completos, branch, SHA, tamanho e validade, com anteriores compactos', () => {
    const motivo = `SDK ausente. ${'Detalhe do serviço. '.repeat(100)}`;
    const anterior = { ...pedido, id: 'anterior', criadoEm: new Date(agora - 60000).toISOString(), estado: 'em-andamento',
      resultados: [{ ...resultado, id: 1, estado: 'agente-pendente' }] };
    render(<ArtefatosDoCard {...base} artefatos={[pacote]} pedidos={[anterior, { ...pedido, resultados: [...pedido.resultados,
      { ...resultado, id: 4, repositorio: 'https://github.com/acme/servico', estado: 'falhou', detalhe: motivo, link: '' }] }]} />);
    expect([...document.querySelectorAll('.pacote-resultado p')].find(p => p.textContent === motivo)).toBeDefined();
    expect(screen.getByText(pedido.resultados[1].detalhe)).toBeInTheDocument();
    expect(screen.getByText('Não gerado')).toBeInTheDocument();
    expect(screen.getByText('Falhou')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Baixar o pacote de desktop · abcdef0' })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Baixar o pacote de servico · abcdef0' })).not.toBeInTheDocument();
    expect(screen.getAllByText(/445 bytes/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/feature\/projeto/).length).toBeGreaterThan(0);
    const anteriores = screen.getByText('Anteriores').parentElement!;
    const resumo = anteriores.querySelector('summary')!;
    expect(resumo).toHaveTextContent('Gerando · agente resolvendo os parâmetros da skill');
    expect(resumo.closest('details')).not.toHaveAttribute('open');
  });

  it('não esconde repositórios sem resultado', () => {
    render(<ArtefatosDoCard {...base} pedidos={[{ ...pedido, resultados: [resultado] }]} />);
    expect(screen.getByText(/Sem resultado neste pedido/)).toHaveTextContent('web');
  });

  it('coleção e retrato simultâneos não duplicam pedidos anteriores e conservam resultado legado avulso', async () => {
    const anterior = { ...pedido, id: 'anterior', criadoEm: new Date(agora - 60000).toISOString(),
      resultados: pedido.resultados.map((r, i) => ({ ...r, id: 10 + i })) };
    const estados = [...pedido.resultados, ...anterior.resultados].map(r => ({ ...r, etapa: 'concluido' }));
    const legado = { ...estados[0], id: 99, repositorio: 'repo-legado', detalhe: 'Legado preservado' };
    render(<ArtefatosDoCard {...base} pedidos={[pedido, anterior]} estados={[...estados, legado]} />);
    const anteriores = within(screen.getByText('Anteriores').parentElement!);
    expect(anteriores.getAllByRole('group')).toHaveLength(2);
    expect(anteriores.getAllByText(/repo-legado/)).toHaveLength(2);
    const resumo = anteriores.getAllByRole('group')[0].querySelector('summary')!;
    await userEvent.click(resumo);
    expect(resumo.closest('details')).toHaveAttribute('open');
    expect(anteriores.getAllByText('desktop')).toHaveLength(1);
    expect(anteriores.getAllByText('web')).toHaveLength(1);
    expect(anteriores.getByText('Legado preservado')).toBeInTheDocument();
  });

  it('Configurar mostra o comando do host sem formulário, geração ou escrita remota', async () => {
    const gerar = vi.fn();
    const escrever = vi.spyOn(globalThis, 'fetch');
    render(<ArtefatosDoCard {...base} gerar={gerar} pedidos={[{ ...pedido, resultados: [
      { ...resultado, tipo: 'preview', estado: 'nao-aplicavel', detalhe: 'Destino da prévia não configurado.', link: '' },
    ] }]} />);
    await userEvent.click(screen.getByRole('button', { name: 'Configurar' }));
    expect(screen.getByRole('note')).toHaveTextContent('devcli projeto --projeto');
    expect(screen.getByRole('note')).toHaveTextContent('--swa-app <app> --swa-grupo <grupo> --swa-assinatura <id> --aplicar');
    expect(screen.getByRole('note')).toHaveTextContent('todos os projetos da máquina');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(gerar).not.toHaveBeenCalled(); expect(escrever).not.toHaveBeenCalled();
    escrever.mockRestore();
  });

  it.each(['local-pronto', 'envio-pendente', 'registro-pendente'])('retoma %s do pacote no mesmo ID', async estado => {
    const gerar = vi.fn().mockResolvedValue(true);
    render(<ArtefatosDoCard {...base} gerar={gerar} estados={[{ ...resultado, estado, etapa: 'upload', idRemoto: 0 }]} />);
    await userEvent.click(screen.getByRole('button', { name: estado === 'registro-pendente' ? 'Tentar registro novamente' : 'Tentar envio novamente' }));
    expect(gerar).toHaveBeenCalledExactlyOnceWith('projeto', { retomar: 2 });
  });

  it('prévia local não recebe ação de upload', () => {
    render(<ArtefatosDoCard {...base} estados={[{ ...resultado, tipo: 'preview', estado: 'local-pronto', etapa: 'preflight', idRemoto: 0 }]} />);
    expect(screen.queryByRole('button', { name: 'Tentar envio novamente' })).not.toBeInTheDocument();
    expect(screen.getByText(/Prévia disponível somente no host/)).toBeInTheDocument();
  });

  it('renova links vencidos pela consulta sem gerar novamente nem abrir URL antiga', async () => {
    const gerar = vi.fn(); const atualizar = vi.fn();
    render(<ArtefatosDoCard {...base} artefatos={[{ ...pacote, expiraEm: new Date(agora - 1).toISOString() }]} gerar={gerar} atualizar={atualizar} />);
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Atualizar lista e links' }));
    expect(atualizar).toHaveBeenCalledOnce(); expect(gerar).not.toHaveBeenCalled();
  });

  it('ACL bloqueia geração, retomada e edição da skill; host incompatível não envia', async () => {
    const gerar = vi.fn(); const salvarSkill = vi.fn();
    const tela = render(<ArtefatosDoCard {...base} gerar={gerar} salvarSkill={salvarSkill} desabilitado capacidade={false} motivo="Exige editor" estados={[{ ...resultado, estado: 'local-pronto', etapa: 'upload' }]} />);
    expect(screen.getByRole('button', { name: 'Gerar pacote' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Tentar envio novamente' })).toBeDisabled();
    expect(screen.getByLabelText('Skill do pacote (opcional)')).toBeDisabled();
    expect(screen.getByText('Exige editor')).toBeInTheDocument();
    expect(screen.queryByText(/Atualize o host/)).not.toBeInTheDocument();
    tela.rerender(<ArtefatosDoCard {...base} gerar={gerar} capacidade={false} />);
    await userEvent.click(screen.getByRole('button', { name: 'Gerar pacote' }));
    expect(gerar).not.toHaveBeenCalled(); expect(salvarSkill).not.toHaveBeenCalled();
  });

  it('grava e limpa a skill só pelo nome e preserva a escolha após recusa de concorrência', async () => {
    const salvarSkill = vi.fn().mockResolvedValue(undefined);
    const skillsDoPacote = [{ nome: 'pacote-dev', tipo: 'somente-comando' as const }];
    const tela = render(<ArtefatosDoCard {...base} skillsDoPacote={skillsDoPacote} salvarSkill={salvarSkill} />);
    await userEvent.selectOptions(screen.getByLabelText('Skill do pacote (opcional)'), 'pacote-dev');
    expect(salvarSkill).toHaveBeenCalledExactlyOnceWith('pacote-dev');
    tela.rerender(<ArtefatosDoCard {...base} skillDoPacote="pacote-dev" skillsDoPacote={skillsDoPacote} salvarSkill={salvarSkill} />);
    await userEvent.selectOptions(screen.getByLabelText('Skill do pacote (opcional)'), '');
    expect(salvarSkill).toHaveBeenLastCalledWith('');
    salvarSkill.mockRejectedValue(new ErroDaApi(409, 'O projeto mudou. Releia antes de salvar.'));
    await userEvent.selectOptions(screen.getByLabelText('Skill do pacote (opcional)'), '');
    expect(screen.getByRole('status')).toHaveTextContent('O projeto mudou');
    expect(screen.getByLabelText('Skill do pacote (opcional)')).toHaveValue('pacote-dev');
  });

  it('a lista de produtores e a edição não transportam conteúdo, comando nem argumentos da skill', async () => {
    const previsao = origensDoPacote(JSON.stringify({ tipo: 'origens-artefato', projeto: 'projeto', resultados: [{
      Repositorio: repo, Formato: 'pacote', Aplicavel: true, Motivo: 'Comando direto', Produtor: 'pacote-dev',
      SkillDoPacote: { Conteudo: 'SEGREDO', Argumento: 'SEGREDO' },
    }] }));
    expect(JSON.stringify(previsao)).not.toMatch(/SEGREDO|Conteudo|Argumento|comando|conteudo|argumentos/);
    const fetch = vi.spyOn(globalThis, 'fetch').mockResolvedValue(Response.json({ skillDoPacote: 'pacote-dev' }));
    try {
      await api.editarSkillDoPacote('token', 1, 7, 'pacote-dev');
      expect(JSON.parse(String(fetch.mock.calls[0][1]?.body))).toEqual({ versaoEsperada: 7, skillDoPacote: 'pacote-dev' });
    } finally { fetch.mockRestore(); }
  });

  it('recusa de autenticação preserva metadados e bloqueia downloads', () => {
    render(<ArtefatosDoCard {...base} artefatos={[pacote]} linksAutorizados={false} />);
    expect(screen.getByText(/sessão autorizada/)).toBeInTheDocument();
    expect(screen.getByText('abcdef0')).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it.each([401, 403, 404, 503])('consulta %i preserva os artefatos anteriores e permite retentar', async status => {
    const consulta = vi.spyOn(api, 'artefatosRecentes').mockResolvedValue([{ projetoId: 1, slug: 'projeto', nome: 'Projeto', artefatos: [pacote] }]);
    try {
      const hook = renderHook(() => useArtefatosDoBoard('token-demo', ''));
      await waitFor(() => expect(hook.result.current.porSlug.get('projeto')).toEqual([pacote]));
      consulta.mockRejectedValue(new ErroDaApi(status, 'Serviço indisponível'));
      act(() => hook.result.current.atualizar());
      await waitFor(() => expect(hook.result.current.erro).not.toBe(''));
      expect(hook.result.current.bloqueado).toBe([401, 403].includes(status));
      expect(hook.result.current.porSlug.get('projeto')).toEqual([pacote]);
      consulta.mockResolvedValue([{ projetoId: 1, slug: 'projeto', nome: 'Projeto', artefatos: [pacote] }]);
      act(() => hook.result.current.atualizar());
      await waitFor(() => expect(hook.result.current.erro).toBe(''));
      hook.unmount();
    } finally { consulta.mockRestore(); }
  });
});
