import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { vi } from 'vitest';
import { api, ErroDaApi } from '../../api/cliente';
import { EditorMatrix } from './EditorMatrix';
import { documentoDeTeste, revisaoDeTeste } from './fixtures';
import type { ResultadoMatrix } from '../../api/matrix';
import { chaveRascunho } from './modelo';

beforeEach(() => {
  sessionStorage.clear();
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
});
afterEach(() => vi.restoreAllMocks());

function montar(editar = true, recorte?: string[]) {
  const d = documentoDeTeste();
  return render(<MemoryRouter><EditorMatrix alvo={{ documento: d, historico: [revisaoDeTeste(d)], mapa: [], registros: [], recorte }}
    token="token-ficticio" conta="demonstracao" editar={editar} /></MemoryRouter>);
}
const abrir = async (layer = 'Page') => {
  // Grade e lista móvel têm o mesmo controle; apenas uma delas é mostrada pelo CSS no navegador.
  await userEvent.click(screen.getAllByRole('button', { name: `Editor × ${layer} · imp-${layer.toLowerCase()}` })[0]);
  return within(screen.getByRole('dialog'));
};

describe('editor web da Matrix', () => {
  it('simula e apresenta tasks afetadas antes de aplicar uma revisão', async () => {
    const gravar = vi.spyOn(api, 'editarMatrix').mockImplementation(async (_token, edicao, aplicar) => ({
      aplicado: aplicar, revisao: revisaoDeTeste(edicao.documento, 2), mudancas: { celulas: ['imp-page'], responsaveis: ['item-4'] },
    } as ResultadoMatrix));
    montar(); const dialogo = await abrir();
    await userEvent.clear(dialogo.getByLabelText('Como validar'));
    await userEvent.type(dialogo.getByLabelText('Como validar'), 'Validar persistência real em 390 px');
    await userEvent.click(dialogo.getByRole('button', { name: 'Revisar e salvar →' }));
    expect(await dialogo.findByText(/Tasks afetadas: item-4/)).toBeInTheDocument();
    expect(gravar).toHaveBeenCalledTimes(1); expect(gravar.mock.calls[0][2]).toBe(false);
    await userEvent.click(dialogo.getByRole('button', { name: 'Salvar r2 e distribuir' }));
    expect(await screen.findByText(/Revisão r2 salva/)).toBeInTheDocument();
    expect(gravar.mock.calls[1][2]).toBe(true);
    expect(gravar.mock.calls[1][1].documento.matriz!.celulas[1]).toEqual(documentoDeTeste().matriz!.celulas[1]);
  });

  it('leitor e dependência fora do recorte não podem alterar a definição', async () => {
    const gravar = vi.spyOn(api, 'editarMatrix');
    const r = montar(false); let dialogo = await abrir();
    expect(dialogo.getByLabelText('Como validar')).toBeDisabled();
    expect(dialogo.queryByRole('button', { name: 'Revisar e salvar →' })).not.toBeInTheDocument();
    r.unmount(); montar(true, ['imp-page']); dialogo = await abrir('API');
    expect(dialogo.getByLabelText('Como validar')).toBeDisabled();
    expect(gravar).not.toHaveBeenCalled();
  });

  it('preserva o rascunho ao receber 401 e restaura ao reabrir', async () => {
    vi.spyOn(api, 'editarMatrix').mockRejectedValue(new ErroDaApi(401, 'Expirou'));
    const r = montar(); const dialogo = await abrir();
    await userEvent.type(dialogo.getByLabelText('Como validar'), ' · rascunho');
    await userEvent.click(dialogo.getByRole('button', { name: 'Revisar e salvar →' }));
    expect(await dialogo.findByRole('alert')).toHaveTextContent('Sessão expirada');
    await waitFor(() => expect(sessionStorage.getItem(chaveRascunho('demonstracao', documentoDeTeste()))).toContain('rascunho'));
    r.unmount(); montar(); const restaurado = await abrir();
    expect(restaurado.getByLabelText('Como validar')).toHaveValue('Validar em 390 px · rascunho');
  });

  it.each([new TypeError('Rede indisponível'), new ErroDaApi(403, 'Permissão revogada')])('preserva definição e motivo quando o servidor recusa ou a rede cai: %s', async falha => {
    const gravar = vi.spyOn(api, 'editarMatrix').mockRejectedValue(falha);
    montar(); const d = await abrir();
    await userEvent.clear(d.getByLabelText('Obrigação neste projeto'));
    await userEvent.type(d.getByLabelText('Obrigação neste projeto'), 'Editar evidências no celular');
    await userEvent.clear(d.getByLabelText('Justificativa / motivo'));
    await userEvent.type(d.getByLabelText('Justificativa / motivo'), 'Preservar o significado entre clientes');
    await userEvent.click(d.getByRole('button', { name: 'Revisar e salvar →' }));
    expect(await d.findByRole('alert')).toHaveTextContent(/rascunho.*preservado/i);
    const valor = gravar.mock.calls[0][1].documento.matriz!.celulas[0].justificativa;
    expect(valor).toContain('Obrigação: Editar evidências no celular');
    expect(valor).toContain('Motivo: Preservar o significado entre clientes');
    expect(gravar.mock.calls[0][2]).toBe(false);
  });

  it('compara conflito do desktop e exige nova revisão após reaplicar, sem sobrescrever outra task', async () => {
    const remoto = documentoDeTeste(); remoto.matriz!.celulas[1].criterio = 'Mudança do desktop';
    const gravar = vi.spyOn(api, 'editarMatrix').mockRejectedValueOnce(new ErroDaApi(409, 'Outra revisão'))
      .mockImplementation(async (_t, e, aplicar) => ({ aplicado: aplicar ?? false, revisao: revisaoDeTeste(e.documento, 3), mudancas: { celulas: ['imp-page'], responsaveis: ['item-4'] } }));
    vi.spyOn(api, 'historicoMatrix').mockResolvedValue([revisaoDeTeste(), revisaoDeTeste(remoto, 2)]);
    montar(); const d = await abrir(); await userEvent.type(d.getByLabelText('Como validar'), ' · local');
    await userEvent.click(d.getByRole('button', { name: 'Revisar e salvar →' }));
    expect(await d.findByRole('heading', { name: 'Existe uma revisão mais recente' })).toBeInTheDocument();
    await userEvent.click(d.getByRole('button', { name: 'Reaplicar e revisar' }));
    expect(gravar).toHaveBeenCalledTimes(1);
    await userEvent.click(d.getByRole('button', { name: 'Revisar e salvar →' }));
    await d.findByRole('button', { name: 'Salvar r3 e distribuir' });
    expect(gravar.mock.calls[1][1].versaoEsperada).toBe(2);
    expect(gravar.mock.calls[1][1].documento.matriz!.celulas.find(c => c.id === 'imp-api')!.criterio).toBe('Mudança do desktop');
  });
});
