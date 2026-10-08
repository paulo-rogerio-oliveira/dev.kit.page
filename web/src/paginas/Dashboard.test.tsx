import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { textoDoBug } from '../componentes/ExcecoesNaoClassificadas';
import { entregues } from '../testes/navegador';
import { renderizar } from '../testes/renderizar';
import { API, demonstracao, erroDetalhe, qualidadeVazia, reacoes, requisicoes, roi, servidor, tokenDoGestor, tokenValido } from '../testes/servidor';

const kpi = (rotulo: string) => screen.getByTestId(`kpi-${rotulo}`);

/** O texto de um arquivo baixado (o Blob do jsdom não tem `text()`). */
const lerTexto = (blob: Blob) => new Promise<string>((pronto) => {
  const leitor = new FileReader();
  leitor.onload = () => pronto(String(leitor.result));
  leitor.readAsText(blob);
});

/** Abre o dashboard e o detalhe do grupo de exceção da fixture. */
async function abrirOGrupo(sessao = tokenValido()) {
  renderizar('/dashboard', sessao);
  const secao = await screen.findByRole('region', { name: 'Exceções não classificadas' });
  await userEvent.click(await within(secao).findByRole('button', { name: 'System.InvalidOperationException' }));
  return within(await screen.findByRole('article', { name: 'Detalhe da exceção' }));
}

describe('Dashboard', () => {
  it('mostra os KPIs de quantidade e qualidade com os valores da API', async () => {
    renderizar('/dashboard', tokenValido());

    await screen.findByRole('heading', { name: 'Quantidade de uso' });
    expect(kpi('Sessões')).toHaveTextContent('3');
    expect(kpi('Turnos')).toHaveTextContent('8');
    expect(kpi('Ferramentas acionadas')).toHaveTextContent('41');
    expect(kpi('Arquivos alterados')).toHaveTextContent('17');
    expect(kpi('Taxa de falha de turno')).toHaveTextContent(/25,0\s?%/);
    expect(kpi('Duração média do turno')).toHaveTextContent('42,5 s');
    expect(kpi('Nota média dos avaliadores')).toHaveTextContent('91,5');
    expect(kpi('Objetivos cumpridos / recusados')).toHaveTextContent('1 / 1');
    expect(screen.getByText('limite-de-uso')).toBeInTheDocument();
  });

  it('mostra os eventos recentes', async () => {
    renderizar('/dashboard', tokenValido());

    const tabela = (await screen.findByText('TurnoExecutado')).closest('table')!;
    expect(within(tabela).getByText('claude')).toBeInTheDocument();
    expect(within(tabela).getByText('máquina a1b2c3d4')).toBeInTheDocument();
    expect(screen.getByText(/Página 1 de 1 · 1 eventos/)).toBeInTheDocument();
  });

  it('divisor zero aparece como traço, nunca NaN', async () => {
    servidor.use(http.get(`${API}/api/dashboard/qualidade`, () => HttpResponse.json(qualidadeVazia)));

    renderizar('/dashboard', tokenValido());

    await screen.findByRole('heading', { name: 'Qualidade de uso' });
    expect(kpi('Taxa de falha de turno')).toHaveTextContent('—');
    expect(kpi('Nota média dos avaliadores')).toHaveTextContent('—');
    expect(kpi('Turnos por objetivo cumprido')).toHaveTextContent('—');
    expect(document.body).not.toHaveTextContent('NaN');
    expect(screen.getByText('Nenhuma falha de turno no período.')).toBeInTheDocument();
  });

  it('os filtros de máquina e de período refazem a consulta', async () => {
    renderizar('/dashboard', tokenValido());
    await screen.findByRole('heading', { name: 'Quantidade de uso' });
    await screen.findByRole('option', { name: /máquina a1b2c3d4/ });

    await userEvent.selectOptions(screen.getByLabelText('Máquina'), '2');
    await waitFor(() => expect(requisicoes.at(-1)?.searchParams.get('maquina')).toBe('2'));

    await userEvent.selectOptions(screen.getByLabelText('Período'), '7');
    await waitFor(() => {
      const ultima = requisicoes.at(-1)!;
      const dias = (Date.parse(ultima.searchParams.get('ate')!) - Date.parse(ultima.searchParams.get('de')!)) / 86_400_000 + 1;
      expect(dias).toBe(7);
    });
  });

  it('lista os pedidos de demonstração e exclui com confirmação', async () => {
    let itens = [demonstracao];
    const excluidos: string[] = [];
    servidor.use(
      http.get(`${API}/api/dashboard/demonstracoes`, () => HttpResponse.json({ itens, total: itens.length, numeroDaPagina: 1, tamanho: 10 })),
      http.delete(`${API}/api/dashboard/demonstracoes/:id`, ({ params }) => {
        excluidos.push(String(params.id));
        itens = [];
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderizar('/dashboard', tokenValido());

    const secao = await screen.findByRole('region', { name: 'Pedidos de demonstração' });
    expect(await within(secao).findByText('Ana Souza')).toBeInTheDocument();
    expect(within(secao).getByRole('link', { name: 'ana@empresa.com.br' })).toHaveAttribute('href', 'mailto:ana@empresa.com.br');

    await userEvent.click(within(secao).getByRole('button', { name: 'Excluir o pedido de Ana Souza' }));

    expect(confirmar).toHaveBeenCalled();
    expect(await within(secao).findByText('Nenhum pedido de demonstração recebido.')).toBeInTheDocument();
    expect(excluidos).toEqual(['7']);
    confirmar.mockRestore();
  });

  it('sem confirmar, o pedido não é excluído', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValue(false);
    renderizar('/dashboard', tokenValido());
    const secao = await screen.findByRole('region', { name: 'Pedidos de demonstração' });

    await userEvent.click(await within(secao).findByRole('button', { name: 'Excluir o pedido de Ana Souza' }));

    expect(within(secao).getByText('Ana Souza')).toBeInTheDocument();
    confirmar.mockRestore();
  });

  it('a lista de pedidos pagina', async () => {
    const paginas: string[] = [];
    servidor.use(http.get(`${API}/api/dashboard/demonstracoes`, ({ request }) => {
      const pagina = new URL(request.url).searchParams.get('pagina')!;
      paginas.push(pagina);
      return HttpResponse.json({ itens: [{ ...demonstracao, nome: `Pessoa ${pagina}` }], total: 15, numeroDaPagina: Number(pagina), tamanho: 10 });
    }));
    renderizar('/dashboard', tokenValido());
    const secao = await screen.findByRole('region', { name: 'Pedidos de demonstração' });
    await within(secao).findByText('Pessoa 1');

    await userEvent.click(within(secao).getByRole('button', { name: 'Próxima' }));

    expect(await within(secao).findByText('Pessoa 2')).toBeInTheDocument();
    expect(within(secao).getByText(/Página 2 de 2 · 15 pedido/)).toBeInTheDocument();
    expect(paginas).toEqual(['1', '2']);
  });

  it('mostra a quantidade de máquinas ativas e registradas (US #381)', async () => {
    renderizar('/dashboard', tokenValido());

    await screen.findByRole('heading', { name: 'Quantidade de uso' });
    expect(kpi('Máquinas ativas')).toHaveTextContent('1');
    expect(kpi('Máquinas registradas')).toHaveTextContent('2');
  });

  it('sem exceção no período, diz que não há nenhuma', async () => {
    servidor.use(http.get(`${API}/api/dashboard/erros`, () => HttpResponse.json({ itens: [], total: 0, numeroDaPagina: 1, tamanho: 10 })));

    renderizar('/dashboard', tokenValido());

    const secao = await screen.findByRole('region', { name: 'Exceções não classificadas' });
    expect(await within(secao).findByText('Nenhuma exceção não classificada no período.')).toBeInTheDocument();
  });

  it('lista os grupos e, ao abrir um, mostra o trace sem caminhos e as ocorrências', async () => {
    const detalhe = await abrirOGrupo();

    const trace = detalhe.getByLabelText('Trace da exceção');
    expect(trace).toHaveTextContent('GitKit.Core.Services.Planejador.Escolher() linha 42');
    expect(trace.textContent).not.toMatch(/[A-Za-z]:\\|\/home\//);
    expect(detalhe.getByRole('figure', { name: 'Ocorrências por dia' })).toBeInTheDocument();
    expect(detalhe.getByText('máquina a1b2c3d4')).toBeInTheDocument();
    const linha = screen.getByRole('button', { name: 'System.InvalidOperationException' }).closest('tr')!;
    expect(within(linha).getByText('1.4.0 → 1.4.2')).toBeInTheDocument();
    expect(within(linha).getByText('Novo')).toBeInTheDocument();
  });

  it('resolver envia o PUT com a versão da correção', async () => {
    const detalhe = await abrirOGrupo();

    expect(detalhe.getByRole('button', { name: 'Resolver na versão' })).toBeDisabled();
    await userEvent.type(detalhe.getByLabelText('Versão da correção'), '1.5.0');
    await userEvent.click(detalhe.getByRole('button', { name: 'Resolver na versão' }));
    await userEvent.click(detalhe.getByRole('button', { name: 'Marcar visto' }));
    await userEvent.click(detalhe.getByRole('button', { name: 'Ignorar' }));

    await waitFor(() => expect(reacoes).toEqual([
      { id: '5', estado: 'Resolvido', versao: '1.5.0' },
      { id: '5', estado: 'Visto', versao: null },
      { id: '5', estado: 'Ignorado', versao: null },
    ]));
    expect(await detalhe.findByText('Marcado como ignorado.')).toBeInTheDocument();
  });

  it('exportar baixa o JSON do grupo e copiar leva o texto do Bug', async () => {
    const detalhe = await abrirOGrupo();

    await userEvent.click(detalhe.getByRole('button', { name: 'Exportar JSON' }));
    await waitFor(() => expect(entregues.arquivos).toHaveLength(1));
    expect(entregues.arquivos[0].nome).toBe('excecao-abc123.json');
    expect(JSON.parse(await lerTexto(entregues.arquivos[0].conteudo))).toMatchObject({ grupo: { assinatura: 'abc123' } });

    await userEvent.click(detalhe.getByRole('button', { name: 'Copiar para Bug' }));
    await waitFor(() => expect(entregues.copiado).toBe(textoDoBug(erroDetalhe)));
    expect(entregues.copiado).toContain('Assinatura: abc123');
    expect(entregues.copiado).toContain('TurnFailures.Padrao');
  });

  it('o gestor vê o trace e exporta, mas não reage', async () => {
    const detalhe = await abrirOGrupo({ ...tokenValido(), papel: 'gestor', ehAdmin: false, empresa: 'Empresa A' });

    expect(detalhe.getByLabelText('Trace da exceção')).toBeInTheDocument();
    expect(detalhe.getByRole('button', { name: 'Exportar JSON' })).toBeInTheDocument();
    expect(detalhe.queryByRole('button', { name: 'Marcar visto' })).not.toBeInTheDocument();
    expect(detalhe.queryByRole('button', { name: 'Ignorar' })).not.toBeInTheDocument();
  });

  it('o gestor filtra por colaborador, exporta o CSV e não vê os pedidos de demonstração nem Empresas', async () => {
    renderizar('/dashboard', tokenDoGestor());

    await screen.findByRole('heading', { name: 'Quantidade de uso' });
    expect(screen.getByText('gestor.a · Empresa A')).toBeInTheDocument();
    const filtro = screen.getByLabelText('Colaborador');
    expect(await within(filtro).findByRole('option', { name: 'Ana Souza' })).toBeInTheDocument();
    expect(within(filtro).getByRole('option', { name: 'máquina k1l2m3n4' })).toBeInTheDocument(); // sem nome informado: o apelido
    expect(screen.queryByRole('region', { name: 'Pedidos de demonstração' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Empresas' })).not.toBeInTheDocument();

    await userEvent.selectOptions(filtro, '1');
    await userEvent.click(screen.getByRole('button', { name: 'Exportar uso (CSV)' }));

    await waitFor(() => expect(entregues.arquivos).toHaveLength(1));
    expect(entregues.arquivos[0].nome).toBe('uso-dos-colaboradores-20260904-20261003.csv');
    expect(await lerTexto(entregues.arquivos[0].conteudo)).toContain('Ana Souza');
    const exportacao = requisicoes.find((r) => r.pathname === '/api/dashboard/exportar')!;
    expect(exportacao.searchParams.get('maquina')).toBe('1');
    expect(exportacao.searchParams.get('formato')).toBe('csv');
    expect(screen.getByText(/registrado na trilha de auditoria/)).toBeInTheDocument();
  });

  it('o admin vê o link de Empresas e o filtro por máquina', async () => {
    renderizar('/dashboard', tokenValido());

    expect(await screen.findByRole('link', { name: 'Empresas' })).toHaveAttribute('href', '/empresas');
    expect(screen.getByLabelText('Máquina')).toBeInTheDocument();
    expect(await screen.findByRole('region', { name: 'Pedidos de demonstração' })).toBeInTheDocument();
  });

  it('o gestor sem colaboradores recebe a instrução de adesão', async () => {
    servidor.use(http.get(`${API}/api/dashboard/colaboradores`, () => HttpResponse.json([])));

    renderizar('/dashboard', tokenDoGestor());

    expect(await screen.findByText(/Nenhum colaborador aderiu ainda/)).toBeInTheDocument();
  });

  it('mostra o ROI por work item: totais, gráfico e tabela (US #387)', async () => {
    renderizar('/dashboard', tokenValido());

    const secao = await screen.findByRole('region', { name: 'ROI por work item' });
    const tabela = await within(secao).findByRole('table', { name: 'ROI por work item' });
    const us = within(tabela).getByText('#387').closest('tr')!;
    expect(within(us).getByText('User Story')).toBeInTheDocument();
    expect(within(us).getByText('Closed')).toBeInTheDocument();
    expect(within(us).getByText('Ana Souza')).toBeInTheDocument();
    expect(within(us).getByText('42')).toBeInTheDocument();
    expect(within(us).getByText('31,5')).toBeInTheDocument();
    expect(within(us).getByText('0,8')).toBeInTheDocument(); // 0,75 h por turno, com uma casa
    expect(within(us).getByText('3,5 dias')).toBeInTheDocument();
    expect(within(us).getByText('1/2')).toBeInTheDocument();

    // O Bug aberto: sem turno, horas/turno é traço (nunca NaN); sem colaborador, o apelido.
    const bug = within(tabela).getByText('#401').closest('tr')!;
    expect(within(bug).getByText('em aberto')).toBeInTheDocument();
    expect(within(bug).getByText('máquina f6g7h8i9')).toBeInTheDocument();
    expect(within(bug).getByText('—')).toBeInTheDocument();

    expect(within(secao).getByTestId('kpi-Work items')).toHaveTextContent('2');
    expect(within(secao).getByTestId('kpi-Turnos do agente')).toHaveTextContent('42');
    expect(within(secao).getByTestId('kpi-Lead time médio')).toHaveTextContent('3,5 dias');
    expect(within(secao).getByTestId('kpi-PRs mergeadas')).toHaveTextContent('1 / 2');

    const grafico = within(secao).getByRole('figure', { name: /Horas lançadas × turnos do agente/ });
    expect(within(grafico).getAllByRole('listitem')).toHaveLength(2);
    await userEvent.hover(within(grafico).getByRole('listitem', { name: /#387 User Story/ }));
    expect(within(grafico).getByRole('tooltip')).toHaveTextContent('lead time 3,5 dias');
    expect(document.body).not.toHaveTextContent('NaN');
  });

  it('sem ROI calculado, ensina a rodar o devcli roi', async () => {
    servidor.use(http.get(`${API}/api/dashboard/roi`, () => HttpResponse.json({ ...roi, itens: [], totais: { itens: 0, turnos: 0, horas: 0, leadTimeMedioDias: null, pullRequests: 0, pullRequestsMergeadas: 0 } })));

    renderizar('/dashboard', tokenValido());

    const secao = await screen.findByRole('region', { name: 'ROI por work item' });
    expect(await within(secao).findByText(/Nenhum ROI calculado/)).toBeInTheDocument();
    expect(within(secao).getByText('devcli roi --id N')).toBeInTheDocument();
    expect(within(secao).queryByRole('table')).not.toBeInTheDocument();
  });

  it('o ROI segue os filtros de máquina e de período', async () => {
    renderizar('/dashboard', tokenValido());
    await screen.findByRole('region', { name: 'ROI por work item' });
    await screen.findByRole('option', { name: /máquina a1b2c3d4/ });

    await userEvent.selectOptions(screen.getByLabelText('Máquina'), '2');

    await waitFor(() => {
      const doRoi = requisicoes.filter((r) => r.pathname === '/api/dashboard/roi').at(-1)!;
      expect(doRoi.searchParams.get('maquina')).toBe('2');
      expect(doRoi.searchParams.get('de')).toBeTruthy();
    });
  });

  it('sem máquinas, explica como ligar o envio', async () => {
    servidor.use(http.get(`${API}/api/dashboard/maquinas`, () => HttpResponse.json([])));

    renderizar('/dashboard', tokenValido());

    expect(await screen.findByText(/Nenhuma máquina enviou telemetria ainda/)).toBeInTheDocument();
  });
});
