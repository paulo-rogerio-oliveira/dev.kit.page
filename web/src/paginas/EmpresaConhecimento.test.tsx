import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { renderizar } from '../testes/renderizar';
import { escritas, tokenDoDev, tokenDoGestor, tokenValido } from '../testes/servidor';

afterEach(() => vi.restoreAllMocks());

describe('Repositórios da empresa (US #491)', () => {
  // Os testes que digitam muito (userEvent, letra a letra) levam o teto do Usuarios.test: no build carregado, 5 s não bastam.
  it('o gestor audita o catálogo de repositórios, corrige uma descrição e remove', { timeout: 15_000 }, async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/empresa/conhecimento', tokenDoGestor());

    const linha = (await screen.findByText('https://github.com/acme/vendas-api')).closest('tr')!;
    expect(linha).toHaveTextContent(/API de vendas \(\.NET 8\).*Ana Souza/);

    await userEvent.click(within(linha).getByRole('button', { name: 'Editar https://github.com/acme/vendas-api' }));
    const formulario = screen.getByRole('form', { name: 'Descrever um repositório' });
    expect(within(formulario).getByLabelText('Caminho')).toHaveValue('https://github.com/acme/vendas-api');
    const descricao = within(formulario).getByLabelText('Descrição');
    await userEvent.clear(descricao);
    await userEvent.type(descricao, 'API de vendas e pedidos (.NET 8)');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Gravar' }));
    expect(await screen.findByText('https://github.com/acme/vendas-api gravado no catálogo da empresa.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Remover https://github.com/acme/vendas-api' }));

    await waitFor(() => expect(escritas).toEqual([
      { metodo: 'POST', caminho: '/api/empresa/repositorios', corpo: { origem: 'https://github.com/acme/vendas-api', descricao: 'API de vendas e pedidos (.NET 8)' } },
      { metodo: 'DELETE', caminho: '/api/empresa/repositorios?origem=https://github.com/acme/vendas-api', corpo: null },
    ]));
  });

  it('o dev audita e corrige o catálogo, mas não remove', async () => {
    renderizar('/empresa/conhecimento', tokenDoDev());

    expect(await screen.findByText('https://github.com/acme/vendas-api')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Editar https://github.com/acme/vendas-api' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Remover/ })).not.toBeInTheDocument();
  });

  /** As convenções viraram o contexto (US #556): o link antigo da aba leva para a página nova. */
  it('o link antigo das convenções leva ao contexto', async () => {
    renderizar('/empresa/conhecimento?aba=convencoes', tokenDoDev());
    expect(await screen.findByRole('heading', { name: 'Contexto do dev.kit' })).toBeInTheDocument();
  });

  it('o admin, que não é de empresa, vai para o dashboard', async () => {
    renderizar('/empresa/conhecimento', tokenValido());
    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
  });
});
