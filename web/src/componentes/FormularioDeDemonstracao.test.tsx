import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { API, pedidosRecebidos, servidor } from '../testes/servidor';
import { FormularioDeDemonstracao, LIMITES, validarPedido } from './FormularioDeDemonstracao';

async function preencher({ nome = 'Ana Souza', email = 'ana@empresa.com.br', consentir = true } = {}) {
  if (nome) await userEvent.type(screen.getByLabelText('Nome'), nome);
  if (email) await userEvent.type(screen.getByLabelText('E-mail'), email);
  await userEvent.type(screen.getByLabelText(/^Empresa/), 'Empresa X');
  if (consentir) await userEvent.click(screen.getByRole('checkbox'));
}

const enviar = () => userEvent.click(screen.getByRole('button', { name: 'Quero uma demonstração' }));

describe('validarPedido', () => {
  const valido = { nome: 'Ana', email: 'ana@empresa.com', empresa: '', mensagem: '', consentimento: true };

  it('aceita o pedido completo e recusa o que a API recusa', () => {
    expect(validarPedido(valido)).toEqual({});
    expect(Object.keys(validarPedido({ ...valido, nome: ' ', email: 'a@b', consentimento: false })).sort()).toEqual(['consentimento', 'email', 'nome']);
    expect(validarPedido({ ...valido, mensagem: 'm'.repeat(LIMITES.mensagem + 1) })).toHaveProperty('mensagem');
    expect(validarPedido({ ...valido, empresa: 'e'.repeat(LIMITES.empresa + 1) })).toHaveProperty('empresa');
  });
});

describe('FormularioDeDemonstracao', () => {
  it('valida antes de enviar: campos obrigatórios e consentimento', async () => {
    render(<FormularioDeDemonstracao />);

    await enviar();

    expect(screen.getByText('Informe o seu nome.')).toBeInTheDocument();
    expect(screen.getByText('Informe o seu e-mail.')).toBeInTheDocument();
    expect(screen.getByText(/É preciso concordar/)).toBeInTheDocument();
    expect(screen.getByLabelText('Nome')).toHaveAttribute('aria-invalid', 'true');
    expect(pedidosRecebidos).toHaveLength(0);
  });

  it('envia o pedido válido pela API e mostra a confirmação', async () => {
    render(<FormularioDeDemonstracao />);
    await preencher();

    await enviar();

    expect(await screen.findByRole('heading', { name: 'Pedido recebido!' })).toBeInTheDocument();
    expect(pedidosRecebidos).toEqual([{ nome: 'Ana Souza', email: 'ana@empresa.com.br', empresa: 'Empresa X', mensagem: '', consentimento: true }]);
  });

  it('o 429 do limite por IP vira uma mensagem para tentar depois', async () => {
    servidor.use(http.post(`${API}/api/demonstracoes`, () => HttpResponse.json({ detail: 'Muitos pedidos.' }, { status: 429 })));
    render(<FormularioDeDemonstracao />);
    await preencher();

    await enviar();

    expect(await screen.findByRole('alert')).toHaveTextContent('Tente de novo em um minuto');
    expect(screen.getByRole('button', { name: 'Quero uma demonstração' })).toBeEnabled();
  });

  it('o 400 da API mostra os erros por campo', async () => {
    servidor.use(http.post(`${API}/api/demonstracoes`, () =>
      HttpResponse.json({ title: 'Confira os campos do pedido.', errors: { email: ['Informe um e-mail válido.'] } }, { status: 400 })));
    render(<FormularioDeDemonstracao />);
    await preencher();

    await enviar();

    expect(await screen.findByText('Informe um e-mail válido.')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('Confira os campos do pedido.');
  });

  it('falha de rede mostra o erro genérico', async () => {
    servidor.use(http.post(`${API}/api/demonstracoes`, () => HttpResponse.error()));
    render(<FormularioDeDemonstracao />);
    await preencher();

    await enviar();

    expect(await screen.findByRole('alert')).toHaveTextContent('Não foi possível enviar agora');
  });
});
