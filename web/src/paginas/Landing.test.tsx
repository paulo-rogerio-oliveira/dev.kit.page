import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { BENEFICIOS, BRACOS, CATALOGO, COMPARATIVO, CONCEITOS, FABRICA, PASSOS, PLANOS, PRODUTOS_COMPARADOS, RECURSOS, SECOES, tituloDaJanela } from '../conteudo/landing';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';

// O nome do hero vem do h1 — e o span do destaque põe um espaço antes da vírgula ("Pull Request , com"): o fim basta.
const hero = () => screen.getByRole('region', { name: /com um time de agentes de IA$/ });
const tour = () => screen.getByRole('tablist', { name: 'Recursos em destaque' });

describe('Landing', () => {
  it('abre sem login com as seções na ordem da landing refeita', () => {
    const { container } = renderizar('/');

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Do work item à Pull Request, com um time de agentes de IA');
    const ordem = [...container.querySelectorAll('main > section')].map((s) => s.id);
    expect(ordem).toEqual(SECOES.map((s) => s.id));
    expect(ordem.slice(0, 4)).toEqual(['inicio', 'como-funciona', 'bracos', 'recursos']);
    expect(ordem.at(-1)).toBe('comecar');
  });

  it('a primeira dobra tem o selo, "Pull Request" em destaque, os dois CTAs, o download e a vitrine do desktop com o celular', async () => {
    renderizar('/');

    expect(within(hero()).getByText('Para times no Azure DevOps')).toBeInTheDocument();
    expect(hero().querySelector('h1 .destaque')).toHaveTextContent(/^Pull Request$/);
    expect(within(hero()).getByRole('link', { name: 'Quero uma demonstração' })).toHaveAttribute('href', '#contato');
    expect(within(hero()).getByRole('link', { name: 'Ver como funciona' })).toHaveAttribute('href', '#como-funciona');
    expect(await within(hero()).findByRole('link', { name: 'Baixar o dev.kit versão 136' })).toBeInTheDocument();

    const vitrine = within(hero()).getByRole('img', { name: /^Amostra do dev\.kit no desktop e no celular/ });
    expect(vitrine).toHaveAccessibleName(/o Orquestrador diz "Destravei a #509/);
    expect(vitrine).toHaveAccessibleName(/o projeto portal-web está na onda 2 de 3/);
    expect(vitrine).toHaveAccessibleName(/Precisa de você com 2 itens/);
    expect(vitrine).toHaveAccessibleName(/Nota 92\/100, aceito: acima da nota mínima/);
    expect(vitrine).toHaveAccessibleName(/o Board do dev\.kit\.page pergunta "Integrar o item 4 no projeto portal-web\?" \(Nota 94 · regressão sem falhas\) e pede: Aprovar com segundo fator/);
    // A moldura do desktop é a mesma Janela dos recursos, e o celular mostra o pedido do segundo fator.
    expect(vitrine.querySelector('.janela .janela-barra')).toHaveTextContent('dev.kit - Board');
    expect(vitrine.querySelector('.lp-celular')).toHaveTextContent('Aprovar com segundo fator');
    expect(vitrine.querySelector('.lp-contagem')).toHaveTextContent('2');
  });

  it('os quatro braços estão na primeira dobra, e cada um leva ao seu detalhe na seção Produto', () => {
    renderizar('/');

    const faixa = within(hero()).getByRole('navigation', { name: 'Os quatro braços do dev.kit' });
    const links = within(faixa).getAllByRole('link');
    expect(links.map((l) => l.getAttribute('href'))).toEqual(['#braco-desktop', '#braco-servico', '#braco-board', '#braco-devcli']);
    for (const [i, braco] of BRACOS.entries()) {
      expect(links[i]).toHaveTextContent(braco.nome);
      expect(links[i]).toHaveTextContent(braco.resumo);
    }
    expect(links[2].querySelector('.recurso-novo')).toHaveTextContent('novo');

    const produto = screen.getByRole('region', { name: 'Um produto, quatro braços' });
    for (const braco of BRACOS) {
      const artigo = within(produto).getByRole('article', { name: braco.titulo });
      expect(artigo).toHaveAttribute('id', `braco-${braco.id}`);
      expect(artigo).toHaveTextContent(braco.texto);
      // A ilustração é decorativa (aria-hidden): os itens da lista são só os marcadores.
      expect(within(artigo).getAllByRole('listitem').map((l) => l.textContent)).toEqual(braco.marcadores);
    }
    expect(produto.querySelector('#braco-devcli .lp-terminal')).toHaveTextContent('devcli horas --aplicar');
    expect(produto.querySelector('#braco-board .lp-notificacao')).toHaveTextContent('dev.kit · decisão pendente');
  });

  it('o como funciona mostra as sete etapas da proposta, sem vídeo', () => {
    renderizar('/');

    const secao = screen.getByRole('region', { name: 'Do work item à Pull Request, em sete etapas' });
    const etapas = within(secao).getAllByRole('listitem');
    expect(etapas.map((e) => e.querySelector('h3')!.textContent)).toEqual(['Clona', 'Implementa', 'Compila', 'Testa', 'Avalia', 'Commit e push', 'Pull Request']);
    expect(etapas[4]).toHaveTextContent('abaixo da mínima, refaz');
    expect(secao.querySelector('video, img')).toBeNull();
    expect(PASSOS).toHaveLength(7);
  });

  it('o tour dos recursos é um tablist: oito abas, uma aberta por vez, e só a mídia da aberta montada', async () => {
    renderizar('/');

    const recursos = screen.getByRole('region', { name: 'Veja cada recurso funcionando' });
    expect(recursos).toHaveTextContent('Novo: os Projetos, com o plano em ondas e os itens em paralelo.');
    const abas = within(tour()).getAllByRole('tab');
    expect(abas.map((a) => a.textContent)).toEqual(['01 Orquestrador', '02 Projetosnovo', '03 O agente', '04 Fluxos', '05 Provedores', '06 Design', '07 Cherry-pick', '08 Horas']);
    expect(abas.map((a) => a.getAttribute('aria-selected'))).toEqual(['true', ...Array<string>(7).fill('false')]);
    // O "roving tabindex": só a aba aberta fica na ordem do Tab; e só o painel dela aparece.
    expect(abas.map((a) => a.tabIndex)).toEqual([0, ...Array<number>(7).fill(-1)]);
    expect(abas.map((a) => a.getAttribute('aria-controls'))).toEqual(RECURSOS.map((r) => `recurso-${r.id}`));
    expect(within(recursos).getAllByRole('tabpanel')).toHaveLength(1);
    const painel = within(recursos).getByRole('tabpanel', { name: '01 Orquestrador' });
    expect(painel).toHaveAttribute('id', 'recurso-orquestrador');
    expect(within(painel).getByRole('img', { name: RECURSOS[0].midia.alt })).toHaveAttribute('src', '/midia/orquestrador.gif');
    expect(recursos.querySelectorAll('.janela img, .janela video')).toHaveLength(1);

    await userEvent.click(within(tour()).getByRole('tab', { name: /^07 Cherry-pick/ }));
    const cherryPick = within(recursos).getByRole('tabpanel', { name: '07 Cherry-pick' });
    expect(cherryPick).toHaveAttribute('id', 'recurso-replicacao');
    expect(cherryPick.querySelector('.janela-barra')).toHaveTextContent('dev.kit - Cherry-pick');
    expect(cherryPick.querySelector('video')).toHaveAttribute('poster', '/midia/replicacao.jpg');
    expect(cherryPick.querySelector('figure > figcaption')).toHaveTextContent('Marque os commits a replicar');
    expect(recursos.querySelectorAll('video')).toHaveLength(1);
    expect(within(recursos).getAllByRole('tabpanel')).toHaveLength(1);
  });

  it('as setas, o Home e o End trocam de aba (dando a volta nas pontas) e levam o foco junto', async () => {
    renderizar('/');
    const usuario = userEvent.setup();
    const aba = (nome: RegExp) => within(tour()).getByRole('tab', { name: nome });

    await usuario.click(aba(/^01 Orquestrador/));
    await usuario.keyboard('{ArrowRight}');
    expect(aba(/^02 Projetos/)).toHaveAttribute('aria-selected', 'true');
    expect(aba(/^02 Projetos/)).toHaveFocus();
    await usuario.keyboard('{End}');
    expect(aba(/^08 Horas/)).toHaveAttribute('aria-selected', 'true');
    expect(aba(/^08 Horas/)).toHaveFocus();
    await usuario.keyboard('{ArrowRight}');
    expect(aba(/^01 Orquestrador/)).toHaveAttribute('aria-selected', 'true');
    await usuario.keyboard('{ArrowLeft}');
    expect(aba(/^08 Horas/)).toHaveFocus();
    await usuario.keyboard('{Home}');
    expect(aba(/^01 Orquestrador/)).toHaveFocus();
    expect(screen.getByRole('tabpanel', { name: '01 Orquestrador' })).toBeVisible();
  });

  it('cada painel tem o título, os marcadores e a mídia numa moldura de janela, com o texto alternativo e a legenda abaixo', async () => {
    renderizar('/');
    const usuario = userEvent.setup();

    for (const recurso of RECURSOS) {
      await usuario.click(within(tour()).getByRole('tab', { name: new RegExp(`^${recurso.numero} `) }));
      const painel = screen.getByRole('tabpanel');
      expect(painel).toHaveAttribute('id', `recurso-${recurso.id}`);
      expect(within(painel).getByRole('heading', { level: 3 })).toHaveTextContent(recurso.titulo);
      expect(within(painel).getAllByRole('listitem').map((l) => l.textContent)).toEqual(recurso.marcadores);
      const moldura = painel.querySelector('.janela')!;
      expect(moldura.querySelector('.janela-barra')).toHaveTextContent(tituloDaJanela(recurso.janela));
      if (recurso.midia.video) {
        const video = moldura.querySelector('video')!;
        expect(video).toHaveAttribute('poster', recurso.midia.poster);
        expect(video).toHaveAccessibleName(recurso.midia.alt);
      } else {
        // O Orquestrador (o GIF das Novidades) e os Projetos (a captura real do plano): imagem com o texto alternativo.
        expect(within(moldura as HTMLElement).getByRole('img', { name: recurso.midia.alt })).toHaveAttribute('src', recurso.midia.captura);
      }
      expect(painel.querySelector('figure > figcaption')).toHaveTextContent(recurso.legenda);
    }
  });

  it('o endereço antigo de um recurso (#recurso-<id>) abre a aba dele, também quando muda com a página aberta', () => {
    window.location.hash = '#recurso-design';
    try {
      renderizar('/');
      expect(within(tour()).getByRole('tab', { name: /^06 Design/ })).toHaveAttribute('aria-selected', 'true');

      act(() => {
        window.location.hash = '#recurso-fluxos';
        window.dispatchEvent(new HashChangeEvent('hashchange'));
      });
      expect(within(tour()).getByRole('tab', { name: /^04 Fluxos/ })).toHaveAttribute('aria-selected', 'true');
      expect(screen.getByRole('tabpanel', { name: '04 Fluxos' })).toHaveAttribute('id', 'recurso-fluxos');
    } finally {
      window.location.hash = '';
    }
  });

  it('o por quê é o bento com os sete cartões, o placar dos avaliadores e as evidências da entrega', () => {
    renderizar('/');

    const secao = screen.getByRole('region', { name: 'O trabalho repetitivo com os agentes. As decisões com o time.' });
    expect(within(secao).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(BENEFICIOS.map((b) => b.titulo));
    const placar = within(secao).getByRole('list', { name: 'Exemplo de avaliação, com nota mínima 90' });
    expect(within(placar).getAllByRole('listitem').map((l) => l.textContent)).toEqual(['Arquitetura93aceita', 'Testes45volta', 'Reuso92aceita']);
    expect(secao).toHaveTextContent('Nota mínima 90: abaixo dela, o trabalho volta ao agente.');
    expect(within(within(secao).getByRole('list', { name: 'O que a entrega guarda' })).getAllByRole('listitem').map((l) => l.textContent))
      .toEqual(['pedido.md', 'resumo', 'logs', 'diffs', 'capturas']);
  });

  it('o contato tem o formulário de demonstração e o FAQ abre em details', () => {
    renderizar('/');

    expect(within(screen.getByRole('region', { name: 'Quero uma demonstração' })).getByRole('form', { name: 'Pedido de demonstração' })).toBeInTheDocument();
    const faq = screen.getByRole('region', { name: 'Perguntas frequentes' });
    expect(faq.querySelectorAll('details').length).toBeGreaterThanOrEqual(10);
  });

  it('o cabeçalho tem o menu das seções, Entrar e Pedir demonstração', () => {
    renderizar('/');

    const menu = screen.getByRole('navigation', { name: 'Seções' });
    expect(within(menu).getAllByRole('listitem').map((l) => l.textContent)).toEqual(['Como funciona', 'Produto', 'Recursos', 'Catálogo', 'Segurança', 'Planos', 'Perguntas']);
    expect(within(menu).getByRole('link', { name: 'Produto' })).toHaveAttribute('href', '#bracos');
    expect(within(menu).getByRole('link', { name: 'Recursos' })).toHaveAttribute('href', '#recursos');
    expect(within(menu).getByRole('link', { name: 'Perguntas' })).toHaveAttribute('href', '#faq');
    expect(within(menu).getByRole('link', { name: 'Pedir demonstração' })).toHaveAttribute('href', '#contato');
    expect(within(menu).getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/login');
  });

  it('na tela estreita o menu abre pelo botão da proposta mobile e fecha ao escolher a seção', async () => {
    renderizar('/');
    const usuario = userEvent.setup();

    const botao = screen.getByRole('button', { name: 'Abrir o menu' });
    expect(botao).toHaveAttribute('aria-expanded', 'false');
    expect(document.getElementById('menu-da-landing')).not.toHaveClass('menu-aberto');

    await usuario.click(botao);
    expect(screen.getByRole('button', { name: 'Fechar o menu' })).toHaveAttribute('aria-expanded', 'true');
    expect(document.getElementById('menu-da-landing')).toHaveClass('menu-aberto');

    await usuario.click(within(screen.getByRole('navigation', { name: 'Seções' })).getByRole('link', { name: 'Planos' }));
    expect(screen.getByRole('button', { name: 'Abrir o menu' })).toHaveAttribute('aria-expanded', 'false');
  });

  it('o rodapé não explica o dashboard: leva ao Produto, aos Conceitos, à Fábrica, às Integrações, ao Comparativo, às Empresas, às Perguntas e ao login', () => {
    const { container } = renderizar('/');

    const rodape = within(container.querySelector('footer')!).getByRole('navigation', { name: 'Rodapé' });
    expect(within(rodape).getAllByRole('link').map((l) => l.textContent)).toEqual([
      'Produto', 'Conceitos', 'Fábrica agêntica', 'Integrações', 'Segurança', 'Comparativo', 'Empresas', 'Perguntas', 'Entrar',
    ]);
    expect(within(rodape).getByRole('link', { name: 'Produto' })).toHaveAttribute('href', '#bracos');
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

  it('os conceitos explicam agente, skill e rule, cada um com o exemplo no formato do arquivo, e o exemplo de ponta a ponta', () => {
    renderizar('/');

    const secao = screen.getByRole('region', { name: CONCEITOS.titulo });
    for (const conceito of CONCEITOS.itens) {
      const artigo = within(secao).getByRole('article', { name: conceito.titulo });
      expect(within(artigo).getAllByRole('listitem').map((l) => l.textContent)).toEqual(conceito.marcadores);
      const exemplo = within(artigo).getByRole('figure');
      expect(exemplo).toHaveTextContent(`Exemplo · ${conceito.exemplo.arquivo}`);
      expect(exemplo.querySelector('pre code')!.textContent).toBe(conceito.exemplo.codigo);
    }
    expect(within(secao).getAllByRole('article').map((a) => a.id)).toEqual(['conceito-agente', 'conceito-skill', 'conceito-rule']);
    const pontaAPonta = within(secao).getByRole('list', { name: 'Um exemplo, de ponta a ponta' });
    expect(within(pontaAPonta).getAllByRole('heading', { level: 4 }).map((h) => h.textContent)).toEqual(['O pedido', 'As rules', 'As skills', 'A entrega']);
  });

  it('a fábrica agêntica tem as estações em ordem, os Projetos em ondas e o time agêntico com os papéis', () => {
    renderizar('/');

    const secao = screen.getByRole('region', { name: FABRICA.titulo });
    const fabrica = within(secao).getByRole('article', { name: 'Fábrica agêntica' });
    const estacoes = within(fabrica).getByRole('list', { name: 'Estações da fábrica' });
    expect(within(estacoes).getAllByRole('heading', { level: 4 }).map((h) => h.textContent)).toEqual(['Planejar', 'Implementar', 'Verificar', 'Avaliar', 'Arbitrar', 'Entregar']);
    expect(fabrica).toHaveTextContent('Best-of-N');
    expect(fabrica).toHaveTextContent('Projetos: o Orquestrador planeja em ondas e dispara os itens em paralelo');
    const time = within(secao).getByRole('article', { name: 'Time agêntico' });
    expect(within(within(time).getByRole('list', { name: 'Papéis do time agêntico' })).getAllByRole('heading', { level: 4 }).map((h) => h.textContent))
      .toEqual(FABRICA.time.papeis.map((p) => p.titulo));
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

  it('a seção Catálogo (US #425) explica a atualização sozinha, lista os fluxos prontos e leva à vitrine', () => {
    renderizar('/');

    const catalogo = screen.getByRole('region', { name: CATALOGO.titulo });
    expect(catalogo).toHaveTextContent('Mais de 60 skills, 20 Rules e 12 fluxos prontos no catálogo básico');
    expect(within(catalogo).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(CATALOGO.itens.map((i) => i.titulo));
    expect(within(within(catalogo).getByRole('list', { name: 'Fluxos prontos' })).getAllByRole('listitem').map((l) => l.textContent)).toEqual(CATALOGO.fluxos);
    expect(within(catalogo).getByRole('link', { name: 'Ver o catálogo completo' })).toHaveAttribute('href', '/catalogo');
  });

  it('a seção Planos (US #425) tem o teste grátis, a assinatura individual e o plano para empresas', () => {
    renderizar('/');

    const planos = screen.getByRole('region', { name: 'Planos' });
    expect(within(planos).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(PLANOS.map((p) => p.nome));
    expect(within(planos).getByRole('link', { name: 'Assinar o individual' })).toHaveAttribute('href', '/comprar');
    expect(within(planos).getByRole('link', { name: 'Falar sobre o plano para empresas' })).toHaveAttribute('href', '#contato');
  });

  it('o FAQ responde sobre o celular, o app fechado, o devcli, o teste grátis, a compra e o cancelamento', () => {
    renderizar('/');

    const faq = screen.getByRole('region', { name: 'Perguntas frequentes' });
    for (const pergunta of [
      'Consigo acompanhar e aprovar pelo celular?', 'O dev.kit precisa ficar aberto?', 'Os agentes podem operar o próprio dev.kit?',
      'Como funciona o teste grátis?', 'Como compro só para mim?', 'Como cancelo a assinatura?', 'O que é o catálogo do dev.kit?',
    ]) expect(within(faq).getByText(pergunta)).toBeInTheDocument();
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

  it('no hero o download é um link discreto abaixo dos dois botões, na primeira dobra', async () => {
    renderizar('/');

    const link = await within(download()).findByRole('link', { name: 'Baixar o dev.kit versão 136' });
    expect(link).toHaveClass('link-baixar');
    expect(link).not.toHaveClass('botao');
    expect(download().previousElementSibling).toHaveClass('ctas');
    expect(hero()).toContainElement(download());
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
