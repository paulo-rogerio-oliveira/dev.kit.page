import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { API, contaIndividual, escritas, servidor, tokenDoDev } from '../testes/servidor';

describe('Adesão por código e e-mail (US #425)', () => {
  it('manda o código e o e-mail e mostra a mensagem — o link vai por e-mail', async () => {
    renderizar('/aderir?codigo=DK-7QH4-M2XA');

    expect(screen.getByLabelText('Código de adesão')).toHaveValue('DK-7QH4-M2XA');
    await userEvent.type(screen.getByLabelText('E-mail'), 'ana@empresa.com');
    await userEvent.type(screen.getByLabelText('Nome'), 'Ana');
    await userEvent.click(screen.getByRole('button', { name: 'Aderir' }));

    expect(await screen.findByRole('status')).toHaveTextContent('enviamos para ana@empresa.com o link para definir a senha');
    expect(escritas).toEqual([{ metodo: 'POST', caminho: '/api/conta/adesao', corpo: { codigo: 'DK-7QH4-M2XA', email: 'ana@empresa.com', nome: 'Ana' } }]);
  });

  it('mostra a recusa da API (e-mail de outra empresa)', async () => {
    renderizar('/aderir?codigo=DK-7QH4-M2XA');

    await userEvent.type(screen.getByLabelText('E-mail'), 'ana@outra.com');
    await userEvent.click(screen.getByRole('button', { name: 'Aderir' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('já está cadastrado em outra empresa');
  });
});

describe('Definir a senha pelo link (US #425)', () => {
  it('define a senha com o token do link e leva ao login', async () => {
    renderizar('/definir-senha?token=abc123');

    await userEvent.type(screen.getByLabelText(/Nova senha/), 'senhaNova2026');
    await userEvent.type(screen.getByLabelText('Confirme a nova senha'), 'senhaNova2026');
    await userEvent.click(screen.getByRole('button', { name: 'Definir senha' }));

    expect(await screen.findByRole('heading', { name: 'Senha definida' })).toBeInTheDocument();
    expect(escritas[0].corpo).toEqual({ token: 'abc123', novaSenha: 'senhaNova2026' });
    expect(screen.getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/login');
  });

  it('o link já usado mostra a recusa, e a confirmação diferente nem chega à API', async () => {
    renderizar('/definir-senha?token=usado');

    await userEvent.type(screen.getByLabelText(/Nova senha/), 'senhaNova2026');
    await userEvent.type(screen.getByLabelText('Confirme a nova senha'), 'outraSenha2026');
    await userEvent.click(screen.getByRole('button', { name: 'Definir senha' }));
    expect(screen.getByRole('alert')).toHaveTextContent('A confirmação não confere');
    expect(escritas).toEqual([]);

    await userEvent.clear(screen.getByLabelText('Confirme a nova senha'));
    await userEvent.type(screen.getByLabelText('Confirme a nova senha'), 'senhaNova2026');
    await userEvent.click(screen.getByRole('button', { name: 'Definir senha' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Este link já foi usado');
  });

  it('sem token, pede um link novo pelo e-mail', async () => {
    renderizar('/definir-senha');

    await userEvent.type(screen.getByLabelText('E-mail'), 'ana@empresa.com');
    await userEvent.click(screen.getByRole('button', { name: 'Enviar o link' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Se o e-mail tiver uma conta');
    expect(escritas).toEqual([{ metodo: 'POST', caminho: '/api/conta/link', corpo: { email: 'ana@empresa.com' } }]);
  });
});

describe('Conta (US #425)', () => {
  it('é o destino do dev depois do login', async () => {
    renderizar('/login');

    await userEvent.type(screen.getByLabelText('Login'), 'ana.dev');
    await userEvent.type(screen.getByLabelText('Senha'), 'certa');
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }));

    expect(await screen.findByRole('heading', { name: 'Sua conta' })).toBeInTheDocument();
  });

  it('mostra quem está logado, a empresa e a política de login', async () => {
    renderizar('/conta', tokenDoDev());

    const dados = (await screen.findByText('Ana Souza')).closest('dl')!;
    expect(dados).toHaveTextContent('Empresa A');
    expect(dados).toHaveTextContent('Ativa');
    expect(dados).toHaveTextContent('Obrigatório');
    expect(screen.queryByRole('link', { name: 'Gerenciar assinatura' })).not.toBeInTheDocument();
  });

  it('na assinatura individual, mostra o Gerenciar assinatura (o portal do gateway)', async () => {
    servidor.use(http.get(`${API}/api/conta`, () => HttpResponse.json(contaIndividual)));

    renderizar('/conta', tokenDoDev());

    const assinatura = await screen.findByRole('region', { name: 'Assinatura' });
    expect(within(assinatura).getByRole('link', { name: 'Gerenciar assinatura' })).toHaveAttribute('href', 'https://gateway.test/portal');
  });

  it('sem sessão, vai ao login; com o token vencido (401), também', async () => {
    renderizar('/conta');
    expect(screen.getByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
  });

  it('o 401 da API encerra a sessão', async () => {
    servidor.use(http.get(`${API}/api/conta`, () => new HttpResponse(null, { status: 401 })));

    renderizar('/conta', tokenDoDev());

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Entrar' })).toBeInTheDocument());
    expect(screen.getByText('Sua sessão expirou. Entre de novo.')).toBeInTheDocument();
  });
});
