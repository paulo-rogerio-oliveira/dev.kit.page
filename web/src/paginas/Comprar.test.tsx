import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderizar } from '../testes/renderizar';
import { escritas } from '../testes/servidor';

describe('Compra individual (US #425)', () => {
  it('o plano, o e-mail e o nome vão ao checkout; o simulado leva à compra concluída', async () => {
    renderizar('/comprar');

    await userEvent.selectOptions(screen.getByLabelText('Plano'), 'individual-anual');
    await userEvent.type(screen.getByLabelText('E-mail'), 'rui@solo.com');
    await userEvent.type(screen.getByLabelText('Nome'), 'Rui');
    await userEvent.click(screen.getByRole('button', { name: 'Ir para o pagamento' }));

    expect(await screen.findByRole('heading', { name: 'Compra concluída' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Checkout simulado: nenhum pagamento foi cobrado.');
    expect(escritas).toEqual([{ metodo: 'POST', caminho: '/api/pagamentos/checkout', corpo: { email: 'rui@solo.com', nome: 'Rui', plano: 'individual-anual' } }]);
  });

  it('a compra concluída explica o e-mail do link e oferece o "não recebi"', () => {
    renderizar('/compra-concluida');

    expect(screen.getByText(/enviamos ao seu e-mail o link para definir a senha/)).toBeInTheDocument();
    expect(screen.queryByText(/Checkout simulado/)).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Não recebi o e-mail' })).toHaveAttribute('href', '/definir-senha');
  });
});
