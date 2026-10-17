import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { renderizar } from '../testes/renderizar';
import { escritas, tokenDoGestor, tokenValido } from '../testes/servidor';

afterEach(() => vi.restoreAllMocks());

describe('Contexto das empresas para o admin do board (US #556)', () => {
  it('o admin escolhe a empresa e vê tudo, inclusive o contexto pessoal e a sugestão do agente', { timeout: 15_000 }, async () => {
    renderizar('/admin/contexto', tokenValido());

    // Duas idas à API antes da visão (as empresas e os usuários, depois o contexto): a espera é maior que a padrão.
    expect(await screen.findByText('Prefiro testes pequenos', {}, { timeout: 5_000 })).toBeInTheDocument();
    expect(screen.getByText('Rode só a suíte do projeto alterado').closest('tr')).toHaveTextContent('Sugestão');
    expect(screen.getByText('.NET 10 e React 19').closest('tr')!.querySelector('button')).not.toBeNull(); // edita a da empresa
    expect(screen.getByRole('button', { name: 'Aprovar Ana Souza em financeiro' })).toBeInTheDocument();

    await userEvent.selectOptions(screen.getByLabelText('Empresa'), '5');
    expect(await screen.findByText(/Nenhum item aqui/)).toBeInTheDocument();
  });

  it('o admin grava em qualquer nível — no usuário escolhendo de quem é — e promove e exclui pelas rotas dele', { timeout: 15_000 }, async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/admin/contexto', tokenValido());
    const formulario = await screen.findByRole('form', { name: 'Gravar no contexto' });
    await screen.findByText('Prefiro testes pequenos');

    expect(within(within(formulario).getByLabelText('Nível')).getAllByRole('option').map((o) => o.textContent)).toEqual(['Empresa', 'Equipe', 'Usuário']);
    await userEvent.selectOptions(within(formulario).getByLabelText('Nível'), 'usuario');
    expect(within(formulario).getByRole('button', { name: 'Gravar' })).toBeDisabled(); // sem dono, não grava
    await userEvent.selectOptions(within(formulario).getByLabelText('Usuário'), '2');
    await userEvent.type(within(formulario).getByLabelText('Texto'), 'Revisado pelo admin');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Gravar' }));
    expect(await screen.findByText('contexto.geral gravado no nível usuário.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Promover contexto.testes (Usuário)' }));
    expect(await screen.findByText('Sugestão contexto.testes promovida a instrução.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Excluir contexto.stack (Empresa)' }));
    expect(await screen.findByText('contexto.stack excluído.')).toBeInTheDocument();

    await waitFor(() => expect(escritas).toEqual([
      { metodo: 'PUT', caminho: '/api/admin/empresas/4/contexto', corpo: { nivel: 'usuario', chave: 'contexto.geral', valor: 'Revisado pelo admin', equipe: '', usuarioId: 2 } },
      { metodo: 'POST', caminho: '/api/admin/empresas/4/contexto/4/promover', corpo: null },
      { metodo: 'DELETE', caminho: '/api/admin/empresas/4/contexto/1', corpo: null },
    ]));
  });

  it('o gestor não entra na visão do admin', async () => {
    renderizar('/admin/contexto', tokenDoGestor());
    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
  });
});
