import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenValido } from '../testes/servidor';

async function entrar(senha: string) {
  await userEvent.type(screen.getByLabelText('Login'), 'admin');
  await userEvent.type(screen.getByLabelText('Senha'), senha);
  await userEvent.click(screen.getByRole('button', { name: 'Entrar' }));
}

describe('Login', () => {
  it('login válido vai ao dashboard', async () => {
    renderizar('/login');

    await entrar('certa');

    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
  });

  it('credencial inválida mostra o erro e fica no login', async () => {
    renderizar('/login');

    await entrar('errada');

    expect(await screen.findByRole('alert')).toHaveTextContent('Login ou senha inválidos.');
    expect(screen.getByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
  });

  it('a troca de senha pendente força a troca antes do dashboard', async () => {
    renderizar('/login');

    await entrar('inicial');

    expect(await screen.findByRole('heading', { name: 'Defina a sua senha' })).toBeInTheDocument();
  });

  it('com a troca pendente, o dashboard redireciona para a troca; trocar leva ao dashboard', async () => {
    renderizar('/dashboard', tokenValido(true));
    expect(screen.getByRole('heading', { name: 'Defina a sua senha' })).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText('Senha atual'), 'inicial');
    await userEvent.type(screen.getByLabelText(/^Nova senha/), 'senhaNova2026');
    await userEvent.type(screen.getByLabelText('Confirme a nova senha'), 'senhaNova2026');
    await userEvent.click(screen.getByRole('button', { name: 'Trocar senha' }));

    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
  });

  it('o dashboard sem token redireciona ao login', () => {
    renderizar('/dashboard');

    expect(screen.getByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
  });

  it('token vencido volta ao login dizendo que a sessão expirou', () => {
    renderizar('/dashboard', { ...tokenValido(), expiraEm: new Date(Date.now() - 1000).toISOString() });

    expect(screen.getByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Sua sessão expirou');
  });

  it('um 401 da API no meio do uso encerra a sessão e volta ao login', async () => {
    servidor.use(http.get(`${API}/api/dashboard/quantidade`, () => new HttpResponse(null, { status: 401 })));

    renderizar('/dashboard', tokenValido());

    expect(await screen.findByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
    expect(screen.getByText('Sua sessão expirou. Entre de novo.')).toBeInTheDocument();
  });
});
