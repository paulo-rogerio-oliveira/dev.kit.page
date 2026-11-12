import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { vi } from 'vitest';
import { api, ErroDaApi } from '../../api/cliente';
import { EditorMatrix } from './EditorMatrix';
import { documentoDeTeste, revisaoDeTeste } from './fixtures';
import type { ResultadoMatrix } from '../../api/matrix';
import { chaveRascunho } from './modelo';
import { CHAVE_DA_SESSAO, SessaoProvider } from '../../sessao';

beforeEach(() => {
  sessionStorage.clear();
  HTMLDialogElement.prototype.showModal = function () { this.setAttribute('open', ''); };
});
afterEach(() => vi.restoreAllMocks());

function montar(editar = true, recorte?: string[], opcoes: { minhasTasks?: string[]; solicitar?: (motivo?: string) => void } = {}) {
  const d = documentoDeTeste();
  return render(<MemoryRouter><SessaoProvider><EditorMatrix alvo={{ documento: d, historico: [revisaoDeTeste(d)], mapa: [], registros: [], recorte, minhasTasks: opcoes.minhasTasks }}
    token="token-ficticio" conta="demonstracao" editar={editar} solicitar={opcoes.solicitar} /></SessaoProvider></MemoryRouter>);
}
const abrir = async (layer = 'Page') => {
  // Grade e lista móvel têm o mesmo controle; apenas uma delas é mostrada pelo CSS no navegador.
  await userEvent.click(screen.getAllByRole('button', { name: `Editor × ${layer} · imp-${layer.toLowerCase()}` })[0]);
  return within(screen.getByRole('dialog'));
};

describe('editor web da Matrix', () => {
  it('Minha task usa o vínculo do contexto e o botão destacado abre a célula sem edição prévia', async () => {
    montar(true, undefined, { minhasTasks: ['item-4'] });
    await userEvent.click(screen.getByRole('button', { name: 'Minha task' }));
    expect(screen.getByRole('combobox', { name: 'Tasks' })).toHaveValue('');
    expect(screen.queryByRole('button', { name: 'Editor × API · imp-api' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Editar IMP-PAGE →' }));
    expect(within(screen.getByRole('dialog')).getByLabelText('Como validar')).toBeEnabled();
  });

  it('dependência em consulta permite solicitar atualização sem alterar o documento', async () => {
    const solicitar = vi.fn(), gravar = vi.spyOn(api, 'editarMatrix');
    montar(true, ['imp-page'], { solicitar }); const d = await abrir('API');
    expect(d.getByLabelText('Como validar')).toBeDisabled();
    const acao = d.getByRole('button', { name: 'Solicitar atualização ao Orquestrador ↗' });
    expect(acao).toBeEnabled(); await userEvent.click(acao);
    expect(solicitar).toHaveBeenCalledWith('Solicitar atualização de impactos fora deste recorte');
    expect(gravar).not.toHaveBeenCalled();
  });

  it('leitor solicita acesso e recorte vinculado vazio conserva as dependências em consulta', async () => {
    const solicitar = vi.fn();
    montar(false, [], { solicitar });
    expect(screen.getByRole('heading', { name: 'Nenhuma obrigação atribuída a esta task' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Pedir revisão do recorte' }));
    expect(solicitar).toHaveBeenCalledWith('Solicitar revisão do recorte vinculado sem obrigações atribuídas');
    const d = await abrir(); expect(d.getByLabelText('Como validar')).toBeDisabled();
    await userEvent.click(d.getByRole('button', { name: 'Solicitar acesso' }));
    expect(solicitar).toHaveBeenCalledWith('Solicitar acesso de editor à definição da Matrix');
    expect(d.queryByRole('button', { name: 'Revisar e salvar →' })).not.toBeInTheDocument();
  });

  it('Tentar novamente repete a simulação com o rascunho preservado após queda de rede', async () => {
    const gravar = vi.spyOn(api, 'editarMatrix').mockRejectedValueOnce(new TypeError('offline')).mockImplementation(async (_t, edicao) => ({
      aplicado: false, revisao: revisaoDeTeste(edicao.documento, 2), mudancas: { celulas: ['imp-page'], responsaveis: ['item-4'] },
    }));
    montar(); const d = await abrir(); await userEvent.type(d.getByLabelText('Como validar'), ' · preservado');
    await userEvent.click(d.getByRole('button', { name: 'Revisar e salvar →' }));
    await userEvent.click(await d.findByRole('button', { name: 'Tentar novamente' }));
    await screen.findByRole('button', { name: 'Salvar r2 e distribuir' });
    expect(gravar.mock.calls[1][1]).toEqual(gravar.mock.calls[0][1]); expect(gravar.mock.calls[1][2]).toBe(false);
  });

  it('editar eixos na prévia invalida a confirmação e exige nova revisão antes de salvar', async () => {
    const gravar = vi.spyOn(api, 'editarMatrix').mockImplementation(async (_t, edicao, aplicar) => ({
      aplicado: aplicar ?? false, revisao: revisaoDeTeste(edicao.documento, 2), mudancas: { celulas: ['imp-page'], responsaveis: ['item-4'] },
    }));
    montar(); const d = await abrir(); await userEvent.type(d.getByLabelText('Como validar'), ' · revisar');
    await userEvent.click(d.getByRole('button', { name: 'Revisar e salvar →' }));
    await screen.findByRole('button', { name: 'Salvar r2 e distribuir' });
    const nome = screen.getByRole('textbox', { name: 'Nome da layer' });
    await userEvent.clear(nome); await userEvent.type(nome, 'Canal Page');
    expect(screen.queryByRole('button', { name: 'Salvar r2 e distribuir' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '+ Layer' })).toBeEnabled();
    await userEvent.click(screen.getByRole('button', { name: 'Revisar eixos alterados' }));
    await screen.findByRole('button', { name: 'Salvar r2 e distribuir' });
    expect(gravar.mock.calls.at(-1)![1].documento.matriz!.layers[0].nome).toBe('Canal Page');
    expect(gravar.mock.calls.every(c => !c[2])).toBe(true);
  });

  it('simula e apresenta tasks afetadas antes de aplicar uma revisão', async () => {
    const gravar = vi.spyOn(api, 'editarMatrix').mockImplementation(async (_token, edicao, aplicar) => ({
      aplicado: aplicar, revisao: revisaoDeTeste(edicao.documento, 2), mudancas: { celulas: ['imp-page'], responsaveis: ['item-4'] },
    } as ResultadoMatrix));
    montar(); const dialogo = await abrir();
    await userEvent.clear(dialogo.getByLabelText('Como validar'));
    await userEvent.type(dialogo.getByLabelText('Como validar'), 'Validar persistência real em 390 px');
    await userEvent.click(dialogo.getByRole('button', { name: 'Revisar e salvar →' }));
    expect(await screen.findByText(/Tasks afetadas: item-4/)).toBeInTheDocument();
    expect(gravar).toHaveBeenCalledTimes(1); expect(gravar.mock.calls[0][2]).toBe(false);
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByRole('heading', { name: 'Histórico e autoria' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Salvar r2 e distribuir' }));
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
    sessionStorage.setItem(CHAVE_DA_SESSAO, JSON.stringify({ token: 'invalido', login: 'demonstracao', papel: 'dev', expiraEm: new Date(Date.now() + 3600000).toISOString() }));
    const r = montar(); const dialogo = await abrir();
    await userEvent.type(dialogo.getByLabelText('Como validar'), ' · rascunho');
    await userEvent.click(dialogo.getByRole('button', { name: 'Revisar e salvar →' }));
    expect(await dialogo.findByRole('alert')).toHaveTextContent('Sessão expirada');
    await waitFor(() => expect(sessionStorage.getItem(chaveRascunho('demonstracao', documentoDeTeste()))).toContain('rascunho'));
    await userEvent.click(dialogo.getByRole('link', { name: 'Entrar novamente' }));
    expect(sessionStorage.getItem(CHAVE_DA_SESSAO)).toBeNull();
    expect(sessionStorage.getItem(chaveRascunho('demonstracao', documentoDeTeste()))).toContain('rascunho');
    r.unmount(); montar(); const restaurado = await abrir();
    expect(restaurado.getByLabelText('Como validar')).toHaveValue('Validar em 390 px · rascunho');
  });
  it('editar e restaurar o original remove o rascunho e não ressuscita a edição ao remontar', async () => {
    const r = montar(), d = await abrir();
    const campo = d.getByLabelText('Como validar');
    await userEvent.type(campo, ' · edição desfeita');
    const chave = chaveRascunho('demonstracao', documentoDeTeste());
    await waitFor(() => expect(sessionStorage.getItem(chave)).toContain('edição desfeita'));
    await userEvent.clear(campo); await userEvent.type(campo, 'Validar em 390 px');
    await waitFor(() => expect(sessionStorage.getItem(chave)).toBeNull());
    r.unmount(); montar();
    expect((await abrir()).getByLabelText('Como validar')).toHaveValue('Validar em 390 px');
  });
  it('filtra a lista móvel por layer e abre o editor da célula selecionada', async () => {
    const r = montar();
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Layer' }), 'api');
    const lista = within(r.container.querySelector('.mx-lista')! as HTMLElement);
    expect(lista.queryByRole('button', { name: 'Editor × Page · imp-page' })).not.toBeInTheDocument();
    await userEvent.click(lista.getByRole('button', { name: 'Editor × API · imp-api' }));
    await userEvent.click(lista.getByRole('button', { name: 'Abrir editor completo →' }));
    expect(within(screen.getByRole('dialog')).getByLabelText('Como validar')).toHaveValue(documentoDeTeste().matriz!.celulas[1].criterio);
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
    expect(await d.findByRole('heading', { name: 'Conflito de edição' })).toBeInTheDocument();
    await userEvent.click(d.getByRole('button', { name: 'Reaplicar e revisar' }));
    expect(gravar).toHaveBeenCalledTimes(1);
    await userEvent.click(d.getByRole('button', { name: 'Revisar e salvar →' }));
    await screen.findByRole('button', { name: 'Salvar r3 e distribuir' });
    expect(gravar.mock.calls[1][1].versaoEsperada).toBe(2);
    expect(gravar.mock.calls[1][1].documento.matriz!.celulas.find(c => c.id === 'imp-api')!.criterio).toBe('Mudança do desktop');
  });
});
