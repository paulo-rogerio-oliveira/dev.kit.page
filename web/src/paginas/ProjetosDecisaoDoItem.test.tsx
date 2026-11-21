import { screen, waitFor, within, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { renderizar } from '../testes/renderizar';
import { API, servidor, tokenDoDev } from '../testes/servidor';
import type { Projeto } from '../api/projetos';
import type { ImpactoDaDecisaoDoItem, PedidoDeDecisaoDoItem } from '../api/cicloDosItens';

const inicial: Projeto = {
  id: 7, slug: 'portal', nome: 'Portal isolado', nivel: 'usuario', time: '', repositorios: ['https://exemplo.test/portal'],
  branch: 'feature/dev/projeto-portal', semanaConvertida: false, definicao: 'Plano', anexos: ['referencia/v009/hash'],
  autonomia: true, paralelismo: 2, destinoDaPr: 'develop', situacao: 'aprovado', versao: 4, autor: 'dev',
  atualizadoEm: '2026-11-21T12:00:00Z', podeApagar: true, featureId: 0, epicId: 0, iteracao: '', board: 'nao', papel: 'editor',
  itens: [
    { numero: 1, titulo: 'Integrado anterior', descricao: '', fluxo: '', repositorios: [], componentes: [], dependencias: [],
      onda: 1, workItemId: 0, situacao: 'integrado', tipo: '', prioridade: 0, horas: 0, branch: 'item-1' },
    { numero: 2, titulo: 'Trabalho parcial', descricao: 'Entrega e histórico preservados.', fluxo: '', repositorios: [], componentes: [], dependencias: [1],
      onda: 2, workItemId: 0, situacao: 'rodando', tipo: '', prioridade: 0, horas: 0, branch: 'item-2' },
    { numero: 3, titulo: 'Continuidade', descricao: '', fluxo: '', repositorios: [], componentes: [], dependencias: [2],
      onda: 3, workItemId: 0, situacao: 'planejado', tipo: '', prioridade: 0, horas: 0, branch: 'item-3' },
  ],
};
let projeto: Projeto;
let pedidos: PedidoDeDecisaoDoItem[];

function resultado(pedido: PedidoDeDecisaoDoItem): ImpactoDaDecisaoDoItem {
  const proposta: Projeto = { ...projeto, versao: projeto.versao + (pedido.aplicar ? 1 : 0), itens: projeto.itens.map(i => i.numero !== 2 ? i : {
    ...i, situacao: pedido.acao === 'cancelar-item' ? 'cancelado' : 'concluido-manual', decisaoHumana: {
      pedido: pedido.pedido, acao: pedido.acao, autor: 'Identidade autenticada', confirmacao: pedido.confirmacao ?? '',
      motivo: pedido.motivo?.trim() ?? '', em: '2026-11-21T12:01:00Z', versaoEsperada: String(pedido.versaoEsperada),
      estadoAnterior: i.situacao, snapshot: 'snapshot portátil', paradaPendente: true, geracao: 1,
    },
  }) };
  if (pedido.aplicar) projeto = proposta;
  return { simulacao: !pedido.aplicar, projeto: proposta, item: 2, dependentes: [3],
    grafoEfetivo: proposta.itens.filter(i => i.numero !== 2), consequencia: 'Reservas ativas dependem de parada confirmada pelo host.' };
}

beforeEach(() => {
  projeto = structuredClone(inicial); pedidos = [];
  servidor.use(
    http.get(`${API}/api/projetos`, () => HttpResponse.json([projeto])),
    http.get(`${API}/api/projetos/7`, () => HttpResponse.json(projeto)),
    http.get(`${API}/api/projetos/recursos`, () => HttpResponse.json({ recursos: ['ciclo-humano-item-v1'] })),
    http.get(`${API}/api/projetos/7/acessos`, () => HttpResponse.json({ recurso: 'projeto', projeto: 7, nome: projeto.nome,
      papel: projeto.papel, dono: { id: 1, login: 'dev', nome: 'Dev' }, acessos: [], candidatos: [] })),
    http.post(`${API}/api/projetos/7/itens/2/decisao`, async ({ request }) => {
      const pedido = await request.json() as PedidoDeDecisaoDoItem;
      pedidos.push(pedido);
      return HttpResponse.json(resultado(pedido));
    }),
  );
});

async function selecionar() {
  renderizar('/projetos', tokenDoDev());
  await userEvent.click(await screen.findByRole('button', { name: '2 · Trabalho parcial' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Concluir manualmente' })).toBeEnabled());
}
async function abrir(acao = 'Cancelar item') {
  await selecionar();
  await userEvent.click(screen.getByRole('button', { name: acao }));
  const modal = within(await screen.findByRole('dialog'));
  await waitFor(() => expect(modal.queryByText('Conferindo a decisão…')).not.toBeInTheDocument());
  return modal;
}

describe('decisão humana do item pelo Board', () => {
  it.each(['Cancelar item', 'Concluir manualmente'])('%s só aplica após motivo e clique humano; preserva integração e recibo', async acao => {
    const modal = await abrir(acao);
    expect(pedidos).toHaveLength(1);
    expect(pedidos[0].aplicar).not.toBe(true);
    expect(projeto.itens[1].situacao).toBe('rodando');
    expect(modal.getByLabelText('Motivo da decisão')).toHaveFocus();
    expect(modal.getByRole('button', { name: acao })).toBeDisabled();
    await userEvent.type(modal.getByLabelText('Motivo da decisão'), 'Manter a entrega parcial');
    await userEvent.click(modal.getByRole('button', { name: acao }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(pedidos).toHaveLength(2);
    expect(pedidos[1]).toMatchObject({ aplicar: true, confirmar: true, motivo: 'Manter a entrega parcial', versaoEsperada: 4,
      pedido: pedidos[0].pedido, confirmacao: `portal/2/${pedidos[0].acao}/remota:4` });
    expect(projeto.itens[0].situacao).toBe('integrado');
    expect(projeto.itens[1].branch).toBe('item-2');
    expect(projeto.anexos).toEqual(['referencia/v009/hash']);
    const detalhe = within(screen.getByRole('region', { name: 'Detalhe do item 2' }));
    expect(detalhe.getByText(/Autor: Identidade autenticada/)).toBeInTheDocument();
    expect(detalhe.getByText(/parada física depende/)).toBeInTheDocument();
    expect(screen.getByText('1 Integrado')).toBeInTheDocument();
  });

  it('Voltar descarta o diálogo sem gravar, mantém seleção e restaura foco', async () => {
    const modal = await abrir();
    await userEvent.type(modal.getByLabelText('Motivo da decisão'), 'Rascunho não aplicado');
    await userEvent.click(modal.getByRole('button', { name: 'Voltar' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancelar item' })).toHaveFocus();
    expect(screen.getByRole('button', { name: '2 · Trabalho parcial' })).toHaveAttribute('aria-pressed', 'true');
    expect(pedidos.every(p => !p.aplicar)).toBe(true);
    expect(projeto.itens[1].situacao).toBe('rodando');
  });

  it.each(['leitor', undefined] as const)('papel %s não envia simulação nem decisão', async papel => {
    projeto.papel = papel;
    renderizar('/projetos', tokenDoDev());
    await userEvent.click(await screen.findByRole('button', { name: '2 · Trabalho parcial' }));
    expect(await screen.findByText('Somente dono ou editor pode decidir em um projeto aberto.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancelar item' })).toBeDisabled();
    expect(pedidos).toHaveLength(0);
  });

  it('API antiga conserva o plano e não oferece execução', async () => {
    servidor.use(http.get(`${API}/api/projetos/recursos`, () => HttpResponse.json({ recursos: [] })));
    renderizar('/projetos', tokenDoDev());
    await userEvent.click(await screen.findByRole('button', { name: '2 · Trabalho parcial' }));
    expect(await screen.findByText(/Esta API ainda não oferece/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancelar item' })).toBeDisabled();
    expect(pedidos).toHaveLength(0);
  });

  it.each([400, 401, 403, 409, 426, 503])('erro%s preserva motivo e exige releitura/nova revisão, sem repetição automática', async status => {
    const modal = await abrir();
    let aplicacoes = 0;
    servidor.use(http.post(`${API}/api/projetos/7/itens/2/decisao`, async ({ request }) => {
      const pedido = await request.json() as PedidoDeDecisaoDoItem;
      if (pedido.aplicar) { aplicacoes++; projeto = { ...projeto, versao: 5 }; return HttpResponse.json({ detail: 'Recusa verificável.' }, { status }); }
      pedidos.push(pedido);
      return HttpResponse.json(resultado(pedido));
    }));
    await userEvent.type(modal.getByLabelText('Motivo da decisão'), 'Texto que deve continuar aqui');
    await userEvent.click(modal.getByRole('button', { name: 'Cancelar item' }));
    expect(await modal.findByRole('alert')).toHaveTextContent('Recusa verificável.');
    expect(modal.getByLabelText('Motivo da decisão')).toHaveValue('Texto que deve continuar aqui');
    expect(aplicacoes).toBe(1);
    expect(modal.queryByRole('button', { name: 'Cancelar item' })).not.toBeInTheDocument();
    await waitFor(() => expect(modal.getByRole('button', { name: 'Reler e revisar' })).toBeEnabled());
    await userEvent.click(modal.getByRole('button', { name: 'Reler e revisar' }));
    await waitFor(() => expect(modal.queryByText('Conferindo a decisão…')).not.toBeInTheDocument());
    expect(pedidos[1].pedido).not.toBe(pedidos[0].pedido);
    expect(pedidos[1].versaoEsperada).toBe(5);
    expect(aplicacoes).toBe(1);
    expect(modal.getByLabelText('Motivo da decisão')).toHaveValue('Texto que deve continuar aqui');
  });

  it('limita motivo a2000 e não aceita branco', async () => {
    const modal = await abrir();
    const campo = modal.getByLabelText('Motivo da decisão');
    expect(campo).toHaveAttribute('maxlength', '2000');
    fireEvent.change(campo, { target: { value: ' '.repeat(3) } });
    expect(modal.getByRole('button', { name: 'Cancelar item' })).toBeDisabled();
    fireEvent.change(campo, { target: { value: 'a'.repeat(2001) } });
    expect(modal.getByRole('button', { name: 'Cancelar item' })).toBeDisabled();
    fireEvent.change(campo, { target: { value: 'a'.repeat(2000) } });
    expect(modal.getByRole('button', { name: 'Cancelar item' })).toBeEnabled();
    expect(pedidos.every(p => !p.aplicar)).toBe(true);
  });

  it('resposta sem recibo não vira êxito; releitura mostra o fato sem aplicar novamente', async () => {
    const modal = await abrir();
    servidor.use(http.post(`${API}/api/projetos/7/itens/2/decisao`, () => HttpResponse.json({ simulacao: false, projeto, item: 2, dependentes: [], grafoEfetivo: [], consequencia: '' })));
    await userEvent.type(modal.getByLabelText('Motivo da decisão'), 'Sem recibo não comprova aplicação');
    await userEvent.click(modal.getByRole('button', { name: 'Cancelar item' }));
    expect(await modal.findByRole('alert')).toHaveTextContent('Sem recibo correspondente');
    expect(projeto.itens[1].situacao).toBe('rodando');
    expect(modal.getByLabelText('Motivo da decisão')).toHaveValue('Sem recibo não comprova aplicação');
  });

  it('trocar projeto preserva o painel e os filtros, mas descarta seleção e confirmação antigas', async () => {
    const outro = { ...inicial, id: 8, slug: 'outro', nome: 'Outro projeto' };
    servidor.use(http.get(`${API}/api/projetos`, () => HttpResponse.json([projeto, outro])),
      http.get(`${API}/api/projetos/8/acessos`, () => HttpResponse.json({ recurso: 'projeto', projeto: 8, nome: outro.nome,
        papel: 'leitor', dono: { id: 1, login: 'dev', nome: 'Dev' }, acessos: [], candidatos: [] })));
    const modal = await abrir();
    await userEvent.click(modal.getByRole('button', { name: 'Voltar' }));
    const painel = screen.getByRole('article', { name: 'Detalhe do projeto' });
    await userEvent.click(screen.getByRole('button', { name: /Outro projeto/ }));
    expect(within(painel).getByRole('heading', { name: 'Outro projeto' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Detalhe do item 2' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Portal isolado/ }));
    expect(screen.queryByRole('region', { name: 'Detalhe do item 2' })).not.toBeInTheDocument();
    expect(pedidos.every(p => !p.aplicar)).toBe(true);
  });

  it('leitura atrasada do item de outro projeto não substitui o projeto escolhido', async () => {
    const outro = { ...inicial, id: 8, slug: 'outro', nome: 'Outro projeto' };
    let liberar!: () => void;
    const liberada = new Promise<void>(resolve => { liberar = resolve; });
    let iniciou = false;
    servidor.use(http.get(`${API}/api/projetos`, () => HttpResponse.json([projeto, outro])),
      http.get(`${API}/api/projetos/7`, async () => { iniciou = true; await liberada; return HttpResponse.json(projeto); }),
      http.get(`${API}/api/projetos/8/acessos`, () => HttpResponse.json({ recurso: 'projeto', projeto: 8, nome: outro.nome,
        papel: 'leitor', dono: { id: 1, login: 'dev', nome: 'Dev' }, acessos: [], candidatos: [] })));
    renderizar('/projetos', tokenDoDev());
    await userEvent.click(await screen.findByRole('button', { name: '2 · Trabalho parcial' }));
    await waitFor(() => expect(iniciou).toBe(true));
    await userEvent.click(screen.getByRole('button', { name: /Outro projeto/ }));
    liberar();
    await waitFor(() => expect(within(screen.getByRole('article', { name: 'Detalhe do projeto' })).getByRole('heading', { name: 'Outro projeto' })).toBeInTheDocument());
    expect(screen.queryByRole('region', { name: 'Detalhe do item 2' })).not.toBeInTheDocument();
    expect(pedidos).toHaveLength(0);
  });
});
