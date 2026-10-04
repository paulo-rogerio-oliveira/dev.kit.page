import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RECURSOS, SECOES } from '../conteudo/landing';
import { renderizar } from '../testes/renderizar';

describe('Landing', () => {
  it('abre sem login com as seções na ordem do critério 1', () => {
    const { container } = renderizar('/');

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('agente de IA');
    const ordem = [...container.querySelectorAll('main > section')].map((s) => s.id);
    expect(ordem).toEqual(SECOES.map((s) => s.id));
  });

  it('o hero tem a mídia em movimento e o CTA leva ao contato', () => {
    renderizar('/');

    const hero = screen.getByRole('region', { name: /agente de IA/ });
    expect(hero.querySelector('video')).toHaveAttribute('poster', '/midia/hero.jpg');
    expect(within(hero).getByRole('link', { name: 'Quero uma demonstração' })).toHaveAttribute('href', '#contato');
  });

  it('cada recurso tem a sua mídia, com texto alternativo', () => {
    renderizar('/');

    const recursos = screen.getByRole('region', { name: 'Recursos' });
    for (const recurso of RECURSOS) {
      const artigo = within(recursos).getByRole('article', { name: recurso.titulo });
      const video = artigo.querySelector('video')!;
      expect(video).toHaveAttribute('poster', recurso.midia.poster);
      expect(video).toHaveAccessibleName(recurso.midia.alt);
    }
    expect(recursos.querySelectorAll('video')).toHaveLength(RECURSOS.length);
  });

  it('o contato tem o formulário de demonstração e o FAQ abre em details', () => {
    renderizar('/');

    expect(within(screen.getByRole('region', { name: 'Quero uma demonstração' })).getByRole('form', { name: 'Pedido de demonstração' })).toBeInTheDocument();
    const faq = screen.getByRole('region', { name: 'Perguntas frequentes' });
    expect(faq.querySelectorAll('details').length).toBeGreaterThanOrEqual(4);
  });

  it('o menu do topo aponta para as âncoras das seções', () => {
    renderizar('/');

    const menu = screen.getByRole('navigation', { name: 'Seções' });
    expect(within(menu).getByRole('link', { name: 'Recursos' })).toHaveAttribute('href', '#recursos');
    expect(within(menu).getByRole('link', { name: 'Pedir demonstração' })).toHaveAttribute('href', '#contato');
  });

  it('o link de entrar leva ao login', async () => {
    renderizar('/');

    await userEvent.click(screen.getAllByRole('link', { name: 'Entrar' })[0]);

    expect(screen.getByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
  });
});
