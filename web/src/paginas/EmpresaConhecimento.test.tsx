import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { renderizar } from '../testes/renderizar';
import { escritas, tokenDoDev, tokenDoGestor, tokenValido } from '../testes/servidor';

afterEach(() => vi.restoreAllMocks());

describe('Repositórios e convenções da empresa (US #491)', () => {
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

  it('as convenções aparecem em seus níveis, com de quem são, o autor, e o filtro por nível', async () => {
    renderizar('/empresa/conhecimento?aba=convencoes', tokenDoGestor());

    const tabela = (await screen.findByText('feature/{equipe}/{id}')).closest('table')!;
    const [empresa, time, usuario] = within(tabela).getAllByRole('row').slice(1);
    expect(empresa).toHaveTextContent(/Empresa.*branch\.feature.*feature\/\{equipe\}\/\{id\}.*Gestor A/);
    expect(time).toHaveTextContent(/Time.*vendas.*vendas\/\{id\}.*Ana Souza/);
    expect(usuario).toHaveTextContent(/Usuário.*Ana Souza.*branch\.bug.*ana\/bug-\{id\}/);

    await userEvent.click(screen.getByRole('button', { name: /^Time/ }));
    expect(within(screen.getByRole('table')).getAllByRole('row').slice(1)).toHaveLength(1);
  });

  it('o gestor grava a convenção da empresa e a de outro tipo de branch, edita a do time e apaga uma', { timeout: 15_000 }, async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/empresa/conhecimento?aba=convencoes', tokenDoGestor());

    const formulario = await screen.findByRole('form', { name: 'Gravar uma convenção' });
    expect(within(formulario).getByLabelText('Nível')).toHaveValue('empresa');
    expect(within(formulario).getByLabelText('Convenção')).toHaveValue('branch.feature');
    await userEvent.type(within(formulario).getByLabelText('Valor'), 'dev/{{id}');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Gravar' }));
    expect(await screen.findByText('branch.feature gravada no nível empresa.')).toBeInTheDocument();

    // Um tipo de branch próprio do processo: a convenção é digitada como branch.<tipo do work item>.
    const convencao = within(formulario).getByLabelText('Convenção');
    await userEvent.clear(convencao);
    await userEvent.type(convencao, 'branch.hotfix');
    await userEvent.type(within(formulario).getByLabelText('Valor'), 'hotfix/{{id}');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Gravar' }));
    expect(await screen.findByText('branch.hotfix gravada no nível empresa.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Editar branch.feature (Time)' }));
    expect(within(formulario).getByLabelText('Time')).toHaveValue('vendas');
    expect(within(formulario).getByLabelText('Valor')).toHaveValue('vendas/{id}');

    await userEvent.click(screen.getByRole('button', { name: 'Apagar branch.bug (Usuário)' }));

    await waitFor(() => expect(escritas.map((e) => e.corpo)).toEqual([
      { nivel: 'empresa', chave: 'branch.feature', valor: 'dev/{id}', time: '' },
      { nivel: 'empresa', chave: 'branch.hotfix', valor: 'hotfix/{id}', time: '' },
      { nivel: 'usuario', chave: 'branch.bug', valor: '', time: '' },
    ]));
  });

  it('o dev audita e edita a do time e a dele, mas não a da empresa nem remove do catálogo', async () => {
    renderizar('/empresa/conhecimento?aba=convencoes', tokenDoDev());

    expect(await screen.findByText('feature/{equipe}/{id}')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Editar branch.feature (Empresa)' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Editar branch.feature (Time)' })).toBeInTheDocument();
    const niveis = within(screen.getByLabelText('Nível')).getAllByRole('option').map((o) => o.textContent);
    expect(niveis).toEqual(['Time', 'Usuário (a sua)']);

    await userEvent.click(screen.getByRole('tab', { name: 'Repositórios' }));
    expect(await screen.findByText('https://github.com/acme/vendas-api')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Remover/ })).not.toBeInTheDocument();
  });

  it('o admin, que não é de empresa, vai para o dashboard', async () => {
    renderizar('/empresa/conhecimento', tokenValido());
    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
  });
});
