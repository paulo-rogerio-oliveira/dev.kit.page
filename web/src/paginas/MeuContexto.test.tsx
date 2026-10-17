import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderizar } from '../testes/renderizar';
import { escritas, tokenDoDev, tokenDoGestor, tokenValido } from '../testes/servidor';

describe('Contexto do dev.kit na landing de quem é da empresa (US #556)', () => {
  it('o dev vê o dele, o da equipe aprovada e o da empresa (só leitura), e o pedido aguardando o gestor', async () => {
    renderizar('/contexto', tokenDoDev());

    expect(await screen.findByText('Prefiro testes pequenos')).toBeInTheDocument();
    expect(screen.getByText('vendas/{id}')).toBeInTheDocument();
    expect(screen.getByText('Rode só a suíte do projeto alterado').closest('tr')).toHaveTextContent('Sugestão');

    // A da empresa aparece, mas sem ação: só o gestor grava; e o nível Empresa nem é oferecido.
    expect(screen.getByText('.NET 10 e React 19').closest('tr')!.querySelector('button')).toBeNull();
    const formulario = screen.getByRole('form', { name: 'Gravar no contexto' });
    expect(within(within(formulario).getByLabelText('Nível')).getAllByRole('option').map((o) => o.textContent)).toEqual(['Usuário (o seu)', 'Equipe']);

    expect(screen.getByRole('status')).toHaveTextContent('Aguardando a aprovação do gestor para a equipe financeiro');
    expect(screen.queryByRole('region', { name: 'Participantes das equipes' })).not.toBeInTheDocument();
  });

  it('o gestor (o admin da empresa) edita o contexto da empresa e decide os pedidos das equipes', { timeout: 15_000 }, async () => {
    renderizar('/contexto', tokenDoGestor());

    const empresa = (await screen.findByText('.NET 10 e React 19')).closest('tr')!;
    expect(within(empresa).getByRole('button', { name: 'Editar contexto.stack (Empresa)' })).toBeInTheDocument();
    expect(screen.queryByText('Prefiro testes pequenos')).not.toBeInTheDocument(); // o pessoal da Ana é dela

    const formulario = screen.getByRole('form', { name: 'Gravar no contexto' });
    await userEvent.selectOptions(within(formulario).getByLabelText('Nível'), 'empresa');
    await userEvent.type(within(formulario).getByLabelText('Texto'), 'Commits com Ab#');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Gravar' }));
    expect(await screen.findByText('contexto.geral gravado no nível empresa.')).toBeInTheDocument();

    const pedido = screen.getByRole('button', { name: 'Aprovar Ana Souza em financeiro' });
    await userEvent.click(pedido);
    expect(await screen.findByText('Ana Souza aprovado na equipe financeiro.')).toBeInTheDocument();

    await waitFor(() => expect(escritas).toEqual([
      { metodo: 'PUT', caminho: '/api/contexto', corpo: { nivel: 'empresa', chave: 'contexto.geral', valor: 'Commits com Ab#', equipe: '' } },
      { metodo: 'PUT', caminho: '/api/contexto/equipes/financeiro/participantes', corpo: { usuarioId: 2, situacao: 'aprovado' } },
    ]));
  });

  it('o admin do board, que não é de empresa, vai para o dashboard (o dele é /admin/contexto)', async () => {
    renderizar('/contexto', tokenValido());
    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
  });
});
