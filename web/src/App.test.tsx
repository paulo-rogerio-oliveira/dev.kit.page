import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from './testes/renderizar';
import { API, projetosDeDesign, servidor, tokenDoDev } from './testes/servidor';
import { comRetorno } from './navegacao';

describe('Retomada de navegação protegida', () => {
  const origem = '/projetos?nivel=usuario&projeto=7&visao=propostas&extra=1';
  const rota = comRetorno('/design/7/checkout/v2?hash=' + projetosDeDesign[0].telas[0].versoes[1].sha256, origem) + '#versao';
  async function entrar() {
    await userEvent.type(screen.getByLabelText('Login'), 'dev.demo');
    await userEvent.type(screen.getByLabelText('Senha'), 'senha-demo');
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }));
  }
  it('sessão expirada retoma o deep-link de Design e seu projeto após login', async () => {
    servidor.use(http.post(`${API}/api/auth/login`, () => HttpResponse.json(tokenDoDev())));
    renderizar(rota, { ...tokenDoDev(), expiraEm: new Date(Date.now() - 1000).toISOString() });
    expect(screen.getByText('Sua sessão expirou. Entre de novo.')).toBeInTheDocument();
    await entrar();
    expect(await screen.findByRole('heading', { name: 'Checkout · v002' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '← Projetos' })).toHaveAttribute('href', origem);
  });
  it('401 durante Design pede login e retoma a mesma referência', async () => {
    servidor.use(
      http.get(`${API}/api/design/projetos/7`, () => new HttpResponse(null, { status: 401 }), { once: true }),
      http.post(`${API}/api/auth/login`, () => HttpResponse.json(tokenDoDev())),
    );
    renderizar(rota, tokenDoDev());
    expect(await screen.findByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
    await entrar();
    expect(await screen.findByRole('heading', { name: 'Checkout · v002' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '← Projetos' })).toHaveAttribute('href', origem);
  });
  it('troca obrigatória conserva o destino protegido', async () => {
    servidor.use(http.post(`${API}/api/auth/trocar-senha`, () => HttpResponse.json(tokenDoDev())));
    renderizar(rota, { ...tokenDoDev(), deveTrocarSenha: true });
    await userEvent.type(screen.getByLabelText('Senha atual'), 'temporaria');
    await userEvent.type(screen.getByLabelText(/^Nova senha/), 'novaSenha2026');
    await userEvent.type(screen.getByLabelText('Confirme a nova senha'), 'novaSenha2026');
    await userEvent.click(screen.getByRole('button', { name: 'Trocar senha' }));
    expect(await screen.findByRole('heading', { name: 'Checkout · v002' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '← Projetos' })).toHaveAttribute('href', origem);
  });
});
