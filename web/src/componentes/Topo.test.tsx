import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderizar } from '../testes/renderizar';
import { tokenDoDev, tokenDoGestor, tokenValido } from '../testes/servidor';

describe('Navegação unificada', () => {
  it.each([
    [tokenValido(), ['Empresas', 'Usuários', 'Uso'], ['Usuários da empresa', 'Catálogo da empresa']],
    [tokenDoGestor(), ['Usuários da empresa', 'Catálogo da empresa', 'Uso'], ['Empresas', 'Usuários']],
    [tokenDoDev(), ['Board', 'Projetos', 'Design', 'Minha conta'], ['Uso', 'Empresas', 'Usuários', 'Relatos']],
  ] as const)('mostra apenas destinos permitidos para $0.papel', (sessao, presentes, ausentes) => {
    renderizar('/conta', sessao);
    const menu = within(screen.getByRole('navigation', { name: 'Navegação principal' }));
    presentes.forEach(nome => expect(menu.getByRole('link', { name: nome })).toBeInTheDocument());
    ausentes.forEach(nome => expect(menu.queryByRole('link', { name: nome })).not.toBeInTheDocument());
    expect(menu.getByRole('link', { name: 'Minha conta' })).toHaveAttribute('aria-current', 'page');
  });
  it('oferece localização e fallback determinístico em URL direta', () => {
    renderizar('/design/7/checkout/v2', tokenDoDev());
    expect(within(screen.getByRole('navigation', { name: 'Localização' })).getByText('Versão')).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: '← Design' })).toHaveAttribute('href', '/design');
  });
  it('Escape fecha o menu e devolve o foco ao acionador', async () => {
    renderizar('/conta', tokenDoDev());
    const botao = screen.getByRole('button', { name: '☰ Menu' });
    await userEvent.click(botao);
    expect(botao).toHaveAttribute('aria-expanded', 'true');
    expect(within(screen.getByRole('navigation', { name: 'Navegação principal' })).getByRole('link', { name: 'Board' })).toHaveFocus();
    await userEvent.keyboard('{Escape}');
    expect(botao).toHaveAttribute('aria-expanded', 'false');
    expect(botao).toHaveFocus();
  });
  it('explica acesso negado e permite continuar', async () => {
    renderizar('/usuarios', tokenDoDev());
    expect(await screen.findByText(/Acesso negado à seção solicitada/)).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Sua conta' })).toBeInTheDocument();
  });
  it('selecionar a própria seção no menu recolhe e leva o foco ao conteúdo', async () => {
    renderizar('/conta', tokenDoDev());
    await userEvent.click(screen.getByRole('button', { name: '☰ Menu' }));
    await userEvent.click(within(screen.getByRole('navigation', { name: 'Navegação principal' })).getByRole('link', { name: 'Minha conta' }));
    expect(screen.getByRole('button', { name: '☰ Menu' })).toHaveAttribute('aria-expanded', 'false');
    await waitFor(() => expect(screen.getByRole('main')).toHaveFocus());
  });
});
