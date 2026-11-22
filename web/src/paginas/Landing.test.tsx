import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import {
  BENEFICIOS, BOARD_WEB, CATALOGO, COMPARATIVO, CONCEITOS, CONTEXTO, FABRICA, PASSOS, PLANOS, PLATAFORMA, PRODUTOS_COMPARADOS, RECURSOS, SECOES, tituloDaJanela,
} from '../conteudo/landing';
import { podeAbrir } from '../navegacao';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev, tokenDoGestor, tokenValido } from '../testes/servidor';

/** As rotas públicas a que a landing leva, e as que pedem login (o rótulo do link diz isso). As duas listas são do App. */
const ROTAS_PUBLICAS = ['/', '/login', '/catalogo', '/comprar'];
const ROTAS_COM_LOGIN = ['/board'];

describe('Landing', () => {
  it('abre sem login com as seções na ordem da proposta ajustes-2-landing v1', () => {
    const { container } = renderizar('/');

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Planeje, acompanhe e entregue com agentes de código.');
    const ordem = [...container.querySelectorAll('main > section')].map((s) => s.id);
    expect(ordem).toEqual(SECOES.map((s) => s.id));
    expect(ordem.slice(0, 4)).toEqual(['inicio', 'plataforma', 'board-web', 'como-funciona']);
    expect(ordem.at(-1)).toBe('comecar');
  });

  it('o hero da proposta: o rótulo, "agentes de código." em destaque, o texto das quatro peças e os dois CTAs', () => {
    renderizar('/');

    const hero = screen.getByRole('region', { name: /agentes de código/ });
    expect(hero.querySelector('.secao-rotulo')).toHaveTextContent('Seu trabalho, do plano à entrega');
    expect(hero.querySelector('h1 .destaque')).toHaveTextContent(/^agentes de código\.$/);
    expect(hero).toHaveTextContent('O dev.kit conecta o desktop, o serviço, o devcli e o Board web. Você acompanha projetos, Design e entregas em um só lugar.');
    expect(within(hero).getByRole('link', { name: 'Quero uma demonstração' })).toHaveAttribute('href', '#contato');
    expect(within(hero).getByRole('link', { name: 'Ver como funciona' })).toHaveAttribute('href', '#como-funciona');
    // O hero antigo saiu: nem o selo do Azure DevOps nem a amostra do Board.
    expect(hero).not.toHaveTextContent('Para times no Azure DevOps');
    expect(within(hero).queryByRole('img', { name: /Amostra do Board/ })).toBeNull();
  });

  it('a amostra do hero é o cartão "dev.kit · projetos" da proposta, marcado como demonstração, com as três etapas e os papéis', () => {
    renderizar('/');

    const hero = screen.getByRole('region', { name: /agentes de código/ });
    const amostra = within(hero).getByRole('complementary', { name: 'Do pedido à entrega' });
    expect(amostra).toHaveTextContent('dev.kit · projetos');
    expect(within(amostra).getByText('Demonstração')).toHaveClass('etiqueta');
    expect(amostra).toHaveTextContent('Planejamento em ondas, propostas visuais e impacto por item.');
    const etapas = within(within(amostra).getByRole('list', { name: 'Etapas do item' })).getAllByRole('listitem');
    expect(etapas.map((e) => [e.querySelector('.amostra-do-projeto-etapa')!.textContent, e.querySelector('.pilula-da-etapa')!.textContent])).toEqual([
      ['Planejar e conferir propostas', 'Planejamento'], ['Implementar no branch do item', 'Agente'], ['Comparar proposta e recaptura', 'Avaliação'],
    ]);
    expect(amostra).toHaveTextContent('Você mantém as decisões de aprovação e publicação.');
  });

  it('os recursos do dev.kit.page: os três cartões com o link da proposta e as quatro peças — desktop, serviço, devcli e Board web', () => {
    renderizar('/');

    const secao = screen.getByRole('region', { name: 'O projeto continua com você, onde estiver.' });
    expect(secao.querySelector('.secao-rotulo')).toHaveTextContent('Recursos do dev.kit.page');
    for (const cartao of PLATAFORMA.cartoes) {
      const item = within(secao).getByRole('heading', { level: 3, name: cartao.titulo }).closest('li')!;
      expect(item).toHaveTextContent(cartao.texto);
      // A seta é decorativa (aria-hidden): o nome do link é só o rótulo.
      expect(within(item).getByRole('link', { name: cartao.link.rotulo })).toHaveAttribute('href', cartao.link.destino);
    }
    const pecas = within(secao).getByRole('list', { name: 'Desktop, serviço, devcli e Board web' });
    expect(within(pecas).getAllByRole('heading', { level: 4 }).map((h) => h.textContent)).toEqual(['App desktop', 'Serviço do Windows', 'devcli', 'Board web']);
    expect(within(pecas).getByText('No navegador e no celular')).toBeInTheDocument();
  });

  it('o Board web mostra a etiqueta do que cada recurso exige, as condições e as chamadas Entrar e Ver os planos', () => {
    renderizar('/');

    const secao = screen.getByRole('region', { name: BOARD_WEB.titulo });
    const cartoes = within(within(secao).getByRole('list', { name: 'Recursos do Board web' })).getAllByRole('listitem');
    expect(cartoes.map((c) => [c.querySelector('h3')!.textContent, c.querySelector('.requisito')!.textContent])).toEqual(
      BOARD_WEB.recursos.map((r) => [r.titulo, BOARD_WEB.requisitos[r.requisito]]),
    );
    expect(cartoes.find((c) => c.textContent!.includes('Conversa remota'))).toHaveTextContent('Com a fila liberada');
    const condicoes = within(secao).getByRole('list', { name: 'O que é preciso' });
    expect(condicoes).toHaveTextContent('criada quando o administrador do dev.kit libera a provisão para a sua conta');
    expect(within(secao).getByRole('link', { name: 'Entrar no Board web' })).toHaveAttribute('href', '/board');
    expect(within(secao).getByRole('link', { name: 'Ver os planos' })).toHaveAttribute('href', '#planos');
  });

  it('todo link da landing leva a uma âncora que existe ou a uma rota do site; o externo é só o download da API', async () => {
    const { container } = renderizar('/');
    // O download só vira link depois da versão: sem esperar por ele, o teste não conferiria o único endereço externo.
    await screen.findByRole('link', { name: 'Baixar o dev.kit para Windows' });

    const links = [...container.querySelectorAll<HTMLAnchorElement>('a[href]')];
    expect(links.length).toBeGreaterThan(40);
    const externos: string[] = [];
    for (const link of links) {
      const href = link.getAttribute('href')!;
      if (href.startsWith('#')) expect(document.getElementById(href.slice(1)), `${link.textContent} → ${href}`).not.toBeNull();
      else if (href.startsWith('/')) expect([...ROTAS_PUBLICAS, ...ROTAS_COM_LOGIN], `${link.textContent} → ${href}`).toContain(href);
      else externos.push(href);
      // A rota que pede login é dita no rótulo: o Entrar.
      if (ROTAS_COM_LOGIN.includes(href)) expect(link).toHaveTextContent(/^Entrar/);
    }
    expect([...new Set(externos)]).toEqual([`${API}/api/versoes/ultima/download`]);
    // O pular para o conteúdo usa a âncora do site: o FocoDaRota dá ao <main> o id conteudo-principal (o #conteudo de antes sumia).
    expect(screen.getByRole('link', { name: 'Pular para o conteúdo' })).toHaveAttribute('href', '#conteudo-principal');
    expect(container.querySelector('main')).toHaveAttribute('id', 'conteudo-principal');
  });

  it('o Entrar no Board web, sem sessão, cai no login: a landing não promete o Board sem a conta', async () => {
    renderizar('/');
    await userEvent.click(within(screen.getByRole('region', { name: BOARD_WEB.titulo })).getByRole('link', { name: 'Entrar no Board web' }));
    expect(screen.getByRole('heading', { name: 'Entrar' })).toBeInTheDocument();
  });

  it('o Explorar o catálogo leva à vitrine pública do catálogo, sem login', async () => {
    renderizar('/');
    await userEvent.click(screen.getByRole('link', { name: 'Explorar o catálogo' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Catálogo do dev.kit' })).toBeInTheDocument();
  });

  it('os recursos do Board web moram em rotas que todo papel logado abre, e o contexto é de quem é de uma empresa', () => {
    for (const sessao of [tokenDoDev(), tokenDoGestor(), tokenValido()])
      for (const { rota, titulo } of BOARD_WEB.recursos) expect(podeAbrir(sessao, rota), `${sessao.papel} → ${titulo} (${rota})`).toBe(true);
    // Repositórios e convenções (o contexto do time): do gestor e do dev, nunca do admin, que não é de empresa.
    expect(podeAbrir(tokenDoDev(), '/empresa/conhecimento')).toBe(true);
    expect(podeAbrir(tokenDoGestor(), '/empresa/conhecimento')).toBe(true);
    expect(podeAbrir(tokenValido(), '/empresa/conhecimento')).toBe(false);
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

  it('os recursos têm as abas 01–07, com o novo da versão 5 marcado, e cada aba leva ao seu bloco (US #467)', () => {
    renderizar('/');

    const recursos = screen.getByRole('region', { name: 'Cada recurso com a sua demonstração' });
    expect(recursos).toHaveTextContent('Novo na versão 5: o Orquestrador, a escolha do provedor cognitivo e o Design.');
    const abas = within(within(recursos).getByRole('navigation', { name: 'Recursos em destaque' })).getAllByRole('link');
    expect(abas.map((a) => a.textContent)).toEqual([
      '01 Orquestradornovo', '02 O agente sobre a task', '03 Provedor cognitivonovo', '04 Designnovo', '05 Fluxos com avaliadores', '06 Cherry-pick', '07 Horas',
    ]);
    expect(abas.filter((a) => a.querySelector('.recurso-novo'))).toHaveLength(3);
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
      if (recurso.midia.video) {
        const video = moldura.querySelector('video')!;
        expect(video).toHaveAttribute('poster', recurso.midia.poster);
        expect(video).toHaveAccessibleName(recurso.midia.alt);
      } else {
        // O Orquestrador: o GIF das Novidades da versão 5, animado, com o texto alternativo.
        const gif = within(moldura as HTMLElement).getByRole('img', { name: recurso.midia.alt });
        expect(gif).toHaveAttribute('src', recurso.midia.captura);
      }
      expect(artigo.querySelector('figure > figcaption')).toHaveTextContent(recurso.legenda);
    }
    expect(recursos.querySelectorAll('video')).toHaveLength(6);
    expect(document.querySelectorAll('main video')).toHaveLength(6);
    expect(recursos.querySelector('#recurso-orquestrador img')).toHaveAttribute('src', '/midia/orquestrador.gif');
  });

  it('o contato tem o formulário de demonstração e o FAQ abre em details', () => {
    renderizar('/');

    expect(within(screen.getByRole('region', { name: 'Quero uma demonstração' })).getByRole('form', { name: 'Pedido de demonstração' })).toBeInTheDocument();
    const faq = screen.getByRole('region', { name: 'Perguntas frequentes' });
    expect(faq.querySelectorAll('details').length).toBeGreaterThanOrEqual(4);
  });

  it('o cabeçalho tem o menu da proposta ajustes-2-landing v1 (Como funciona, Recursos, Catálogo e Planos), Entrar e Pedir demonstração', () => {
    renderizar('/');

    const menu = screen.getByRole('navigation', { name: 'Seções' });
    expect(within(menu).getAllByRole('listitem').map((l) => l.textContent)).toEqual(['Como funciona', 'Recursos', 'Catálogo', 'Planos']);
    expect(within(menu).getByRole('link', { name: 'Recursos' })).toHaveAttribute('href', '#recursos');
    expect(within(menu).getByRole('link', { name: 'Catálogo' })).toHaveAttribute('href', '#catalogo');
    expect(within(menu).getByRole('link', { name: 'Planos' })).toHaveAttribute('href', '#planos');
    expect(within(menu).getByRole('link', { name: 'Pedir demonstração' })).toHaveAttribute('href', '#contato');
    expect(within(menu).getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/login');
  });

  it('o que saiu do menu do topo está no rodapé: plataforma, Board web, integrações, segurança e perguntas', () => {
    const { container } = renderizar('/');

    const rodape = within(container.querySelector('footer')!).getByRole('navigation', { name: 'Rodapé' });
    for (const [nome, href] of [['Plataforma', '#plataforma'], ['Board web', '#board-web'], ['Integrações', '#integracoes'], ['Segurança', '#seguranca'], ['Perguntas', '#faq']])
      expect(within(rodape).getByRole('link', { name: nome })).toHaveAttribute('href', href);
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

    await usuario.click(screen.getByRole('link', { name: 'Planos' }));
    expect(screen.getByRole('button', { name: 'Abrir o menu' })).toHaveAttribute('aria-expanded', 'false');
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

  it('a fábrica agêntica tem as estações em ordem e o time agêntico os papéis, com o rodapé apontando as duas seções', () => {
    const { container } = renderizar('/');

    const secao = screen.getByRole('region', { name: FABRICA.titulo });
    const fabrica = within(secao).getByRole('article', { name: 'Fábrica agêntica' });
    const estacoes = within(fabrica).getByRole('list', { name: 'Estações da fábrica' });
    expect(within(estacoes).getAllByRole('heading', { level: 4 }).map((h) => h.textContent)).toEqual(['Planejar', 'Implementar', 'Verificar', 'Avaliar', 'Arbitrar', 'Entregar']);
    expect(fabrica).toHaveTextContent('Best-of-N');
    const time = within(secao).getByRole('article', { name: 'Time agêntico' });
    expect(within(within(time).getByRole('list', { name: 'Papéis do time agêntico' })).getAllByRole('heading', { level: 4 }).map((h) => h.textContent))
      .toEqual(FABRICA.time.papeis.map((p) => p.titulo));

    const rodape = within(container.querySelector('footer')!).getByRole('navigation', { name: 'Rodapé' });
    expect(within(rodape).getByRole('link', { name: 'Conceitos' })).toHaveAttribute('href', '#conceitos');
    expect(within(rodape).getByRole('link', { name: 'Fábrica agêntica' })).toHaveAttribute('href', '#fabrica');
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

  it('a seção Catálogo e contexto explica a atualização sozinha, o contexto do time e leva à vitrine', () => {
    renderizar('/');

    const catalogo = screen.getByRole('region', { name: CATALOGO.titulo });
    expect(catalogo.querySelector('.secao-rotulo')).toHaveTextContent('Catálogo e contexto');
    expect(within(within(catalogo).getByRole('list', { name: 'O catálogo' })).getAllByRole('heading', { level: 3 }).map((h) => h.textContent))
      .toEqual(CATALOGO.itens.map((i) => i.titulo));
    const contexto = within(catalogo).getByRole('list', { name: CONTEXTO.titulo });
    expect(within(contexto).getAllByRole('heading', { level: 4 }).map((h) => h.textContent)).toEqual(CONTEXTO.itens.map((i) => i.titulo));
    expect(catalogo).toHaveTextContent('Com a conta de uma empresa ou da assinatura individual');
    expect(within(catalogo).getByRole('link', { name: 'Ver o catálogo completo' })).toHaveAttribute('href', '/catalogo');
  });

  it('a seção Planos (US #425) tem o teste grátis, a assinatura individual e o plano para empresas', () => {
    renderizar('/');

    const planos = screen.getByRole('region', { name: 'Planos' });
    expect(within(planos).getAllByRole('heading', { level: 3 }).map((h) => h.textContent)).toEqual(PLANOS.map((p) => p.nome));
    expect(within(planos).getByRole('link', { name: 'Assinar o individual' })).toHaveAttribute('href', '/comprar');
    expect(within(planos).getByRole('link', { name: 'Falar sobre o plano para empresas' })).toHaveAttribute('href', '#contato');
  });

  it('o FAQ responde sobre o teste grátis, a compra, o cancelamento e o Board web longe da máquina', () => {
    renderizar('/');

    const faq = screen.getByRole('region', { name: 'Perguntas frequentes' });
    for (const pergunta of [
      'Como funciona o teste grátis?', 'Como compro só para mim?', 'Como cancelo a assinatura?', 'O que é o catálogo do dev.kit?',
      'O que eu faço pelo Board web, longe da máquina?', 'Preciso deixar o dev.kit aberto para o Board web funcionar?',
    ])
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

  it('é o link "Baixar o dev.kit para Windows" da proposta, com a versão, a data, o tamanho e o teste grátis sem login ao lado', async () => {
    renderizar('/');

    const link = await within(download()).findByRole('link', { name: 'Baixar o dev.kit para Windows' });
    expect(link).toHaveAttribute('href', ENDERECO);
    expect(download()).toHaveTextContent('versão 136 · 09/10/2026 · 50,0 MB · 14 dias grátis, sem login');
    expect(within(download()).queryByRole('button')).not.toBeInTheDocument();
  });

  it('o endereço do download aparece escrito na página, para copiar e colar na barra do navegador', async () => {
    renderizar('/');

    await within(download()).findByRole('link', { name: 'Baixar o dev.kit para Windows' });
    expect(within(download()).getByText(ENDERECO).tagName).toBe('CODE');
    expect(download()).toHaveTextContent(`Ou copie o endereço: ${ENDERECO}`);
  });

  it('o link é o alias permanente e anônimo: o mesmo href com ou sem login, e não o da versão', async () => {
    renderizar('/', tokenDoDev());

    const link = await within(download()).findByRole('link', { name: 'Baixar o dev.kit para Windows' });
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

    const link = await within(download()).findByRole('link', { name: 'Baixar o dev.kit para Windows' });
    expect(link).toHaveClass('link-baixar');
    expect(link).not.toHaveClass('botao');
    expect(download().previousElementSibling).toHaveClass('ctas');
  });

  it('com a atualização indisponível (503), não há link e a nota diz por quê: o download não é prometido sem recurso', async () => {
    servidor.use(http.get(`${API}/api/versoes/ultima`, () => HttpResponse.json({ detail: 'indisponível' }, { status: 503 })));

    renderizar('/');

    expect(await within(download()).findByText('Download indisponível no momento.')).toBeInTheDocument();
    expect(within(download()).queryByRole('link')).not.toBeInTheDocument();
    expect(within(download()).getByText('Baixar o dev.kit para Windows')).toHaveAttribute('aria-disabled', 'true');
    const planos = screen.getByRole('region', { name: 'Planos' });
    expect(await within(planos).findByRole('button', { name: 'Testar grátis por 14 dias' })).toBeDisabled();
  });
});
