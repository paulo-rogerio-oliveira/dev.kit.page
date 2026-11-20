import { act, render, renderHook, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ArtefatosDoCard, tamanhoDoArtefato, useArtefatosDoBoard } from './ArtefatosDoProjeto';
import { api, ErroDaApi } from '../api/cliente';
import type { Artefato } from '../api/projetos';

describe('tamanho do pacote no card de artefatos', () => {
  // (controle-v4, item 5) A evidência ponta a ponta achou o pacote de 445 bytes como "0 MB": abaixo de 1 MB sai em KB ou bytes.
  it.each([
    [445, '445 bytes'],
    [1023, '1023 bytes'],
    [1024, '1 KB'],
    [12_800, '12,5 KB'],
    [1_048_524, '1.023,9 KB'],
    [1048575, '1,0 MB'],
    [1048576, '1,0 MB'],
    [43_200_000, '41,2 MB'],
  ])('%i bytes → %s', (bytes, esperado) => {
    expect(tamanhoDoArtefato(bytes)).toBe(esperado);
  });

  it('o link Baixar do pacote pequeno mostra o tamanho em bytes, e não "0 MB"', () => {
    const pacote: Artefato = {
      id: 1, projetoId: 11, tipo: 'pacote', repositorio: 'https://github.com/empresa-demo/git.kit', sha: '2412df94c1', link: 'https://exemplo.test/zip',
      expiraEm: new Date(Date.now() + 23.5 * 3600_000).toISOString(), hash: 'ab'.repeat(32), tamanho: 445, ambiente: '', autor: 'Ana Demo', criadoEm: new Date().toISOString(),
    };
    render(<ArtefatosDoCard nome="controle-v4" slug="controle-v4" artefatos={[pacote]} gerar={() => undefined} desabilitado={false} motivo="" agora={Date.now()} />);
    const baixar = screen.getByRole('link', { name: 'Baixar o pacote de git.kit · 2412df9' });
    expect(baixar).toHaveTextContent('445 bytes · expira em 23 h');
    expect(baixar).not.toHaveTextContent('0 MB');
  });
});

describe('geração e resultado dos artefatos', () => {
  const repo = 'https://github.com/acme/desktop';
  const web = 'https://github.com/acme/web';
  const sha = 'abcdef0123456789abcdef0123456789abcdef01';
  const base = { nome: 'Projeto', slug: 'projeto', artefatos: [] as Artefato[], gerar: vi.fn().mockResolvedValue(true),
    desabilitado: false, motivo: '', agora: Date.now(), repositorios: [repo, web], capacidade: true };
  const pacote: Artefato = { id: 2, projetoId: 1, tipo: 'pacote', repositorio: repo, sha, link: 'https://exemplo.test/pacote',
    expiraEm: new Date(Date.now() - 60000).toISOString(), hash: 'a'.repeat(64), tamanho: 42, ambiente: '', autor: 'Demo', criadoEm: new Date().toISOString() };

  it('confere a origem escolhida e só gera depois da confirmação do SHA integral', async () => {
    const gerar = vi.fn().mockResolvedValue(true);
    const tela = render(<ArtefatosDoCard {...base} gerar={gerar} />);
    await userEvent.click(screen.getByRole('button', { name: 'Gerar artefato' }));
    expect(screen.getByLabelText('Repositório')).toHaveFocus();
    await userEvent.selectOptions(screen.getByLabelText('Repositório'), web);
    await userEvent.selectOptions(screen.getByLabelText('Artefato'), 'preview');
    await userEvent.type(screen.getByLabelText('Commit (opcional)'), sha.slice(0, 7));
    await userEvent.click(screen.getByRole('button', { name: 'Conferir origem' }));
    expect(gerar).toHaveBeenCalledExactlyOnceWith('projeto', { conferir: true, repositorio: web, formato: 'preview', sha: sha.slice(0, 7) });
    expect(screen.queryByRole('button', { name: 'Confirmar geração' })).not.toBeInTheDocument();
    tela.rerender(<ArtefatosDoCard {...base} gerar={gerar} plano={{ tipo: 'origem-artefato', projeto: 'projeto', repositorio: web, branch: 'feature/projeto', sha, formato: 'preview' }} />);
    expect(screen.getByText(sha)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar geração' }));
    expect(gerar).toHaveBeenLastCalledWith('projeto', { repositorio: web, formato: 'preview', sha });
    expect(gerar).toHaveBeenCalledTimes(2);
    expect(screen.getByRole('status')).toHaveTextContent('A execução e a publicação ainda precisam terminar');
  });

  it('mostra falha após o aceite, fila, execução e pendência sem expor download local; retoma o mesmo ID', async () => {
    const gerar = vi.fn().mockResolvedValue(true);
    render(<ArtefatosDoCard {...base} gerar={gerar} estados={['enfileirado', 'gerando', 'local-pronto', 'envio-pendente', 'registro-pendente', 'falhou'].map((estado, i) => ({
      id: i + 1, tipo: 'pacote', repositorio: repo, sha, estado, etapa: 'upload', detalhe: estado === 'falhou' ? 'Host sem SDK: instale a capacidade e tente novamente.' : '', idRemoto: 0, criadoEm: new Date().toISOString(),
    }))} />);
    for (const texto of ['Na fila do executor', 'Em execução', 'Arquivo local pronto', 'Envio pendente', 'Registro pendente', 'Falhou']) expect(screen.getByText(texto)).toBeInTheDocument();
    expect(screen.getByText(/Host sem SDK/)).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Tentar registro novamente' }));
    expect(gerar).toHaveBeenCalledExactlyOnceWith('projeto', { retomar: 5 });
  });

  it('link expirado não abre e a renovação só atualiza a lista, sem gerar pacote', async () => {
    const gerar = vi.fn(); const atualizar = vi.fn();
    render(<ArtefatosDoCard {...base} artefatos={[pacote]} gerar={gerar} atualizar={atualizar} />);
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    expect(screen.getByText(/O link venceu/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Atualizar lista e links' }));
    expect(atualizar).toHaveBeenCalledOnce(); expect(gerar).not.toHaveBeenCalled();
  });

  it.each([
    ['falhou', 'Entre novamente no host para consultar o destino.'],
    ['falhou', 'SDK ausente: instale a capacidade no host.'],
    ['local-pronto', 'Preview sem registro remoto.'],
  ])('recupera %s preservando repo/tipo/SHA e exige confirmar um novo pedido (%s)', async (estado, detalhe) => {
    const gerar = vi.fn().mockResolvedValue(true);
    const estados = [{ id: 31, tipo: 'preview', repositorio: web, sha, estado, etapa: 'preflight', detalhe, idRemoto: 0, criadoEm: new Date().toISOString() }];
    const tela = render(<ArtefatosDoCard {...base} gerar={gerar} estados={estados} />);
    expect(screen.queryByRole('button', { name: 'Tentar envio novamente' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Preparar novo pedido' }));
    expect(screen.getByLabelText('Repositório')).toHaveValue(web);
    expect(screen.getByLabelText('Repositório')).toHaveFocus();
    expect(screen.getByLabelText('Artefato')).toHaveValue('preview');
    expect(screen.getByLabelText('Commit (opcional)')).toHaveValue(sha);
    expect(screen.getByRole('status')).toHaveTextContent('O resultado anterior será preservado');
    expect(gerar).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(gerar).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Preparar novo pedido' }));
    await userEvent.click(screen.getByRole('button', { name: 'Conferir origem' }));
    expect(gerar).toHaveBeenCalledExactlyOnceWith('projeto', { repositorio: web, formato: 'preview', sha, conferir: true });
    tela.rerender(<ArtefatosDoCard {...base} gerar={gerar} estados={estados} plano={{ tipo: 'origem-artefato', projeto: 'projeto', repositorio: web, sha, formato: 'preview', branch: 'feature/projeto' }} />);
    gerar.mockResolvedValue(false);
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar geração' }));
    expect(gerar).toHaveBeenLastCalledWith('projeto', { repositorio: web, formato: 'preview', sha });
    expect(screen.getByRole('status')).toHaveTextContent('O pedido não foi enviado');
    expect(screen.getByText(detalhe)).toBeInTheDocument();
    expect(screen.getByLabelText('Commit (opcional)')).toHaveValue(sha);
    gerar.mockResolvedValue(true);
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar geração' }));
    expect(gerar).toHaveBeenCalledTimes(3);
    expect(screen.getByText(detalhe)).toBeInTheDocument();
  });

  it('leitor não prepara recuperação e pacote local retoma somente o envio', async () => {
    const gerar = vi.fn().mockResolvedValue(true);
    const estado = { id: 32, tipo: 'pacote', repositorio: repo, sha, estado: 'falhou', etapa: 'preflight', detalhe: 'SDK ausente', idRemoto: 0, criadoEm: new Date().toISOString() };
    const tela = render(<ArtefatosDoCard {...base} gerar={gerar} desabilitado estados={[estado]} />);
    expect(screen.getByRole('button', { name: 'Preparar novo pedido' })).toBeDisabled();
    tela.rerender(<ArtefatosDoCard {...base} gerar={gerar} estados={[{ ...estado, estado: 'local-pronto' }]} />);
    await userEvent.click(screen.getByRole('button', { name: 'Tentar envio novamente' }));
    expect(gerar).toHaveBeenCalledExactlyOnceWith('projeto', { retomar: 32 });
  });

  it('leitor não gera nem retoma; host antigo mostra a falta de capacidade sem enviar pedido', async () => {
    const gerar = vi.fn();
    const tela = render(<ArtefatosDoCard {...base} gerar={gerar} desabilitado motivo="Exige editor" />);
    expect(screen.getByRole('button', { name: 'Gerar artefato' })).toBeDisabled();
    tela.rerender(<ArtefatosDoCard {...base} gerar={gerar} capacidade={false} />);
    await userEvent.click(screen.getByRole('button', { name: 'Gerar artefato' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Atualize o dev.kit');
    expect(screen.getByRole('button', { name: 'Conferir origem' })).toBeDisabled();
    expect(gerar).not.toHaveBeenCalled();
  });

  it('preserva os metadados mas bloqueia o link anterior depois de recusa de autenticação ou ACL', () => {
    render(<ArtefatosDoCard {...base} artefatos={[{ ...pacote, expiraEm: new Date(Date.now() + 60000).toISOString() }]} linksAutorizados={false} />);
    expect(screen.getByText('desktop · abcdef0')).toBeInTheDocument();
    expect(screen.getByText(/sessão autorizada/)).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it.each([401, 403, 404, 503])('consulta %i preserva os artefatos anteriores e permite retentar a leitura', async status => {
    const consulta = vi.spyOn(api, 'artefatosRecentes').mockResolvedValue([{ projetoId: 1, slug: 'projeto', nome: 'Projeto', artefatos: [pacote] }]);
    try {
      const hook = renderHook(() => useArtefatosDoBoard('token-demo', ''));
      await waitFor(() => expect(hook.result.current.porSlug.get('projeto')).toEqual([pacote]));
      consulta.mockRejectedValue(new ErroDaApi(status, 'Serviço indisponível'));
      act(() => hook.result.current.atualizar());
      await waitFor(() => expect(hook.result.current.erro).not.toBe(''));
      expect(hook.result.current.bloqueado).toBe([401, 403].includes(status));
      expect(hook.result.current.porSlug.get('projeto')).toEqual([pacote]);
      consulta.mockResolvedValue([{ projetoId: 1, slug: 'projeto', nome: 'Projeto', artefatos: [{ ...pacote, expiraEm: new Date(Date.now() + 60000).toISOString() }] }]);
      act(() => hook.result.current.atualizar());
      await waitFor(() => expect(hook.result.current.erro).toBe(''));
      expect(hook.result.current.bloqueado).toBe(false);
      hook.unmount();
    } finally { consulta.mockRestore(); }
  });
});
