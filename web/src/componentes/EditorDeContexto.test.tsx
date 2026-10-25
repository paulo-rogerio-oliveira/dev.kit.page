import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import type { ConvencaoDaEmpresa } from '../api/tipos';
import { EditorDeContexto, TETO_DO_CONTEXTO } from './EditorDeContexto';

afterEach(() => vi.restoreAllMocks());

const temas: ConvencaoDaEmpresa[] = [
  { nivel: 'empresa', chave: 'contexto.stack', valor: '.NET 10\nReact 19', time: '', usuario: '', autor: 'Gestor A', atualizadoEm: '2026-10-16T09:00:00Z' },
  { nivel: 'empresa', chave: 'branch.feature', valor: 'feature/x', time: '', usuario: '', autor: 'Gestor A', atualizadoEm: null },
];

describe('EditorDeContexto (US #550)', () => {
  it('lista só os temas do contexto, com o texto de várias linhas e quem gravou', () => {
    render(<EditorDeContexto rotulo="Contexto da empresa" itens={temas} editavel aoGravar={async () => true} />);

    const lista = screen.getByRole('list', { name: 'Contexto da empresa' });
    expect(within(lista).getAllByRole('listitem')).toHaveLength(1);
    expect(within(lista).getByText('stack')).toBeInTheDocument();
    expect(within(lista).getByText(/\.NET 10\s+React 19/)).toHaveClass('texto-do-contexto');
    expect(within(lista).getByText(/Gestor A/)).toBeInTheDocument();
  });

  it('grava um tema novo com o contador do teto e normaliza o nome do tema', async () => {
    const aoGravar = vi.fn(async () => true);
    render(<EditorDeContexto rotulo="Seu contexto" itens={[]} editavel aoGravar={aoGravar} vazio="Nada ainda." />);

    expect(screen.getByText('Nada ainda.')).toBeInTheDocument();
    const formulario = screen.getByRole('form', { name: 'Gravar no seu contexto' });
    await userEvent.type(within(formulario).getByLabelText('Tema'), 'Revisão Final');
    await userEvent.type(within(formulario).getByLabelText('Texto'), 'Rode os testes.');
    expect(within(formulario).getByText(`15 / ${TETO_DO_CONTEXTO} caracteres. O mesmo tema troca o texto.`)).toBeInTheDocument();
    expect(within(formulario).getByLabelText('Texto')).toHaveAttribute('maxLength', String(TETO_DO_CONTEXTO));
    await userEvent.click(within(formulario).getByRole('button', { name: 'Gravar' }));

    expect(aoGravar).toHaveBeenCalledWith('contexto.revisao-final', 'Rode os testes.');
    expect(within(formulario).getByLabelText('Tema')).toHaveValue('');
  });

  it('editar preenche o formulário; remover confirma e grava vazio', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const aoGravar = vi.fn(async () => true);
    render(<EditorDeContexto rotulo="Contexto da empresa" itens={temas} editavel aoGravar={aoGravar} />);

    await userEvent.click(screen.getByRole('button', { name: 'Editar o tema stack' }));
    expect(screen.getByLabelText('Tema')).toHaveValue('stack');
    expect(screen.getByLabelText('Texto')).toHaveValue('.NET 10\nReact 19');

    await userEvent.click(screen.getByRole('button', { name: 'Remover o tema stack' }));
    expect(aoGravar).toHaveBeenCalledWith('contexto.stack', '');
  });

  it('sem edição, só lê', () => {
    render(<EditorDeContexto rotulo="Contexto da empresa" itens={temas} editavel={false} aoGravar={async () => true} />);

    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.queryByRole('form')).not.toBeInTheDocument();
  });
});

describe('EditorDeContexto — Regra (do usuário) × Sugestão (do agente) (US #663)', () => {
  const comSugestoes: ConvencaoDaEmpresa[] = [
    ...temas,
    { nivel: 'empresa', chave: 'contexto.stack', valor: 'Angular 19 standalone.', time: '', usuario: '', autor: 'Ana Souza', atualizadoEm: '2026-10-22T18:00:00Z', id: 7, origem: 'agente', versao: 2 },
    { nivel: 'empresa', chave: 'contexto.testes', valor: 'Rode o lint antes do PR.', time: '', usuario: '', autor: 'Ana Souza', atualizadoEm: '2026-10-22T18:10:00Z', id: 8, origem: 'agente', versao: 1 },
  ];

  it('mostra as duas partes com rótulos distintos, e a sugestão com autor e data, fora da lista de regras', () => {
    render(<EditorDeContexto rotulo="Contexto da empresa" itens={comSugestoes} editavel aoGravar={async () => true} />);

    expect(screen.getByRole('heading', { name: 'Regra (do usuário)' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Sugestão (do agente)' })).toBeInTheDocument();
    const regras = screen.getByRole('list', { name: 'Contexto da empresa' });
    expect(within(regras).getAllByRole('listitem')).toHaveLength(1);
    expect(within(regras).queryByText('Angular 19 standalone.')).not.toBeInTheDocument();

    const sugestoes = screen.getByRole('list', { name: 'Contexto da empresa — sugestões do agente' });
    expect(within(sugestoes).getAllByRole('listitem')).toHaveLength(2);
    expect(within(sugestoes).getAllByText(/agente de Ana Souza/)).toHaveLength(2);
    // O tema que já tem regra avisa que a regra prevalece.
    expect(within(sugestoes).getByText('Este tema já tem regra: a regra prevalece.')).toBeInTheDocument();
  });

  it('promover confirma (avisando que a regra atual será substituída) e chama a ação com a sugestão', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(true);
    const aoPromover = vi.fn(async () => true);
    render(<EditorDeContexto rotulo="Contexto da empresa" itens={comSugestoes} editavel aoGravar={async () => true} aoPromover={aoPromover} aoDescartar={async () => true} />);

    await userEvent.click(screen.getByRole('button', { name: 'Promover a regra a sugestão stack' }));

    expect(confirmar).toHaveBeenCalledWith(expect.stringContaining('A regra atual deste tema será substituída.'));
    expect(aoPromover).toHaveBeenCalledWith(expect.objectContaining({ id: 7, versao: 2, origem: 'agente' }));
  });

  it('descartar confirma e chama a ação só com a sugestão (a regra do mesmo tema fica)', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const aoDescartar = vi.fn(async () => true);
    const aoGravar = vi.fn(async () => true);
    render(<EditorDeContexto rotulo="Contexto da empresa" itens={comSugestoes} editavel aoGravar={aoGravar} aoPromover={async () => true} aoDescartar={aoDescartar} />);

    await userEvent.click(screen.getByRole('button', { name: 'Descartar a sugestão stack' }));

    expect(aoDescartar).toHaveBeenCalledWith(expect.objectContaining({ id: 7 }));
    expect(aoGravar).not.toHaveBeenCalled();
  });

  it('sem edição não promove; o descarte só aparece com a ação (a API decide se é o autor)', () => {
    const { rerender } = render(<EditorDeContexto rotulo="Contexto da empresa" itens={comSugestoes} editavel={false} aoGravar={async () => true} aoPromover={async () => true} />);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();

    rerender(<EditorDeContexto rotulo="Contexto da empresa" itens={comSugestoes} editavel={false} aoGravar={async () => true} aoPromover={async () => true} aoDescartar={async () => true} />);
    expect(screen.queryByRole('button', { name: /Promover/ })).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /Descartar a sugestão/ })).toHaveLength(2);
  });

  it('sem sugestões, diz que não há', () => {
    render(<EditorDeContexto rotulo="Contexto da empresa" itens={temas} editavel aoGravar={async () => true} />);

    expect(screen.getByText('Nenhuma sugestão dos agentes neste nível.')).toBeInTheDocument();
  });
});
