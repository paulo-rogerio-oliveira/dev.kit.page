import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { BENEFICIOS, COMPARATIVO, PASSOS, PRODUTOS_COMPARADOS, RECURSOS, SECOES } from '../conteudo/landing';
import { entregues } from '../testes/navegador';
import { renderizar } from '../testes/renderizar';
import { API, downloads, servidor, tokenDoDev, tokenValido } from '../testes/servidor';

describe('Landing', () => {
  it('abre sem login com as seções na ordem da proposta (US #405)', () => {
    const { container } = renderizar('/');

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('O agente de IA que leva a sua task do work item à Pull Request');
    const ordem = [...container.querySelectorAll('main > section')].map((s) => s.id);
    expect(ordem).toEqual(SECOES.map((s) => s.id));
    expect(ordem.slice(0, 3)).toEqual(['inicio', 'como-funciona', 'beneficios']);
  });

  it('o hero tem o selo, "Pull Request" em destaque, a amostra do Board com a nota e o CTA para o contato', () => {
    renderizar('/');

    const hero = screen.getByRole('region', { name: /agente de IA/ });
    expect(within(hero).getByText('Para times no Azure DevOps')).toBeInTheDocument();
    expect(hero.querySelector('h1 .destaque')).toHaveTextContent(/^Pull Request$/);
    const amostra = within(hero).getByRole('img', { name: /Amostra do Board/ });
    expect(amostra).toHaveAccessibleName(/Precisa de você/);
    expect(amostra).toHaveAccessibleName(/Nota 92\/100, aceito: acima da nota mínima/);
    expect(within(hero).getByRole('link', { name: 'Quero uma demonstração' })).toHaveAttribute('href', '#contato');
    expect(within(hero).getByRole('link', { name: 'Ver como funciona' })).toHaveAttribute('href', '#como-funciona');
  });

  it('o como funciona mostra as sete etapas da proposta e o vídeo do fluxo', () => {
    renderizar('/');

    const secao = screen.getByRole('region', { name: 'Do work item à Pull Request, em sete etapas' });
    const etapas = within(secao).getAllByRole('listitem');
    expect(etapas.map((e) => e.querySelector('h3')!.textContent)).toEqual(['Clona', 'Implementa', 'Compila', 'Testa', 'Avalia', 'Commit e push', 'Pull Request']);
    expect(etapas[4]).toHaveTextContent('abaixo da mínima, refaz');
    expect(secao.querySelector('video')).toHaveAttribute('poster', '/midia/hero.jpg');
    expect(PASSOS).toHaveLength(7);
  });

  it('o por que o dev.kit tem os três cartões', () => {
    renderizar('/');

    const secao = screen.getByRole('region', { name: 'O trabalho repetitivo com o agente. As decisões com o time.' });
    expect(within(secao).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(BENEFICIOS.map((b) => b.titulo));
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

  it('o cabeçalho tem o menu da proposta, Entrar e Pedir demonstração', () => {
    renderizar('/');

    const menu = screen.getByRole('navigation', { name: 'Seções' });
    expect(within(menu).getAllByRole('listitem').map((l) => l.textContent)).toEqual(['Como funciona', 'Recursos', 'Integrações', 'Segurança', 'Perguntas']);
    expect(within(menu).getByRole('link', { name: 'Recursos' })).toHaveAttribute('href', '#recursos');
    expect(within(menu).getByRole('link', { name: 'Perguntas' })).toHaveAttribute('href', '#faq');
    expect(within(menu).getByRole('link', { name: 'Pedir demonstração' })).toHaveAttribute('href', '#contato');
    expect(within(menu).getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/login');
  });

  it('o rodapé não explica o dashboard: leva a Segurança, Comparativo, Empresas, Perguntas e ao login', () => {
    const { container } = renderizar('/');

    const rodape = within(container.querySelector('footer')!).getByRole('navigation', { name: 'Rodapé' });
    expect(within(rodape).getByRole('link', { name: 'Comparativo' })).toHaveAttribute('href', '#comparativo');
    expect(within(rodape).getByRole('link', { name: 'Empresas' })).toHaveAttribute('href', '#empresas');
    expect(within(rodape).getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/login');
    expect(container.querySelector('footer')).toHaveTextContent('desenvolvido por Paulo Rogério');
    expect(container).not.toHaveTextContent(/dashboard/i);
  });

  it('a tabela comparativa tem a legenda, os cabeçalhos dos produtos e uma linha por recurso', () => {
    renderizar('/');

    const tabela = within(screen.getByRole('region', { name: 'dev.kit e as alternativas' })).getByRole('table');
    expect(tabela).toHaveAccessibleName(/Comparativo do dev\.kit/);
    const colunas = within(tabela).getAllByRole('columnheader').map((c) => c.textContent);
    expect(colunas).toEqual(['Recurso', ...PRODUTOS_COMPARADOS.map((p) => p.nome)]);
    expect(within(tabela).getAllByRole('rowheader').map((c) => c.textContent)).toEqual(COMPARATIVO.map((l) => l.recurso));
  });

  it('a seção Para empresas leva ao formulário de contato', () => {
    renderizar('/');

    const empresas = screen.getByRole('region', { name: 'Para empresas' });
    expect(within(empresas).getByText('Consentimento do colaborador')).toBeInTheDocument();
    expect(within(empresas).getByRole('link', { name: 'Falar sobre o plano para empresas' })).toHaveAttribute('href', '#contato');
  });

  it('o CTA escuro chama para a demonstração e para o login', () => {
    renderizar('/');

    const cta = screen.getByRole('region', { name: 'Veja o dev.kit numa task do seu time' });
    expect(cta).toHaveTextContent('do clone à Pull Request');
    expect(within(cta).getByRole('link', { name: 'Quero uma demonstração' })).toHaveAttribute('href', '#contato');
    expect(within(cta).getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/login');
  });

  it('o link de entrar leva ao login', async () => {
    renderizar('/');

    await userEvent.click(screen.getAllByRole('link', { name: 'Entrar' })[0]);

    expect(screen.getByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
  });
});

describe('Baixar o dev.kit (US #405)', () => {
  const download = () => screen.getByRole('group', { name: 'Download do dev.kit' });

  it('mostra a versão da API, a data e o tamanho', async () => {
    renderizar('/');

    expect(await within(download()).findByRole('button', { name: 'Baixar o dev.kit 136' })).toBeEnabled();
    expect(download()).toHaveTextContent('versão 136 · 09/10/2026 · 50,0 MB');
    expect(download()).toHaveTextContent('entre para baixar');
  });

  it('sem login, o botão leva ao login — e o login volta para a landing', async () => {
    renderizar('/');

    await userEvent.click(await within(download()).findByRole('button', { name: 'Baixar o dev.kit 136' }));

    expect(screen.getByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
    expect(screen.getByText('Entre para baixar o dev.kit.')).toBeInTheDocument();
    expect(downloads).toEqual([]);

    await userEvent.type(screen.getByLabelText('Login'), 'ana.dev');
    await userEvent.type(screen.getByLabelText('Senha'), 'certa');
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }));
    expect(await screen.findByRole('button', { name: 'Baixar o dev.kit 136' })).toBeInTheDocument();
    expect(download()).not.toHaveTextContent('entre para baixar');
  });

  it('com login, baixa por fetch com o Bearer e entrega o zip', async () => {
    renderizar('/', tokenDoDev());

    await userEvent.click(await within(download()).findByRole('button', { name: 'Baixar o dev.kit 136' }));

    await waitFor(() => expect(entregues.arquivos).toHaveLength(1));
    expect(entregues.arquivos[0].nome).toBe('devkit-136.zip');
    expect(downloads).toEqual(['Bearer token-dev']);
    expect(download()).not.toHaveTextContent('entre para baixar');
  });

  it('a sessão com a troca pendente vai para a troca antes de baixar', async () => {
    renderizar('/', tokenValido(true));

    await userEvent.click(await within(download()).findByRole('button', { name: 'Baixar o dev.kit 136' }));

    expect(screen.getByRole('heading', { name: 'Defina a sua senha' })).toBeInTheDocument();
    expect(downloads).toEqual([]);
  });

  it('com a atualização indisponível (503), o botão fica desabilitado e diz por quê', async () => {
    servidor.use(http.get(`${API}/api/versoes/ultima`, () => HttpResponse.json({ detail: 'indisponível' }, { status: 503 })));

    renderizar('/');

    expect(await within(download()).findByText('Download indisponível no momento.')).toBeInTheDocument();
    expect(within(download()).getByRole('button', { name: 'Baixar o dev.kit' })).toBeDisabled();
  });
});
