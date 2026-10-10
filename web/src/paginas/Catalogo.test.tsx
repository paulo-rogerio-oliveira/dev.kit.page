import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev, tokenDoGestor, tokenValido } from '../testes/servidor';

describe('Vitrine do catálogo (US #425)', () => {
  it('é pública e agrupa os itens por tecnologia, com o tipo e a versão', async () => {
    renderizar('/catalogo');

    const git = await screen.findByRole('region', { name: 'Git' });
    expect(screen.getByRole('region', { name: 'Angular' })).toBeInTheDocument();
    expect(within(git).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(['revisar-alteracoes', 'tarefa-avulsa']);
    expect(within(git).getByText('Fluxo')).toBeInTheDocument();
    expect(within(git).getByText('versão 2')).toBeInTheDocument();
    expect(screen.getByText(/Versão 2 do catálogo, publicada em 08\/10\/2026/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/login');
  });

  it('com o catálogo indisponível (503), diz por quê', async () => {
    servidor.use(http.get(`${API}/api/catalogo/base/vitrine`, () => HttpResponse.json({ detail: 'indisponível' }, { status: 503 })));

    renderizar('/catalogo');

    expect(await screen.findByText(/O catálogo está indisponível no momento/)).toBeInTheDocument();
  });
});

describe('Releases do catálogo básico (US #425)', () => {
  it('o admin vê as releases catalogo-vN com os itens, os retirados e o hash', async () => {
    renderizar('/catalogo-base', tokenValido());

    const tabela = (await screen.findByText('catalogo-v2')).closest('table')!;
    const linhas = within(tabela).getAllByRole('row').slice(1);
    expect(linhas[0]).toHaveTextContent(/catalogo-v2.*4.*1.*3,0 MB.*bbbbbbbbbbbb/);
    expect(linhas[1]).toHaveTextContent('—');
  });

  it('o gestor e o dev não abrem a página do admin', async () => {
    renderizar('/catalogo-base', tokenDoGestor());
    expect(await screen.findByRole('heading', { name: 'Quantidade de uso' })).toBeInTheDocument();
  });

  it('o dev vai para a conta dele', async () => {
    renderizar('/catalogo-base', tokenDoDev());
    expect(await screen.findByRole('heading', { name: 'Sua conta' })).toBeInTheDocument();
  });
});
