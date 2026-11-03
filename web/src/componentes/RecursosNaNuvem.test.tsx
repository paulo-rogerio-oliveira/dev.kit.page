import { act, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { vi } from 'vitest';
import type { RecursoNaNuvem, SituacaoDaProvisao } from '../api/provisao';
import { renderizar } from '../testes/renderizar';
import { API, escritas, filaPedida, servidor, situacaoSemProvisao, tokenDoDev } from '../testes/servidor';
import { ACOMPANHAMENTO_MS, estadoDoRecurso } from './RecursosNaNuvem';

afterEach(() => {
  vi.restoreAllMocks();
  vi.useRealTimers();
});

const liberada: SituacaoDaProvisao = { ...situacaoSemProvisao, liberada: true, origem: 'usuario' };

/** A API da provisão de mentira: a situação devolvida e o que chegou às escritas. */
function provisao(situacao: () => SituacaoDaProvisao) {
  servidor.use(
    http.get(`${API}/api/provisao/v1`, () => HttpResponse.json(situacao())),
    http.post(`${API}/api/provisao/v1/filas`, async ({ request }) => {
      escritas.push({ metodo: 'POST', caminho: '/api/provisao/v1/filas', corpo: await request.json() });
      return HttpResponse.json(filaPedida, { status: 202 });
    }),
    http.post(`${API}/api/provisao/v1/recursos/:id/repetir`, ({ params }) => {
      escritas.push({ metodo: 'POST', caminho: `/api/provisao/v1/recursos/${params.id}/repetir`, corpo: null });
      return HttpResponse.json(filaPedida, { status: 202 });
    }),
    http.delete(`${API}/api/provisao/v1/recursos/:id`, ({ params }) => {
      escritas.push({ metodo: 'DELETE', caminho: `/api/provisao/v1/recursos/${params.id}`, corpo: null });
      return HttpResponse.json({ ...filaPedida, operacao: 'remover' }, { status: 202 });
    }),
  );
}

async function secao() {
  return within(await screen.findByRole('region', { name: 'Recursos na nuvem' }));
}

describe('Recursos na nuvem na conta (item 4 do controle remoto)', () => {
  it('sem a liberação e sem recurso, a ação não aparece', async () => {
    renderizar('/conta', tokenDoDev());

    expect(await screen.findByRole('heading', { name: 'Sua conta' })).toBeInTheDocument();
    await screen.findByText('Ana Souza');
    expect(screen.queryByRole('region', { name: 'Recursos na nuvem' })).not.toBeInTheDocument();
  });

  it('liberada, pede a fila na região escolhida com a chave do clique e mostra o pedido', async () => {
    let recursos: RecursoNaNuvem[] = [];
    provisao(() => ({ ...liberada, recursos }));
    renderizar('/conta', tokenDoDev());
    const nuvem = await secao();

    await userEvent.selectOptions(nuvem.getByLabelText('Região'), 'eastus2');
    recursos = [filaPedida];
    await userEvent.click(nuvem.getByRole('button', { name: 'Criar a fila' }));

    expect(await nuvem.findByText(/Fila pedida em brazilsouth: o trabalhador da provisão cria a conta dk4mq7vz2kq3ab5xty/)).toBeInTheDocument();
    const pedido = escritas[0];
    expect(pedido).toMatchObject({ metodo: 'POST', caminho: '/api/provisao/v1/filas', corpo: { regiao: 'eastus2' } });
    expect((pedido.corpo as { chaveDoCliente: string }).chaveDoCliente).toMatch(/^site-/);
    expect(await nuvem.findByText('Pedida')).toHaveClass('insignia-info');
    // Com a fila ativa, não se pede outra.
    expect(nuvem.queryByRole('button', { name: 'Criar a fila' })).not.toBeInTheDocument();
  });

  it('relê sozinha enquanto o trabalhador mexe, e para quando fica pronta', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    let leituras = 0;
    provisao(() => {
      leituras += 1;
      return { ...liberada, recursos: [leituras < 2 ? { ...filaPedida, estado: 'em-provisao' } : { ...filaPedida, estado: 'pronto', prontoEm: '2026-11-02T21:05:00Z' }] };
    });
    renderizar('/conta', tokenDoDev());
    const nuvem = await secao();
    expect(await nuvem.findByText('Em provisão')).toBeInTheDocument();

    await act(async () => { await vi.advanceTimersByTimeAsync(ACOMPANHAMENTO_MS); });

    expect(await nuvem.findByText('Pronta')).toHaveClass('insignia-sucesso');
    await act(async () => { await vi.advanceTimersByTimeAsync(ACOMPANHAMENTO_MS * 3); });
    expect(leituras).toBe(2);
  });

  it('a que falhou mostra o motivo e repete', async () => {
    provisao(() => ({ ...liberada, recursos: [{ ...filaPedida, estado: 'falhou', tentativas: 1, ultimoErro: 'O Azure recusou: AuthorizationFailed' }] }));
    renderizar('/conta', tokenDoDev());
    const nuvem = await secao();

    expect(await nuvem.findByText('Motivo: O Azure recusou: AuthorizationFailed')).toBeInTheDocument();
    await userEvent.click(nuvem.getByRole('button', { name: 'Repetir' }));

    expect(await nuvem.findByText('Pedido repetido: o trabalhador tenta de novo.')).toBeInTheDocument();
    expect(escritas).toEqual([{ metodo: 'POST', caminho: '/api/provisao/v1/recursos/7/repetir', corpo: null }]);
  });

  it('remover pede confirmação, e sem ela nada vai à API', async () => {
    const confirmar = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
    provisao(() => ({ ...liberada, recursos: [{ ...filaPedida, estado: 'pronto', prontoEm: '2026-11-02T21:05:00Z' }] }));
    renderizar('/conta', tokenDoDev());
    const nuvem = await secao();

    await userEvent.click(await nuvem.findByRole('button', { name: 'Remover' }));
    expect(escritas).toEqual([]);
    await userEvent.click(nuvem.getByRole('button', { name: 'Remover' }));

    expect(await nuvem.findByText(/Remoção pedida: o trabalhador apaga a conta/)).toBeInTheDocument();
    expect(escritas).toEqual([{ metodo: 'DELETE', caminho: '/api/provisao/v1/recursos/7', corpo: null }]);
    expect(confirmar).toHaveBeenCalledWith(expect.stringContaining('não há como desfazer'));
  });

  it('o histórico mostra a auditoria: quem moveu e por quê', async () => {
    provisao(() => ({ ...liberada, recursos: [{ ...filaPedida, estado: 'pronto', prontoEm: '2026-11-02T21:05:00Z' }] }));
    servidor.use(http.get(`${API}/api/provisao/v1/recursos/7`, () => HttpResponse.json({
      ...filaPedida, estado: 'pronto', historico: [
        { em: '2026-11-02T21:04:00Z', operacao: 'criar', estado: 'pedido', autor: 'usuario', mensagem: 'Fila pedida em brazilsouth.' },
        { em: '2026-11-02T21:05:00Z', operacao: 'criar', estado: 'pronto', autor: 'trabalhador:ca-provisao-1', mensagem: 'Fila pronta no Azure.' },
      ],
    })));
    renderizar('/conta', tokenDoDev());
    const nuvem = await secao();

    await userEvent.click(await nuvem.findByRole('button', { name: 'Histórico da fila dk4mq7vz2kq3ab5xty' }));

    const auditoria = await nuvem.findByRole('list', { name: 'Auditoria da fila dk4mq7vz2kq3ab5xty' });
    const linhas = within(auditoria).getAllByRole('listitem').map((l) => l.textContent);
    expect(linhas[0]).toContain('você · Fila pedida em brazilsouth.');
    expect(linhas[1]).toContain('trabalhador:ca-provisao-1 · Fila pronta no Azure.');
  });

  it('revogada, quem ainda tem a fila a vê e a remove, mas não pede outra', async () => {
    provisao(() => ({ ...situacaoSemProvisao, recursos: [{ ...filaPedida, estado: 'pronto', prontoEm: '2026-11-02T21:05:00Z' }] }));
    renderizar('/conta', tokenDoDev());
    const nuvem = await secao();

    expect(await nuvem.findByText(/não está liberada para o seu usuário: dá para acompanhar e remover/)).toBeInTheDocument();
    expect(nuvem.getByRole('button', { name: 'Remover' })).toBeInTheDocument();
    expect(nuvem.queryByRole('form', { name: 'Pedir a fila' })).not.toBeInTheDocument();
  });

  it('a recusa da API aparece na seção', async () => {
    provisao(() => liberada);
    servidor.use(http.post(`${API}/api/provisao/v1/filas`, () =>
      HttpResponse.json({ detail: 'A provisão não está liberada para o seu usuário: peça a liberação ao administrador do dev.kit.' }, { status: 403 })));
    renderizar('/conta', tokenDoDev());
    const nuvem = await secao();

    await userEvent.click(await nuvem.findByRole('button', { name: 'Criar a fila' }));

    expect(await nuvem.findByRole('alert')).toHaveTextContent('peça a liberação ao administrador');
  });

  it('cada estado tem o seu nome, e a remoção os dela', () => {
    expect(estadoDoRecurso({ ...filaPedida, estado: 'pedido' }).texto).toBe('Pedida');
    expect(estadoDoRecurso({ ...filaPedida, operacao: 'remover', estado: 'em-provisao' }).texto).toBe('Removendo');
    expect(estadoDoRecurso({ ...filaPedida, operacao: 'remover', estado: 'falhou' }).texto).toBe('A remoção falhou');
    expect(estadoDoRecurso({ ...filaPedida, estado: 'removido' })).toEqual({ texto: 'Removida', variante: 'neutro' });
    expect(estadoDoRecurso({ ...filaPedida, estado: 'falhou' }).variante).toBe('marca');
  });
});
