import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { textoDoBug } from '../componentes/ExcecoesNaoClassificadas';
import { entregues } from '../testes/navegador';
import { renderizar } from '../testes/renderizar';
import { API, demonstracao, erroDetalhe, qualidadeVazia, reacoes, requisicoes, roi, servidor, tokenDoDev, tokenDoGestor, tokenValido, utilidade } from '../testes/servidor';

const kpi = (rotulo: string) => screen.getByTestId(`kpi-${rotulo}`);

/**
 * O texto de um arquivo baixado. O Blob pode vir de dois lugares conforme o Node: o do `fetch`
 * (undici, que tem `text()` mas o FileReader do jsdom recusa) ou o do jsdom (sem `text()`, lido pelo
 * FileReader) — no CI (Linux) é o primeiro, no Windows já foi o segundo.
 */
const lerTexto = (blob: Blob): Promise<string> => {
  if (typeof blob.text === 'function') return blob.text();
  return new Promise<string>((pronto) => {
    const leitor = new FileReader();
    leitor.onload = () => pronto(String(leitor.result));
    leitor.readAsText(blob);
  });
};

/** Abre o dashboard e o detalhe do grupo de exceção da fixture. */
async function abrirOGrupo(sessao = tokenValido()) {
  renderizar('/dashboard', sessao);
  const secao = await screen.findByRole('region', { name: 'Exceções não classificadas' });
  await userEvent.click(await within(secao).findByRole('button', { name: 'System.InvalidOperationException' }));
  return within(await screen.findByRole('article', { name: 'Detalhe da exceção' }));
}

describe('Dashboard', () => {
  // US #687 (proposta admin-dashboard v008): a saúde de agora e o resultado do período, com os valores da API.
  it('mostra a saúde de agora e o resultado do período, e a qualidade abaixo', async () => {
    renderizar('/dashboard', tokenValido());

    await screen.findByRole('heading', { name: 'Saúde agora' });
    expect(screen.getByTestId('saude-Tasks paradas sem processo')).toHaveTextContent('6+4 em 24 h');
    expect(screen.getByRole('group', { name: 'Tasks paradas sem processo' })).toHaveTextContent('a mais antiga desde 08/10/2026');
    expect(screen.getByTestId('saude-Perguntas esperando o dev')).toHaveTextContent('10');
    expect(screen.getByRole('group', { name: 'Fechamentos que falharam' })).toHaveTextContent('#669 e #585');
    expect(screen.getByRole('group', { name: 'Máquinas com recurso no limite' })).toHaveTextContent('disco 8 MB · memória 124 MB livres');
    expect(kpi('Entregas aprovadas · integradas')).toHaveTextContent('14 · 1111 na semana');
    expect(kpi('Aprovadas na 1ª avaliação')).toHaveTextContent('57%+9 p.p.');
    expect(kpi('Tempo até a aprovação (mediana)')).toHaveTextContent('6h40+1h50');
    expect(kpi('Turnos por entrega')).toHaveTextContent('17,7');
    expect(screen.getByRole('group', { name: 'Turnos por entrega' })).toHaveTextContent('248 turnos · 63 comandos delegados · 296 arquivos');
    expect(kpi('Taxa de falha de turno')).toHaveTextContent(/25,0\s?%/);
    expect(kpi('Duração média do turno')).toHaveTextContent('42,5 s');
    expect(kpi('Nota média dos avaliadores')).toHaveTextContent('91,5');
    expect(kpi('Objetivos cumpridos / recusados')).toHaveTextContent('1 / 1');
    expect(screen.getByText('limite-de-uso')).toBeInTheDocument();
  });

  // A faixa Adoção (admin-dashboard v003 da semana6): os usuários ativos ao lado das máquinas ativas.
  it('mostra os usuários ativos ao lado das máquinas ativas', async () => {
    renderizar('/dashboard', tokenValido());

    await screen.findByRole('heading', { name: 'Adoção' });
    expect(screen.getByTestId('adocao-Usuários ativos')).toHaveTextContent('3');
    expect(screen.getByRole('group', { name: 'Usuários ativos' })).toHaveTextContent('de 4 cadastrados · 1 máquina sem login');
    expect(screen.getByTestId('adocao-Máquinas ativas')).toHaveTextContent('4');
    expect(screen.getByRole('group', { name: 'Máquinas ativas' })).toHaveTextContent('de 5 registradas');
  });

  it('sem a adoção na resposta (API anterior), a faixa não aparece', async () => {
    servidor.use(http.get(`${API}/api/dashboard/utilidade`, () => HttpResponse.json({ ...utilidade, adocao: undefined })));

    renderizar('/dashboard', tokenValido());

    await screen.findByRole('heading', { name: 'Saúde agora' });
    expect(screen.queryByRole('heading', { name: 'Adoção' })).not.toBeInTheDocument();
  });

  // Bug de 26/10: o 500 do painel aparecia só como "An error occurred while processing your request".
  it('o 500 mostra a mensagem útil da API, e sem ela a rota que falhou com o código do log', async () => {
    servidor.use(http.get(`${API}/api/dashboard/utilidade`, () => HttpResponse.json(
      { title: 'A API falhou ao processar o pedido.', status: 500, detail: 'GET /api/dashboard/utilidade falhou: O banco não tem a tabela FotosDeEstado (Us687.sql).' },
      { status: 500 },
    )));
    const { unmount } = renderizar('/dashboard', tokenValido());

    expect(await screen.findByText(/O banco não tem a tabela FotosDeEstado \(Us687\.sql\)/)).toBeInTheDocument();
    unmount();

    servidor.use(http.get(`${API}/api/dashboard/utilidade`, () => HttpResponse.json(
      { title: 'An error occurred while processing your request.', status: 500, traceId: '00-abc-01' },
      { status: 500 },
    )));
    renderizar('/dashboard', tokenValido());

    expect(await screen.findByText('A API falhou ao processar /api/dashboard/utilidade (500). Código para o log da API: 00-abc-01.')).toBeInTheDocument();
    expect(screen.queryByText(/An error occurred/)).not.toBeInTheDocument();
  });

  it('mostra os eventos recentes', async () => {
    renderizar('/dashboard', tokenValido());

    // A célula, e não o texto: o filtro Tipo do log também oferece "TurnoExecutado".
    const tabela = (await screen.findByRole('cell', { name: 'TurnoExecutado' })).closest('table')!;
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
    await screen.findByRole('heading', { name: 'Saúde agora' });
    // Só nas opções da Máquina: na página inteira, cada tentativa calcula o nome de todas as opções (harness, tipo do log).
    await within(screen.getByLabelText('Máquina')).findByRole('option', { name: /máquina a1b2c3d4/ });

    await userEvent.selectOptions(screen.getByLabelText('Máquina'), '2');
    await waitFor(() => expect(requisicoes.at(-1)?.searchParams.get('maquina')).toBe('2'));

    await userEvent.selectOptions(screen.getByLabelText('Período'), '7');
    await waitFor(() => {
      const ultima = requisicoes.at(-1)!;
      const dias = (Date.parse(ultima.searchParams.get('ate')!) - Date.parse(ultima.searchParams.get('de')!)) / 86_400_000 + 1;
      expect(dias).toBe(7);
    });
  });

  /** A última requisição que chegou a uma rota do servidor de mentira. */
  const ultima = (caminho: string) => requisicoes.filter((r) => r.pathname === caminho).at(-1)!;

  it('o harness filtra a adoção, o log e as exceções, e não os totais', async () => {
    renderizar('/dashboard', tokenValido());
    await screen.findByRole('heading', { name: 'Saúde agora' });
    expect(ultima('/api/dashboard/utilidade').searchParams.has('harness')).toBe(false);

    await userEvent.selectOptions(screen.getByLabelText('Harness'), 'kiro');
    await waitFor(() => {
      expect(ultima('/api/dashboard/utilidade').searchParams.get('harness')).toBe('kiro');
      expect(ultima('/api/dashboard/eventos').searchParams.get('harness')).toBe('kiro');
      expect(ultima('/api/dashboard/erros').searchParams.get('harness')).toBe('kiro');
    });
    expect(ultima('/api/dashboard/qualidade').searchParams.has('harness')).toBe(false); // os totais não têm sessão
    expect(within(screen.getByLabelText('Harness')).getByRole('option', { name: 'Codex' })).toBeInTheDocument();
  });

  it('o recorte do log leva o tipo e o detalhe', async () => {
    renderizar('/dashboard', tokenValido());
    await screen.findByRole('heading', { name: 'Saúde agora' });

    const log = within(screen.getByRole('form', { name: 'Filtrar o log' }));
    await userEvent.selectOptions(log.getByLabelText('Tipo'), 'TurnoFalhou');
    // Colado, e não digitado: cada tecla re-renderiza o Dashboard inteiro, e o teste passava do teto de 5 s.
    await userEvent.click(log.getByLabelText('Detalhe'));
    await userEvent.paste('nao-classificada');
    await userEvent.click(log.getByRole('button', { name: 'Filtrar' }));

    await waitFor(() => {
      const eventos = ultima('/api/dashboard/eventos').searchParams;
      expect([eventos.get('tipo'), eventos.get('detalhe')]).toEqual(['TurnoFalhou', 'nao-classificada']);
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

  it('os turnos por dia empilham pelo desfecho, com a legenda e a tabela, e onde as tasks travam traz o selo', async () => {
    renderizar('/dashboard', tokenValido());

    const grafico = await screen.findByRole('figure', { name: 'Turnos por dia, pelo desfecho' });
    expect(within(grafico).getByRole('list', { name: 'Legenda' })).toHaveTextContent('trabalhouesperou o devlimite de usofalhou');
    expect(within(grafico).getByRole('row', { name: '24/10 9 2 1 1' })).toBeInTheDocument();
    const travam = screen.getByRole('figure', { name: 'Onde as tasks travam' });
    expect(within(travam).getByRole('row', { name: /Esperou processo em segundo plano que morreu 9 sugestão #3/ })).toBeInTheDocument();
    expect(within(travam).getByText('corrigido #568')).toHaveClass('insignia-sucesso');
  });

  it('a ação de um cartão da saúde recorta o log pelo caso', async () => {
    renderizar('/dashboard', tokenValido());
    const cartao = within(await screen.findByRole('group', { name: 'Tasks paradas sem processo' }));

    await userEvent.click(cartao.getByRole('button', { name: 'Ver ›' }));

    await waitFor(() => {
      const eventos = ultima('/api/dashboard/eventos').searchParams;
      expect([eventos.get('tipo'), eventos.get('detalhe')]).toEqual(['SituacaoDaTask', 'parada-sem-processo']);
    });
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

    await screen.findByRole('heading', { name: 'Saúde agora' });
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
    await within(screen.getByLabelText('Máquina')).findByRole('option', { name: /máquina a1b2c3d4/ });

    await userEvent.selectOptions(screen.getByLabelText('Máquina'), '2');

    await waitFor(() => {
      const doRoi = requisicoes.filter((r) => r.pathname === '/api/dashboard/roi').at(-1)!;
      expect(doRoi.searchParams.get('maquina')).toBe('2');
      expect(doRoi.searchParams.get('de')).toBeTruthy();
    });
  });

  it('mostra os cartões Impasses e Árbitro na qualidade de uso (US #405)', async () => {
    renderizar('/dashboard', tokenValido());

    const impasses = within(await screen.findByRole('article', { name: 'Impasses' }));
    expect(impasses.getByTestId('kpi-Impasses detectados')).toHaveTextContent('3');
    expect(impasses.getByText('2 destravado(s)')).toBeInTheDocument();
    expect(impasses.getByTestId('kpi-Tempo médio parado')).toHaveTextContent('32,5 min');
    expect(impasses.getByTestId('kpi-Tempo médio até destravar')).toHaveTextContent('12,0 min');
    expect(impasses.getByText('o árbitro reagiu')).toBeInTheDocument();
    expect(impasses.getByText('o dev falou')).toBeInTheDocument();

    const arbitro = within(screen.getByRole('article', { name: 'Árbitro' }));
    expect(arbitro.getByTestId('kpi-Cobranças do árbitro')).toHaveTextContent('4');
    expect(arbitro.getByText('3 corrigida(s)')).toBeInTheDocument();
    expect(arbitro.getByTestId('kpi-Taxa de correção')).toHaveTextContent(/75,0\s?%/);
    expect(arbitro.getByTestId('kpi-Escaladas ao dev')).toHaveTextContent('1');
    expect(arbitro.getByText('Arquivos alterados no turno')).toBeInTheDocument();
    expect(arbitro.getByText('sem seção')).toBeInTheDocument(); // a cobrança sem a seção da regra
  });

  it('os cartões Impasses e Árbitro vazios mostram traço e o aviso, nunca NaN', async () => {
    servidor.use(http.get(`${API}/api/dashboard/qualidade`, () => HttpResponse.json(qualidadeVazia)));

    renderizar('/dashboard', tokenValido());

    const impasses = within(await screen.findByRole('article', { name: 'Impasses' }));
    expect(impasses.getByTestId('kpi-Impasses detectados')).toHaveTextContent('0');
    expect(impasses.getByTestId('kpi-Tempo médio parado')).toHaveTextContent('—');
    expect(impasses.getByTestId('kpi-Tempo médio até destravar')).toHaveTextContent('—');
    expect(impasses.getByText('Nenhum impasse destravado no período.')).toBeInTheDocument();
    const arbitro = within(screen.getByRole('article', { name: 'Árbitro' }));
    expect(arbitro.getByTestId('kpi-Taxa de correção')).toHaveTextContent('—');
    expect(arbitro.getByText('Nenhuma cobrança no período.')).toBeInTheDocument();
    expect(document.body).not.toHaveTextContent('NaN');
  });

  it('uma API anterior aos cartões (sem os campos) mostra os cartões vazios', async () => {
    const antiga = { ...qualidadeVazia, impasses: undefined, arbitro: undefined }; // o JSON sai sem os dois campos
    servidor.use(http.get(`${API}/api/dashboard/qualidade`, () => HttpResponse.json(antiga)));

    renderizar('/dashboard', tokenValido());

    const arbitro = within(await screen.findByRole('article', { name: 'Árbitro' }));
    expect(arbitro.getByTestId('kpi-Cobranças do árbitro')).toHaveTextContent('0');
    expect(arbitro.getByTestId('kpi-Taxa de correção')).toHaveTextContent('—');
  });

  it('o dev não entra no painel: o dashboard leva à conta dele (US #405, US #425)', async () => {
    // Só as chamadas COM O TOKEN DO DEV: a lista global também recebe a chamada que o teste anterior (o do admin) ainda
    // tinha em voo quando a limpeza rodou — e o teste falhava às vezes no CI por isso, sem o dev ter chamado nada.
    const doDev: string[] = [];
    const ouvir = ({ request }: { request: Request }) => {
      if (request.headers.get('Authorization') === `Bearer ${tokenDoDev().token}`) doDev.push(new URL(request.url).pathname);
    };
    servidor.events.on('request:start', ouvir);
    try {
      renderizar('/dashboard', tokenDoDev());

      expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('Sua conta');
      expect(doDev.filter((caminho) => caminho.startsWith('/api/dashboard'))).toEqual([]);
    } finally {
      servidor.events.removeListener('request:start', ouvir);
    }
  });

  it('sem máquinas, explica como ligar o envio', async () => {
    servidor.use(http.get(`${API}/api/dashboard/maquinas`, () => HttpResponse.json([])));

    renderizar('/dashboard', tokenValido());

    expect(await screen.findByText(/Nenhuma máquina enviou telemetria ainda/)).toBeInTheDocument();
  });
});
