import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { vi } from 'vitest';
import {
  API, appServiceParcial, appServicePronto, contaIndividual, escritas, filaPronta, permissaoComAplicativos, permissaoLiberada, provisao, servidor,
  siteEmProvisao, tokenDoDev,
} from '../testes/servidor';
import { INTERVALO_DE_ACOMPANHAMENTO_MS } from '../componentes/FilaDoControleRemoto';

afterEach(() => vi.restoreAllMocks());

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

  it('Meu contexto (US #550): lista o do usuário e grava um tema novo no nível usuário', async () => {
    renderizar('/conta', tokenDoDev());

    const secao = await screen.findByRole('region', { name: 'Meu contexto' });
    expect(await within(secao).findByText('Respostas curtas, com o porquê.')).toBeInTheDocument();
    expect(within(secao).getByText('A empresa já define: stack.')).toBeInTheDocument();

    const formulario = within(secao).getByRole('form', { name: 'Gravar no seu contexto' });
    await userEvent.type(within(formulario).getByLabelText('Tema'), 'revisao');
    await userEvent.type(within(formulario).getByLabelText('Texto'), 'Sempre com testes.');
    await userEvent.click(within(formulario).getByRole('button', { name: 'Gravar' }));

    expect(await within(secao).findByText('Tema revisao gravado no seu contexto.')).toBeInTheDocument();
    expect(escritas).toEqual([{ metodo: 'PUT', caminho: '/api/empresa/convencoes', corpo: { nivel: 'usuario', chave: 'contexto.revisao', valor: 'Sempre com testes.', time: '' } }]);
  });

  it('Meu contexto (US #663): a sugestão do agente fica à parte da regra, e promover a torna regra', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/conta', tokenDoDev());

    const secao = await screen.findByRole('region', { name: 'Meu contexto' });
    const regras = await within(secao).findByRole('list', { name: 'Seu contexto' });
    expect(within(regras).queryByText('Prefere diffs pequenos.')).not.toBeInTheDocument();
    const sugestoes = within(secao).getByRole('list', { name: 'Seu contexto — sugestões do agente' });
    expect(within(sugestoes).getByText('Prefere diffs pequenos.')).toBeInTheDocument();
    expect(within(sugestoes).getByText(/agente de Ana Souza/)).toBeInTheDocument();
    // A sugestão da empresa não entra no "A empresa já define" (lá só as regras).
    expect(within(secao).getByText('A empresa já define: stack.')).toBeInTheDocument();

    await userEvent.click(within(sugestoes).getByRole('button', { name: 'Promover a regra a sugestão revisao' }));

    expect(await within(secao).findByText('Sugestão revisao promovida a regra do seu contexto.')).toBeInTheDocument();
    expect(escritas).toEqual([{ metodo: 'POST', caminho: '/api/empresa/contexto/103/promover', corpo: { versaoEsperada: 1 } }]);
  });

  it('Meu contexto (US #550): sem empresa, avisa e não mostra o editor', async () => {
    servidor.use(http.get(`${API}/api/conta`, () => HttpResponse.json({ ...contaIndividual, empresa: null })));

    renderizar('/conta', tokenDoDev());

    const secao = await screen.findByRole('region', { name: 'Meu contexto' });
    expect(within(secao).getByText(/não é de uma empresa/)).toBeInTheDocument();
    expect(within(secao).queryByRole('form')).not.toBeInTheDocument();
  });

  it('o 401 da API encerra a sessão', async () => {
    servidor.use(http.get(`${API}/api/conta`, () => new HttpResponse(null, { status: 401 })));

    renderizar('/conta', tokenDoDev());

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Entrar' })).toBeInTheDocument());
    expect(screen.getByText('Sua sessão expirou. Entre de novo.')).toBeInTheDocument();
  });
});

describe('Fila do controle remoto na conta (controle remoto v2, item 4)', () => {
  it('sem a provisão liberada, a seção não existe', async () => {
    renderizar('/conta', tokenDoDev());

    expect(await screen.findByText('Ana Souza')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Fila do controle remoto' })).not.toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'Criar a fila' })).not.toBeInTheDocument();
  });

  it('liberada e sem fila, pede na região escolhida e mostra o pedido', async () => {
    provisao.permissao = permissaoLiberada;
    renderizar('/conta', tokenDoDev());

    const secao = await screen.findByRole('region', { name: 'Fila do controle remoto' });
    expect(await within(secao).findByText(/Nenhuma fila ainda/)).toBeInTheDocument();
    await userEvent.selectOptions(within(secao).getByLabelText('Região'), 'eastus2');
    await userEvent.click(within(secao).getByRole('button', { name: 'Criar a fila' }));

    expect(await within(secao).findByText(/Pedido aceito: o worker cria o grupo, a conta e a fila/)).toBeInTheDocument();
    expect(escritas).toEqual([{ metodo: 'POST', caminho: '/api/provisao/filas', corpo: { regiao: 'eastus2' } }]);
    const fila = await within(secao).findByRole('article', { name: 'Fila comandos na conta dkq2novafila1' });
    expect(within(fila).getByText('Pedida')).toBeInTheDocument();
    // A cota cheia: o Criar fica desligado, com o porquê.
    expect(within(secao).getByRole('button', { name: 'Criar a fila' })).toBeDisabled();
    expect(within(secao).getByText('Uma fila por usuário: remova a atual para criar outra.')).toBeInTheDocument();
  });

  it('a fila pronta mostra os nomes, o histórico da auditoria e remove com confirmação', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(true);
    provisao.permissao = permissaoLiberada;
    provisao.recursos = [filaPronta()];
    renderizar('/conta', tokenDoDev());

    const fila = await screen.findByRole('article', { name: 'Fila comandos na conta dkq2x7k4m9pa' });
    expect(within(fila).getByText('Pronta')).toBeInTheDocument();
    expect(within(fila).getByText('rg-devkit-u2')).toBeInTheDocument();
    expect(within(fila).queryByRole('button', { name: 'Repetir' })).not.toBeInTheDocument();

    await userEvent.click(within(fila).getByRole('button', { name: 'Histórico' }));
    const historico = await within(fila).findByRole('list', { name: 'Histórico' });
    expect(within(historico).getByText(/Pedido · ana.dev · brazilsouth/)).toBeInTheDocument();
    expect(within(historico).getByText(/Falhou \(transitória\) · O Azure limitou os pedidos/)).toBeInTheDocument();
    expect(within(historico).getByText(/Pronta · grupo, conta e fila comandos prontos/)).toBeInTheDocument();

    await userEvent.click(within(fila).getByRole('button', { name: 'Remover' }));

    expect(confirmar).toHaveBeenCalledWith(expect.stringMatching(/Remover a conta dkq2x7k4m9pa e a fila comandos do Azure/));
    expect(await screen.findByText(/Remoção pedida: o worker apaga a conta dkq2x7k4m9pa/)).toBeInTheDocument();
    expect(escritas).toEqual([{ metodo: 'DELETE', caminho: '/api/provisao/recursos/11', corpo: null }]);
    expect(await within(fila).findByText('Remoção pedida')).toBeInTheDocument();
    confirmar.mockRestore();
  });

  it('o que falhou mostra o erro e se repete', async () => {
    provisao.permissao = permissaoLiberada;
    provisao.recursos = [{ ...filaPronta(), estado: 'falhou', prontoEm: null, endpoint: '', erro: 'A identidade do worker não tem permissão para a conta de armazenamento (AuthorizationFailed).' }];
    renderizar('/conta', tokenDoDev());

    const fila = await screen.findByRole('article', { name: 'Fila comandos na conta dkq2x7k4m9pa' });
    expect(within(fila).getByText('Falhou')).toBeInTheDocument();
    expect(within(fila).getByRole('alert')).toHaveTextContent('AuthorizationFailed');
    await userEvent.click(within(fila).getByRole('button', { name: 'Repetir' }));

    expect(await screen.findByText(/Pedido devolvido ao worker/)).toBeInTheDocument();
    expect(escritas).toEqual([{ metodo: 'POST', caminho: '/api/provisao/recursos/11/repetir', corpo: null }]);
  });

  it('acompanha o worker: o pedido em provisão vira pronto na releitura', async () => {
    // Só o relógio do acompanhamento é capturado (o teste o dispara à mão); os outros, como o do waitFor, seguem de verdade.
    const relogios: (() => void)[] = [];
    const original = window.setInterval.bind(window);
    const intervalo = vi.spyOn(window, 'setInterval').mockImplementation(((acao: () => void, ms?: number, ...resto: unknown[]) => {
      if (ms !== INTERVALO_DE_ACOMPANHAMENTO_MS) return original(acao, ms, ...resto);
      relogios.push(acao);
      return 0;
    }) as typeof window.setInterval);
    provisao.permissao = permissaoLiberada;
    provisao.recursos = [{ ...filaPronta(), estado: 'em-provisao', prontoEm: null, tentativas: 1 }];
    renderizar('/conta', tokenDoDev());

    const fila = await screen.findByRole('article', { name: 'Fila comandos na conta dkq2x7k4m9pa' });
    expect(within(fila).getByText('Em provisão')).toBeInTheDocument();
    await waitFor(() => expect(relogios.length).toBeGreaterThan(0));

    provisao.recursos = [filaPronta()];
    relogios.at(-1)!();

    expect(await within(fila).findByText('Pronta')).toBeInTheDocument();
    intervalo.mockRestore();
  });

  it('o 403 da API (a liberação retirada no meio) aparece como erro', async () => {
    provisao.permissao = permissaoLiberada;
    servidor.use(http.post(`${API}/api/provisao/filas`, () =>
      HttpResponse.json({ detail: 'A provisão no Azure não está liberada para o seu usuário. Peça a liberação ao administrador do dev.kit.' }, { status: 403 })));
    renderizar('/conta', tokenDoDev());

    const secao = await screen.findByRole('region', { name: 'Fila do controle remoto' });
    await within(secao).findByText(/Nenhuma fila ainda/);
    await userEvent.click(within(secao).getByRole('button', { name: 'Criar a fila' }));

    expect(await within(secao).findByRole('alert')).toHaveTextContent('não está liberada para o seu usuário');
  });
});

describe('Sites e APIs no Azure na conta (controle remoto v2, item 12)', () => {
  it('sem a liberação, ou numa API anterior aos aplicativos, a seção não existe', async () => {
    provisao.permissao = permissaoLiberada;
    renderizar('/conta', tokenDoDev());

    expect(await screen.findByRole('region', { name: 'Fila do controle remoto' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Sites e APIs no Azure' })).not.toBeInTheDocument();
  });

  it('mostra a cota de cada tipo, o custo com o alerta e só os aplicativos (a fila fica na seção dela)', async () => {
    provisao.permissao = permissaoComAplicativos;
    provisao.recursos = [filaPronta(), appServicePronto(), { ...appServiceParcial() }, siteEmProvisao()];
    renderizar('/conta', tokenDoDev());

    const secao = await screen.findByRole('region', { name: 'Sites e APIs no Azure' });
    const cota = await within(secao).findByLabelText('Cota e custo');
    expect(cota).toHaveTextContent('App Service2 de 2Cota cheia: remova um para criar outro.');
    expect(cota).toHaveTextContent('Static Web App1 de 3Plano Free · cabe mais 2.');
    expect(cota).toHaveTextContent('Custo estimado do mêsR$ 123,40 de R$ 150,00');
    expect(within(cota).getByRole('meter', { name: 'Custo do mês' })).toHaveAttribute('aria-valuenow', '123.4');
    expect(within(secao).getByText(/O custo estimado do mês passou de 80% do seu limite \(R\$ 123,40 de R\$ 150,00\)/)).toBeInTheDocument();

    expect(within(secao).getAllByRole('article').map((a) => a.getAttribute('aria-label')))
      .toEqual(['App Service api-pedidos', 'App Service api-relatorios', 'Static Web App site-docs']);
    expect(within(screen.getByRole('region', { name: 'Fila do controle remoto' })).getAllByRole('article')).toHaveLength(1);

    const pronto = within(secao).getByRole('article', { name: 'App Service api-pedidos' });
    expect(within(pronto).getByText('Pronto')).toBeInTheDocument();
    expect(pronto).toHaveTextContent('PlanoB1 · Linux');
    expect(pronto).toHaveTextContent('Custo do mêsR$ 61,70 (estimado)');
    expect(within(pronto).getByRole('link', { name: 'https://app-u2-api-pedidos-7f3k.azurewebsites.net' })).toBeInTheDocument();
    expect(within(within(secao).getByRole('article', { name: 'Static Web App site-docs' })).getByText('Em provisão')).toBeInTheDocument();
  });

  it('com a cota de App Service cheia, o tipo fica desabilitado e o pedido vai como Static Web App', async () => {
    provisao.permissao = permissaoComAplicativos;
    provisao.recursos = [appServicePronto(), { ...appServicePronto(), id: 24, rotulo: 'api-dois', nome: 'app-u2-api-dois-aaaa' }];
    renderizar('/conta', tokenDoDev());

    const secao = await screen.findByRole('region', { name: 'Sites e APIs no Azure' });
    const tipo = await within(secao).findByLabelText('Tipo');
    await waitFor(() => expect(tipo).toHaveValue('static-web-app'));
    expect(within(tipo).getByRole('option', { name: 'App Service · cota cheia' })).toBeDisabled();
    expect(within(secao).getByLabelText('Região')).toHaveValue('eastus2');
    expect(within(within(secao).getByLabelText('Plano')).getByRole('option', { name: 'Free · R$ 0,00/mês' })).toBeInTheDocument();
    expect(within(secao).getByText('Até 2 App Services e 3 Static Web Apps por usuário. O nome no Azure ganha o prefixo e um sufixo únicos.')).toBeInTheDocument();

    await userEvent.type(within(secao).getByLabelText('Nome'), 'Site-Blog');
    await userEvent.click(within(secao).getByRole('button', { name: 'Criar' }));

    expect(await within(secao).findByText(/Pedido aceito: o worker cria o site swa-u2-site-blog-n3w0/)).toBeInTheDocument();
    expect(escritas).toEqual([{ metodo: 'POST', caminho: '/api/provisao/aplicativos', corpo: { tipo: 'static-web-app', nome: 'site-blog', regiao: 'eastus2', plano: 'Free' } }]);
    expect(await within(secao).findByRole('article', { name: 'Static Web App site-blog' })).toHaveTextContent('Pedida');
  });

  it('a recusa da API (a cota cheia num pedido simultâneo) aparece como erro', async () => {
    provisao.permissao = permissaoComAplicativos;
    servidor.use(http.post(`${API}/api/provisao/aplicativos`, () =>
      HttpResponse.json({ detail: 'A cota de App Service está cheia (2 de 2). Remova um para criar outro.', codigo: 'cota-cheia' }, { status: 409 })));
    renderizar('/conta', tokenDoDev());

    const secao = await screen.findByRole('region', { name: 'Sites e APIs no Azure' });
    await within(secao).findByText(/Nenhum site ou API ainda/);
    await userEvent.type(within(secao).getByLabelText('Nome'), 'api-pedidos');
    await userEvent.click(within(secao).getByRole('button', { name: 'Criar' }));

    expect(await within(secao).findByRole('alert')).toHaveTextContent('A cota de App Service está cheia (2 de 2)');
  });

  it('a falha parcial diz o que ficou no Azure e o Repetir retoma do aplicativo', async () => {
    provisao.permissao = permissaoComAplicativos;
    provisao.recursos = [appServiceParcial()];
    renderizar('/conta', tokenDoDev());

    const parcial = await screen.findByRole('article', { name: 'App Service api-relatorios' });
    expect(within(parcial).getByText('Falhou em parte')).toBeInTheDocument();
    expect(parcial).toHaveTextContent('PlanoB1 · Linux (criado)');
    expect(within(parcial).getByRole('alert')).toHaveTextContent(
      'Falha parcial: o plano asp-u2-api-relatorios-q8m2 foi criado e o aplicativo não — o Azure respondeu 409, nome em uso. Repetir retoma do aplicativo; Remover apaga também o plano que ficou.');

    await userEvent.click(within(parcial).getByRole('button', { name: 'Repetir' }));

    expect(await screen.findByText('Pedido devolvido ao worker: ele retoma do aplicativo, sem criar o plano de novo.')).toBeInTheDocument();
    expect(escritas).toEqual([{ metodo: 'POST', caminho: '/api/provisao/recursos/22/repetir', corpo: null }]);
  });

  it('remover pede o código do segundo fator no próprio cartão; o código errado é recusado', async () => {
    provisao.permissao = permissaoComAplicativos;
    provisao.recursos = [appServicePronto()];
    renderizar('/conta', tokenDoDev());

    const app = await screen.findByRole('article', { name: 'App Service api-pedidos' });
    await userEvent.click(within(app).getByRole('button', { name: 'Remover' }));
    const confirmacao = within(app).getByRole('group', { name: 'Confirmar a remoção' });
    expect(confirmacao).toHaveTextContent('Remover o App Service api-pedidos? O site sai do ar e o plano B1 é apagado do Azure. Não tem volta.');
    const botao = within(confirmacao).getByRole('button', { name: 'Remover com segundo fator' });
    expect(botao).toBeDisabled();

    // O código só se digita depois de conferido o autenticador (o GET do segundo fator).
    const codigo = within(confirmacao).getByLabelText('Código de 6 dígitos');
    await waitFor(() => expect(codigo).toBeEnabled());
    await userEvent.type(codigo, '000000');
    await waitFor(() => expect(botao).toBeEnabled());
    await userEvent.click(botao);
    expect(await within(confirmacao).findByRole('alert')).toHaveTextContent('Código incorreto');

    await userEvent.type(within(confirmacao).getByLabelText('Código de 6 dígitos'), '123456');
    await userEvent.click(within(confirmacao).getByRole('button', { name: 'Remover com segundo fator' }));

    expect(await screen.findByText(/Remoção pedida: o worker apaga o site app-u2-api-pedidos-7f3k e o plano asp-u2-api-pedidos-7f3k/)).toBeInTheDocument();
    expect(escritas.map((e) => e.corpo)).toEqual([{ codigo: '000000' }, { codigo: '123456' }]);
    expect(await within(app).findByText('Remoção pedida')).toBeInTheDocument();
    expect(within(app).queryByRole('group', { name: 'Confirmar a remoção' })).not.toBeInTheDocument();
  });

  it('sem o autenticador, configura-o ali mesmo e então confirma a remoção', async () => {
    provisao.permissao = permissaoComAplicativos;
    provisao.recursos = [{ ...siteEmProvisao(), estado: 'pronto', prontoEm: '2026-11-05T17:40:00Z', endpoint: 'https://gentle-sea.azurestaticapps.net' }];
    provisao.segundoFatorAtivo = false;
    renderizar('/conta', tokenDoDev());

    const site = await screen.findByRole('article', { name: 'Static Web App site-docs' });
    await userEvent.click(within(site).getByRole('button', { name: 'Remover' }));
    const confirmacao = within(site).getByRole('group', { name: 'Confirmar a remoção' });
    expect(confirmacao).toHaveTextContent('O site sai do ar e é apagado do Azure. Não tem volta.');

    await userEvent.type(await within(confirmacao).findByLabelText('Senha atual'), 'minha-senha');
    await userEvent.click(within(confirmacao).getByRole('button', { name: 'Configurar autenticador' }));
    expect(await within(confirmacao).findByText('JBSWY3DPEHPK3PXP')).toBeInTheDocument();
    await userEvent.type(within(confirmacao).getByLabelText('Código de 6 dígitos'), '654321');
    await userEvent.click(within(confirmacao).getByRole('button', { name: 'Ativar autenticador' }));

    const botao = await within(confirmacao).findByRole('button', { name: 'Remover com segundo fator' });
    await userEvent.type(within(confirmacao).getByLabelText('Código de 6 dígitos'), '123456');
    await userEvent.click(botao);

    expect(await screen.findByText(/Remoção pedida: o worker apaga o site swa-u2-site-docs-2m9q em instantes/)).toBeInTheDocument();
    expect(escritas.map((e) => e.caminho)).toEqual(['/api/remoto/segundo-fator/preparar', '/api/remoto/segundo-fator/confirmar', '/api/provisao/recursos/23/remover']);
  });
});
