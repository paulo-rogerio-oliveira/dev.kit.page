import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { vi } from 'vitest';
import { renderizar } from '../testes/renderizar';
import { escritas, sugestoesDaEmpresa, tokenDoDev, tokenDoGestor, tokenValido } from '../testes/servidor';

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

  it('aba Contexto (US #550): o gestor grava o da empresa e o da equipe e só lê o dos usuários', { timeout: 15_000 }, async () => {
    renderizar('/empresa/conhecimento?aba=contexto', tokenDoGestor());

    const empresa = await screen.findByRole('region', { name: 'Empresa' });
    expect(within(empresa).getByText('.NET 10 na API e React 19 na web.')).toBeInTheDocument();
    const equipe = screen.getByRole('region', { name: 'Equipe' });
    expect(within(equipe).getByLabelText('Equipe')).toHaveValue('vendas');
    expect(within(equipe).getByText(/Angular 19 no front\.\s+Testes com Jasmine\./)).toBeInTheDocument();
    const usuarios = screen.getByRole('region', { name: 'Usuários' });
    expect(within(usuarios).getByText('Respostas curtas, com o porquê.')).toBeInTheDocument();
    expect(within(usuarios).queryByRole('button')).not.toBeInTheDocument();

    const doFormulario = within(empresa).getByRole('form', { name: 'Gravar no contexto da empresa' });
    await userEvent.type(within(doFormulario).getByLabelText('Tema'), 'revisao');
    await userEvent.type(within(doFormulario).getByLabelText('Texto'), 'Checklist do time.');
    await userEvent.click(within(doFormulario).getByRole('button', { name: 'Gravar' }));
    expect(await screen.findByText('Tema revisao gravado no contexto.')).toBeInTheDocument();

    // Uma equipe nova é digitada.
    const campo = within(screen.getByRole('region', { name: 'Equipe' })).getByLabelText('Equipe');
    await userEvent.clear(campo);
    await userEvent.type(campo, 'Financeiro');
    const daEquipe = within(screen.getByRole('region', { name: 'Equipe' })).getByRole('form', { name: 'Gravar no contexto da equipe financeiro' });
    await userEvent.type(within(daEquipe).getByLabelText('Tema'), 'stack');
    await userEvent.type(within(daEquipe).getByLabelText('Texto'), 'Power BI.');
    await userEvent.click(within(daEquipe).getByRole('button', { name: 'Gravar' }));

    await waitFor(() => expect(escritas.map((e) => e.corpo)).toEqual([
      { nivel: 'empresa', chave: 'contexto.revisao', valor: 'Checklist do time.', time: '' },
      { nivel: 'time', chave: 'contexto.stack', valor: 'Power BI.', time: 'financeiro' },
    ]));
  });

  it('aba Contexto (US #550): o dev lê o da empresa e o da equipe, sem editar', async () => {
    renderizar('/empresa/conhecimento?aba=contexto', tokenDoDev());

    const empresa = await screen.findByRole('region', { name: 'Empresa' });
    expect(within(empresa).getByText('.NET 10 na API e React 19 na web.')).toBeInTheDocument();
    expect(screen.queryByRole('form')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Editar o tema/ })).not.toBeInTheDocument();
    expect(screen.getByText(/Só o gestor edita o da empresa e o das equipes\./)).toBeInTheDocument();
    // US #663: a sugestão do agente aparece à parte; o dev não promove, mas pode descartar (a API confere o autor).
    const sugestoes = within(empresa).getByRole('list', { name: 'Contexto da empresa — sugestões do agente' });
    expect(within(sugestoes).getByText('Rode o lint antes do PR.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Promover a regra/ })).not.toBeInTheDocument();
    expect(within(sugestoes).getByRole('button', { name: 'Descartar a sugestão testes' })).toBeInTheDocument();
  });

  it('aba Contexto (US #663): o gestor promove a sugestão do agente com a versão lida, e o 409 aparece como conflito', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/empresa/conhecimento?aba=contexto', tokenDoGestor());

    const empresa = await screen.findByRole('region', { name: 'Empresa' });
    expect(within(empresa).getByRole('heading', { name: 'Regra (do usuário)' })).toBeInTheDocument();
    expect(within(empresa).getByRole('heading', { name: 'Sugestão (do agente)' })).toBeInTheDocument();
    await userEvent.click(within(empresa).getByRole('button', { name: 'Promover a regra a sugestão testes' }));

    expect(await screen.findByText('Sugestão testes promovida a regra.')).toBeInTheDocument();
    expect(escritas).toContainEqual({ metodo: 'POST', caminho: '/api/empresa/contexto/101/promover', corpo: { versaoEsperada: 1 } });

    // A equipe "vendas" tem a sugestão na versão 2; uma leitura velha (versão 1) recebe o conflito.
    sugestoesDaEmpresa[1].versao = 1;
    try {
      const equipe = screen.getByRole('region', { name: 'Equipe' });
      await userEvent.click(within(equipe).getByRole('button', { name: 'Promover a regra a sugestão stack' }));
      expect(await screen.findByRole('alert')).toHaveTextContent('A sugestão mudou depois que você a leu');
    } finally {
      sugestoesDaEmpresa[1].versao = 2;
    }
  });

  it('aba Contexto (US #663): descartar tira só a sugestão', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/empresa/conhecimento?aba=contexto', tokenDoGestor());

    const empresa = await screen.findByRole('region', { name: 'Empresa' });
    await userEvent.click(within(empresa).getByRole('button', { name: 'Descartar a sugestão testes' }));

    expect(await screen.findByText('Sugestão testes descartada.')).toBeInTheDocument();
    expect(escritas).toContainEqual({ metodo: 'DELETE', caminho: '/api/empresa/contexto/101', corpo: null });
    expect(escritas.some((e) => e.metodo === 'PUT')).toBe(false);
  });

  it('a aba Convenções não mistura o contexto', async () => {
    renderizar('/empresa/conhecimento?aba=convencoes', tokenDoGestor());

    expect(await screen.findByText('feature/{equipe}/{id}')).toBeInTheDocument();
    expect(screen.queryByText('contexto.stack')).not.toBeInTheDocument();
  });

  it('o admin, que não é de empresa, vai para o dashboard', async () => {
    renderizar('/empresa/conhecimento', tokenValido());
    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
  });
});
