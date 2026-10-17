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
