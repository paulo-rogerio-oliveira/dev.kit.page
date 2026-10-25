import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderizar } from '../testes/renderizar';
import { entregues } from '../testes/navegador';
import { vi } from 'vitest';
import { cadastros, escritas, tokenDoGestor, tokenValido } from '../testes/servidor';

afterEach(() => vi.restoreAllMocks());

describe('Empresas (US #381; visual da US #461)', () => {
  it('lista as empresas com o plano, os assentos em uso, o código de adesão e o gestor', async () => {
    renderizar('/empresas', tokenValido());

    const linha = (await screen.findByText('Empresa A')).closest('tr')!;
    expect(within(linha).getByText('Empresarial')).toHaveClass('insignia');
    expect(within(linha).getByText('3 de 10 em uso')).toBeInTheDocument();
    expect(within(linha).getByRole('progressbar', { name: 'Assentos em uso de Empresa A' })).toHaveAttribute('aria-valuenow', '3');
    expect(within(linha).getByText('DK-7QH4-M2XA')).toBeInTheDocument();
    expect(within(linha).getByText('Sem gestor')).toBeInTheDocument();
    expect(within(linha).getByText('E', { selector: '.avatar' })).toBeInTheDocument();

    const outra = screen.getByText('Empresa B').closest('tr')!;
    expect(within(outra).getByText('gestor.b')).toBeInTheDocument();
    expect(within(outra).queryByText('Sem gestor')).not.toBeInTheDocument();
    expect(within(outra).getByText('1 de 1 em uso')).toBeInTheDocument();

    // O cabeçalho resume a lista; a página usa o Topo com o menu do admin.
    expect(screen.getByRole('heading', { level: 1, name: 'Empresas' })).toBeInTheDocument();
    expect(screen.getByText('2 empresas · 11 assentos contratados · 4 em uso')).toBeInTheDocument();
    expect(within(linha).getByText('11.222.333/0001-81 · São Paulo/SP')).toBeInTheDocument();
    expect(within(outra).getByText('sem CNPJ cadastrado')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Empresas' })).toHaveClass('link-ativo');
    expect(screen.getByRole('link', { name: 'Catálogo' })).toHaveAttribute('href', '/catalogo');
  });

  it('copia o código de adesão', async () => {
    renderizar('/empresas', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Copiar o código de adesão de Empresa A' }));

    expect(entregues.copiado).toBe('DK-7QH4-M2XA');
    expect(await screen.findByText('Copiado')).toBeInTheDocument();
  });

  it('cria a empresa pelo painel lateral, com o nome, o plano e os assentos', async () => {
    renderizar('/empresas', tokenValido());
    await userEvent.click(await screen.findByRole('button', { name: '+ Nova empresa' }));
    const painel = await screen.findByRole('complementary', { name: 'Nova empresa' });
    const formulario = within(painel).getByRole('form', { name: 'Nova empresa' });
    expect(within(painel).getByText('Ao criar, a empresa recebe um código de adesão para os devs entrarem.')).toBeInTheDocument();

    await userEvent.type(within(formulario).getByLabelText('Nome'), 'Empresa C');
    await userEvent.clear(within(formulario).getByLabelText('Assentos'));
    await userEvent.type(within(formulario).getByLabelText('Assentos'), '25');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Criar empresa' }));

    await waitFor(() => expect(cadastros.empresas).toEqual([{ nome: 'Empresa C', plano: 'Empresarial', assentos: 25 }]));
    expect(within(formulario).getByLabelText('Nome')).toHaveValue('');
  });

  it('convida o gestor pelo botão da linha e mostra a senha inicial uma vez', async () => {
    renderizar('/empresas', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Convidar o gestor de Empresa A' }));
    await userEvent.type(screen.getByLabelText('Login do gestor de Empresa A'), 'gestor.a');
    await userEvent.click(screen.getByRole('button', { name: 'Enviar convite' }));

    expect(await screen.findByText('DkSENHA1234a1')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('o primeiro acesso exige a troca');
    expect(cadastros.gestores).toEqual([{ empresa: '4', login: 'gestor.a' }]);
    expect(screen.queryByLabelText('Login do gestor de Empresa A')).not.toBeInTheDocument();
  });

  it('o convite se cancela sem chamar a API', async () => {
    renderizar('/empresas', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Convidar o gestor de Empresa A' }));
    await userEvent.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(screen.queryByLabelText('Login do gestor de Empresa A')).not.toBeInTheDocument();
    expect(cadastros.gestores).toEqual([]);
  });

  it('o painel Contexto (US #550): o admin vê os três níveis e grava o contexto de um usuário da empresa', { timeout: 15_000 }, async () => {
    renderizar('/empresas', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Contexto de Empresa A' }));
    const painel = await screen.findByRole('complementary', { name: 'Contexto de Empresa A' });
    expect(await within(painel).findByText('.NET 10 na API e React 19 na web.')).toBeInTheDocument();
    expect(within(painel).getByText(/Angular 19 no front/)).toBeInTheDocument();

    // Os usuários são os da empresa (o admin, sem empresa, fica de fora); o contexto da Ana aparece e é editável.
    const usuarios = within(painel).getByRole('region', { name: 'Usuários' });
    expect(within(within(usuarios).getByLabelText('Usuário')).getAllByRole('option').map((o) => o.textContent)).toEqual(['Ana Souza (ana.dev)']);
    expect(within(usuarios).getByText('Respostas curtas, com o porquê.')).toBeInTheDocument();
    const formulario = within(usuarios).getByRole('form', { name: 'Gravar no contexto de ana souza' });
    await userEvent.type(within(formulario).getByLabelText('Tema'), 'estilo');
    await userEvent.type(within(formulario).getByLabelText('Texto'), 'Explique o porquê.');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Gravar' }));

    expect(await within(painel).findByText('Tema estilo gravado.')).toBeInTheDocument();
    expect(escritas).toEqual([{
      metodo: 'PUT', caminho: '/api/empresas/4/convencoes',
      corpo: { nivel: 'usuario', chave: 'contexto.estilo', valor: 'Explique o porquê.', time: '', usuario: 'ana.dev' },
    }]);

    await userEvent.click(within(painel).getByRole('button', { name: 'Fechar' }));
    expect(screen.queryByRole('complementary', { name: 'Contexto de Empresa A' })).not.toBeInTheDocument();
  });

  it('o painel Contexto (US #663): o admin promove e descarta a sugestão do agente pelas rotas dele', { timeout: 15_000 }, async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/empresas', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Contexto de Empresa A' }));
    const painel = await screen.findByRole('complementary', { name: 'Contexto de Empresa A' });
    const empresa = await within(painel).findByRole('region', { name: 'Empresa' });
    expect(within(empresa).getByText('Rode o lint antes do PR.')).toBeInTheDocument();

    await userEvent.click(within(empresa).getByRole('button', { name: 'Promover a regra a sugestão testes' }));
    expect(await within(painel).findByText('Sugestão testes promovida a regra.')).toBeInTheDocument();
    await userEvent.click(within(empresa).getByRole('button', { name: 'Descartar a sugestão testes' }));
    expect(await within(painel).findByText('Sugestão testes descartada.')).toBeInTheDocument();

    expect(escritas).toEqual([
      { metodo: 'POST', caminho: '/api/empresas/4/contexto/101/promover', corpo: { versaoEsperada: 1 } },
      { metodo: 'DELETE', caminho: '/api/empresas/4/contexto/101', corpo: null },
    ]);
  });

  // US #687 (proposta admin-empresas v009): o Editar por linha abre o painel com os dados, o plano, o contrato e os assentos.
  it('edita a empresa pelo painel: os dados, o contrato e os assentos com a prévia, nunca abaixo dos em uso', async () => {
    renderizar('/empresas', tokenValido());

    await userEvent.click(await screen.findByRole('button', { name: 'Editar Empresa A' }));
    const painel = within(await screen.findByRole('complementary', { name: 'Editar Empresa A' }));
    expect(painel.getByLabelText('CNPJ')).toHaveValue('11.222.333/0001-81');
    expect(painel.getByLabelText('Contrato até')).toHaveValue('2027-03-16');
    expect(painel.getByText(/Última alteração: 22\/10\/2026 por admin/)).toBeInTheDocument();
    // A coluna "Criada em" sai enquanto o painel está aberto (para caber o Editar).
    expect(screen.queryByRole('columnheader', { name: 'Criada em' })).not.toBeInTheDocument();

    for (let i = 0; i < 5; i++) await userEvent.click(painel.getByRole('button', { name: 'Mais um assento' }));
    expect(painel.getByRole('status')).toHaveTextContent('+5 assentos (10 → 15). Mínimo permitido: 3, os que estão em uso.');
    await userEvent.clear(painel.getByLabelText('Assentos'));
    expect(painel.getByLabelText('Assentos')).toHaveValue(3); // não desce dos em uso
    expect(painel.getByRole('button', { name: 'Menos um assento' })).toBeDisabled();

    await userEvent.clear(painel.getByLabelText('CNPJ'));
    await userEvent.type(painel.getByLabelText('CNPJ'), '12345678000190');
    await userEvent.click(painel.getByRole('button', { name: 'Salvar alterações' }));
    expect(painel.getByRole('alert')).toHaveTextContent('CNPJ inválido');

    await userEvent.clear(painel.getByLabelText('CNPJ'));
    await userEvent.type(painel.getByLabelText('CNPJ'), '11222333000181');
    await userEvent.click(painel.getByRole('button', { name: 'Mais um assento' }));
    await userEvent.click(painel.getByRole('button', { name: 'Salvar alterações' }));

    await waitFor(() => expect(escritas.at(-1)).toMatchObject({
      metodo: 'PUT', caminho: '/api/empresas/4', corpo: { nome: 'Empresa A', assentos: 4, cnpj: '11222333000181', uf: 'SP', contratoAte: '2027-03-16' },
    }));
    expect(await screen.findByText('Empresa Empresa A atualizada: 4 assentos.')).toBeInTheDocument();
  });

  it('o gestor que abre Empresas volta ao dashboard da empresa dele', async () => {
    renderizar('/empresas', tokenDoGestor());

    expect(await screen.findByRole('heading', { name: 'Saúde agora' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Nova empresa' })).not.toBeInTheDocument();
  });
});
