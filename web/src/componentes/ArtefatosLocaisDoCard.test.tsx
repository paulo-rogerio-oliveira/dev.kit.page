import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ArtefatosDoCard } from './ArtefatosDoProjeto';
import { artefatosLocais, maquinaDoPreview } from '../testes/artefatosLocais';

const props = () => ({ nome: 'Projeto demo', slug: 'demo', artefatos: [], gerar: vi.fn(), desabilitado: false,
  motivo: '', agora: Date.now(), maquina: maquinaDoPreview(), atualizado: true, consultar: vi.fn(), operarPreview: vi.fn() });

describe('artefatos locais no card', () => {
  it('mostra host e URL como texto; ações usam o mesmo snapshot e consultar não pede processo', async () => {
    const p = props();
    const locais = artefatosLocais();
    const { rerender } = render(<ArtefatosDoCard {...p} locais={locais} />);
    expect(screen.getByText('Preview local · DEMO-PC')).toBeInTheDocument();
    expect(screen.getByText('http://127.0.0.1:4317/')).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Abrir no host' }));
    expect(p.operarPreview).toHaveBeenLastCalledWith({ acao: 'preview-local', projeto: 'demo', artefato: 3,
      receita: 'a'.repeat(64), aplicacao: 'site', operacao: 'abrir', versao: 3, conjunto: 'e'.repeat(64) });
    await userEvent.click(screen.getByRole('button', { name: 'Consultar' }));
    expect(p.consultar).toHaveBeenCalledOnce();
    expect(p.operarPreview).toHaveBeenCalledTimes(1);
    await userEvent.click(screen.getByRole('button', { name: 'Parar' }));
    expect(p.operarPreview).toHaveBeenLastCalledWith(expect.objectContaining({ operacao: 'parar', versao: 3, artefato: 3 }));
    const preview = locais.artefatos[0].previews[0];
    locais.artefatos[0].previews = [{ ...preview, pedido: 'parar', situacao: 'parando', versao: 4, url: null,
      prontidao: { front: false, api: false, dependencias: false, jornada: false } }];
    rerender(<ArtefatosDoCard {...p} locais={locais} />);
    expect(screen.queryByText('http://127.0.0.1:4317/')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Abrir no host' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Parar' })).toBeDisabled();
  });

  it('iniciar escolhe explicitamente a aplicação consultada e não antecipa prontidão', async () => {
    const p = props();
    const locais = artefatosLocais();
    Object.assign(locais.artefatos[0].previews[0], { situacao: 'nao-solicitado', versao: 0, pedido: 'nenhum', url: null,
      diagnostico: 'Nenhum processo solicitado.', prontidao: { front: false, api: false, dependencias: false, jornada: false }, componentes: [] });
    render(<ArtefatosDoCard {...p} locais={locais} />);
    await userEvent.click(screen.getByRole('button', { name: 'Iniciar preview no host' }));
    expect(p.operarPreview).toHaveBeenCalledWith({ acao: 'preview-local', projeto: 'demo', artefato: 3,
      receita: 'a'.repeat(64), aplicacao: 'site', operacao: 'iniciar', versao: 0, conjunto: 'e'.repeat(64) });
    expect(screen.queryByText('http://127.0.0.1:4317/')).not.toBeInTheDocument();
    expect(screen.getByText('não solicitado')).toBeInTheDocument();
  });

  it.each(['leitor-ou-ponte-antiga', 'app-fechado', 'desconectado', 'retrato-antigo'])('recusa escrita em %s mas mantém consulta', async modo => {
    const p = props();
    const locais = artefatosLocais();
    if (modo === 'leitor-ou-ponte-antiga') locais.operacoesDisponiveis = false;
    if (modo === 'app-fechado') p.maquina.appAberto = false;
    if (modo === 'desconectado') p.maquina.conectada = false;
    if (modo === 'retrato-antigo') p.atualizado = false;
    render(<ArtefatosDoCard {...p} locais={locais} />);
    for (const name of ['Abrir no host', 'Parar', 'Iniciar preview no host']) expect(screen.getByRole('button', { name })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Consultar' }));
    expect(p.consultar).toHaveBeenCalledOnce();
    expect(p.operarPreview).not.toHaveBeenCalled();
  });

  it('não expõe URL pronta quando jornada ou máquina não foram confirmadas', () => {
    const locais = artefatosLocais();
    locais.artefatos[0].previews[0].prontidao.jornada = false;
    render(<ArtefatosDoCard {...props()} locais={locais} />);
    expect(screen.getByText('prontidão não confirmada')).toBeInTheDocument();
    expect(screen.queryByText('http://127.0.0.1:4317/')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Abrir no host' })).toBeDisabled();
  });

  it('timeout confirmado substitui gerando sem perder fontes, comando e log; exit isolado não inventa causa', () => {
    const p = props();
    const locais = artefatosLocais();
    const a = locais.artefatos[0];
    a.situacao = 'gerando'; a.previews = [];
    const { rerender } = render(<ArtefatosDoCard {...p} locais={locais} />);
    expect(screen.getByText('gerando')).toBeInTheDocument();
    a.situacao = 'falhou'; a.diagnostico = 'Comando confirmou timeout.';
    a.comando = { ...a.comando!, exit: 124, motivoTermino: 'timeout', logParcial: 'Saída parcial preservada.' };
    rerender(<ArtefatosDoCard {...p} locais={locais} />);
    expect(screen.queryByText('gerando')).not.toBeInTheDocument();
    const geracao = within(screen.getByRole('region', { name: 'Geração 3' }));
    expect(geracao.getByText('Saída parcial preservada.')).toBeInTheDocument();
    expect(geracao.getAllByText('a'.repeat(40))).toHaveLength(2);
    expect(geracao.getAllByText('d'.repeat(40))).toHaveLength(2);
    expect(geracao.getByText('timeout · exit 124')).toBeInTheDocument();
    a.comando.motivoTermino = null;
    rerender(<ArtefatosDoCard {...p} locais={locais} />);
    expect(geracao.getByText('Causa de término não registrada · exit 124')).toBeInTheDocument();
    expect(p.operarPreview).not.toHaveBeenCalled();
  });
});
