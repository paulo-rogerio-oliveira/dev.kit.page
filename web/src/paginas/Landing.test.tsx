import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { BENEFICIOS, CATALOGO, COMPARATIVO, PASSOS, PLANOS, PRODUTOS_COMPARADOS, RECURSOS, SECOES, tituloDaJanela } from '../conteudo/landing';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';

describe('Landing', () => {
  it('abre sem login com as seções na ordem da proposta da home (US #467)', () => {
    const { container } = renderizar('/');

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('O agente de IA que leva a sua task do work item à Pull Request');
    const ordem = [...container.querySelectorAll('main > section')].map((s) => s.id);
    expect(ordem).toEqual(SECOES.map((s) => s.id));
    expect(ordem.slice(0, 4)).toEqual(['inicio', 'como-funciona', 'recursos', 'beneficios']);
    expect(ordem.at(-1)).toBe('comecar');
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

  it('o como funciona mostra as sete etapas da proposta, sem o vídeo do hero', () => {
    renderizar('/');

    const secao = screen.getByRole('region', { name: 'Do work item à Pull Request, em sete etapas' });
    const etapas = within(secao).getAllByRole('listitem');
    expect(etapas.map((e) => e.querySelector('h3')!.textContent)).toEqual(['Clona', 'Implementa', 'Compila', 'Testa', 'Avalia', 'Commit e push', 'Pull Request']);
    expect(etapas[4]).toHaveTextContent('abaixo da mínima, refaz');
    expect(secao.querySelector('video, img')).toBeNull();
    expect(PASSOS).toHaveLength(7);
  });

  it('o por que o dev.kit tem os três cartões', () => {
    renderizar('/');

    const secao = screen.getByRole('region', { name: 'O trabalho repetitivo com o agente. As decisões com o time.' });
    expect(within(secao).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(BENEFICIOS.map((b) => b.titulo));
  });

  it('os recursos têm as abas 01–04, e cada aba leva ao seu bloco (US #467)', () => {
    renderizar('/');

    const recursos = screen.getByRole('region', { name: 'Cada recurso com a sua demonstração' });
    const abas = within(within(recursos).getByRole('navigation', { name: 'Recursos em destaque' })).getAllByRole('link');
    expect(abas.map((a) => a.textContent)).toEqual(['01 O agente sobre a task', '02 Fluxos com avaliadores', '03 Cherry-pick', '04 Horas']);
    for (const [i, recurso] of RECURSOS.entries()) {
      expect(abas[i]).toHaveAttribute('href', `#recurso-${recurso.id}`);
      expect(within(recursos).getByRole('article', { name: recurso.titulo })).toHaveAttribute('id', `recurso-${recurso.id}`);
    }
  });

  it('cada recurso tem os marcadores e a mídia numa moldura de janela, com o texto alternativo e a legenda abaixo', () => {
    renderizar('/');

    const recursos = screen.getByRole('region', { name: 'Cada recurso com a sua demonstração' });
    for (const recurso of RECURSOS) {
      const artigo = within(recursos).getByRole('article', { name: recurso.titulo });
      expect(within(artigo).getAllByRole('listitem').map((l) => l.textContent)).toEqual(recurso.marcadores);
      const moldura = artigo.querySelector('.janela')!;
      expect(moldura.querySelector('.janela-barra')).toHaveTextContent(tituloDaJanela(recurso.janela));
      const video = moldura.querySelector('video')!;
      expect(video).toHaveAttribute('poster', recurso.midia.poster);
      expect(video).toHaveAccessibleName(recurso.midia.alt);
      expect(artigo.querySelector('figure > figcaption')).toHaveTextContent(recurso.legenda);
    }
    expect(recursos.querySelectorAll('video')).toHaveLength(4);
    expect(document.querySelectorAll('main video')).toHaveLength(4);
  });

  it('a amostra do Board do hero usa a mesma moldura de janela dos recursos', () => {
    renderizar('/');

    const amostra = screen.getByRole('img', { name: /Amostra do Board/ });
    expect(amostra.querySelector('.janela .janela-barra')).toHaveTextContent('dev.kit - Board');
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
    expect(within(menu).getAllByRole('listitem').map((l) => l.textContent)).toEqual(['Como funciona', 'Recursos', 'Catálogo', 'Integrações', 'Segurança', 'Planos', 'Perguntas']);
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

  it('a seção Catálogo (US #425) explica a atualização sozinha e leva à vitrine', () => {
    renderizar('/');

    const catalogo = screen.getByRole('region', { name: CATALOGO.titulo });
    expect(within(catalogo).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(CATALOGO.itens.map((i) => i.titulo));
    expect(within(catalogo).getByRole('link', { name: 'Ver o catálogo completo' })).toHaveAttribute('href', '/catalogo');
  });

  it('a seção Planos (US #425) tem o teste grátis, a assinatura individual e o plano para empresas', () => {
    renderizar('/');

    const planos = screen.getByRole('region', { name: 'Planos' });
    expect(within(planos).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(PLANOS.map((p) => p.nome));
    expect(within(planos).getByRole('link', { name: 'Assinar o individual' })).toHaveAttribute('href', '/comprar');
    expect(within(planos).getByRole('link', { name: 'Falar sobre o plano para empresas' })).toHaveAttribute('href', '#contato');
  });

  it('o FAQ novo responde sobre o teste grátis, a compra e o cancelamento', () => {
    renderizar('/');

    const faq = screen.getByRole('region', { name: 'Perguntas frequentes' });
    for (const pergunta of ['Como funciona o teste grátis?', 'Como compro só para mim?', 'Como cancelo a assinatura?', 'O que é o catálogo do dev.kit?'])
      expect(within(faq).getByText(pergunta)).toBeInTheDocument();
  });

  it('o link de entrar leva ao login', async () => {
    renderizar('/');

    await userEvent.click(screen.getAllByRole('link', { name: 'Entrar' })[0]);

    expect(screen.getByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
  });
});

describe('Baixar o dev.kit (US #405, anônimo desde a US #425, link copiável desde a US #467)', () => {
  /** O endereço que o visitante copia: o alias permanente da API, ABSOLUTO (o VITE_API_URL dos testes). */
  const ENDERECO = `${API}/api/versoes/ultima/download`;
  const download = () => screen.getByRole('group', { name: 'Download do dev.kit' });

  it('é um link com o rótulo "dev.kit versão N", a versão, a data, o tamanho e o teste grátis sem login', async () => {
    renderizar('/');

    const link = await within(download()).findByRole('link', { name: 'Baixar o dev.kit versão 136' });
    expect(link).toHaveAttribute('href', ENDERECO);
    expect(download()).toHaveTextContent('versão 136 · 09/10/2026 · 50,0 MB · 14 dias grátis, sem login');
    expect(within(download()).queryByRole('button')).not.toBeInTheDocument();
  });

  it('o endereço do download aparece escrito na página, para copiar e colar na barra do navegador', async () => {
    renderizar('/');

    await within(download()).findByRole('link', { name: 'Baixar o dev.kit versão 136' });
    expect(within(download()).getByText(ENDERECO).tagName).toBe('CODE');
    expect(download()).toHaveTextContent(`Ou copie o endereço: ${ENDERECO}`);
  });

  it('o link é o alias permanente e anônimo: o mesmo href com ou sem login, e não o da versão', async () => {
    renderizar('/', tokenDoDev());

    const link = await within(download()).findByRole('link', { name: 'Baixar o dev.kit versão 136' });
    expect(link).toHaveAttribute('href', ENDERECO);
    expect(link.getAttribute('href')).not.toContain('/136/');
  });

  it('o "Testar grátis por 14 dias" dos planos é o mesmo link de download', async () => {
    renderizar('/');

    const planos = screen.getByRole('region', { name: 'Planos' });
    const link = await within(planos).findByRole('link', { name: 'Testar grátis por 14 dias' });
    expect(link).toHaveAttribute('href', ENDERECO);
    expect(link).toHaveClass('botao');
  });

  it('no hero o download é um link discreto abaixo dos dois botões da proposta', async () => {
    renderizar('/');

    const link = await within(download()).findByRole('link', { name: 'Baixar o dev.kit versão 136' });
    expect(link).toHaveClass('link-baixar');
    expect(link).not.toHaveClass('botao');
    expect(download().previousElementSibling).toHaveClass('ctas');
  });

  it('com a atualização indisponível (503), não há link e a nota diz por quê', async () => {
    servidor.use(http.get(`${API}/api/versoes/ultima`, () => HttpResponse.json({ detail: 'indisponível' }, { status: 503 })));

    renderizar('/');

    expect(await within(download()).findByText('Download indisponível no momento.')).toBeInTheDocument();
    expect(within(download()).queryByRole('link')).not.toBeInTheDocument();
    expect(within(download()).getByText('Baixar o dev.kit')).toHaveAttribute('aria-disabled', 'true');
    const planos = screen.getByRole('region', { name: 'Planos' });
    expect(await within(planos).findByRole('button', { name: 'Testar grátis por 14 dias' })).toBeDisabled();
  });
});
